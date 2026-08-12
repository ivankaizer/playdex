using Microsoft.EntityFrameworkCore;
using Npgsql;
using Api.Infrastructure.Persistence;

namespace Api.Modules.Admin;

public sealed class AdminRepository(PlaydexDbContext db)
{
    public async Task<List<AdminRecentSessionRow>> GetRecentSessions(CancellationToken ct)
    {
        return await db.Database.SqlQueryRaw<AdminRecentSessionRow>("""
            WITH ranked AS (
                SELECT u.id AS UserId, u.email AS Email, ps.id AS SessionId,
                       g.title AS GameName, ps.started_at AS StartedAt,
                       CASE WHEN ps.ended_at IS NOT NULL
                           THEN EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                           ELSE EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'utc' - ps.started_at))
                       END AS DurationSeconds,
                       ps.ended_at IS NULL AS Live,
                       ROW_NUMBER() OVER (PARTITION BY u.id ORDER BY ps.started_at DESC, ps.id DESC) AS rn
                FROM play_sessions ps
                JOIN users u ON u.id = ps.user_id
                JOIN games g ON g.id = ps.game_id
            )
            SELECT UserId, Email, SessionId, GameName, StartedAt, DurationSeconds, Live
            FROM ranked
            WHERE rn <= 3
            ORDER BY Email, StartedAt DESC, SessionId DESC
            """).ToListAsync(ct);
    }

