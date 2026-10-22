using System.Collections.Concurrent;

namespace Api.Modules.Polling;

/// <summary>
/// Remembers games whose capsule lookup found nothing or failed, so a provider's metadata endpoint is not called on
/// every poll of a game that has no capsule. In memory: after a restart each such game is looked up once more.
/// </summary>
public sealed class CapsuleLookupBackoff
{
    public static readonly TimeSpan MissRetry = TimeSpan.FromHours(12);
    public static readonly TimeSpan FailureRetry = TimeSpan.FromMinutes(30);
    private const int PruneThreshold = 10_000;

    private readonly ConcurrentDictionary<long, DateTimeOffset> _retryAt = new();

    public bool IsDue(long gameId, DateTimeOffset now) =>
        !_retryAt.TryGetValue(gameId, out var retryAt) || retryAt <= now;

    public void RecordMiss(long gameId, DateTimeOffset now) => Set(gameId, now + MissRetry, now);

    public void RecordFailure(long gameId, DateTimeOffset now, TimeSpan? retryAfter) =>
        Set(gameId, now + (retryAfter > FailureRetry ? retryAfter.Value : FailureRetry), now);

    public void Forget(long gameId) => _retryAt.TryRemove(gameId, out _);

    private void Set(long gameId, DateTimeOffset retryAt, DateTimeOffset now)
    {
        if (_retryAt.Count >= PruneThreshold)
        {
            foreach (var expired in _retryAt.Where(entry => entry.Value <= now))
            {
                _retryAt.TryRemove(expired);
            }
        }

        _retryAt[gameId] = retryAt;
    }
}
