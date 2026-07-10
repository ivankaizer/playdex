using Dapper;
using Npgsql;

namespace Playdex.Api.Recap;

public interface IRecapRepository
{
    Task<List<RecapSessionRow>> ListRecapSessions(
        long userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);

    Task<List<int>> ListRecapYears(long userId, CancellationToken ct);

    Task<List<RecapFirstSessionRow>> ListFirstSessionsByGame(
        long userId, CancellationToken ct);

    Task<List<ManualMappingRow>> LoadManualMappings(
        long userId, string[] canonicalTitles, CancellationToken ct);

    Task<List<(long GameId, long ConnectedGameId)>> ListConnectionEdges(
        long userId, CancellationToken ct);
}

public sealed class RecapRepository(NpgsqlDataSource dataSource) : IRecapRepository
{
    public async Task<List<RecapSessionRow>> ListRecapSessions(
        long userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecapSessionRow>("""
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
            WHERE pc.user_id = @UserId
              AND NOT (
                  COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                  AND ps.ended_at IS NOT NULL
                  AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                      < COALESCE(us.ignore_short_sessions_minutes, 0) * 60
              )
              AND (@From IS NULL OR ps.started_at >= @From)
              AND (@To IS NULL OR ps.started_at < @To)
            ORDER BY ps.started_at ASC
            """, new { UserId = userId, From = from, To = to });

        return rows.AsList();
    }

    public async Task<List<int>> ListRecapYears(long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<int>("""
            SELECT DISTINCT
                EXTRACT(YEAR FROM ps.started_at AT TIME ZONE 'utc')::int AS Year
            FROM play_sessions ps
            JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
            LEFT JOIN user_settings us ON us.user_id = pc.user_id
            WHERE pc.user_id = @UserId
              AND NOT (
                  COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                  AND ps.ended_at IS NOT NULL
                  AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                      < COALESCE(us.ignore_short_sessions_minutes, 0) * 60
              )
            ORDER BY 1 DESC
            """, new { UserId = userId });

        return rows.AsList();
    }

    public async Task<List<RecapFirstSessionRow>> ListFirstSessionsByGame(
        long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<RecapFirstSessionRow>("""
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
            WHERE pc.user_id = @UserId
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
            """, new { UserId = userId });

        return rows.AsList();
    }

    public async Task<List<ManualMappingRow>> LoadManualMappings(
        long userId, string[] canonicalTitles, CancellationToken ct)
    {
        if (canonicalTitles.Length == 0)
            return [];

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ManualMappingRow>("""
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
            WHERE mgi.user_id = @UserId
              AND mgi.canonical_title = ANY(@Titles)
            """, new { UserId = userId, Titles = canonicalTitles });

        return rows.AsList();
    }

    public async Task<List<(long GameId, long ConnectedGameId)>> ListConnectionEdges(
        long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long, long)>(
            "SELECT game_id, connected_game_id FROM game_connections WHERE user_id = @UserId",
            new { UserId = userId });

        return rows.AsList();
    }
}

// ── Internal row types for Dapper mapping ──────────────────────

public sealed class RecapSessionRow
{
    public long Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long GameId { get; set; }
    public required string Title { get; set; }
    public string? CapsuleUrl { get; set; }
    public required string Platform { get; set; }
    public required string GamePlatform { get; set; }
    public required string GameCanonicalKey { get; set; }
}

public sealed class RecapFirstSessionRow
{
    public long GameId { get; set; }
    public DateTimeOffset FirstStartedAt { get; set; }
    public required string Title { get; set; }
    public string? CapsuleUrl { get; set; }
    public required string Platform { get; set; }
    public required string GamePlatform { get; set; }
    public required string GameCanonicalKey { get; set; }
}

public sealed class ManualMappingRow
{
    public required string CanonicalTitle { get; set; }
    public long? MappedGameId { get; set; }
    public string? MappedTitle { get; set; }
    public string? MappedPlatform { get; set; }
    public string? MappedCapsuleUrl { get; set; }
    public string? MappedCanonicalKey { get; set; }
}
