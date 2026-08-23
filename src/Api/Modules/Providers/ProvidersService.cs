using System.Security.Cryptography;
using Api.Infrastructure.Http;

namespace Api.Modules.Providers;

public sealed class ProvidersService(ProvidersRepository repository)
{
    public async Task<ProvidersResponseDto> ListProviders(long userId, CancellationToken ct)
    {
        var providers = await repository.ListProviders(userId, ct);
        return new ProvidersResponseDto(providers.Select(ToResponse).ToList());
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

        var provider = await repository.UpdateCredential(
            userId,
            providerId,
            changes.ApiKeyChanged,
            changes.ApiKey,
            changes.AccountIdChanged,
            changes.AccountId,
            ct);
        if (provider is null)
        {
            throw AppProblemException.NotFound("Provider not found.");
        }

        return new ProviderCredentialUpdateResponseDto(ToResponse(provider));
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
        if (await repository.IsPlatformApiKeyLocked(userId, "xbox", ct))
            throw AppProblemException.Validation("OpenXBL API key is managed by an administrator.");

        var provider = await repository.ConnectOpenXbl(
            userId,
            apiKey,
            string.IsNullOrEmpty(xuid) ? null : xuid,
            ct);
        if (provider is null)
            throw AppProblemException.NotFound("Xbox provider is not installed.");

        return new OpenXblConnectResponseDto(ToResponse(provider));
    }

    internal static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return "plx_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static ProviderStatusDto ToResponse(ProviderStatusDto provider) =>
        provider with
        {
            ApiKey = provider.ApiKeyLocked && provider.Platform != "api" ? null : provider.ApiKey,
        };
}
