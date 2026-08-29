using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Providers;

public sealed class ProvidersRepository(PlaydexDbContext db)
{
    public const int MaxIntegrationsPerType = 3;

    public async Task<List<ProviderStatusDto>> ListProviders(long userId, CancellationToken ct)
    {
        var rows = await db.Integrations
            .AsNoTracking()
            .Where(credential => credential.UserId == userId)
            .OrderBy(credential => credential.Id)
            .Select(credential => new
            {
                credential.Id,
                Platform = credential.Type == IntegrationType.Steam ? "steam" :
                        credential.Type == IntegrationType.Psn ? "psn" :
                        credential.Type == IntegrationType.Xbox ? "xbox" : "api",
                credential.AccountId,
                Status = credential.Active ? "active" : "paused",
                credential.ApiKey,
                ApiKeyLocked = credential.ApiKeyLocked || credential.Type == IntegrationType.Api,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ProviderStatusDto(
            r.Id, r.Platform, r.AccountId, r.Status, r.ApiKey, r.ApiKeyLocked)).ToList();
    }

    public async Task<ProviderCreateResult> CreateProvider(
        long userId,
        string platform,
        IntegrationType type,
        string apiKey,
        string? accountId,
        CancellationToken ct)
    {
        var existingCount = await db.Integrations.AsNoTracking()
            .CountAsync(item => item.UserId == userId && item.Type == type, ct);
        if (existingCount >= MaxIntegrationsPerType)
        {
            return new ProviderCreateResult(null, LimitReached: true);
        }

        var platformId = await db.Platforms
            .AsNoTracking()
            .Where(item => item.Key == platform)
            .Select(item => (long?)item.Id)
            .FirstOrDefaultAsync(ct);
        if (!platformId.HasValue)
            return new ProviderCreateResult(null, LimitReached: false);

        var now = DateTimeOffset.UtcNow;
        var integration = new Integration
        {
            UserId = userId,
            Type = type,
            PlatformId = platformId.Value,
            ApiKey = apiKey,
            AccountId = accountId,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync(ct);

        return new ProviderCreateResult(
            new ProviderStatusDto(
                integration.Id,
                platform,
                integration.AccountId,
                "active",
                integration.ApiKey,
                integration.ApiKeyLocked),
            LimitReached: false);
    }

    public async Task<ProviderStatusDto?> UpdateProviderStatus(long userId, long providerId, string status, CancellationToken ct)
    {
        var credential = await db.Integrations
            .FirstOrDefaultAsync(
                item => item.Id == providerId && item.UserId == userId,
                ct);
        if (credential is null) return null;

        credential.Active = status == "active";
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var platform = credential.PlatformId is null ? "api" : await db.Platforms
            .AsNoTracking()
            .Where(item => item.Id == credential.PlatformId.Value)
            .Select(item => item.Key)
            .FirstAsync(ct);
        return new ProviderStatusDto(
            credential.Id,
            platform,
            credential.AccountId,
            credential.Active ? "active" : "paused",
            credential.ApiKey,
            credential.ApiKeyLocked || credential.Type == IntegrationType.Api);
    }

    public async Task<ProviderStatusDto?> UpdateCredential(
        long userId,
        long providerId,
        bool apiKeyChanged,
        string? apiKey,
        bool accountIdChanged,
        string? accountId,
        CancellationToken ct)
    {
        var credential = await db.Integrations
            .FirstOrDefaultAsync(
                item => item.Id == providerId && item.UserId == userId,
                ct);
        if (credential is null) return null;

        if (apiKeyChanged) credential.ApiKey = apiKey;
        if (accountIdChanged) credential.AccountId = accountId;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var platform = credential.PlatformId is null ? "api" : await db.Platforms
            .AsNoTracking()
            .Where(item => item.Id == credential.PlatformId.Value)
            .Select(item => item.Key)
            .FirstAsync(ct);
        return new ProviderStatusDto(
            credential.Id,
            platform,
            credential.AccountId,
            credential.Active ? "active" : "paused",
            credential.ApiKey,
            credential.ApiKeyLocked || credential.Type == IntegrationType.Api);
    }

    public async Task<bool> IsApiKeyLocked(long userId, long providerId, CancellationToken ct) =>
        await db.Integrations
            .AsNoTracking()
            .Where(item => item.Id == providerId && item.UserId == userId)
            .Select(item => item.ApiKeyLocked || item.Type == IntegrationType.Api)
            .FirstOrDefaultAsync(ct);

    public Task<bool> IsPlatformApiKeyLocked(long userId, string platformKey, CancellationToken ct) =>
        db.Integrations
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.ApiKeyLocked)
            .Join(
                db.Platforms.AsNoTracking().Where(platform => platform.Key == platformKey),
                credential => credential.PlatformId,
                platform => platform.Id,
                (credential, _) => credential)
            .AnyAsync(ct);

    public async Task<ProviderStatusDto?> ConnectOpenXbl(
        long userId,
        string apiKey,
        string? xuid,
        CancellationToken ct)
    {
        var platformId = await db.Platforms
            .AsNoTracking()
            .Where(platform => platform.Key == "xbox")
            .Select(platform => (long?)platform.Id)
            .FirstOrDefaultAsync(ct);
        if (!platformId.HasValue) return null;

        var credential = await db.Integrations
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.PlatformId == platformId.Value,
                ct);
        var now = DateTimeOffset.UtcNow;
        if (credential is null)
        {
            credential = new Integration
            {
                UserId = userId,
                Type = IntegrationType.Xbox,
                PlatformId = platformId.Value,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Integrations.Add(credential);
        }

        credential.ApiKey = apiKey;
        credential.AccountId = xuid;
        credential.Active = true;
        credential.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return new ProviderStatusDto(
            credential.Id,
            "xbox",
            credential.AccountId,
            "active",
            credential.ApiKey,
            credential.ApiKeyLocked);
    }

    public async Task<string> GetOrCreateApiKey(long userId, string apiKey, CancellationToken ct)
    {
        var existing = await db.Integrations
            .Where(credential => credential.UserId == userId && credential.Type == IntegrationType.Api)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(existing?.ApiKey)) return existing.ApiKey;

        if (existing is not null)
        {
            existing.ApiKey = apiKey;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return apiKey;
        }

        var now = DateTimeOffset.UtcNow;
        db.Integrations.Add(new Integration
        {
            UserId = userId,
            Type = IntegrationType.Api,
            PlatformId = null,
            ApiKey = apiKey,
            AccountId = "api",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        return apiKey;
    }
}
