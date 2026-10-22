namespace Api.Modules.Polling;

/// <summary>
/// The fields of a polling <see cref="Playdex.Persistence.Integration"/> a provider client needs. Not an entity:
/// <see cref="PollingRepository.LoadActiveCredentials"/> projects it from <c>integrations</c>.
/// </summary>
public sealed class IntegrationCredentials
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? ApiKey { get; set; }
    public string? AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Active { get; set; }
    public long PlatformId { get; set; }
}

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
    Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct);
}

/// <summary>
/// Optional provider capability for enriching a newly persisted game.
/// </summary>
public interface IProviderGameMetadataClient
{
    Task<string?> GetCapsuleUrl(
        IntegrationCredentials credential,
        string externalId,
        CancellationToken ct);
}

public sealed class ProviderPollException(
    string message,
    TimeSpan? retryAfter = null,
    Exception? innerException = null,
    bool authenticationFailure = false) : Exception(message, innerException)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>The provider rejected the credential (an expired token, a revoked key): retrying will not help.</summary>
    public bool IsAuthenticationFailure { get; } = authenticationFailure;
}
