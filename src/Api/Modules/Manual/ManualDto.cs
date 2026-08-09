namespace Api.Modules.Manual;

// ── Request DTOs ──────────────────────────────────────────────

public sealed record ManualSessionStartRequest(string Title);

public sealed record ManualEntryCreateRequest(
    string Date,
    int DurationMinutes,
    string? GameId,
    string? IgdbId,
    string? Title,
    string? PlatformKey,
    string? CapsuleUrl);

public sealed record ManualMapInputRequest(
    string? GameId,
    string? IgdbId,
    string? Title,
    string? PlatformKey,
    int? PlatformId,
    string? CapsuleUrl);

// ── Response DTOs ─────────────────────────────────────────────

public sealed record ManualGameDto(
    string Id,
    string Title,
    string Platform,
    string? CapsuleUrl);

public sealed record ManualInputDto(
    long Id,
    string Input,
    string CanonicalTitle,
    string? MappedGameId,
    string? MappedSource,
    ManualGameDto? MappedGame);

public sealed record ManualInputsResponseDto(IReadOnlyList<ManualInputDto> Inputs);

public sealed record ManualSessionStartResponseDto(long SessionId);

public sealed record ManualSessionStopResponseDto(int EndedSessionCount);

public sealed record ManualMappingSearchResultDto(
    string Source,
    string? GameId,
    string? IgdbId,
    string Title,
    string? Platform,
    int? PlatformId,
    string? CapsuleUrl);

public sealed record ManualMappingSearchResponseDto(
    string Query,
    bool IgdbAvailable,
    IReadOnlyList<ManualMappingSearchResultDto> Results);

public sealed record ManualMappingResponseDto(long InputId, string? MappedGameId);

public sealed record ManualEntryCreateResponseDto(long SessionId);
