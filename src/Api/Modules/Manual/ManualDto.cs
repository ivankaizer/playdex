namespace Api.Modules.Manual;

// ── Request DTOs ──────────────────────────────────────────────

public sealed record ManualSessionStartRequest(
    string? Title,
    string? GameId = null,
    string? PlatformKey = null,
    string? IgdbId = null,
    string? CapsuleUrl = null);

public sealed record ManualEntryCreateRequest(
    string Date,
    int DurationMinutes,
    string? GameId,
    string? IgdbId,
    string? Title,
    string? PlatformKey,
    string? CapsuleUrl);

// ── Response DTOs ─────────────────────────────────────────────

public sealed record ManualSessionStartResponseDto(long SessionId);

public sealed record ManualSessionStopResponseDto(int EndedSessionCount);

public sealed record ManualMappingSearchResultDto(
    string Source,
    string? GameId,
    string? IgdbId,
    string Title,
    string? Platform,
    int? PlatformId,
    string? CapsuleUrl,
    bool PreviouslyPlayed = false);

public sealed record ManualMappingSearchResponseDto(
    string Query,
    bool IgdbAvailable,
    IReadOnlyList<ManualMappingSearchResultDto> Results);

public sealed record ManualEntryCreateResponseDto(long SessionId);

public sealed record ManualPlatformDto(string Key, string Name);
public sealed record ManualPlatformsResponseDto(IReadOnlyList<ManualPlatformDto> Platforms);

public sealed record ManualActiveSessionDto(
    long SessionId,
    string GameId,
    string Title,
    string PlatformKey,
    string PlatformName,
    DateTimeOffset StartedAt,
    string? CapsuleUrl);

public sealed record ManualActiveSessionResponseDto(ManualActiveSessionDto? Session);

internal sealed record ManualActiveSessionRow(
    long SessionId,
    long GameId,
    string Title,
    string PlatformKey,
    string PlatformName,
    DateTimeOffset StartedAt,
    string? CapsuleUrl);

internal sealed record ValidatedManualGameSelection(
    long? GameId,
    string? IgdbId);

internal sealed record ResolvedManualGameSelection(
    long? GameId,
    Api.Modules.Games.GameResolutionRequest? IgdbResolution);

internal sealed record IgdbSearchAttempt(
    bool Available,
    IReadOnlyList<Api.Modules.Polling.IgdbSearchResult> Results);
