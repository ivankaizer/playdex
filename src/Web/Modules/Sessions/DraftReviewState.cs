using System.Globalization;
using Web.Infrastructure.Models;

namespace Web.Modules.Sessions;

public sealed class DraftRowEdit
{
    public DraftRowEdit(DraftRowDto row) => Apply(row);
    private DraftRowUpdate? _saved;
    public bool IsDirty => Update != _saved;
    public Guid Id { get; private set; }
    public int Revision { get; private set; }
    public string Title { get; private set; } = "";
    public string Date { get; set; } = "";
    public string Hours { get; set; } = "";
    public string Minutes { get; set; } = "";
    public string PlatformKey { get; set; } = "unknown";
    public ManualMappingSearchResult? Selection { get; set; }
    public IReadOnlyList<ManualMappingSearchResult> Results { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    public string Error { get; set; } = "";
    public bool IsAccepted { get; private set; }
    public long? AcceptedSessionId { get; private set; }
    public DraftAcceptRow Acceptance => new(Id, Revision);
    private int? Duration => int.TryParse(Hours, out var hours) && int.TryParse(Minutes, out var minutes) && hours >= 0 && minutes >= 0 && (long)hours * 60 + minutes is > 0 and <= int.MaxValue ? (int)((long)hours * 60 + minutes) : null;
    public bool HasGameSelection => Selection is not null;
    public bool HasValidDate => DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    public bool HasValidDuration => Duration is not null;
    public bool HasValidPlatform => !string.IsNullOrWhiteSpace(PlatformKey);
    public bool NeedsPlatformReview => !HasValidPlatform || PlatformKey == "unknown";
    public string Corrections => string.Join(" ", new[] {
        !HasGameSelection ? "Select a verified game." : "",
        !HasValidDate ? "Select a valid date." : "",
        !HasValidDuration ? "Enter a positive whole-minute duration." : "",
        !HasValidPlatform ? "Select a platform." : ""
    }.Where(value => value.Length > 0));
    public bool CanAccept => !IsAccepted && Corrections.Length == 0;
    public DraftRowUpdate Update => new(Revision, Title, string.IsNullOrWhiteSpace(Date) ? null : Date, Duration, PlatformKey, Selection?.GameId, Selection?.IgdbId);
    public void SetTitle(string title) { if (Title == title) return; Title = title; Selection = null; Results = []; }
    public void Apply(DraftRowDto row)
    {
        Id = row.Id; Revision = row.Revision; Title = row.Title; Date = row.Date ?? "";
        Hours = row.DurationMinutes is {} duration ? (duration / 60).ToString(CultureInfo.InvariantCulture) : "0";
        Minutes = row.DurationMinutes is {} minutes ? (minutes % 60).ToString(CultureInfo.InvariantCulture) : "";
        PlatformKey = row.PlatformKey; Selection = row.Selection; Warnings = row.Warnings;
        IsAccepted = row.Status == "accepted"; AcceptedSessionId = row.AcceptedSessionId; Error = ""; _saved = Update;
    }
    public void MarkAccepted(long sessionId) { IsAccepted = true; AcceptedSessionId = sessionId; Error = ""; }
}
