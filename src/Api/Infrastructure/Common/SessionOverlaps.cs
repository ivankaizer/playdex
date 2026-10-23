using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Infrastructure.Common;

/// <summary>One of the user's sessions on a platform; <see cref="EndedAt"/> is null while it runs.</summary>
public sealed record PlatformSession(
    long SessionId,
    string Game,
    string Platform,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);

/// <summary>
/// Keeps sessions made by hand (manual entries, accepted AI drafts, edits) from overlapping the user's other sessions
/// on the same platform: one person plays one thing at a time there. Capture and polling are not checked. They record
/// play as it happens, by the server's clock, and a hand-made entry must not make them drop real play.
/// </summary>
public static class SessionOverlaps
{
    /// <summary>
    /// Holds the user's session lock until the transaction ends, so two hand-made writes cannot both claim the same
    /// free time. Manual live starts and stops take the same lock.
    /// </summary>
    public static Task LockUserSessions(this PlaydexDbContext db, long userId, CancellationToken ct)
    {
        var key = $"manual-session-user:{userId}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            ct);
    }

    /// <summary>
    /// The user's sessions on <paramref name="platformId"/> that overlap [<paramref name="rangeStart"/>,
    /// <paramref name="rangeEnd"/>), earliest first, except <paramref name="exceptSessionId"/>. A running session
    /// counts as running until <paramref name="now"/>.
    /// </summary>
    public static Task<List<PlatformSession>> ListPlatformSessions(
        this PlaydexDbContext db,
        long userId,
        long platformId,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        DateTimeOffset now,
        long? exceptSessionId,
        CancellationToken ct)
    {
        var except = exceptSessionId ?? 0;
        return (
                from ps in db.PlaySessions.AsNoTracking()
                join g in db.Games on ps.GameId equals g.Id
                join p in db.Platforms on ps.PlatformId equals p.Id
                where ps.UserId == userId
                    && ps.PlatformId == platformId
                    && ps.Id != except
                    && ps.StartedAt < rangeEnd
                    && (ps.EndedAt ?? now) > rangeStart
                orderby ps.StartedAt
                select new PlatformSession(ps.Id, g.Title, p.Name, ps.StartedAt, ps.EndedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The earliest start at or after <paramref name="dayStart"/> for a session of <paramref name="duration"/> that
    /// overlaps none of <paramref name="taken"/> (a running one taken until <paramref name="now"/>) and ends by
    /// <paramref name="dayEnd"/>; null when the day has no such gap.
    /// </summary>
    public static DateTimeOffset? FirstFreeStart(
        IEnumerable<PlatformSession> taken,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        TimeSpan duration,
        DateTimeOffset now)
    {
        var start = dayStart;
        foreach (var session in taken.OrderBy(session => session.StartedAt))
        {
            if (session.StartedAt >= start + duration)
            {
                break;
            }

            var end = session.EndedAt ?? now;
            if (end > start)
            {
                start = end;
            }
        }
        return start + duration <= dayEnd ? start : null;
    }

    /// <summary>"2 h 30 min", "45 min", "24 h".</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        var minutes = (int)Math.Round(duration.TotalMinutes);
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours == 0 ? $"{rest} min"
            : rest == 0 ? $"{hours} h"
            : $"{hours} h {rest} min";
    }

    /// <summary>
    /// Why a session cannot overlap <paramref name="session"/>, its times shown at <paramref name="offset"/> (the
    /// client's UTC offset when it sent one).
    /// </summary>
    public static string ConflictMessage(PlatformSession session, TimeSpan offset)
    {
        var started = session.StartedAt.ToOffset(offset);
        var startText = started.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string when;
        if (session.EndedAt is { } endedAt)
        {
            var ended = endedAt.ToOffset(offset);
            var endFormat = ended.Date == started.Date ? "HH:mm" : "yyyy-MM-dd HH:mm";
            when = $"from {startText} to {ended.ToString(endFormat, CultureInfo.InvariantCulture)}";
        }
        else
        {
            when = $"started {startText}, still running";
        }
        return $"This would overlap your {session.Game} session on {session.Platform} ({when}). "
            + "Sessions on the same platform cannot overlap.";
    }
}
