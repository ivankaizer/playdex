using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;

namespace Api.Modules.Providers;

public sealed class ProvidersRepository(PlaydexDbContext db)
{
    public async Task<List<ProviderStatusDto>> ListProviders(long userId, CancellationToken ct)
    {
        var rows = await db.PlatformCredentials
            .AsNoTracking()
            .Where(credential => credential.UserId == userId)
            .OrderBy(credential => credential.Id)
            .Join(
                db.Platforms.AsNoTracking(),
                credential => credential.PlatformId,
                platform => platform.Id,
                (credential, platform) => new
                {
                    credential.Id,
                    Platform = platform.Key,
                    credential.AccountId,
                    Status = credential.Active ? "active" : "paused",
                    credential.ApiKey,
                    credential.ApiKeyLocked,
                })
            .ToListAsync(ct);

        return rows.Select(r => new ProviderStatusDto(
            r.Id, r.Platform, r.AccountId, r.Status, r.ApiKey, r.ApiKeyLocked)).ToList();
    }

    public async Task<ProviderStatusDto?> UpdateProviderStatus(long userId, long providerId, string status, CancellationToken ct)
    {
        var credential = await db.PlatformCredentials
            .FirstOrDefaultAsync(
                item => item.Id == providerId && item.UserId == userId,
                ct);
        if (credential is null) return null;

        credential.Active = status == "active";
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var platform = await db.Platforms
            .AsNoTracking()
            .Where(item => item.Id == credential.PlatformId)
            .Select(item => item.Key)
            .FirstAsync(ct);
        return new ProviderStatusDto(
            credential.Id,
            platform,
            credential.AccountId,
            credential.Active ? "active" : "paused",
            credential.ApiKey,
            credential.ApiKeyLocked);
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
        var credential = await db.PlatformCredentials
            .FirstOrDefaultAsync(
                item => item.Id == providerId && item.UserId == userId,
                ct);
        if (credential is null) return null;

        if (apiKeyChanged) credential.ApiKey = apiKey;
        if (accountIdChanged) credential.AccountId = accountId;
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var platform = await db.Platforms
            .AsNoTracking()
            .Where(item => item.Id == credential.PlatformId)
            .Select(item => item.Key)
            .FirstAsync(ct);
        return new ProviderStatusDto(
            credential.Id,
            platform,
            credential.AccountId,
            credential.Active ? "active" : "paused",
            credential.ApiKey,
            credential.ApiKeyLocked);
    }

    public async Task<bool> IsApiKeyLocked(long userId, long providerId, CancellationToken ct) =>
        await db.PlatformCredentials
            .AsNoTracking()
            .Where(item => item.Id == providerId && item.UserId == userId)
            .Select(item => item.ApiKeyLocked)
            .FirstOrDefaultAsync(ct);

    public async Task<string> GetOrCreatePlayniteKey(long userId, string apiKey, CancellationToken ct)
    {
        var existing = await db.PlatformCredentials
            .Where(credential => credential.UserId == userId)
            .Join(
                db.Platforms.AsNoTracking().Where(platform => platform.Key == "playnite"),
                credential => credential.PlatformId,
                platform => platform.Id,
                (credential, _) => credential)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(existing?.ApiKey)) return existing.ApiKey;

        if (existing is not null)
        {
            existing.ApiKey = apiKey;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return apiKey;
        }

        var platformId = await db.Platforms
            .AsNoTracking()
            .Where(platform => platform.Key == "playnite")
            .Select(platform => (long?)platform.Id)
            .FirstOrDefaultAsync(ct);
        if (platformId.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            db.PlatformCredentials.Add(new PlatformCredential
            {
                UserId = userId,
                PlatformId = platformId.Value,
                ApiKey = apiKey,
                AccountId = "local",
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }

        return apiKey;
    }
}
