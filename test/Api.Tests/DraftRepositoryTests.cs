using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class DraftRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OwnerScopeAndRequestDeduplicationPreserveRows()
    {
        if (!postgres.IsConfigured) return;
        await using var db = await Prepare();
        var user = await AddUser(db);
        try
        {
            var repository = new DraftRepository(db);
            var batch = Batch(user.Id);
            await repository.CreateBatch(batch, default);
            var replay = await repository.CreateBatch(Batch(user.Id, batch.RequestId), default);
            Assert.Equal(batch.Id, replay.Id);
            Assert.Single(replay.Rows);
            Assert.Null(await repository.FindBatch(user.Id + 1, batch.Id, default));
            Assert.Null(await repository.FindRequest(user.Id + 1, batch.RequestId, default));
            Assert.Empty(await repository.ListBatches(user.Id + 1, default));
            Assert.Single(await repository.ListBatches(user.Id, default));
            var failure = await Assert.ThrowsAsync<AppProblemException>(() => repository.WithLockedRow(
                user.Id + 1, batch.Id, batch.Rows[0].Id, row => Task.FromResult(row.Title), default));
            Assert.Equal(404, failure.StatusCode);
        }
        finally { await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task ConcurrentRequestsCreateOneBatch()
    {
        if (!postgres.IsConfigured) return;
        await using var db = await Prepare();
        var user = await AddUser(db);
        try
        {
            var requestId = Guid.NewGuid();
            await using var otherDb = CreateDb();
            var batches = await Task.WhenAll(
                new DraftRepository(db).CreateBatch(Batch(user.Id, requestId), default),
                new DraftRepository(otherDb).CreateBatch(Batch(user.Id, requestId), default));
            Assert.Equal(batches[0].Id, batches[1].Id);
            Assert.Equal(1, await db.SessionDraftBatches.CountAsync(x => x.UserId == user.Id));
        }
        finally { await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task RowLockSerializesEditsAndRollbackDiscardsAddedEntities()
    {
        if (!postgres.IsConfigured) return;
        await using var db = await Prepare();
        var user = await AddUser(db);
        try
        {
            var batch = await new DraftRepository(db).CreateBatch(Batch(user.Id), default);
            var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = new DraftRepository(db).WithLockedRow(user.Id, batch.Id, batch.Rows[0].Id, async row =>
            {
                locked.SetResult();
                await release.Task;
                row.Revision++;
                return row.Revision;
            }, default);
            await locked.Task;
            await using var otherDb = CreateDb();
            var second = new DraftRepository(otherDb).WithLockedRow(user.Id, batch.Id, batch.Rows[0].Id,
                row => Task.FromResult(row.Revision), default);
            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(200)));
            release.SetResult();
            Assert.Equal(2, await first);
            Assert.Equal(2, await second);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DraftRepository(db).WithLockedRow<int>(
                user.Id, batch.Id, batch.Rows[0].Id, async row =>
                {
                    row.Title = "Must roll back";
                    db.Games.Add(new Game { Title = "Failed draft acceptance" });
                    await db.SaveChangesAsync();
                    throw new InvalidOperationException("acceptance failed");
                }, default));
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal("Hades", (await new DraftRepository(db).FindBatch(user.Id, batch.Id, default))!.Rows[0].Title);
            Assert.False(await db.Games.AnyAsync(x => x.Title == "Failed draft acceptance"));
        }
        finally { await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(); }
    }

    [Theory]
    [InlineData("invalid", 1, null)]
    [InlineData("pending", 0, null)]
    [InlineData("accepted", 1, null)]
    [InlineData("pending", 1, 123L)]
    public async Task InvalidLifecycleStateIsRejected(string status, int revision, long? sessionId)
    {
        if (!postgres.IsConfigured) return;
        await using var db = await Prepare();
        var user = await AddUser(db);
        try
        {
            var batch = Batch(user.Id);
            batch.Rows[0].Status = status;
            batch.Rows[0].Revision = revision;
            batch.Rows[0].AcceptedSessionId = sessionId;
            await Assert.ThrowsAsync<DbUpdateException>(() => new DraftRepository(db).CreateBatch(batch, default));
        }
        finally { await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task AcceptedLinkDoesNotRequireASurvivingSession()
    {
        if (!postgres.IsConfigured) return;
        await using var db = await Prepare();
        var user = await AddUser(db);
        try
        {
            var batch = Batch(user.Id);
            batch.Rows[0].Status = "accepted";
            batch.Rows[0].AcceptedSessionId = long.MaxValue;
            await new DraftRepository(db).CreateBatch(batch, default);
            Assert.Equal(long.MaxValue, (await new DraftRepository(db).FindBatch(user.Id, batch.Id, default))!.Rows[0].AcceptedSessionId);
        }
        finally { await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task CompletedBatchesAreExcludedFromSavedReview()
    {
        if (!postgres.IsConfigured) return;
        await using var db = await Prepare();
        var user = await AddUser(db);
        try
        {
            var pending = Batch(user.Id);
            var completed = Batch(user.Id);
            completed.Rows[0].Status = "accepted";
            completed.Rows[0].AcceptedSessionId = long.MaxValue;
            await new DraftRepository(db).CreateBatch(pending, default);
            await new DraftRepository(db).CreateBatch(completed, default);

            var saved = await new DraftRepository(db).ListBatches(user.Id, default);

            var only = Assert.Single(saved);
            Assert.Equal(pending.Id, only.Id);
        }
        finally { await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task DuplicateRowPositionsAndAcceptedLinksAreRejected()
    {
        if (!postgres.IsConfigured) return;
        await using var db = await Prepare();
        var user = await AddUser(db);
        try
        {
            var batch = Batch(user.Id);
            batch.Rows.Add(new SessionDraftRow { Id = Guid.NewGuid(), Position = 0, Title = "Second" });
            await Assert.ThrowsAsync<DbUpdateException>(() => new DraftRepository(db).CreateBatch(batch, default));
            batch = Batch(user.Id);
            batch.Rows[0].Status = "accepted";
            batch.Rows[0].AcceptedSessionId = long.MaxValue - 1;
            batch.Rows.Add(new SessionDraftRow
            {
                Id = Guid.NewGuid(), Position = 1, Title = "Second", Status = "accepted", AcceptedSessionId = long.MaxValue - 1,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => new DraftRepository(db).CreateBatch(batch, default));
        }
        finally { await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(); }
    }

    private async Task<PlaydexDbContext> Prepare()
    {
        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await new AiPlaySessionDraftsMigration().ApplyAsync(connection, transaction, default);
        await transaction.CommitAsync();
        return CreateDb();
    }

    private static async Task<User> AddUser(PlaydexDbContext db)
    {
        var user = new User { Email = $"draft-{Guid.NewGuid():N}@example.test", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static SessionDraftBatch Batch(long userId, Guid? requestId = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, RequestId = requestId ?? Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow, Timezone = "Europe/Warsaw", ReferenceDate = new DateOnly(2026, 9, 9),
        Rows = [new SessionDraftRow { Id = Guid.NewGuid(), Position = 0, Title = "Hades", Warnings = ["date_missing"] }],
    };

    private PlaydexDbContext CreateDb() => new(new DbContextOptionsBuilder<PlaydexDbContext>()
        .UseNpgsql(postgres.DataSource!, options =>
        {
            options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            options.MapEnum<IntegrationType>("integration_type");
            options.MapEnum<CaptureMethod>("capture_method");
        }).Options);
}
