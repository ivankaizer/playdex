using Microsoft.EntityFrameworkCore;
using Api.Shared.Persistence;

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
                pc_platform.key AS Platform,
                game_platform.key AS GamePlatform,
                g.canonical_key AS GameCanonicalKey
            FROM play_sessions ps
            JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
            LEFT JOIN user_settings us ON us.user_id = pc.user_id
            JOIN games g ON g.id = ps.game_id
            JOIN platforms pc_platform ON pc_platform.id = pc.platform_id
            JOIN platforms game_platform ON game_platform.id = g.platform_id
            WHERE pc.user_id = {userId}
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
            JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
            LEFT JOIN user_settings us ON us.user_id = pc.user_id
            WHERE pc.user_id = {userId}
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
                pc_platform.key AS Platform,
                game_platform.key AS GamePlatform,
                g.canonical_key AS GameCanonicalKey
            FROM play_sessions ps
            JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
            LEFT JOIN user_settings us ON us.user_id = pc.user_id
            JOIN games g ON g.id = ps.game_id
            JOIN platforms pc_platform ON pc_platform.id = pc.platform_id
            JOIN platforms game_platform ON game_platform.id = g.platform_id
            WHERE pc.user_id = {userId}
              AND NOT (
                  COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                  AND ps.ended_at IS NOT NULL
                  AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                      < COALESCE(us.ignore_short_sessions_minutes, 0) * 60
              )
            GROUP BY
                ps.game_id, g.title, g.capsule_url,
                pc_platform.key, game_platform.key, g.canonical_key
            ORDER BY FirstStartedAt ASC
            """).ToListAsync(ct);
    }

    public async Task<List<ManualMappingRow>> LoadManualMappings(
        long userId, string[] canonicalTitles, CancellationToken ct)
    {
        if (canonicalTitles.Length == 0)
            return [];

        return await db.Database.SqlQuery<ManualMappingRow>($"""
            SELECT
                mgi.canonical_title AS CanonicalTitle,
                mgi.mapped_game_id AS MappedGameId,
                g.title AS MappedTitle,
                gp.key AS MappedPlatform,
                g.capsule_url AS MappedCapsuleUrl,
                g.canonical_key AS MappedCanonicalKey
            FROM manual_game_inputs mgi
            LEFT JOIN games g ON g.id = mgi.mapped_game_id
            LEFT JOIN platforms gp ON gp.id = g.platform_id
            WHERE mgi.user_id = {userId}
              AND mgi.canonical_title = ANY({canonicalTitles})
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
    public string Platform { get; set; } = string.Empty;
    public string GamePlatform { get; set; } = string.Empty;
    public string GameCanonicalKey { get; set; } = string.Empty;
}

public sealed class RecapFirstSessionRow
{
    public long GameId { get; set; }
    public DateTimeOffset FirstStartedAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CapsuleUrl { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string GamePlatform { get; set; } = string.Empty;
    public string GameCanonicalKey { get; set; } = string.Empty;
}

public sealed class ManualMappingRow
{
    public string CanonicalTitle { get; set; } = string.Empty;
    public long? MappedGameId { get; set; }
    public string? MappedTitle { get; set; }
    public string? MappedPlatform { get; set; }
    public string? MappedCapsuleUrl { get; set; }
    public string? MappedCanonicalKey { get; set; }
}
