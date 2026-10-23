using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Polling;

/// <param name="Delay">How long the credential is skipped.</param>
/// <param name="Failures">Failed polls since the last successful one, this one included.</param>
/// <param name="AuthFailures">How many of <paramref name="Failures"/> were the provider rejecting the credential.</param>
/// <param name="FailingSince">When the first of <paramref name="Failures"/> happened: identifies the outage.</param>
public sealed record CredentialFailure(TimeSpan Delay, int Failures, int AuthFailures, DateTimeOffset FailingSince)
{
    /// <summary>The credential was healthy until this failure.</summary>
    public bool IsFirst => Failures == 1;
}

/// <summary>Per-credential poll failure backoff.</summary>
public interface ICredentialBackoff
{
    /// <summary>Counts a failed poll and pushes the next one back: 5 s, doubling up to 10 min, or the provider's
    /// Retry-After when that is longer.</summary>
    Task<CredentialFailure> RecordFailure(
        long credentialId,
        DateTimeOffset now,
        TimeSpan? retryAfter,
        bool authenticationFailure,
        CancellationToken ct);

    /// <summary>Skips a working credential until <paramref name="until"/> without counting a failure.</summary>
    Task Defer(long credentialId, DateTimeOffset until, CancellationToken ct);

    /// <summary>The credential works again: forget its failures.</summary>
    Task Reset(long credentialId, CancellationToken ct);
}

/// <summary>
/// The backoff in <c>integration_poll_backoff</c>, shared by every API instance. Kept in memory, each replica counted
/// its own failures and announced the same outage, and a restart forgot it. Each change is one statement, so two
/// instances recording a failure at once still count two failures and only one of them sees the first.
/// </summary>
public sealed class CredentialBackoff(PlaydexDbContext db) : ICredentialBackoff
{
    internal static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(10);

    public async Task<CredentialFailure> RecordFailure(
        long credentialId,
        DateTimeOffset now,
        TimeSpan? retryAfter,
        bool authenticationFailure,
        CancellationToken ct)
    {
        now = ToDatabasePrecision(now);
        var authFailure = authenticationFailure ? 1 : 0;
        var minimumDelay = retryAfter ?? TimeSpan.Zero;
        var rows = await db.Database.SqlQuery<FailureRow>($"""
            insert into integration_poll_backoff as backoff
                (integration_id, failures, auth_failures, failing_since, next_poll_at, updated_at)
            values ({credentialId}, 1, {authFailure}, {now}, {now} + greatest({BaseDelay}, {minimumDelay}), {now})
            on conflict (integration_id) do update set
                failures = backoff.failures + 1,
                auth_failures = backoff.auth_failures + {authFailure},
                failing_since = coalesce(backoff.failing_since, {now}),
                next_poll_at = {now} + greatest(
                    least({BaseDelay} * power(2, least(backoff.failures, 20)), {MaximumDelay}),
                    {minimumDelay}),
                updated_at = {now}
            returning
                failures as "Failures",
                auth_failures as "AuthFailures",
                failing_since as "FailingSince",
                next_poll_at as "NextPollAt"
            """).ToListAsync(ct);
        var row = rows.Single();
        return new CredentialFailure(row.NextPollAt - now, row.Failures, row.AuthFailures, row.FailingSince);
    }

    public async Task Defer(long credentialId, DateTimeOffset until, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into integration_poll_backoff
                (integration_id, failures, auth_failures, failing_since, next_poll_at, updated_at)
            values ({credentialId}, 0, 0, null, {until.ToUniversalTime()}, {now})
            on conflict (integration_id) do update set
                failures = 0,
                auth_failures = 0,
                failing_since = null,
                next_poll_at = excluded.next_poll_at,
                updated_at = excluded.updated_at
            """, ct);
    }

    public Task Reset(long credentialId, CancellationToken ct) =>
        db.IntegrationPollBackoffs.Where(row => row.IntegrationId == credentialId).ExecuteDeleteAsync(ct);

    /// <summary>timestamptz keeps microseconds; rounding first keeps the computed delay exact.</summary>
    private static DateTimeOffset ToDatabasePrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % 10));
    }

    private sealed class FailureRow
    {
        public int Failures { get; set; }
        public int AuthFailures { get; set; }
        public DateTimeOffset FailingSince { get; set; }
        public DateTimeOffset NextPollAt { get; set; }
    }
}
