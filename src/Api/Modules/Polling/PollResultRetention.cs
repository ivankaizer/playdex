using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Modules.Polling;

/// <summary>Deletes raw provider responses older than <see cref="PollingOptions.PollResultRetentionDays"/>.</summary>
public sealed class PollResultRetention(NpgsqlDataSource dataSource, IOptions<PollingOptions> options, TimeProvider clock)
{
    public async Task<int> DeleteExpired(CancellationToken ct)
    {
        var days = options.Value.PollResultRetentionDays;
        if (days <= 0)
        {
            return 0;
        }

        await using var command = dataSource.CreateCommand("delete from poll_results where polled_at < @cutoff");
        command.Parameters.AddWithValue("cutoff", clock.GetUtcNow().AddDays(-days));
        return await command.ExecuteNonQueryAsync(ct);
    }
}

public sealed partial class PollResultCleanup(PollResultRetention retention, ILogger<PollResultCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await retention.DeleteExpired(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            // Any failure, not only database ones: an escaping exception would stop this service for good.
            catch (Exception exception) { LogCleanupFailed(exception); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poll result cleanup failed; will retry next hour.")]
    private partial void LogCleanupFailed(Exception exception);
}
