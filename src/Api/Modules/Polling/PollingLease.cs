using Npgsql;

namespace Api.Modules.Polling;

/// <summary>
/// One poll cycle at a time across all API instances. Every replica runs the polling loop; a cycle runs only on the
/// instance that holds this session-level advisory lock, so a credential is not polled (and its session not
/// started or ended) by several replicas at once. The lock goes with the connection if the process dies.
/// </summary>
public sealed class PollingLease(NpgsqlDataSource dataSource)
{
    private const long LockKey = 7_256_394_813_101;

    /// <returns>The held lease, to dispose when the cycle ends, or null when another instance is polling.</returns>
    public async Task<IAsyncDisposable?> TryAcquire(CancellationToken ct)
    {
        var connection = await dataSource.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            if (await command.ExecuteScalarAsync(ct) is true)
            {
                return new Held(connection);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Held(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("select pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", LockKey);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
