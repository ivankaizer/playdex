using Playdex.Api.Data;

namespace Playdex.Api.Polling;

/// <summary>
/// Result from a provider's "currently playing" API call.
/// When a user is playing nothing, the result is null.
/// </summary>
public sealed record CurrentlyPlayingResult(
    long? GameId,
    string Title,
    string? ExternalId,
    string? CapsuleUrl,
    string Platform
);

/// <summary>
/// Abstraction over a third-party gaming platform API (Steam, PSN, etc.)
/// used by the polling background service to detect active gameplay.
/// </summary>
public interface IProviderClient
{
    /// <summary>
    /// The platform key this client handles (e.g. "steam", "psn").
    /// Must match the <c>key</c> column in the <c>platforms</c> table.
    /// </summary>
    string PlatformKey { get; }

    /// <summary>
    /// Queries the provider for the user's currently playing game.
    /// Returns null when the user is not playing anything or the
    /// provider cannot be reached.
    /// </summary>
    Task<CurrentlyPlayingResult?> GetCurrentlyPlaying(PlatformCredential credential, CancellationToken ct);
}
