using Microsoft.EntityFrameworkCore;
using Shared.Persistence;

namespace Api.Modules.Recap;

public sealed class RecapRepository(PlaydexDbContext db)
{
    public async Task<List<RecapSessionRow>> ListRecapSessions(
        long userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        return await db.Database.SqlQuery<RecapSessionRow>($"""
            SELECT
                ps.id AS Id,
                ps.started_at AS StartedAt,
                ps.ended_at AS EndedAt,
                g.id AS GameId,
                g.title AS Title,
                g.capsule_url AS CapsuleUrl,
                session_platform.key AS PlatformKey,
                session_platform.name AS PlatformName
            FROM play_sessions ps
            LEFT JOIN user_settings us ON us.user_id = ps.user_id
            JOIN games g ON g.id = ps.game_id
            JOIN platforms session_platform ON session_platform.id = ps.platform_id
            WHERE ps.user_id = {userId}
              AND NOT (
                  COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                  AND ps.ended_at IS NOT NULL
                  AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                      < COALESCE(us.ignore_short_sessions_minutes, 0) * 60
              )
              AND (CAST({from} AS timestamptz) IS NULL OR ps.started_at >= {from})
              AND (CAST({to} AS timestamptz) IS NULL OR ps.started_at < {to})
            ORDER BY ps.started_at ASC
            """).ToListAsync(ct);
    }

    public async Task<List<int>> ListRecapYears(long userId, CancellationToken ct)
    {
        return await db.Database.SqlQuery<int>($"""
            SELECT DISTINCT
                EXTRACT(YEAR FROM ps.started_at AT TIME ZONE 'utc')::int AS Year
            FROM play_sessions ps
            LEFT JOIN user_settings us ON us.user_id = ps.user_id
            WHERE ps.user_id = {userId}
              AND NOT (
                  COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                  AND ps.ended_at IS NOT NULL
                  AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                      < COALESCE(us.ignore_short_sessions_minutes, 0) * 60
              )
            ORDER BY 1 DESC
            """).ToListAsync(ct);
    }

    public async Task<List<RecapFirstSessionRow>> ListFirstSessionsByGame(
        long userId, CancellationToken ct)
    {
        return await db.Database.SqlQuery<RecapFirstSessionRow>($"""
            SELECT
                ps.game_id AS GameId,
                MIN(ps.started_at) AS FirstStartedAt,
                g.title AS Title,
                g.capsule_url AS CapsuleUrl,
                session_platform.key AS PlatformKey,
                session_platform.name AS PlatformName
            FROM play_sessions ps
            LEFT JOIN user_settings us ON us.user_id = ps.user_id
            JOIN games g ON g.id = ps.game_id
            JOIN platforms session_platform ON session_platform.id = ps.platform_id
            WHERE ps.user_id = {userId}
              AND NOT (
                  COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                  AND ps.ended_at IS NOT NULL
                  AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                      < COALESCE(us.ignore_short_sessions_minutes, 0) * 60
              )
            GROUP BY
                ps.game_id, g.title, g.capsule_url,
                session_platform.key, session_platform.name
            ORDER BY FirstStartedAt ASC
            """).ToListAsync(ct);
    }

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
