using System.Text.Json;
using Api.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Notifications;

/// <summary>A notification claimed for delivery.</summary>
/// <param name="Attempts">Delivery attempts so far, this one included.</param>
public sealed record OutboxNotification(long Id, string Kind, string Payload, int Attempts);

/// <summary>
/// Notifications waiting for delivery, in <c>notification_outbox</c>. They used to wait in memory and were lost at
/// shutdown; now a restart (or another instance) sends them. Each event has a key, so it is queued once however many
/// times it is published, and delivered rows are kept for <see cref="NotificationDispatcher.Retention"/> to remember
/// it. A claimed row is hidden from other senders until the claim runs out, so it is sent by one of them; a sender
/// that dies mid-delivery leaves it to be sent again once the claim expires.
/// </summary>
public sealed class NotificationOutbox(PlaydexDbContext db, TimeProvider clock)
{
    /// <summary>Attempts before a notification is given up (about three hours of retries).</summary>
    public const int MaxAttempts = 10;

    private static readonly TimeSpan _firstRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _longestRetry = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <returns>False when the notification is not one this outbox knows, or the same event is already queued.</returns>
    public async Task<bool> Enqueue(object notification, CancellationToken ct)
    {
        if (Describe(notification) is not { } described)
        {
            return false;
        }

        var (kind, dedupeKey) = described;

        var payload = JsonSerializer.Serialize(notification, notification.GetType(), _jsonOptions);
        var now = clock.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into notification_outbox (dedupe_key, kind, payload, next_attempt_at, created_at)
            values ({dedupeKey}, {kind}, cast({payload} as jsonb), {now}, {now})
            on conflict (dedupe_key) do nothing
            """, ct);
        return inserted > 0;
    }

    /// <summary>Takes the oldest due notification for <paramref name="claimFor"/>, or null when none is due.</summary>
    public async Task<OutboxNotification?> ClaimNext(TimeSpan claimFor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var claimedUntil = now + claimFor;
        var rows = await db.Database.SqlQuery<ClaimedRow>($"""
            update notification_outbox
            set attempts = attempts + 1, next_attempt_at = {claimedUntil}
            where id = (
                select id from notification_outbox
                where delivered_at is null and failed_at is null and next_attempt_at <= {now}
                order by id
                limit 1
                for update skip locked)
            returning id as "Id", kind as "Kind", payload::text as "Payload", attempts as "Attempts"
            """).ToListAsync(ct);
        return rows.Select(row => new OutboxNotification(row.Id, row.Kind, row.Payload, row.Attempts)).SingleOrDefault();
    }

    public Task<int> MarkDelivered(long id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.NotificationOutbox
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(row => row.DeliveredAt, now)
                    .SetProperty(row => row.LastError, (string?)null),
                ct);
    }

    /// <summary>
    /// Schedules another attempt (30 s after the first failure, doubling up to an hour), or gives the notification
    /// up after <see cref="MaxAttempts"/> or when <paramref name="permanent"/>.
    /// </summary>
    /// <returns>True when the notification was given up.</returns>
    public async Task<bool> MarkFailed(OutboxNotification notification, string error, bool permanent, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var giveUp = permanent || notification.Attempts >= MaxAttempts;
        var retryDelay = TimeSpan.FromTicks(Math.Min(
            _firstRetry.Ticks * (1L << Math.Clamp(notification.Attempts - 1, 0, 20)),
            _longestRetry.Ticks));
        await db.NotificationOutbox
            .Where(row => row.Id == notification.Id)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(row => row.LastError, error)
                    .SetProperty(row => row.NextAttemptAt, now + retryDelay)
                    .SetProperty(row => row.FailedAt, giveUp ? now : (DateTimeOffset?)null),
                ct);
        return giveUp;
    }

    /// <summary>Hands a claimed notification back untried (shutdown): due again at once, the attempt not counted.</summary>
    public Task<int> Release(OutboxNotification notification, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.NotificationOutbox
            .Where(row => row.Id == notification.Id && row.DeliveredAt == null)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(row => row.NextAttemptAt, now)
                    .SetProperty(row => row.Attempts, row => row.Attempts - 1),
                ct);
    }

    /// <summary>Forgets notifications delivered or given up before <paramref name="before"/>.</summary>
    public Task<int> DeleteFinished(DateTimeOffset before, CancellationToken ct) =>
        db.NotificationOutbox
            .Where(row => (row.DeliveredAt != null && row.DeliveredAt < before) ||
                          (row.FailedAt != null && row.FailedAt < before))
            .ExecuteDeleteAsync(ct);

    /// <returns>The notification, or null when its kind is unknown (a newer release queued it).</returns>
    public static object? Deserialize(OutboxNotification notification) => notification.Kind switch
    {
        nameof(SessionStarted) => JsonSerializer.Deserialize<SessionStarted>(notification.Payload, _jsonOptions),
        nameof(SessionEnded) => JsonSerializer.Deserialize<SessionEnded>(notification.Payload, _jsonOptions),
        // Queued before users stopped being told about poll failures: drained as skipped, never sent.
        nameof(PollFailed) => JsonSerializer.Deserialize<PollFailed>(notification.Payload, _jsonOptions),
        nameof(IntegrationPaused) => JsonSerializer.Deserialize<IntegrationPaused>(notification.Payload, _jsonOptions),
        _ => null,
    };

    /// <summary>The kind, and a key that is the same every time the same event is published.</summary>
    internal static (string Kind, string DedupeKey)? Describe(object notification) => notification switch
    {
        SessionStarted started => (nameof(SessionStarted), $"session-started:{started.SessionId}"),
        SessionEnded ended => (nameof(SessionEnded), $"session-ended:{ended.SessionId}"),
        IntegrationPaused paused =>
            (nameof(IntegrationPaused), $"integration-paused:{paused.CredentialId}:{paused.PausedAt.UtcTicks}"),
        _ => null,
    };

    private sealed class ClaimedRow
    {
        public long Id { get; set; }
        public string Kind { get; set; } = "";
        public string Payload { get; set; } = "";
        public int Attempts { get; set; }
    }
}
