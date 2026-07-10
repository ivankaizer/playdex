using Dapper;
using Npgsql;
using Playdex.Api.Data;

namespace Playdex.Api.Admin;

public interface IAdminRepository
{
    Task<List<AdminRecentSessionRow>> GetRecentSessions(CancellationToken ct);
    Task<(List<AdminMovableSessionRow> Sessions, int Total)> SearchSessions(string query, int limit, int offset, CancellationToken ct);
    Task<int> MoveSessions(long[] sessionIds, long targetCredentialId, CancellationToken ct);
    Task<List<AdminPlatformStatusRow>> GetPlatformStatuses(CancellationToken ct);
    Task<int> UpdatePlatformStatus(string platform, bool enabled, CancellationToken ct);
    Task<List<AdminCredentialRow>> ListCredentials(CancellationToken ct);
    Task<AdminCredentialRow?> UpdateCredential(long credentialId, string? apiKey, string? accountUser, CancellationToken ct);
    Task<List<AdminGameSearchRow>> SearchGames(string query, int limit, CancellationToken ct);
    Task<AdminGameUpdateRow?> UpdateGame(long gameId, string? title, string? imageUrl, bool? lockTitle, bool? lockImage, CancellationToken ct);
    Task<AdminGameUpdateRow?> GetGameById(long gameId, CancellationToken ct);
}

public sealed class AdminRepository(NpgsqlDataSource dataSource) : IAdminRepository
{
    public async Task<List<AdminRecentSessionRow>> GetRecentSessions(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AdminRecentSessionRow>("""
            WITH ranked AS (
                SELECT u.id AS UserId, u.email AS Email, ps.id AS SessionId,
                       g.title AS GameName, ps.started_at AS StartedAt,
                       CASE WHEN ps.ended_at IS NOT NULL
                           THEN EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                           ELSE EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'utc' - ps.started_at))
                       END AS DurationSeconds,
                       ps.ended_at IS NULL AS Live,
                       ROW_NUMBER() OVER (PARTITION BY u.id ORDER BY ps.id DESC) AS rn
                FROM play_sessions ps
                JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
                JOIN users u ON u.id = pc.user_id
                JOIN games g ON g.id = ps.game_id
            )
            SELECT UserId, Email, SessionId, GameName, StartedAt, DurationSeconds, Live
            FROM ranked
            WHERE rn <= 3
            ORDER BY UserId, StartedAt DESC
            """);

        return rows.AsList();
    }

    public async Task<(List<AdminMovableSessionRow> Sessions, int Total)> SearchSessions(
        string query, int limit, int offset, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var likeQuery = $"%{query}%";

        var baseSql = """
            FROM play_sessions ps
            JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
            JOIN users u ON u.id = pc.user_id
            JOIN platforms p ON p.id = pc.platform_id
            JOIN games g ON g.id = ps.game_id
            WHERE u.email ILIKE @Query OR g.title ILIKE @Query OR p.key ILIKE @Query
            """;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) {baseSql}", new { Query = likeQuery });

        var sessions = await conn.QueryAsync<AdminMovableSessionRow>($"""
            SELECT ps.id AS SessionId, pc.id AS SourceCredentialId,
                   u.email AS UserEmail, p.key AS Platform,
                   pc.account_id AS AccountUser, g.title AS GameName,
                   ps.started_at AS StartedAt, ps.ended_at AS EndedAt,
                   CASE WHEN ps.ended_at IS NOT NULL
                       THEN EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                       ELSE EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'utc' - ps.started_at))
                   END AS DurationSeconds,
                   ps.ended_at IS NULL AS Live
            {baseSql}
            ORDER BY ps.id DESC
            OFFSET @Offset LIMIT @Limit
            """, new { Query = likeQuery, Offset = offset, Limit = limit });

