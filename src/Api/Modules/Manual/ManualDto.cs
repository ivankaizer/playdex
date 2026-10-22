namespace Api.Modules.Manual;

// ── Request DTOs ──────────────────────────────────────────────

public sealed record ManualSessionStartRequest(
    string? Title,
    string? GameId = null,
    string? PlatformKey = null,
    string? IgdbId = null,
    string? CapsuleUrl = null);

/// <param name="Date">The day played, "yyyy-MM-dd", in <paramref name="TimeZone"/>.</param>
/// <param name="TimeZone">The client's IANA or Windows time zone id; the entry starts when its date begins there.</param>
public sealed record ManualEntryCreateRequest(
    string Date,
    int DurationMinutes,
    string? GameId,
    string? IgdbId,
    string? Title,
    string? PlatformKey,
    string? CapsuleUrl,
    string? TimeZone = null);

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

/// <summary>
/// A dated entry or draft row: the day it was played on, as instants in the client's (or batch's) time zone, and how
/// long it lasted. It has no time of day; <see cref="ManualService.PlaceEntry"/> picks one.
/// </summary>
internal sealed record ManualEntryDay(
    DateOnly Date,
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd,
    TimeSpan Duration)
{
    /// <summary>The day <paramref name="date"/> in <paramref name="zone"/> (UTC when null).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The day is outside the range of <see cref="DateTimeOffset"/>.</exception>
    public static ManualEntryDay Of(DateOnly date, TimeZoneInfo? zone, TimeSpan duration)
    {
        var calendar = zone ?? TimeZoneInfo.Utc;
        return new ManualEntryDay(
            date,
            Api.Infrastructure.Common.LocalCalendar.StartOf(date, calendar),
            Api.Infrastructure.Common.LocalCalendar.StartOf(date.AddDays(1), calendar),
            duration);
    }
}

internal sealed record IgdbSearchAttempt(
    bool Available,
    IReadOnlyList<Api.Modules.Polling.IgdbSearchResult> Results);
