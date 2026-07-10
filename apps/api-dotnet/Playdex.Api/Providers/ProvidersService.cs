using Playdex.Api.Http;

namespace Playdex.Api.Providers;

public sealed class ProvidersService(IProvidersRepository repository)
{
    public async Task<ProvidersResponseDto> ListProviders(long userId, CancellationToken ct)
    {
        var providers = await repository.ListProviders(userId, ct);
        return new ProvidersResponseDto(providers);
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

        return new ProviderUpdateResponseDto(provider);
    }

    public async Task<PlayniteKeyResponseDto> GetOrCreatePlayniteKey(long userId, CancellationToken ct)
    {
        var apiKey = GenerateApiKey();
        var key = await repository.GetOrCreatePlayniteKey(userId, apiKey, ct);
        return new PlayniteKeyResponseDto(key);
    }

    private static string GenerateApiKey()
    {
        var bytes = new byte[16];
        Random.Shared.NextBytes(bytes);
        return "plx_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
