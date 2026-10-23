using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Common;
using Playdex.Persistence;

namespace Api.Modules.Recap;

public sealed class RecapRepository(PlaydexDbContext db)
{
    public async Task<List<RecapSessionRow>> ListRecapSessions(
        long userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var sessions = CountedSessions(userId);
        if (from is { } fromValue)
        {
            sessions = sessions.Where(ps => ps.StartedAt >= fromValue);
        }

        if (to is { } toValue)
        {
            sessions = sessions.Where(ps => ps.StartedAt < toValue);
        }

        return await (
                from ps in sessions
                join g in db.Games on ps.GameId equals g.Id
                join p in db.Platforms on ps.PlatformId equals p.Id
                orderby ps.StartedAt
                select new RecapSessionRow
                {
                    Id = ps.Id,
                    StartedAt = ps.StartedAt,
                    EndedAt = ps.EndedAt,
                    GameId = g.Id,
                    Title = g.Title,
                    CapsuleUrl = g.CapsuleUrl,
                    PlatformKey = p.Key,
                    PlatformName = p.Name,
                })
            .ToListAsync(ct);
    }

    /// <summary>The calendar years of <paramref name="zone"/> with at least one counted session, latest first.</summary>
    public async Task<List<int>> ListRecapYears(long userId, TimeZoneInfo zone, CancellationToken ct)
    {
        // A UTC year spans at most two local years (offsets stay under a day), and local time only moves forward, so
        // the local years of its first and last session are exactly the local years it holds.
        var bounds = await CountedSessions(userId)
            .GroupBy(ps => ps.StartedAt.UtcDateTime.Year)
            .Select(year => new { First = year.Min(ps => ps.StartedAt), Last = year.Max(ps => ps.StartedAt) })
            .ToListAsync(ct);
        return bounds
            .SelectMany(year => new[] { LocalCalendar.DateOf(year.First, zone).Year, LocalCalendar.DateOf(year.Last, zone).Year })
            .Distinct()
            .OrderDescending()
            .ToList();
    }

    public async Task<List<RecapFirstSessionRow>> ListFirstSessionsByGame(
        long userId, CancellationToken ct)
    {
        return await (
                from ps in CountedSessions(userId)
                join g in db.Games on ps.GameId equals g.Id
                join p in db.Platforms on ps.PlatformId equals p.Id
                group ps.StartedAt by new { ps.GameId, g.Title, g.CapsuleUrl, p.Key, p.Name } into grouped
                select new RecapFirstSessionRow
                {
                    GameId = grouped.Key.GameId,
                    FirstStartedAt = grouped.Min(),
                    Title = grouped.Key.Title,
                    CapsuleUrl = grouped.Key.CapsuleUrl,
                    PlatformKey = grouped.Key.Key,
                    PlatformName = grouped.Key.Name,
                })
            .OrderBy(row => row.FirstStartedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The sessions a recap counts: not those the ignore-short setting hides, including a running session that has
    /// not yet lasted that long (the recap counts a running session up to now).
    /// </summary>
    private IQueryable<PlaySession> CountedSessions(long userId) =>
        db.ClassifiedPlaySessions(runningUntil: DateTimeOffset.UtcNow)
            .Where(row => row.Session.UserId == userId && !row.Ignored)
            .Select(row => row.Session);

    public async Task<List<(long GameId, long ConnectedGameId)>> ListConnectionEdges(
        long userId, CancellationToken ct)
    {
        var edges = await db.GameConnections
            .Where(gc => gc.UserId == userId)
            .Select(gc => new { gc.GameId, gc.ConnectedGameId })
            .ToListAsync(ct);

        return edges.Select(e => (e.GameId, e.ConnectedGameId)).ToList();
    }
}

public sealed class RecapSessionRow
{
    public long Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long GameId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CapsuleUrl { get; set; }
    public string PlatformKey { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
}

public sealed class RecapFirstSessionRow
{
    public long GameId { get; set; }
    public DateTimeOffset FirstStartedAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CapsuleUrl { get; set; }
    public string PlatformKey { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
}