        return (sessions.AsList(), total);
    }

    public async Task<int> MoveSessions(long[] sessionIds, long targetCredentialId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync("""
            UPDATE play_sessions
            SET platform_credential_id = @TargetId, updated_at = NOW()
            WHERE id = ANY(@Ids) AND platform_credential_id != @TargetId
            """, new { Ids = sessionIds, TargetId = targetCredentialId });

        return affected;
    }

    public async Task<List<AdminPlatformStatusRow>> GetPlatformStatuses(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AdminPlatformStatusRow>("""
            SELECT p.key AS Platform,
                   COUNT(*) AS TotalCredentials,
                   COUNT(*) FILTER (WHERE pc.active) AS ActiveCredentials
            FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            GROUP BY p.key
            ORDER BY p.key
            """);

        return rows.AsList();
    }

    public async Task<int> UpdatePlatformStatus(string platform, bool enabled, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync("""
            UPDATE platform_credentials
            SET active = @Enabled, updated_at = NOW()
            WHERE platform_id = (SELECT id FROM platforms WHERE key = @Platform)
            """, new { Platform = platform, Enabled = enabled });

        return affected;
    }

    public async Task<List<AdminCredentialRow>> ListCredentials(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AdminCredentialRow>("""
            SELECT pc.id AS Id, u.email AS UserEmail, p.key AS Platform,
                   pc.api_key AS ApiKey, pc.account_id AS AccountUser, pc.active AS Active
            FROM platform_credentials pc
            JOIN users u ON u.id = pc.user_id
            JOIN platforms p ON p.id = pc.platform_id
            ORDER BY u.email, p.key
            """);

        return rows.AsList();
    }

    public async Task<AdminCredentialRow?> UpdateCredential(
        long credentialId, string? apiKey, string? accountUser, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<AdminCredentialRow>("""
            UPDATE platform_credentials
            SET api_key = COALESCE(@ApiKey, api_key),
                account_id = COALESCE(@AccountUser, account_id),
                updated_at = NOW()
            WHERE id = @Id
            RETURNING id
            """, new { Id = credentialId, ApiKey = apiKey, AccountUser = accountUser });

        if (row is null) return null;

        // Fetch the full credential row with joined data after update
        return await conn.QueryFirstOrDefaultAsync<AdminCredentialRow>("""
            SELECT pc.id AS Id, u.email AS UserEmail, p.key AS Platform,
                   pc.api_key AS ApiKey, pc.account_id AS AccountUser, pc.active AS Active
            FROM platform_credentials pc
            JOIN users u ON u.id = pc.user_id
            JOIN platforms p ON p.id = pc.platform_id
            WHERE pc.id = @Id
            """, new { Id = credentialId });
    }

    public async Task<List<AdminGameSearchRow>> SearchGames(string query, int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        // Try exact game ID match first (encoded or raw)
        var (exactId, _) = TryParseGameId(query);

        var sql = """
            SELECT g.id, g.title, g.title_locked, p.key AS Platform,
                   g.capsule_url AS ImageUrl, g.capsule_url_locked AS ImageLocked
            FROM games g
            JOIN platforms p ON p.id = g.platform_id
            WHERE g.title ILIKE @Query OR g.capsule_url ILIKE @Query
            """;

        if (exactId.HasValue)
        {
            sql += " OR g.id = @ExactId";
        }

        sql += "\nORDER BY g.title\nLIMIT @Limit";

        var rows = await conn.QueryAsync<AdminGameSearchRow>(sql,
            new { Query = $"%{query}%", ExactId = exactId, Limit = limit });

        return rows.AsList();
    }

    public async Task<AdminGameUpdateRow?> UpdateGame(
        long gameId, string? title, string? imageUrl,
        bool? lockTitle, bool? lockImage, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<AdminGameUpdateRow>("""
            UPDATE games
            SET title = COALESCE(@Title, title),
                capsule_url = COALESCE(@ImageUrl, capsule_url),
                title_locked = COALESCE(@LockTitle, title_locked),
                capsule_url_locked = COALESCE(@LockImage, capsule_url_locked),
                updated_at = NOW()
            WHERE id = @Id
            RETURNING id, title, title_locked, capsule_url, capsule_url_locked, platform_id
            """, new
            {
                Id = gameId,
                Title = title,
                ImageUrl = imageUrl,
                LockTitle = lockTitle,
                LockImage = lockImage,
            });

        if (row is null) return null;

        // Load platform key and set it on the returned row
        var platformKey = await conn.ExecuteScalarAsync<string>(
            "SELECT key FROM platforms WHERE id = @Id", new { Id = row.PlatformId });

        return row with { PlatformKey = platformKey };
    }

    public async Task<AdminGameUpdateRow?> GetGameById(long gameId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<AdminGameUpdateRow>("""
            SELECT g.id, g.title, g.title_locked, g.capsule_url, g.capsule_url_locked, g.platform_id,
                   p.key AS PlatformKey
            FROM games g
            JOIN platforms p ON p.id = g.platform_id
            WHERE g.id = @Id
            """, new { Id = gameId });

        return row;
    }

    private static (long? Id, bool IsEncoded) TryParseGameId(string value)
    {
        if (long.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var rawId) && rawId > 0)
        {
            return (rawId, false);
        }

        var decoded = Playdex.Api.Common.GameId.Decode(value);
        if (decoded.HasValue)
        {
            return (decoded, true);
        }

        return (null, false);
    }
}
