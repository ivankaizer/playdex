using System.Collections.Concurrent;

namespace Playdex.Api.Metrics;

public sealed class MetricsCollector
{
    private long _pollSuccessTotal;
    private long _pollFailureTotal;
    private readonly ConcurrentDictionary<string, long> _pollSuccessByPlatform = new();
    private readonly ConcurrentDictionary<string, long> _pollFailureByPlatform = new();
    private string? _lastPollSuccessAt;
    private string? _lastPollFailureAt;
    private long? _lastCycleDurationMs;
    private readonly object _lock = new();

    public void RecordPollSuccess(string platform, DateTimeOffset? timestamp = null)
    {
        Interlocked.Increment(ref _pollSuccessTotal);
        _pollSuccessByPlatform.AddOrUpdate(platform, _ => 1, (_, count) => count + 1);

        if (timestamp.HasValue)
        {
            lock (_lock)
            {
                _lastPollSuccessAt = timestamp.Value.ToString("o");
            }
        }
    }

    public void RecordPollFailure(string platform, DateTimeOffset? timestamp = null)
    {
        Interlocked.Increment(ref _pollFailureTotal);
        _pollFailureByPlatform.AddOrUpdate(platform, _ => 1, (_, count) => count + 1);

        if (timestamp.HasValue)
        {
            lock (_lock)
            {
                _lastPollFailureAt = timestamp.Value.ToString("o");
            }
        }
    }

    public void RecordCycleDuration(long durationMs)
    {
        lock (_lock)
        {
            _lastCycleDurationMs = durationMs;
        }
    }

    public MetricsSnapshotDto Snapshot()
    {
        long successTotal, failureTotal;
        Dictionary<string, int> successByPlatform, failureByPlatform;
        string? lastPollSuccessAt, lastPollFailureAt;
        long? lastCycleDurationMs;

        // Snapshot atomically readable fields
        successTotal = Interlocked.Read(ref _pollSuccessTotal);
        failureTotal = Interlocked.Read(ref _pollFailureTotal);

        // Snapshot concurrent dictionaries
        successByPlatform = _pollSuccessByPlatform.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value);
        failureByPlatform = _pollFailureByPlatform.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value);

        // Snapshot lock-protected fields
        lock (_lock)
        {
            lastPollSuccessAt = _lastPollSuccessAt;
            lastPollFailureAt = _lastPollFailureAt;
            lastCycleDurationMs = _lastCycleDurationMs;
        }

        return new MetricsSnapshotDto(
            (int)successTotal,
            (int)failureTotal,
            successByPlatform,
            failureByPlatform,
            lastPollSuccessAt,
            lastPollFailureAt,
            lastCycleDurationMs
        );
    }
}
