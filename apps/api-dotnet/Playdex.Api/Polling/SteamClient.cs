using Playdex.Api.Data;

namespace Playdex.Api.Polling;

/// <summary>
/// Stub Steam API client. Returns null until the Steam API integration
/// is configured with a client ID and secret.
///
/// Real implementation would use <see cref="PlatformCredential.ApiKey"/>
/// as the Steam API key and <see cref="PlatformCredential.AccountId"/>
/// as the Steam user identifier.
/// </summary>
public sealed class SteamClient : IProviderClient
{
    public string PlatformKey => "steam";

    public Task<CurrentlyPlayingResult?> GetCurrentlyPlaying(PlatformCredential credential, CancellationToken ct)
    {
        // Stub: no-op until Steam Web API credentials are configured.
        // credential.ApiKey  → Steam API key
        // credential.AccountId → SteamId64
        return Task.FromResult<CurrentlyPlayingResult?>(null);
    }
}
