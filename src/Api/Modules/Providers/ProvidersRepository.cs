using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Persistence;

namespace Api.Modules.Providers;

public sealed class ProvidersRepository(PlaydexDbContext db)
{
    public const int MaxIntegrationsPerType = 3;

    internal static IReadOnlyList<ProviderPlatformDto> BuildSessionPlatformOptions(
        long mainPlatformId,
        IReadOnlyList<Platform> platforms)
        => BuildSessionPlatformTree(mainPlatformId, platforms)
            .Select(platform => new ProviderPlatformDto(platform.Key, platform.Name))
            .ToArray();

    private static List<Platform> BuildSessionPlatformTree(
        long mainPlatformId,
        IReadOnlyList<Platform> platforms)
    {
        var main = platforms.FirstOrDefault(platform => platform.Id == mainPlatformId);
        if (main is null) return [];

        var options = new List<Platform> { main };
        var pending = new Queue<long>();
        pending.Enqueue(main.Id);
        while (pending.TryDequeue(out var parentId))
        {
            foreach (var child in platforms
                         .Where(platform => platform.ParentId == parentId)
                         .OrderBy(platform => platform.Name)
                         .ThenBy(platform => platform.Key))
            {
                options.Add(child);
                pending.Enqueue(child.Id);
            }
        }

        return options;
    }

    public async Task<List<ProviderStatusDto>> ListProviders(long userId, CancellationToken ct)
    {
        var integrations = await db.Integrations
            .AsNoTracking()
            .Where(integration => integration.UserId == userId)
            .OrderBy(integration => integration.Id)
            .ToListAsync(ct);
        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);

        return integrations.Select(integration => ToStatus(integration, platforms)).ToList();
    }

    public async Task<ProviderCreateResult> CreateProvider(
        long userId,
        string platform,
        IntegrationType type,
        string apiKey,
        string? accountId,
        CancellationToken ct)
    {
        // Count and insert under one lock per user and type, or parallel requests could all pass the limit check.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lockKey = $"integration-limit:{userId}:{type}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
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
        if (!await TrySaveAccount(integration, ct))
        {
            return new ProviderCreateResult(null, LimitReached: false, DuplicateAccount: true);
        }

        await transaction.CommitAsync(ct);
        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);
        return new ProviderCreateResult(ToStatus(integration, platforms), LimitReached: false);
    }

    /// <returns>False when another integration of the user already polls this account.</returns>
    private async Task<bool> TrySaveAccount(Integration integration, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  {
                                                      SqlState: PostgresErrorCodes.UniqueViolation,
                                                      ConstraintName: PlaydexDbContext.UniqueAccountIndex,
                                                  })
        {
            db.Entry(integration).State = EntityState.Detached;
            return false;
        }
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
        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);
        return ToStatus(credential, platforms);
    }

    /// <exception cref="DuplicateIntegrationAccountException">The new account is already connected.</exception>
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
        if (!await TrySaveAccount(credential, ct))
            throw new DuplicateIntegrationAccountException();
        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);
        return ToStatus(credential, platforms);
    }

    public async Task<ProviderSessionPlatformUpdateResult> UpdateSessionPlatform(
        long userId,
        long providerId,
        string platformKey,
        bool rewriteExistingSessions,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({providerId})",
            ct);
        var integration = await db.Integrations.FirstOrDefaultAsync(
            item => item.Id == providerId && item.UserId == userId,
            ct);
        if (integration is null)
            return new ProviderSessionPlatformUpdateResult(null, InvalidPlatform: false);
        if (integration.PlatformId is not { } mainPlatformId)
        {
            return new ProviderSessionPlatformUpdateResult(
                null,
                InvalidPlatform: false,
                UnsupportedIntegration: true);
        }

        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);
        var allowedPlatforms = BuildSessionPlatformTree(mainPlatformId, platforms);
        var selectedPlatform = allowedPlatforms.FirstOrDefault(
            platform => platform.Key == platformKey);
        if (selectedPlatform is null)
            return new ProviderSessionPlatformUpdateResult(null, InvalidPlatform: true);

        var now = DateTimeOffset.UtcNow;
        integration.SessionPlatformId = selectedPlatform.Id == mainPlatformId
            ? null
            : selectedPlatform.Id;
        integration.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        if (rewriteExistingSessions)
        {
            await db.PlaySessions
                .Where(session => session.IntegrationId == integration.Id)
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(session => session.PlatformId, selectedPlatform.Id)
                        .SetProperty(session => session.UpdatedAt, now),
                    ct);
        }

        await transaction.CommitAsync(ct);
        return new ProviderSessionPlatformUpdateResult(
            ToStatus(integration, platforms),
            InvalidPlatform: false);
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
        if (!await TrySaveAccount(credential, ct))
            throw new DuplicateIntegrationAccountException();

        var platforms = await db.Platforms.AsNoTracking().ToListAsync(ct);
        return ToStatus(credential, platforms);
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

    private static ProviderStatusDto ToStatus(
        Integration integration,
        IReadOnlyList<Platform> platforms)
    {
        var options = integration.PlatformId is { } mainPlatformId
            ? BuildSessionPlatformOptions(mainPlatformId, platforms)
            : [];
        var effectivePlatformId = integration.SessionPlatformId ?? integration.PlatformId;
        var effectivePlatform = effectivePlatformId is { } platformId
            ? platforms.FirstOrDefault(platform => platform.Id == platformId)
            : null;

        return new ProviderStatusDto(
            integration.Id,
            integration.Type switch
            {
                IntegrationType.Steam => "steam",
                IntegrationType.Psn => "psn",
                IntegrationType.Xbox => "xbox",
                _ => "api",
            },
            integration.AccountId,
            integration.Active ? "active" : "paused",
            integration.ApiKey,
            integration.ApiKeyLocked || integration.Type == IntegrationType.Api,
            effectivePlatform is null
                ? null
                : new ProviderPlatformDto(effectivePlatform.Key, effectivePlatform.Name),
            options);
    }
}

/// <summary>Another integration of the same user and type already uses this account.</summary>
public sealed class DuplicateIntegrationAccountException() : Exception("This account is already connected.");
