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

public sealed class IntegrationCredentials : IHasTimestamps
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? ApiKey { get; set; }
    public string? AccountId { get; set; }
    public bool ApiKeyLocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Active { get; set; }
    public long PlatformId { get; set; }
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
    public long? IntegrationId { get; set; }
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
