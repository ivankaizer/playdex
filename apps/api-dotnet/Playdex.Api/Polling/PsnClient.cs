using Playdex.Api.Data;

namespace Playdex.Api.Polling;

/// <summary>
/// Stub PSN API client. Returns null until the PSN API integration
/// is configured with client credentials.
///
/// Real implementation would use <see cref="PlatformCredential.ApiKey"/>
/// as the PSN NPSSO token and <see cref="PlatformCredential.AccountId"/>
/// as the PSN account ID.
/// </summary>
public sealed class PsnClient : IProviderClient
{
    public string PlatformKey => "psn";

    public Task<CurrentlyPlayingResult?> GetCurrentlyPlaying(PlatformCredential credential, CancellationToken ct)
    {
        // Stub: no-op until PSN API credentials are configured.
        // credential.ApiKey  → NPSSO token
        // credential.AccountId → PSN account ID
        return Task.FromResult<CurrentlyPlayingResult?>(null);
    }
}
