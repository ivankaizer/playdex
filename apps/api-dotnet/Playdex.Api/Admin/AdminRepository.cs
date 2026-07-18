using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Data;

namespace Playdex.Api.Admin;

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
                JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
                JOIN users u ON u.id = pc.user_id
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
            join credential in db.PlatformCredentials.AsNoTracking()
                on session.PlatformCredentialId equals credential.Id
            join user in db.Users.AsNoTracking() on credential.UserId equals user.Id
            join platform in db.Platforms.AsNoTracking() on credential.PlatformId equals platform.Id
            join game in db.Games.AsNoTracking() on session.GameId equals game.Id
            where EF.Functions.ILike(user.Email, likeQuery)
                || EF.Functions.ILike(game.Title, likeQuery)
                || (credential.AccountId != null
                    && EF.Functions.ILike(credential.AccountId!, likeQuery))
                || EF.Functions.ILike(platform.Key, likeQuery)
            select new
            {
                SessionId = session.Id,
                SourceCredentialId = credential.Id,
                UserEmail = user.Email,
                Platform = platform.Key,
                AccountUser = credential.AccountId,
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
            SET platform_credential_id = @TargetId, updated_at = NOW()
            WHERE id = ANY(@Ids) AND platform_credential_id != @TargetId
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
            FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            GROUP BY p.key
            ORDER BY p.key
            """).ToListAsync(ct);
    }

    public async Task<int> UpdatePlatformStatus(string platform, bool enabled, CancellationToken ct)
    {
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE platform_credentials
            SET active = {enabled}, updated_at = NOW()
            WHERE platform_id = (SELECT id FROM platforms WHERE key = {platform})
            """, ct);
    }

    public Task<bool> PlatformExists(string platform, CancellationToken ct) =>
        db.Platforms.AsNoTracking().AnyAsync(item => item.Key == platform, ct);

    public async Task<List<AdminCredentialRow>> ListCredentials(CancellationToken ct)
    {
        var rows = await db.PlatformCredentials
            .AsNoTracking()
            .Join(
                db.Users.AsNoTracking(),
                credential => credential.UserId,
                user => user.Id,
                (credential, user) => new { credential, user })
            .Join(
                db.Platforms.AsNoTracking(),
                value => value.credential.PlatformId,
                platform => platform.Id,
                (value, platform) => new
                {
                    value.credential.Id,
                    UserEmail = value.user.Email,
                    Platform = platform.Key,
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
        var credential = await db.PlatformCredentials.FirstOrDefaultAsync(item => item.Id == credentialId, ct);
        if (credential is null) return null;
        if (apiKeyChanged) credential.ApiKey = apiKey;
        if (accountUserChanged) credential.AccountId = accountUser;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await LoadCredentialRow(credentialId, ct);
    }

    private async Task<AdminCredentialRow?> LoadCredentialRow(long credentialId, CancellationToken ct)
    {
        var row = await db.PlatformCredentials
            .AsNoTracking()
            .Where(credential => credential.Id == credentialId)
            .Join(
                db.Users.AsNoTracking(),
                credential => credential.UserId,
                user => user.Id,
                (credential, user) => new { credential, user })
            .Join(
                db.Platforms.AsNoTracking(),
                value => value.credential.PlatformId,
                platform => platform.Id,
                (value, platform) => new
                {
                    value.credential.Id,
                    UserEmail = value.user.Email,
                    Platform = platform.Key,
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
                SELECT g.id, g.title, g.title_locked, p.key AS Platform,
                       g.capsule_url AS ImageUrl, g.capsule_url_locked AS ImageLocked
                FROM games g
                JOIN platforms p ON p.id = g.platform_id
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
            SELECT g.id, g.title, g.title_locked, p.key AS Platform,
                   g.capsule_url AS ImageUrl, g.capsule_url_locked AS ImageLocked
            FROM games g
            JOIN platforms p ON p.id = g.platform_id
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
        var row = await db.Games
            .AsNoTracking()
            .Where(game => game.Id == gameId)
            .Join(
                db.Platforms.AsNoTracking(),
                game => game.PlatformId,
                platform => platform.Id,
                (game, platform) => new { game, PlatformKey = platform.Key })
            .FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : new AdminGameUpdateRow(
                row.game.Id,
                row.game.Title,
                row.game.TitleLocked,
                row.game.CapsuleUrl,
                row.game.CapsuleUrlLocked,
                row.game.PlatformId,
                row.PlatformKey);
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
