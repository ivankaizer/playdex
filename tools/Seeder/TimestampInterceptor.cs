using Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Seeder;

public sealed class TimestampInterceptor(DateTimeOffset now)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyTimestamps(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTimestamps(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void ApplyTimestamps(DbContext? db)
    {
        if (db is null)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            entry.Entity.CreatedAt = now;
            entry.Entity.UpdatedAt = now;
        }
    }
}
