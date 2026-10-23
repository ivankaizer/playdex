using Api.Modules.Polling;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PollingLeaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OnlyOneInstanceHoldsTheLeaseAtATime()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var lease = new PollingLease(postgres.DataSource!);
        var otherInstance = new PollingLease(postgres.DataSource!);

        await using (var held = await AcquireEventually(lease))
        {
            Assert.Null(await otherInstance.TryAcquire(CancellationToken.None));
        }

        await using var afterRelease = await AcquireEventually(otherInstance);
    }

    // A test host running the real API (AdminApiFixture) may hold the lease for a moment during its own poll cycle.
    private static async Task<IAsyncDisposable> AcquireEventually(PollingLease lease)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await lease.TryAcquire(CancellationToken.None) is { } held)
            {
                return held;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The polling lease stayed held by another session.");
    }
}