    public async Task<(List<AdminMovableSessionRow> Sessions, int Total)> SearchSessions(
        string query, int limit, int offset, CancellationToken ct)
    {
        var likeQuery = $"%{query}%";
        var queryable =
            from session in db.PlaySessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UserId equals user.Id
            join platform in db.Platforms.AsNoTracking() on session.PlatformId equals platform.Id
            join integration in db.Integrations.AsNoTracking() on session.IntegrationId equals integration.Id into integrations
            from credential in integrations.DefaultIfEmpty()
            join game in db.Games.AsNoTracking() on session.GameId equals game.Id
            where EF.Functions.ILike(user.Email, likeQuery)
                || EF.Functions.ILike(game.Title, likeQuery)
                || (credential.AccountId != null
                    && EF.Functions.ILike(credential.AccountId!, likeQuery))
                || EF.Functions.ILike(platform.Key, likeQuery)
            select new
            {
                SessionId = session.Id,
                SourceCredentialId = session.IntegrationId ?? 0,
                UserEmail = user.Email,
                Platform = platform.Key,
                AccountUser = credential == null ? null : credential.AccountId,
                GameName = game.Title,
                session.StartedAt,
                session.EndedAt,
            };

        var total = await queryable.CountAsync(ct);
        var rows = await queryable
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.SessionId)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var sessions = rows.Select(row => new AdminMovableSessionRow(
            row.SessionId,
            row.SourceCredentialId,
            row.UserEmail,
            row.Platform,
            row.AccountUser,
            row.GameName,
            row.StartedAt,
            row.EndedAt,
            ((row.EndedAt ?? now) - row.StartedAt).TotalSeconds,
            row.EndedAt is null)).ToList();
        return (sessions, total);
    }

    public async Task<int> MoveSessions(long[] sessionIds, long targetCredentialId, CancellationToken ct)
    {
        return await db.Database.ExecuteSqlRawAsync("""
            UPDATE play_sessions
            SET integration_id = i.id, user_id = i.user_id,
                platform_id = COALESCE(i.platform_id, play_sessions.platform_id), updated_at = NOW()
            FROM integrations i
            WHERE i.id = @TargetId
              AND play_sessions.id = ANY(@Ids)
              AND play_sessions.integration_id IS DISTINCT FROM @TargetId
            """,
            new NpgsqlParameter("TargetId", targetCredentialId),
            new NpgsqlParameter("Ids", sessionIds));
    }

    public async Task<List<AdminPlatformStatusRow>> GetPlatformStatuses(CancellationToken ct)
    {
        return await db.Database.SqlQueryRaw<AdminPlatformStatusRow>("""
            SELECT p.key AS Platform,
                   COUNT(*) AS TotalCredentials,
                   COUNT(*) FILTER (WHERE pc.active) AS ActiveCredentials
            FROM integrations pc
            JOIN platforms p ON p.id = pc.platform_id
            GROUP BY p.key
            ORDER BY p.key
            """).ToListAsync(ct);
    }

    public async Task<int> UpdatePlatformStatus(string platform, bool enabled, CancellationToken ct)
    {
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE integrations
            SET active = {enabled}, updated_at = NOW()
            WHERE platform_id = (SELECT id FROM platforms WHERE key = {platform})
            """, ct);
    }

    public Task<bool> PlatformExists(string platform, CancellationToken ct) =>
        db.Platforms.AsNoTracking().AnyAsync(item => item.Key == platform, ct);

    public async Task<List<AdminCredentialRow>> ListCredentials(CancellationToken ct)
    {
        var rows = await db.Integrations
            .AsNoTracking()
            .Join(
                db.Users.AsNoTracking(),
                credential => credential.UserId,
                user => user.Id,
                (credential, user) => new { credential, user })
            .Select(value => new
            {
                value.credential.Id,
                UserEmail = value.user.Email,
                Platform = value.credential.Type == IntegrationType.Api ? "api" : value.credential.Type.ToString().ToLowerInvariant(),
                value.credential.ApiKey,
                AccountUser = value.credential.AccountId,
                value.credential.Active,
            })
            .OrderBy(row => row.UserEmail)
            .ThenBy(row => row.Platform)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);
        return rows.Select(row => new AdminCredentialRow(
            row.Id,
            row.UserEmail,
            row.Platform,
            row.ApiKey,
            row.AccountUser,
            row.Active)).ToList();
    }

    public async Task<AdminCredentialRow?> GetCredential(long credentialId, CancellationToken ct) =>
        await LoadCredentialRow(credentialId, ct);

    public async Task<AdminCredentialRow?> UpdateCredential(
        long credentialId,
        bool apiKeyChanged,
        string? apiKey,
        bool accountUserChanged,
        string? accountUser,
        CancellationToken ct)
    {
        var credential = await db.Integrations.FirstOrDefaultAsync(item => item.Id == credentialId, ct);
        if (credential is null) return null;
        if (apiKeyChanged) credential.ApiKey = apiKey;
        if (accountUserChanged) credential.AccountId = accountUser;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await LoadCredentialRow(credentialId, ct);
    }

    private async Task<AdminCredentialRow?> LoadCredentialRow(long credentialId, CancellationToken ct)
    {
        var row = await db.Integrations
            .AsNoTracking()
            .Where(credential => credential.Id == credentialId)
            .Join(
                db.Users.AsNoTracking(),
                credential => credential.UserId,
                user => user.Id,
                (credential, user) => new { credential, user })
            .Select(value => new
            {
                value.credential.Id,
                UserEmail = value.user.Email,
                Platform = value.credential.Type == IntegrationType.Api ? "api" : value.credential.Type.ToString().ToLowerInvariant(),
                value.credential.ApiKey,
                AccountUser = value.credential.AccountId,
                value.credential.Active,
            })
            .FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : new AdminCredentialRow(
                row.Id,
                row.UserEmail,
                row.Platform,
                row.ApiKey,
                row.AccountUser,
                row.Active);
    }

    public async Task<List<AdminGameSearchRow>> SearchGames(string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var (exactId, _) = TryParseGameId(query);

        if (exactId.HasValue)
        {
            return await db.Database.SqlQueryRaw<AdminGameSearchRow>("""
                SELECT g.id, g.title, g.title_locked, 'unknown'::text AS Platform,
                       g.capsule_url AS ImageUrl, g.capsule_url_locked AS ImageLocked
                FROM games g
                WHERE g.title ILIKE @Query OR g.capsule_url ILIKE @Query OR g.id = @ExactId
                ORDER BY g.updated_at DESC
                LIMIT @Limit
                """,
                new NpgsqlParameter("Query", pattern),
                new NpgsqlParameter("ExactId", exactId.Value),
                new NpgsqlParameter("Limit", limit))
                .ToListAsync(ct);
        }

        return await db.Database.SqlQueryRaw<AdminGameSearchRow>("""
            SELECT g.id, g.title, g.title_locked, 'unknown'::text AS Platform,
                   g.capsule_url AS ImageUrl, g.capsule_url_locked AS ImageLocked
            FROM games g
            WHERE g.title ILIKE @Query OR g.capsule_url ILIKE @Query
            ORDER BY g.updated_at DESC
            LIMIT @Limit
            """,
            new NpgsqlParameter("Query", pattern),
            new NpgsqlParameter("Limit", limit))
            .ToListAsync(ct);
    }

    public async Task<AdminGameUpdateRow?> UpdateGame(
        long gameId,
        bool titleChanged,
        string title,
        bool imageUrlChanged,
        string? imageUrl,
        bool lockTitleChanged,
        bool lockTitle,
        bool lockImageChanged,
        bool lockImage,
        CancellationToken ct)
    {
        var game = await db.Games.FirstOrDefaultAsync(item => item.Id == gameId, ct);
        if (game is null) return null;
        if (titleChanged) game.Title = title;
        if (lockTitleChanged) game.TitleLocked = lockTitle;
        else if (titleChanged) game.TitleLocked = true;
        if (imageUrlChanged) game.CapsuleUrl = imageUrl;
        if (lockImageChanged) game.CapsuleUrlLocked = lockImage;
        else if (imageUrlChanged) game.CapsuleUrlLocked = true;
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetGameById(gameId, ct);
    }

    public async Task<AdminGameUpdateRow?> GetGameById(long gameId, CancellationToken ct)
    {
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(game => game.Id == gameId, ct);
        return game is null
            ? null
            : new AdminGameUpdateRow(
                game.Id, game.Title, game.TitleLocked, game.CapsuleUrl, game.CapsuleUrlLocked, 0, null);
    }

    private static (long? Id, bool IsEncoded) TryParseGameId(string value)
    {
        if (long.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var rawId) && rawId > 0)
        {
            return (rawId, false);
        }

        var decoded = Api.Infrastructure.Common.GameId.Decode(value);
        if (decoded.HasValue)
        {
            return (decoded, true);
        }

        return (null, false);
    }
}
