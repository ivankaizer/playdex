using System.Globalization;

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
    public static bool TryGetTimes(string? date, int? minutes, out DateTimeOffset start, out DateTimeOffset end)
    {
        start = end = default;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) || minutes is null or <= 0) return false;
        start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        try { end = start.AddMinutes(minutes.Value); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
