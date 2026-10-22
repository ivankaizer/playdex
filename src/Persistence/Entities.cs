namespace Playdex.Persistence;

public sealed class User : IHasTimestamps
{
    public long Id { get; set; }
    public required string Email { get; set; }
    public string? Password { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsAdmin { get; set; }
    /// <summary>Bumped to revoke every token issued so far (see AuthTokenService.TokenVersionClaim).</summary>
    public int TokenVersion { get; set; }
}

public sealed class NotificationChannel : IHasTimestamps
{
    public const string Telegram = "telegram";

    public long UserId { get; set; }
    public required string Transport { get; set; }
    public required string Destination { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Platform : IHasTimestamps
{
    public long Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public int? IgdbPlatformId { get; set; }
    public long? ParentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum IntegrationType
{
    Steam,
    Psn,
    Xbox,
    Api,
}

public sealed class Integration : IHasTimestamps
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public IntegrationType Type { get; set; }
    public long? PlatformId { get; set; }
    public long? SessionPlatformId { get; set; }
    public string? ApiKey { get; set; }
    public string? AccountId { get; set; }
    public bool ApiKeyLocked { get; set; }
    public bool Active { get; set; }
    /// <summary>Why Playdex paused polling itself (a rejected credential); null when active or paused by the user.</summary>
    public string? PausedReason { get; set; }
    /// <summary>When Playdex paused polling itself; null when active or paused by the user.</summary>
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Game : IHasTimestamps
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CapsuleUrl { get; set; }
    public bool CapsuleUrlLocked { get; set; }
    public bool TitleLocked { get; set; }
}

public enum GameExternalIdentityType
{
    Steam,
    Psn,
    Xbox,
    Igdb,
}

public sealed class GameExternalIdentity : IHasTimestamps
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public GameExternalIdentityType Type { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum CaptureMethod
{
    Polling,
    Api,
    Manual,
}

public sealed class PlaySession : IHasTimestamps
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public long UserId { get; set; }
    public long PlatformId { get; set; }
    public CaptureMethod CaptureMethod { get; set; }
    public long? IntegrationId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PollResult
{
    public long Id { get; set; }
    public long IntegrationId { get; set; }
    public long? GameExternalIdentityId { get; set; }
    public DateTimeOffset PolledAt { get; set; }
    public string? RawPayload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IgnoredGame : IHasTimestamps
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GameRating : IHasTimestamps
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long GameId { get; set; }
    public short Rating { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserSettings : IHasTimestamps
{
    public long UserId { get; set; }
    public int IgnoreShortSessionsMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GameConnection : IHasTimestamps
{
    public long UserId { get; set; }
    public long GameId { get; set; }
    public long ConnectedGameId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Poll failure backoff of a polling integration, shared by every API instance. No row: the integration is healthy
/// and due. A row with no failures: a provider asked to be left alone until <see cref="NextPollAt"/>.
/// </summary>
public sealed class IntegrationPollBackoff
{
    public long IntegrationId { get; set; }
    /// <summary>Failed polls since the last successful one.</summary>
    public int Failures { get; set; }
    /// <summary>How many of <see cref="Failures"/> were the provider rejecting the credential.</summary>
    public int AuthFailures { get; set; }
    /// <summary>When the first of <see cref="Failures"/> happened; null without failures.</summary>
    public DateTimeOffset? FailingSince { get; set; }
    public DateTimeOffset NextPollAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A notification waiting for delivery, or delivered recently (kept to recognise a duplicate).</summary>
public sealed class NotificationOutboxEntry
{
    public long Id { get; set; }
    /// <summary>Identifies the event; a second notification with the same key is not queued.</summary>
    public required string DedupeKey { get; set; }
    public required string Kind { get; set; }
    public required string Payload { get; set; }
    public int Attempts { get; set; }
    /// <summary>When the next delivery attempt is due. A claimed entry is pushed past the time its claim lasts.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    /// <summary>Set when delivery was given up.</summary>
    public DateTimeOffset? FailedAt { get; set; }
}
