using System.Linq.Expressions;
using Api.Infrastructure.Common;
using Api.Modules.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playdex.Persistence;

namespace Api.Modules.Sessions;

/// <summary>
/// Starts, keeps alive and ends play sessions for polling, API capture and manual timers. Changes run in the caller's
/// transaction, after it took <see cref="LockIntegration"/> (or <see cref="LockManualTimer"/>); once it has committed,
/// <see cref="PublishPending"/> announces the integration sessions that started and ended. A session is ended by a
/// guarded <c>UPDATE … WHERE ended_at IS NULL RETURNING</c>, so it ends, and is announced, once. Manual timers are not
/// announced: they have no integration, and the user starts and stops them by hand.
/// </summary>
public sealed partial class SessionLifecycle(
    PlaydexDbContext db,
    EventBus eventBus,
    IOptions<PollingOptions> pollingOptions,
    ILogger<SessionLifecycle> logger)
{
    /// <summary>Capture clients ping every 30 s; a session not pinged for this long ends this long after its last ping.</summary>
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromMinutes(3);

    /// <summary>A manual timer nobody stopped ends this long after it started.</summary>
    public static readonly TimeSpan ManualTimerLimit = TimeSpan.FromHours(12);

    private const int PollingGapIntervals = 5;
    private static readonly TimeSpan _minimumPollingGap = TimeSpan.FromMinutes(5);

    private readonly List<object> _pending = [];

    [LoggerMessage(Level = LogLevel.Error, Message = "Session event handler failed for {EventType}")]
    private partial void LogEventHandlerFailed(Exception exception, string eventType);

    private TimeSpan PollingInterval => TimeSpan.FromSeconds(Math.Max(1, pollingOptions.Value.IntervalSeconds));

    /// <summary>
    /// A polling session unseen for longer than this (an outage, an expired token, a paused integration) ended one
    /// interval after it was last seen: the gap is not counted as play. A slow poll cycle or a brief provider error
    /// stays below it.
    /// </summary>
    private TimeSpan PollingGap
    {
        get
        {
            var gap = PollingInterval * PollingGapIntervals;
            return gap > _minimumPollingGap ? gap : _minimumPollingGap;
        }
    }

    /// <summary>Serializes the session changes of one integration. Polling and provider settings take the same lock.</summary>
    public Task LockIntegration(long integrationId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({integrationId})", ct);

    public Task LockManualTimer(long userId, CancellationToken ct)
    {
        var key = $"manual-session-user:{userId}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            ct);
    }

    /// <summary>
    /// A poll of a polling integration saw <paramref name="gameId"/> (null: no game, or an ignored one). A provider
    /// reports one game, so the integration has one session: it goes on while that game is seen and ends at the poll
    /// that sees another game or none. After a gap it ended one interval after it was last seen, and a game still
    /// running starts a new session.
    /// </summary>
    /// <returns>True when a session ended or started.</returns>
    public async Task<bool> ApplyPoll(
        long integrationId,
        long userId,
        long? gameId,
        long platformId,
        DateTimeOffset polledAt,
        CancellationToken ct)
    {
        long? continuing = null;
        var changed = false;
        foreach (var session in await OpenSessions(row => row.IntegrationId == integrationId, ct))
        {
            var unseen = polledAt - session.UpdatedAt > PollingGap;
            if (!unseen && continuing is null && session.GameId == gameId)
            {
                continuing = session.Id;
                continue;
            }

            changed |= await End([session.Id], polledAt, unseen ? PollingInterval : null, ct) > 0;
        }

        if (continuing is { } sessionId && await Touch(sessionId, polledAt, ct))
        {
            return changed;
        }

        if (gameId is not { } detectedGameId)
        {
            return changed;
        }

        await Start(new PlaySession
        {
            GameId = detectedGameId,
            UserId = userId,
            PlatformId = platformId,
            CaptureMethod = CaptureMethod.Polling,
            IntegrationId = integrationId,
            StartedAt = polledAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = polledAt,
        }, ct);
        return true;
    }

    /// <summary>A capture client started <paramref name="gameId"/>; an open session of that game restarts.</summary>
    public async Task<CaptureOutcome> StartCapture(
        long integrationId,
        long userId,
        long gameId,
        long platformId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ended = await EndTimedOutCaptures(integrationId, now, ct);
        ended += await EndWhere(row => row.IntegrationId == integrationId && row.GameId == gameId, now, null, ct);
        var sessionId = await Start(CaptureSession(integrationId, userId, gameId, platformId, now), ct);
        return new CaptureOutcome(sessionId, true, ended);
    }

    /// <summary>
    /// A capture client still runs <paramref name="gameId"/>. Every game a client runs has its own session, since
    /// several can run at once; a ping for another platform restarts the game's session. Sessions of the integration
    /// not pinged for <see cref="CaptureTimeout"/> end first.
    /// </summary>
    public async Task<CaptureOutcome> PingCapture(
        long integrationId,
        long userId,
        long gameId,
        long platformId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ended = await EndTimedOutCaptures(integrationId, now, ct);
        long? continuing = null;
        foreach (var session in await OpenSessions(
                     row => row.IntegrationId == integrationId && row.GameId == gameId, ct))
        {
            if (continuing is null && session.PlatformId == platformId)
            {
                continuing = session.Id;
                continue;
            }

            ended += await End([session.Id], now, null, ct);
        }

        if (continuing is { } sessionId && await Touch(sessionId, now, ct))
        {
            return new CaptureOutcome(sessionId, false, ended);
        }

        var startedId = await Start(CaptureSession(integrationId, userId, gameId, platformId, now), ct);
        return new CaptureOutcome(startedId, true, ended);
    }

    /// <summary>A capture client says <paramref name="gameId"/> exited.</summary>
    public Task<int> EndCapture(long integrationId, long gameId, long platformId, CancellationToken ct) =>
        EndWhere(
            row => row.IntegrationId == integrationId && row.GameId == gameId && row.PlatformId == platformId,
            DateTimeOffset.UtcNow,
            null,
            ct);

    /// <summary>A whole session a capture client sends after the fact (it was offline); announced as ended.</summary>
    public async Task<long> RecordCapture(
        long integrationId,
        long userId,
        long gameId,
        long platformId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var session = CaptureSession(integrationId, userId, gameId, platformId, startedAt);
        session.EndedAt = endedAt;
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync(ct);
        _pending.Add(new SessionEnded(session.Id, integrationId, gameId, startedAt, endedAt));
        return session.Id;
    }

    /// <summary>The user starts a timer for <paramref name="gameId"/>; a timer already running ends.</summary>
    public async Task<long> StartManualTimer(
        long userId,
        long gameId,
        long platformId,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        await StopManualTimers(userId, startedAt, ct);
        var now = DateTimeOffset.UtcNow;
        return await Start(new PlaySession
        {
            GameId = gameId,
            UserId = userId,
            PlatformId = platformId,
            CaptureMethod = CaptureMethod.Manual,
            StartedAt = startedAt,
            CreatedAt = now,
            UpdatedAt = now,
        }, ct);
    }

    public Task<int> StopManualTimers(long userId, DateTimeOffset endedAt, CancellationToken ct) =>
        EndWhere(row => row.UserId == userId && row.CaptureMethod == CaptureMethod.Manual, endedAt, null, ct);

    /// <summary>
    /// The integration was paused, so nothing will see its sessions end: they end now, or a poll interval (capture
    /// timeout) after they were last seen if that is earlier.
    /// </summary>
    public Task<int> EndForPausedIntegration(long integrationId, IntegrationType type, CancellationToken ct) =>
        EndWhere(
            row => row.IntegrationId == integrationId,
            DateTimeOffset.UtcNow,
            type == IntegrationType.Api ? CaptureTimeout : PollingInterval,
            ct);

    /// <summary>
    /// Ends the sessions nobody else will: capture sessions not pinged for <see cref="CaptureTimeout"/>, polling
    /// sessions unseen for longer than the polling gap (a failing or paused integration) and manual timers running for
    /// <see cref="ManualTimerLimit"/>. Runs before every poll cycle and announces what it ended.
    /// </summary>
    public async Task<int> SweepStaleSessions(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ended = await EndUnseen("api", now - CaptureTimeout, CaptureTimeout, now, ct);
        ended.AddRange(await EndUnseen("polling", now - PollingGap, PollingInterval, now, ct));
        ended.AddRange(await EndOverdueManualTimers(now - ManualTimerLimit, now, ct));
        await transaction.CommitAsync(ct);
        Announce(ended);
        await PublishPending(ct);
        return ended.Count;
    }

    /// <summary>Announces the sessions started and ended since the last call. Call it after the transaction commits.</summary>
    public async Task PublishPending(CancellationToken ct)
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        foreach (var @event in pending)
        {
            switch (@event)
            {
                case SessionStarted started:
                    SessionMetrics.Record("started", started.CredentialId);
                    await SafePublish(started, ct);
                    break;
                case SessionEnded ended:
                    SessionMetrics.Record("ended", ended.CredentialId);
                    await SafePublish(ended, ct);
                    break;
            }
        }
    }

    private static PlaySession CaptureSession(
        long integrationId, long userId, long gameId, long platformId, DateTimeOffset startedAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlaySession
        {
            GameId = gameId,
            UserId = userId,
            PlatformId = platformId,
            CaptureMethod = CaptureMethod.Api,
            IntegrationId = integrationId,
            StartedAt = startedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private Task<int> EndTimedOutCaptures(long integrationId, DateTimeOffset now, CancellationToken ct)
    {
        var pingedBefore = now - CaptureTimeout;
        return EndWhere(
            row => row.IntegrationId == integrationId && row.UpdatedAt < pingedBefore,
            now,
            CaptureTimeout,
            ct);
    }

    private async Task<long> Start(PlaySession session, CancellationToken ct)
    {
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync(ct);
        if (session.IntegrationId is { } integrationId)
        {
            _pending.Add(new SessionStarted(session.Id, integrationId, session.GameId, session.StartedAt));
        }

        return session.Id;
    }

    private async Task<bool> Touch(long sessionId, DateTimeOffset seenAt, CancellationToken ct) =>
        await db.PlaySessions
            .Where(row => row.Id == sessionId && row.EndedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.UpdatedAt, seenAt), ct) > 0;

    private Task<List<OpenSession>> OpenSessions(
        Expression<Func<PlaySession, bool>> filter,
        CancellationToken ct) =>
        db.PlaySessions
            .AsNoTracking()
            .Where(row => row.EndedAt == null)
            .Where(filter)
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.Id)
            .Select(row => new OpenSession(row.Id, row.GameId, row.PlatformId, row.UpdatedAt))
            .ToListAsync(ct);

    private async Task<int> EndWhere(
        Expression<Func<PlaySession, bool>> filter,
        DateTimeOffset endedAt,
        TimeSpan? lastSeenGrace,
        CancellationToken ct)
    {
        var sessions = await OpenSessions(filter, ct);
        return sessions.Count == 0
            ? 0
            : await End(sessions.Select(row => row.Id).ToArray(), endedAt, lastSeenGrace, ct);
    }

    /// <summary>Ends at <paramref name="endedAt"/>, or <paramref name="lastSeenGrace"/> after the last sighting if earlier.</summary>
    private async Task<int> End(long[] sessionIds, DateTimeOffset endedAt, TimeSpan? lastSeenGrace, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ended = await db.Database.SqlQuery<EndedSessionRow>($"""
            UPDATE play_sessions ps
            SET ended_at = LEAST({endedAt}, ps.updated_at + CAST({lastSeenGrace} AS interval)),
                updated_at = {now}
            WHERE ps.id = ANY({sessionIds})
              AND ps.ended_at IS NULL
            RETURNING
                ps.id AS "Id",
                ps.integration_id AS "IntegrationId",
                ps.game_id AS "GameId",
                ps.started_at AS "StartedAt",
                ps.ended_at AS "EndedAt"
            """).ToListAsync(ct);
        Announce(ended);
        return ended.Count;
    }

    private Task<List<EndedSessionRow>> EndUnseen(
        string captureMethod,
        DateTimeOffset seenBefore,
        TimeSpan lastSeenGrace,
        DateTimeOffset now,
        CancellationToken ct) =>
        db.Database.SqlQuery<EndedSessionRow>($"""
            UPDATE play_sessions ps
            SET ended_at = LEAST({now}, ps.updated_at + {lastSeenGrace}),
                updated_at = {now}
            WHERE ps.ended_at IS NULL
              AND ps.capture_method = CAST({captureMethod} AS capture_method)
              AND ps.updated_at < {seenBefore}
            RETURNING
                ps.id AS "Id",
                ps.integration_id AS "IntegrationId",
                ps.game_id AS "GameId",
                ps.started_at AS "StartedAt",
                ps.ended_at AS "EndedAt"
            """).ToListAsync(ct);

    private Task<List<EndedSessionRow>> EndOverdueManualTimers(
        DateTimeOffset startedBefore,
        DateTimeOffset now,
        CancellationToken ct) =>
        db.Database.SqlQuery<EndedSessionRow>($"""
            UPDATE play_sessions ps
            SET ended_at = ps.started_at + {ManualTimerLimit},
                updated_at = {now}
            WHERE ps.ended_at IS NULL
              AND ps.capture_method = 'manual'
              AND ps.started_at < {startedBefore}
            RETURNING
                ps.id AS "Id",
                ps.integration_id AS "IntegrationId",
                ps.game_id AS "GameId",
                ps.started_at AS "StartedAt",
                ps.ended_at AS "EndedAt"
            """).ToListAsync(ct);

    private void Announce(IEnumerable<EndedSessionRow> ended)
    {
        foreach (var session in ended)
        {
            if (session.IntegrationId is { } integrationId)
            {
                _pending.Add(new SessionEnded(
                    session.Id, integrationId, session.GameId, session.StartedAt, session.EndedAt));
            }
        }
    }

    private async Task SafePublish<T>(T @event, CancellationToken ct)
    {
        try
        {
            await eventBus.Publish(@event, ct);
        }
        catch (Exception ex)
        {
            LogEventHandlerFailed(ex, typeof(T).Name);
        }
    }

    private sealed record OpenSession(long Id, long GameId, long PlatformId, DateTimeOffset UpdatedAt);
}

public sealed record CaptureOutcome(long SessionId, bool Started, int EndedSessionCount);

public sealed class EndedSessionRow
{
    public long Id { get; set; }
    public long? IntegrationId { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
}
