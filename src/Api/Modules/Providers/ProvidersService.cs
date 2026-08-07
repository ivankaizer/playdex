using System.Security.Cryptography;
using Api.Shared.Http;

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
            throw AppProblem.Validation("Status must be 'active' or 'paused'.");
        }

        var provider = await repository.UpdateProviderStatus(userId, request.ProviderId, request.Status, ct);
        if (provider is null)
        {
            throw AppProblem.NotFound("Provider not found.");
        }

        return new ProviderUpdateResponseDto(ToResponse(provider));
    }

    public async Task<PlayniteKeyResponseDto> GetOrCreatePlayniteKey(long userId, CancellationToken ct)
    {
        var apiKey = GenerateApiKey();
        var key = await repository.GetOrCreatePlayniteKey(userId, apiKey, ct);
        return new PlayniteKeyResponseDto(key);
    }

    internal static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return "plx_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static ProviderStatusDto ToResponse(ProviderStatusDto provider) =>
        provider.Platform == "playnite" ? provider : provider with { ApiKey = null };
}
