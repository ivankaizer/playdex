namespace Playdex.Web.Models;

public sealed record ManualSessionStartRequest(string Title);
public sealed record ManualEntryCreateRequest(
    string Date,
    int DurationMinutes,
    string? GameId,
    int? IgdbId,
    string? Title,
    string? PlatformKey,
    string? CapsuleUrl);
public sealed record ManualMapInputRequest(
    string? GameId,
    int? IgdbId,
    string? Title,
    string? PlatformKey,
    int? PlatformId,
    string? CapsuleUrl);
public sealed record ManualGame(string Id, string Title, string Platform, string? CapsuleUrl);
public sealed record ManualInput(
    long Id,
    string Input,
    string CanonicalTitle,
    string? MappedGameId,
    string? MappedSource,
    ManualGame? MappedGame);
public sealed record ManualInputsResponse(IReadOnlyList<ManualInput> Inputs);
public sealed record ManualSessionStartResponse(long SessionId);
public sealed record ManualSessionStopResponse(int EndedSessionCount);
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
public sealed record ManualMappingResponse(long InputId, string? MappedGameId);
public sealed record ManualEntryCreateResponse(long SessionId);
