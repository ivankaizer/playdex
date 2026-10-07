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
            sessions = sessions.Where(ps => ps.StartedAt >= fromValue);
        if (to is { } toValue)
            sessions = sessions.Where(ps => ps.StartedAt < toValue);

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

    public async Task<List<int>> ListRecapYears(long userId, CancellationToken ct)
    {
        return await CountedSessions(userId)
            .Select(ps => ps.StartedAt.UtcDateTime.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(ct);
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

    private IQueryable<PlaySession> CountedSessions(long userId) =>
        db.ClassifiedPlaySessions()
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
