using System.Security.Cryptography;
using Api.Infrastructure.Http;
using Playdex.Persistence;

namespace Api.Modules.Providers;

public sealed class ProvidersService(ProvidersRepository repository)
{
    public async Task<ProvidersResponseDto> ListProviders(long userId, CancellationToken ct)
    {
        var providers = await repository.ListProviders(userId, ct);
        return new ProvidersResponseDto(providers.Select(ToResponse).ToList());
    }

    public async Task<CreateProviderResponseDto> CreateProvider(
        long userId,
        CreateProviderRequest request,
        CancellationToken ct)
    {
        var creation = ValidateCreation(request);
        var result = await repository.CreateProvider(
            userId,
            creation.Platform,
            creation.Type,
            creation.ApiKey,
            creation.AccountId,
            ct);
        if (result.LimitReached)
            throw AppProblemException.Conflict(
                $"{PlatformName(creation.Platform)} supports up to {ProvidersRepository.MaxIntegrationsPerType} integrations.");
        if (result.Provider is null)
            throw AppProblemException.NotFound($"{PlatformName(creation.Platform)} platform is not installed.");

        return new CreateProviderResponseDto(ToResponse(result.Provider));
    }

    public async Task<ProviderUpdateResponseDto> UpdateProviderStatus(long userId, ProviderUpdateRequest request, CancellationToken ct)
    {
        if (request.Status is not "active" and not "paused")
        {
            throw AppProblemException.Validation("Status must be 'active' or 'paused'.");
        }

        var provider = await repository.UpdateProviderStatus(userId, request.ProviderId, request.Status, ct);
        if (provider is null)
        {
            throw AppProblemException.NotFound("Provider not found.");
        }

        return new ProviderUpdateResponseDto(ToResponse(provider));
    }

    public async Task<ApiKeyResponseDto> GetOrCreateApiKey(long userId, CancellationToken ct)
    {
        var apiKey = GenerateApiKey();
        var key = await repository.GetOrCreateApiKey(userId, apiKey, ct);
        return new ApiKeyResponseDto(key);
    }

    public async Task<ProviderCredentialUpdateResponseDto> UpdateCredential(
        long userId,
        long providerId,
        ProviderCredentialChanges changes,
        CancellationToken ct)
    {
        if (!changes.ApiKeyChanged && !changes.AccountIdChanged)
        {
            throw AppProblemException.Validation("At least one field must be provided.");
        }

        if (changes.ApiKeyChanged && await repository.IsApiKeyLocked(userId, providerId, ct))
        {
            throw AppProblemException.Validation("API key is managed by an administrator.");
        }

        // The merged credential must pass the same rules as a new one, or polling would fail on every run.
        var provider = await repository.UpdateCredential(
            userId,
            providerId,
            changes.ApiKeyChanged,
            changes.ApiKey,
            changes.AccountIdChanged,
            changes.AccountId,
            ValidateCredential,
            ct);
        if (provider is null)
        {
            throw AppProblemException.NotFound("Provider not found.");
        }

        return new ProviderCredentialUpdateResponseDto(ToResponse(provider));
    }

    public async Task<ProviderSessionPlatformUpdateResponseDto> UpdateSessionPlatform(
        long userId,
        long providerId,
        ProviderSessionPlatformUpdateRequest request,
        CancellationToken ct)
    {
        var platform = NormalizeSessionPlatformKey(request.Platform);
        var result = await repository.UpdateSessionPlatform(
            userId,
            providerId,
            platform,
            request.RewriteExistingSessions,
            ct);
        if (result.UnsupportedIntegration)
        {
            throw AppProblemException.Validation(
                "This integration does not define a main platform.");
        }
        if (result.InvalidPlatform)
        {
            throw AppProblemException.Validation(
                "Platform must be the integration's main platform or one of its children.");
        }
        if (result.Provider is null)
            throw AppProblemException.NotFound("Provider not found.");

        return new ProviderSessionPlatformUpdateResponseDto(ToResponse(result.Provider));
    }

    public async Task<OpenXblConnectResponseDto> ConnectOpenXbl(
        long userId,
        OpenXblConnectRequest request,
        CancellationToken ct)
    {
        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            throw AppProblemException.Validation("OpenXBL API key is required.");

        var xuid = request.Xuid?.Trim();
        if (!string.IsNullOrEmpty(xuid) && xuid.Any(character => !char.IsAsciiDigit(character)))
            throw AppProblemException.Validation("Xbox XUID must contain digits only.");
        if (request.ProviderId is <= 0)
            throw AppProblemException.Validation("providerId must be a positive integer.");

        var result = await repository.ConnectOpenXbl(
            userId,
            request.ProviderId,
            apiKey,
            string.IsNullOrEmpty(xuid) ? null : xuid,
            ct);
        if (result.ProviderMissing)
            throw AppProblemException.NotFound("Provider not found.");
        if (result.ApiKeyLocked)
            throw AppProblemException.Validation("OpenXBL API key is managed by an administrator.");
        if (result.Provider is null)
            throw AppProblemException.NotFound("Xbox provider is not installed.");

        return new OpenXblConnectResponseDto(ToResponse(result.Provider));
    }

    internal static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return "plx_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string NormalizeSessionPlatformKey(string? value)
    {
        var platform = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(platform))
            throw AppProblemException.Validation("Platform is required.");
        return platform;
    }

    internal static ValidatedProviderCreation ValidateCreation(CreateProviderRequest request)
    {
        var platform = request.Platform?.Trim().ToLowerInvariant();
        var type = platform switch
        {
            "steam" => IntegrationType.Steam,
            "psn" => IntegrationType.Psn,
            "xbox" => IntegrationType.Xbox,
            _ => throw AppProblemException.Validation("Platform must be steam, psn, or xbox."),
        };
        var apiKey = request.ApiKey?.Trim();
        var accountId = request.AccountId?.Trim();
        accountId = string.IsNullOrEmpty(accountId) ? null : accountId;

        ValidateCredential(type, apiKey, accountId);
        return new ValidatedProviderCreation(platform, type, apiKey!, accountId);
    }

    /// <summary>
    /// What a polled integration needs to poll at all, checked when it is created and again after every edit:
    /// an API key (Steam key, NPSSO or OpenXBL key), a Steam ID for Steam and a numeric XUID, if any, for Xbox.
    /// API integrations hold a generated key and are not polled.
    /// </summary>
    internal static void ValidateCredential(IntegrationType type, string? apiKey, string? accountId)
    {
        if (type == IntegrationType.Api)
            return;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw AppProblemException.Validation(type switch
            {
                IntegrationType.Steam => "Steam Web API key is required.",
                IntegrationType.Psn => "PlayStation NPSSO token is required.",
                _ => "OpenXBL API key is required.",
            });
        }
        if (type == IntegrationType.Steam && string.IsNullOrWhiteSpace(accountId))
            throw AppProblemException.Validation("Steam ID is required.");
        if (type == IntegrationType.Xbox && accountId is not null &&
            accountId.Any(character => !char.IsAsciiDigit(character)))
        {
            throw AppProblemException.Validation("Xbox XUID must contain digits only.");
        }
    }

    internal static ProviderStatusDto ToResponse(ProviderStatusDto provider) =>
        provider with
        {
            ApiKey = provider.ApiKeyLocked && provider.Platform != "api" ? null : provider.ApiKey,
        };

    private static string PlatformName(string platform) => platform switch
    {
        "steam" => "Steam",
        "psn" => "PlayStation",
        _ => "Xbox",
    };
}
