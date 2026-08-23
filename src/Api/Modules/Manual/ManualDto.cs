namespace Api.Modules.Manual;

// ── Request DTOs ──────────────────────────────────────────────

public sealed record ManualSessionStartRequest(string Title, string? GameId = null, string? PlatformKey = null);

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
    string? CapsuleUrl);

public sealed record ManualMappingSearchResponseDto(
    string Query,
    bool IgdbAvailable,
    IReadOnlyList<ManualMappingSearchResultDto> Results);

public sealed record ManualEntryCreateResponseDto(long SessionId);
