using System.Globalization;
using Api.Infrastructure.Common;

namespace Api.Modules.Manual.Drafts;

public sealed record DraftBatchDto(Guid Id, DateTimeOffset CreatedAt, IReadOnlyList<DraftRowDto> Rows);
public sealed record DraftRowDto(Guid Id, int Revision, string Status, string Title, string? Date,
    int? DurationMinutes, string PlatformKey, ManualMappingSearchResultDto? Selection,
    IReadOnlyList<string> Warnings, bool Ready, long? AcceptedSessionId);
public sealed record DraftUpdateRequest(int Revision, string Title, string? Date, int? DurationMinutes,
    string PlatformKey, string? GameId, string? IgdbId);
public sealed record DraftAcceptRow(Guid Id, int Revision);
public sealed record DraftAcceptRequest(IReadOnlyList<DraftAcceptRow> Rows);
public sealed record DraftAcceptResult(Guid Id, long? SessionId, string? Error);
public sealed record DraftAcceptResponse(IReadOnlyList<DraftAcceptResult> Results);

public static class DraftRules
{
    public const string InvalidTimesWarning = "Provide a valid date and positive duration.";
    public const string TooLongWarning = "A session can last at most 24 hours.";
    public const string FutureDateWarning = "The date cannot be in the future.";

    /// <summary>
    /// The session a row becomes: it starts when its date begins in the batch's time zone (<paramref name="zone"/>,
    /// UTC when unknown) and lasts the reviewed minutes.
    /// </summary>
    public static bool TryGetTimes(string? date, int? minutes, TimeZoneInfo? zone, DateTimeOffset now,
        out DateTimeOffset start, out DateTimeOffset end) =>
        Problem(date, minutes, zone, now, out start, out end) is null;

    /// <summary>Why a row cannot become a session yet, or null when it can.</summary>
    public static string? Problem(string? date, int? minutes, TimeZoneInfo? zone, DateTimeOffset now,
        out DateTimeOffset start, out DateTimeOffset end)
    {
        start = end = default;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) || minutes is null or <= 0) return InvalidTimesWarning;
        if (minutes > SessionLimits.MaxDurationMinutes) return TooLongWarning;
        if (day > SessionLimits.LatestDate(now, zone)) return FutureDateWarning;
        try
        {
            start = LocalCalendar.StartOf(day, zone ?? TimeZoneInfo.Utc);
            end = start.AddMinutes(minutes.Value);
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            start = end = default;
            return InvalidTimesWarning;
        }
    }
}
