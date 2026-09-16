using System.Globalization;
using Web.Infrastructure.Models;

namespace Web.Modules.Sessions;

public enum SessionCreateMode
{
    Live,
    Historical,
}

public sealed record SessionCreateSubmission(string Path, HttpMethod Method, object Body);

public sealed class SessionCreateState
{
    public bool IsOpen { get; private set; }
    public SessionCreateMode Mode { get; private set; }
    public string Query { get; private set; } = string.Empty;
    public IReadOnlyList<ManualMappingSearchResult> Results { get; private set; } = [];
    public ManualMappingSearchResult? SelectedGame { get; private set; }
    public string PlatformKey { get; private set; } = string.Empty;
    public string Date { get; private set; } = string.Empty;
    public string Hours { get; private set; } = "1";
    public string Minutes { get; private set; } = "0";
    public string Error { get; private set; } = string.Empty;
    public string SearchError { get; private set; } = string.Empty;
    public bool IsSearching { get; private set; }
    public bool IsSaving { get; private set; }
    public bool IsIgdbAvailable { get; private set; } = true;

    public void Open(SessionCreateMode mode, DateTimeOffset now)
    {
        Reset();
        IsOpen = true;
        Mode = mode;
        Date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public void Close() => Reset();

    public void SetMode(SessionCreateMode mode)
    {
        Mode = mode;
        Error = string.Empty;
    }

    public void SetQuery(string query)
    {
        Query = query;
        Results = [];
        SelectedGame = null;
        SearchError = string.Empty;
        Error = string.Empty;
    }

    public bool TryBeginSearch(out string? path)
    {
        var query = Query.Trim();
        if (query.Length == 0)
        {
            path = null;
            SearchError = "Enter a game title.";
            return false;
        }

        IsSearching = true;
        SearchError = string.Empty;
        path = $"/manual/search?query={Uri.EscapeDataString(query)}";
        return true;
    }

    public void CompleteSearch(ManualMappingSearchResponse response)
    {
        Results = response.Results;
        IsIgdbAvailable = response.IgdbAvailable;
        IsSearching = false;
        SearchError = string.Empty;
    }

    public void FailSearch(string error)
    {
        Results = [];
        IsSearching = false;
        SearchError = error;
    }

    public void CancelSearch() => IsSearching = false;

    public void SelectGame(ManualMappingSearchResult game)
    {
        SelectedGame = game;
        PlatformKey = string.Empty;
        Error = string.Empty;
    }

    public void SetPlatform(string platformKey)
    {
        PlatformKey = platformKey;
        Error = string.Empty;
    }

    public void SetDate(string date) => Date = date;
    public void SetHours(string hours) => Hours = hours;
    public void SetMinutes(string minutes) => Minutes = minutes;

    public bool TryBuildSubmission(out SessionCreateSubmission? submission)
    {
        submission = null;
        Error = string.Empty;
        if (SelectedGame is null)
        {
            Error = "Select a game.";
            return false;
        }
        if (PlatformKey.Trim().Length == 0)
        {
            Error = "Select a platform.";
            return false;
        }

        if (Mode == SessionCreateMode.Live)
        {
            submission = new SessionCreateSubmission(
                "/manual/sessions/start",
                HttpMethod.Post,
                new ManualSessionStartRequest(
                    SelectedGame.Title,
                    SelectedGame.GameId,
                    PlatformKey,
                    SelectedGame.IgdbId,
                    SelectedGame.CapsuleUrl));
            return true;
        }

        if (Date.Length == 0)
        {
            Error = "Select a date.";
            return false;
        }
        if (!TryParseDuration(out var durationMinutes)) return false;

        submission = new SessionCreateSubmission(
            "/manual/entries",
            HttpMethod.Post,
            new ManualEntryCreateRequest(
                Date,
                durationMinutes,
                SelectedGame.GameId,
                SelectedGame.IgdbId,
                SelectedGame.Title,
                PlatformKey,
                SelectedGame.CapsuleUrl));
        return true;
    }

    public bool TryBeginSave()
    {
        if (IsSaving) return false;
        IsSaving = true;
        Error = string.Empty;
        return true;
    }

    public void FailSave(string error)
    {
        IsSaving = false;
        Error = error;
    }

    public void CompleteSave() => Reset();

    private bool TryParseDuration(out int durationMinutes)
    {
        durationMinutes = 0;
        if (!double.TryParse(Hours, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) ||
            !double.TryParse(Minutes, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) ||
            !double.IsFinite(hours) || !double.IsFinite(minutes) || hours < 0 || minutes < 0)
        {
            Error = "Enter a valid duration.";
            return false;
        }

        durationMinutes = (int)Math.Floor(hours * 60 + minutes);
        if (durationMinutes > 0) return true;
        Error = "Duration must be at least 1 minute.";
        return false;
    }

    private void Reset()
    {
        IsOpen = false;
        Mode = SessionCreateMode.Live;
        Query = string.Empty;
        Results = [];
        SelectedGame = null;
        PlatformKey = string.Empty;
        Date = string.Empty;
        Hours = "1";
        Minutes = "0";
        Error = string.Empty;
        SearchError = string.Empty;
        IsSearching = false;
        IsSaving = false;
        IsIgdbAvailable = true;
    }
}
