using Playdex.Api.Data;

namespace Playdex.Api.Polling;

/// <summary>
/// Result from a provider's "currently playing" API call.
/// When a user is playing nothing, Game is null but RawPayload still
/// captures the provider's raw response for debugging.
/// Returns null only when the provider cannot be reached at all.
/// </summary>
public sealed record ProviderPollResult(
    CurrentlyPlayingResult? Game,
    string? RawPayload
);

/// <summary>
/// Game data extracted from a provider's response when a user is
/// actively playing something.
/// </summary>
public sealed record CurrentlyPlayingResult(
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
    /// Returns null when the provider cannot be reached.
    /// When the provider responds but the user is not playing,
    /// the result's <see cref="ProviderPollResult.Game"/> is null
    /// but <see cref="ProviderPollResult.RawPayload"/> still
    /// contains the raw response for debugging.
    /// </summary>
    Task<ProviderPollResult?> GetCurrentlyPlaying(PlatformCredential credential, CancellationToken ct);
}
