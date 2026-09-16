namespace Web.Infrastructure.Models;

public sealed record ManualSessionStartRequest(
    string? Title,
    string? GameId,
    string? PlatformKey,
    string? IgdbId,
    string? CapsuleUrl);

public sealed record ManualEntryCreateRequest(
    string Date,
    int DurationMinutes,
    string? GameId,
    string? IgdbId,
    string? Title,
    string? PlatformKey,
    string? CapsuleUrl);

public sealed record ManualSessionStartResponse(long SessionId);
public sealed record ManualSessionStopResponse(int EndedSessionCount);
public sealed record ManualEntryCreateResponse(long SessionId);

public sealed record ManualMappingSearchResult(
    string Source,
    string? GameId,
    string? IgdbId,
    string Title,
    string? Platform,
    int? PlatformId,
    string? CapsuleUrl);

public sealed record ManualMappingSearchResponse(
    string Query,
    bool IgdbAvailable,
    IReadOnlyList<ManualMappingSearchResult> Results);

public sealed record ManualPlatform(string Key, string Name);
public sealed record ManualPlatformsResponse(IReadOnlyList<ManualPlatform> Platforms);

public sealed record ManualActiveSession(
    long SessionId,
    string GameId,
    string Title,
    string PlatformKey,
    string PlatformName,
    DateTimeOffset StartedAt,
    string? CapsuleUrl);

public sealed record ManualActiveSessionResponse(ManualActiveSession? Session);
