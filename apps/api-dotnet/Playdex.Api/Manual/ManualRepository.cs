using Microsoft.EntityFrameworkCore;
using Playdex.Api.Common;
using Playdex.Api.Data;
using Playdex.Api.Http;

namespace Playdex.Api.Manual;

public sealed class ManualRepository(PlaydexDbContext db)
{
    public async Task<List<ManualInputRow>> ListManualInputs(long userId, CancellationToken ct)
    {
        return await db.Database.SqlQuery<ManualInputRow>($"""
            SELECT
                mgi.id AS Id,
                mgi.raw_title AS RawTitle,
                mgi.canonical_title AS CanonicalTitle,
                mgi.mapped_game_id AS MappedGameId,
                g.id AS GameId,
                g.title AS GameTitle,
                g.capsule_url AS CapsuleUrl,
                g.canonical_key AS CanonicalKey,
                p.key AS Platform
            FROM manual_game_inputs mgi
            LEFT JOIN games g ON g.id = mgi.mapped_game_id
            LEFT JOIN platforms p ON p.id = g.platform_id
            WHERE mgi.user_id = {userId}
            ORDER BY mgi.updated_at DESC
            """).ToListAsync(ct);
    }

    public async Task<T> ExecuteInTransaction<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<long?> GetManualCredential(long userId, CancellationToken ct)
    {
        return await db.PlatformCredentials
            .AsNoTracking()
            .Where(pc => pc.UserId == userId)
            .Join(
                db.Platforms.Where(platform => platform.Key == "manual"),
                credential => credential.PlatformId,
                platform => platform.Id,
                (credential, _) => (long?)credential.Id)
            .FirstOrDefaultAsync(ct);
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
            ApiKey = "manual",
            AccountId = "manual",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.PlatformCredentials.Add(credential);
        await db.SaveChangesAsync(ct);
        return credential.Id;
    }

    public async Task<ManualInputLookupRow> UpsertManualGameInput(
        long userId,
        string rawTitle,
        string canonicalTitle,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var input = await db.ManualGameInputs
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.CanonicalTitle == canonicalTitle,
                ct);
        if (input is null)
        {
            input = new ManualGameInput
            {
                UserId = userId,
                RawTitle = rawTitle,
                CanonicalTitle = canonicalTitle,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ManualGameInputs.Add(input);
        }
        else
        {
            input.RawTitle = rawTitle;
            input.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return new ManualInputLookupRow { Id = input.Id, MappedGameId = input.MappedGameId };
    }

    public async Task<int> EndActiveSessions(long platformCredentialId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE play_sessions
            SET ended_at = {now}, updated_at = {now}
            WHERE platform_credential_id = {platformCredentialId} AND ended_at IS NULL
            """, ct);
    }

    public async Task<long> CreatePlaySession(
        long platformCredentialId, long gameId,
        DateTimeOffset startedAt, DateTimeOffset? endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new PlaySession
        {
            PlatformCredentialId = platformCredentialId,
            GameId = gameId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }

    public async Task<List<GameSearchRow>> SearchLocalGames(string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        return await db.Database.SqlQuery<GameSearchRow>($"""
            SELECT g.id, g.title, p.key AS platform, g.capsule_url
            FROM games g
            JOIN platforms p ON p.id = g.platform_id
            WHERE g.title ILIKE {pattern}
            ORDER BY g.updated_at DESC
            LIMIT {limit}
            """).ToListAsync(ct);
    }

    public async Task<long> UpsertGame(
        string title, string canonicalKey, string? externalId, long platformId,
        string? capsuleUrl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var game = await db.Games.FirstOrDefaultAsync(item => item.CanonicalKey == canonicalKey, ct);
        if (game is null)
        {
            game = new Game
            {
                Title = title,
                ExternalId = externalId,
                CanonicalKey = canonicalKey,
                PlatformId = platformId,
                CapsuleUrl = capsuleUrl,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Games.Add(game);
        }
        else
        {
            if (!game.TitleLocked) game.Title = title;
            if (externalId is not null) game.ExternalId = externalId;
            if (!game.CapsuleUrlLocked && capsuleUrl is not null) game.CapsuleUrl = capsuleUrl;
            game.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return game.Id;
    }

    public async Task<long?> GetPlatformIdByKey(string key, CancellationToken ct)
    {
        return await db.Platforms
            .AsNoTracking()
            .Where(p => p.Key == key)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> GetPlatformKeyByIgdbId(int igdbPlatformId, CancellationToken ct)
    {
        return await db.Platforms
            .AsNoTracking()
            .Where(platform => platform.IgdbPlatformId == igdbPlatformId)
            .Select(platform => platform.Key)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ManualInputLookupRow?> MapManualInput(
        long userId,
        long inputId,
        long gameId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var input = await db.ManualGameInputs
            .FirstOrDefaultAsync(item => item.Id == inputId && item.UserId == userId, ct);
        if (input is null) return null;

        input.MappedGameId = gameId;
        input.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new ManualInputLookupRow { Id = input.Id, MappedGameId = input.MappedGameId };
    }

    public async Task<Game?> GetGameEntity(long gameId, CancellationToken ct)
    {
        return await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct);
    }
}

public sealed class ManualInputRow
{
    public long Id { get; set; }
    public string RawTitle { get; set; } = string.Empty;
    public string CanonicalTitle { get; set; } = string.Empty;
    public long? MappedGameId { get; set; }
    public long? GameId { get; set; }
    public string? GameTitle { get; set; }
    public string? CapsuleUrl { get; set; }
    public string? CanonicalKey { get; set; }
    public string? Platform { get; set; }
}

public sealed class ManualInputLookupRow
{
    public long Id { get; set; }
    public long? MappedGameId { get; set; }
}

public sealed class GameSearchRow
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? CapsuleUrl { get; set; }
}
