using Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Manual.Drafts;

public sealed class DraftRepository(PlaydexDbContext db)
{
    public Task<SessionDraftBatch?> FindBatch(long userId, Guid batchId, CancellationToken cancellationToken) =>
        Batches(userId).SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);

    public Task<SessionDraftBatch?> FindRequest(long userId, Guid requestId, CancellationToken cancellationToken) =>
        Batches(userId).SingleOrDefaultAsync(batch => batch.RequestId == requestId, cancellationToken);

    public Task<List<SessionDraftBatch>> ListBatches(long userId, CancellationToken cancellationToken) =>
        Batches(userId)
            .Where(batch => batch.Rows.Any(row => row.Status == "pending"))
            .OrderByDescending(batch => batch.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<SessionDraftBatch> CreateBatch(SessionDraftBatch batch, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var lockKey = $"draft-request:{batch.UserId}:{batch.RequestId:D}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
            var existing = await FindRequest(batch.UserId, batch.RequestId, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }
            db.SessionDraftBatches.Add(batch);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            // Reads and subsequent row locks must observe fresh database state.
            db.Entry(batch).State = EntityState.Detached;
            foreach (var row in batch.Rows) db.Entry(row).State = EntityState.Detached;
            return batch;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<T> WithLockedRow<T>(long userId, Guid batchId, Guid rowId,
        Func<SessionDraftRow, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.SessionDraftBatches.AnyAsync(batch => batch.Id == batchId && batch.UserId == userId, cancellationToken))
                throw AppProblemException.NotFound("Draft batch not found.");

            // Detach a previous operation's row so a later lock cannot reuse stale tracked values.
            foreach (var entry in db.ChangeTracker.Entries<SessionDraftRow>().Where(entry => entry.Entity.Id == rowId).ToArray())
                entry.State = EntityState.Detached;
            var row = await db.SessionDraftRows.FromSqlInterpolated(
                    $"select * from session_draft_rows where batch_id = {batchId} and id = {rowId} for update")
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw AppProblemException.NotFound("Draft row not found.");
            var result = await action(row);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.Entry(row).State = EntityState.Detached;
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private IQueryable<SessionDraftBatch> Batches(long userId) => db.SessionDraftBatches.AsNoTracking()
        .Where(batch => batch.UserId == userId).Include(batch => batch.Rows.OrderBy(row => row.Position));
}
