using System.Text.Json;

namespace Api.Shared.Persistence;

public sealed class User
{
    public long Id { get; set; }
    public required string Email { get; set; }
    public string? Password { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ChatId { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class Platform
{
    public long Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public int? IgdbPlatformId { get; set; }
    public long? ParentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PlatformCredential
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

public sealed class Game
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public string? ExternalId { get; set; }
    public required string CanonicalKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CapsuleUrl { get; set; }
    public long PlatformId { get; set; }
    public bool CapsuleUrlLocked { get; set; }
    public bool TitleLocked { get; set; }
}

public sealed class PlaySession
{
    public long Id { get; set; }
    public long PlatformCredentialId { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PollResult
{
    public long Id { get; set; }
    public long PlatformCredentialId { get; set; }
    public long? GameId { get; set; }
    public DateTimeOffset PolledAt { get; set; }
    public string? RawPayload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ManualGameInput
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public required string RawTitle { get; set; }
    public required string CanonicalTitle { get; set; }
    public long? MappedGameId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IgnoredGame
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GameRating
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long GameId { get; set; }
    public short Rating { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserSettings
{
    public long UserId { get; set; }
    public int IgnoreShortSessionsMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GameConnection
{
    public long UserId { get; set; }
    public long GameId { get; set; }
    public long ConnectedGameId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
