using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Common;
using Playdex.Api.Data;
using Playdex.Api.Http;

namespace Playdex.Api.Manual;

public interface IManualRepository
{
    Task<List<ManualInputRow>> ListManualInputs(long userId, CancellationToken ct);
    Task<long> GetOrCreateManualCredential(long userId, CancellationToken ct);
    Task<long> UpsertManualGameInput(long userId, string rawTitle, string canonicalTitle, CancellationToken ct);
    Task<int> EndActiveSessions(long platformCredentialId, CancellationToken ct);
    Task<long> CreatePlaySession(long platformCredentialId, long gameId, DateTimeOffset startedAt, DateTimeOffset? endedAt, CancellationToken ct);
    Task<List<GameSearchRow>> SearchLocalGames(string query, int limit, CancellationToken ct);
    Task<long> UpsertGame(string title, string canonicalKey, long platformId, string? capsuleUrl, CancellationToken ct);
    Task<long?> GetPlatformIdByKey(string key, CancellationToken ct);
    Task MapManualInput(long inputId, long? gameId, CancellationToken ct);
    Task<Game?> GetGameEntity(long gameId, CancellationToken ct);
}

public sealed class ManualRepository(NpgsqlDataSource dataSource, PlaydexDbContext db) : IManualRepository
{
    public async Task<List<ManualInputRow>> ListManualInputs(long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ManualInputRow>("""
            SELECT
                mgi.id AS Id,
                mgi.raw_title AS RawTitle,
                mgi.canonical_title AS CanonicalTitle,
                mgi.mapped_game_id AS MappedGameId,
                g.id AS GameId,
                g.title AS GameTitle,
                g.capsule_url AS CapsuleUrl,
                p.name AS Platform
            FROM manual_game_inputs mgi
            LEFT JOIN games g ON g.id = mgi.mapped_game_id
            LEFT JOIN platforms p ON p.id = g.platform_id
            WHERE mgi.user_id = @UserId
            ORDER BY mgi.updated_at DESC
            """, new { UserId = userId });

        return rows.AsList();
    }

    public async Task<long> GetOrCreateManualCredential(long userId, CancellationToken ct)
    {
        var platformId = await db.Platforms
            .AsNoTracking()
            .Where(p => p.Key == "manual")
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (platformId is null)
            throw AppProblem.NotFound("Manual platform not found.");

        var existing = await db.PlatformCredentials
            .AsNoTracking()
            .Where(pc => pc.UserId == userId && pc.PlatformId == platformId.Value)
            .Select(pc => (long?)pc.Id)
            .FirstOrDefaultAsync(ct);

        if (existing.HasValue)
            return existing.Value;

        var now = DateTimeOffset.UtcNow;
        var credential = new PlatformCredential
        {
            UserId = userId,
            PlatformId = platformId.Value,
            ApiKey = null,
            AccountId = null,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.PlatformCredentials.Add(credential);
        await db.SaveChangesAsync(ct);
        return credential.Id;
    }

    public async Task<long> UpsertManualGameInput(long userId, string rawTitle, string canonicalTitle, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("""
            INSERT INTO manual_game_inputs (user_id, raw_title, canonical_title, created_at, updated_at)
            VALUES (@UserId, @RawTitle, @CanonicalTitle, @Now, @Now)
            ON CONFLICT (user_id, canonical_title) DO UPDATE SET
                raw_title = @RawTitle,
                updated_at = @Now
            RETURNING id
            """, new { UserId = userId, RawTitle = rawTitle, CanonicalTitle = canonicalTitle, Now = now });

        return id;
    }

    public async Task<int> EndActiveSessions(long platformCredentialId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var affected = await conn.ExecuteAsync("""
            UPDATE play_sessions
            SET ended_at = @Now, updated_at = @Now
            WHERE platform_credential_id = @CredId AND ended_at IS NULL
            """, new { CredId = platformCredentialId, Now = now });

        return affected;
    }

    public async Task<long> CreatePlaySession(
        long platformCredentialId, long gameId,
        DateTimeOffset startedAt, DateTimeOffset? endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("""
            INSERT INTO play_sessions (platform_credential_id, game_id, started_at, ended_at, created_at, updated_at)
            VALUES (@CredId, @GameId, @StartedAt, @EndedAt, @Now, @Now)
            RETURNING id
            """, new { CredId = platformCredentialId, GameId = gameId, StartedAt = startedAt, EndedAt = endedAt, Now = now });

        return id;
    }

    public async Task<List<GameSearchRow>> SearchLocalGames(string query, int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<GameSearchRow>("""
            SELECT g.id, g.title, p.name AS platform, g.capsule_url
            FROM games g
            JOIN platforms p ON p.id = g.platform_id
            WHERE g.title ILIKE @Query
            ORDER BY g.title
            LIMIT @Limit
            """, new { Query = $"%{query}%", Limit = limit });

        return rows.AsList();
    }

    public async Task<long> UpsertGame(
        string title, string canonicalKey, long platformId,
        string? capsuleUrl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("""
            INSERT INTO games (title, canonical_key, platform_id, capsule_url, created_at, updated_at)
            VALUES (@Title, @CanonicalKey, @PlatformId, @CapsuleUrl, @Now, @Now)
            ON CONFLICT (canonical_key) DO UPDATE SET
                title = CASE WHEN games.title_locked THEN games.title ELSE @Title END,
                capsule_url = CASE
                    WHEN games.capsule_url_locked THEN games.capsule_url
                    ELSE COALESCE(@CapsuleUrl, games.capsule_url)
                END,
                updated_at = @Now
            RETURNING id
            """, new
        {
            Title = title,
            CanonicalKey = canonicalKey,
            PlatformId = platformId,
            CapsuleUrl = capsuleUrl,
            Now = now
        });

        return id;
    }

    public async Task<long?> GetPlatformIdByKey(string key, CancellationToken ct)
    {
        return await db.Platforms
            .AsNoTracking()
            .Where(p => p.Key == key)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task MapManualInput(long inputId, long? gameId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("""
            UPDATE manual_game_inputs
            SET mapped_game_id = @GameId, updated_at = @Now
            WHERE id = @InputId
            """, new { InputId = inputId, GameId = gameId, Now = now });
    }

    public async Task<Game?> GetGameEntity(long gameId, CancellationToken ct)
    {
        return await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct);
    }
}

// ── Internal row types for Dapper mapping ──────────────────────

public sealed class ManualInputRow
{
    public long Id { get; set; }
    public required string RawTitle { get; set; }
    public required string CanonicalTitle { get; set; }
    public long? MappedGameId { get; set; }
    public long? GameId { get; set; }
    public string? GameTitle { get; set; }
    public string? CapsuleUrl { get; set; }
    public string? Platform { get; set; }
}

public sealed class GameSearchRow
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public required string Platform { get; set; }
    public string? CapsuleUrl { get; set; }
}
