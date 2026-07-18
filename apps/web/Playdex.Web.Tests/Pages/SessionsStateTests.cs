using Playdex.Web.Models;
using Playdex.Web.Pages;
using Xunit;

namespace Playdex.Web.Tests.Pages;

public sealed class SessionsStateTests
{
    [Fact]
    public void InitialState_UsesTheUtcMondayThroughSundayRangeAndExactReactUrls()
    {
        var state = new SessionsState(
            new DateTimeOffset(2026, 7, 13, 23, 30, 0, TimeSpan.FromHours(-7)));

        Assert.Equal("this-week", state.Preset);
        Assert.Equal("2026-07-13", state.From);
        Assert.Equal("2026-07-19", state.To);
        Assert.Equal(10, state.Limit);
        Assert.Equal(
            "/sessions?limit=10&offset=0&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
            state.BuildListRequest());
        Assert.Equal(
            "/stats?from=2026-07-13&to=2026-07-19",
            state.BuildStatsRequest());
    }

    [Fact]
    public void DebouncedTitle_IsTrimmedResetsPagingAndOnlyChangesTheListUrl()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetOffset(30);

        var version = state.SetTitle("  Hades II  ");
        Assert.True(state.ApplyDebouncedTitle(version));

        Assert.Equal(0, state.Offset);
        Assert.Equal("Hades II", state.DebouncedTitle);
        Assert.Equal(
            "/sessions?limit=10&offset=0&title=Hades+II&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
            state.BuildListRequest());
        Assert.Equal(
            "/stats?from=2026-07-13&to=2026-07-19",
            state.BuildStatsRequest());
    }

    [Fact]
    public void DebouncedTitle_UsesUrlSearchParamsFormEncodingForReactSpecialCharacters()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));

        var version = state.SetTitle("A~B! (Test)*");
        Assert.True(state.ApplyDebouncedTitle(version));

        Assert.Equal(
            "/sessions?limit=10&offset=0&title=A%7EB%21+%28Test%29*&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
            state.BuildListRequest());
    }

    [Fact]
    public void DebouncedTitle_RejectsAStaleVersionSoOlderWorkCannotWin()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetOffset(20);
        var stale = state.SetTitle("older");
        var current = state.SetTitle("newer");

        Assert.False(state.ApplyDebouncedTitle(stale));
        Assert.Equal(20, state.Offset);
        Assert.Empty(state.DebouncedTitle);
        Assert.True(state.ApplyDebouncedTitle(current));
        Assert.Equal("newer", state.DebouncedTitle);
    }

    [Fact]
    public void CustomDates_ResetPagingAndStatsRequireBothBounds()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetOffset(20);

        state.SetFrom(string.Empty);

        Assert.Equal(0, state.Offset);
        Assert.Equal("custom", state.Preset);
        Assert.Equal(
            "/sessions?limit=10&offset=0&to=2026-07-19&sort=date&direction=desc",
            state.BuildListRequest());
        Assert.Null(state.BuildStatsRequest());

        state.SetTo(string.Empty);
        Assert.Equal(
            "/sessions?limit=10&offset=0&sort=date&direction=desc",
            state.BuildListRequest());
        Assert.Null(state.BuildStatsRequest());
    }

    [Theory]
    [InlineData("today", "2026-07-14", "2026-07-14")]
    [InlineData("yesterday", "2026-07-13", "2026-07-13")]
    [InlineData("this-week", "2026-07-13", "2026-07-19")]
    [InlineData("last-week", "2026-07-06", "2026-07-12")]
    [InlineData("this-month", "2026-07-01", "2026-07-31")]
    [InlineData("this-year", "2026-01-01", "2026-12-31")]
    [InlineData("last-year", "2025-01-01", "2025-12-31")]
    public void Presets_UseUtcCalendarRangesAndResetPaging(string preset, string from, string to)
    {
        var now = new DateTimeOffset(2026, 7, 13, 23, 30, 0, TimeSpan.FromHours(-7));
        var state = new SessionsState(now);
        state.SetOffset(20);

        state.ApplyPreset(preset, now);

        Assert.Equal(preset, state.Preset);
        Assert.Equal(from, state.From);
        Assert.Equal(to, state.To);
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void Sort_ResetsPagingTogglesTheActiveKeyAndDefaultsANewKeyToDescending()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetOffset(20);

        state.SortBy("date");
        Assert.Equal("date", state.SortKey);
        Assert.Equal("asc", state.SortDirection);
        Assert.Equal(0, state.Offset);

        state.SetOffset(10);
        state.SortBy("duration");
        Assert.Equal("duration", state.SortKey);
        Assert.Equal("desc", state.SortDirection);
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void ListAndStats_HaveIndependentLoadingDataAndErrorLifecycles()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        var stats = new AccountStatsPeriod(3600, 1, "Hades II", [], []);

        state.BeginListLoad();
        state.BeginStatsLoad();
        state.FailListLoad("List unavailable.");

        Assert.False(state.IsListLoading);
        Assert.Equal("List unavailable.", state.ListError);
        Assert.Empty(state.Rows);
        Assert.True(state.IsStatsLoading);

        state.CompleteStatsLoad(new AccountStatsRangeResponse(1, stats));

        Assert.False(state.IsStatsLoading);
        Assert.Empty(state.StatsError);
        Assert.Same(stats, state.Stats);

        state.BeginStatsLoad();
        state.FailStatsLoad("Stats unavailable.");

        Assert.False(state.IsStatsLoading);
        Assert.Equal("Stats unavailable.", state.StatsError);
        Assert.Null(state.Stats);
        Assert.Equal("List unavailable.", state.ListError);
    }

    [Fact]
    public void CompletedList_DrivesTheReactShowingAndFourPageWindow()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetOffset(20);
        state.BeginListLoad();
        var row = new AccountSession(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            "42", "steam", "Hades II", null, false, null, null);

        state.CompleteListLoad(new AccountSessionsResponse(1, [row], 47, 10, 20));

        Assert.False(state.IsListLoading);
        Assert.Same(row, Assert.Single(state.Rows));
        Assert.Equal(47, state.Total);
        Assert.Equal(21, state.Start);
        Assert.Equal(30, state.End);
        Assert.Equal(3, state.CurrentPage);
        Assert.Equal([2, 3, 4, 5], state.PageNumbers);
    }

    [Fact]
    public void StatsLoad_IsSkippedAndClearedWhenEitherDateBoundIsMissing()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        var stats = new AccountStatsPeriod(3600, 1, null, [], []);
        state.CompleteStatsLoad(new AccountStatsRangeResponse(1, stats));
        state.SetFrom(string.Empty);

        Assert.False(state.TryBeginStatsLoad());
        Assert.False(state.IsStatsLoading);
        Assert.Null(state.Stats);

        state.SetFrom("2026-07-13");
        Assert.True(state.TryBeginStatsLoad());
        Assert.True(state.IsStatsLoading);
    }

    [Fact]
    public async Task RequestVersions_RejectDelayedStatsAfterTheRangeBecomesIncomplete()
    {
        var versions = new SessionsRequestVersions();
        var request = versions.BeginStatsRequest();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedApply = ApplyAfterReleaseAsync();

        versions.BeginStatsRequest();
        completion.SetResult();

        Assert.False(await delayedApply);

        async Task<bool> ApplyAfterReleaseAsync()
        {
            await completion.Task;
            return versions.IsCurrentStatsRequest(request);
        }
    }

    [Fact]
    public async Task RequestVersions_RejectDelayedManualWorkAfterEditCloseAndReopen()
    {
        var versions = new SessionsRequestVersions();
        versions.OpenManualModal();
        var search = versions.BeginManualSearch();
        var save = versions.BeginManualSave();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedApply = ApplyAfterReleaseAsync();

        versions.InvalidateManualSearch();
        versions.CloseManualModal();
        versions.OpenManualModal();
        completion.SetResult();

        var (searchIsCurrent, saveIsCurrent) = await delayedApply;
        Assert.False(searchIsCurrent);
        Assert.False(saveIsCurrent);

        async Task<(bool SearchIsCurrent, bool SaveIsCurrent)> ApplyAfterReleaseAsync()
        {
            await completion.Task;
            return (
                versions.IsCurrentManualSearch(search),
                versions.IsCurrentManualSave(save));
        }
    }

    [Fact]
    public void ManualLocalEntry_UsesTheExactReactBodyAndRefreshesOnlyAfterSuccess()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.OpenManualEntry(new DateTimeOffset(2026, 7, 14, 1, 0, 0, TimeSpan.FromHours(2)));
        state.SetManualDuration("1", "30");
        state.SelectManualResult(new ManualMappingSearchResult(
            "local", "42", null, "Hades II", "steam", null, null));

        Assert.True(state.TryBuildManualEntry(out var submission));
        Assert.NotNull(submission);
        Assert.Equal("/manual/entries", submission.Path);
        Assert.Equal("2026-07-13", submission.Body["date"]);
        Assert.Equal(90, submission.Body["durationMinutes"]);
        Assert.Equal("3H4h3", submission.Body["gameId"]);
        Assert.Equal(["date", "durationMinutes", "gameId"], submission.Body.Keys);

        Assert.True(state.IsManualEntryOpen);
        state.CompleteManualEntry();
        Assert.False(state.IsManualEntryOpen);
        Assert.Equal(1, state.RefreshVersion);
    }

    [Fact]
    public void ManualEntryValidation_PreservesTheExactReactMessages()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.OpenManualEntry(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));

        state.SetManualDuration("not-a-number", "0");
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Duration must be a positive number.", state.ManualError);

        state.SetManualDuration("0", "0");
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Duration must be at least 1 minute.", state.ManualError);

        state.SetManualDuration(string.Empty, string.Empty);
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Duration must be at least 1 minute.", state.ManualError);

        state.SetManualDuration("0", "1");
        state.SetManualDate(string.Empty);
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Select a date for this entry.", state.ManualError);

        state.SetManualDate("2026-07-13");
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Select a game to log.", state.ManualError);

        state.SelectManualResult(new ManualMappingSearchResult(
            "igdb", null, "123", "Hades II", null, null, null));
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Select a platform for the IGDB result.", state.ManualError);

        state.SelectManualResult(new ManualMappingSearchResult(
            "local", string.Empty, null, "Hades II", "steam", null, null));
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Select a saved game.", state.ManualError);

        state.SelectManualResult(new ManualMappingSearchResult(
            "igdb", null, string.Empty, "Hades II", null, null, null));
        state.SetManualPlatform("pc");
        Assert.False(state.TryBuildManualEntry(out _));
        Assert.Equal("Select an IGDB game.", state.ManualError);
    }

    [Fact]
    public void ManualIgdbEntry_UsesTheExactReactBodyIncludingStringIdAndCapsule()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.OpenManualEntry(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetManualDuration("0", "45");
        state.SelectManualResult(new ManualMappingSearchResult(
            "igdb", null, "123", "Hades II", null, null, "https://images.test/hades.jpg"));
        state.SetManualPlatform("pc");

        Assert.True(state.TryBuildManualEntry(out var submission));
        Assert.NotNull(submission);
        Assert.Equal("2026-07-13", submission.Body["date"]);
        Assert.Equal(45, submission.Body["durationMinutes"]);
        Assert.Equal("123", submission.Body["igdbId"]);
        Assert.Equal("Hades II", submission.Body["title"]);
        Assert.Equal("pc", submission.Body["platformKey"]);
        Assert.Equal("https://images.test/hades.jpg", submission.Body["capsuleUrl"]);
        Assert.Equal(
            ["date", "durationMinutes", "igdbId", "title", "platformKey", "capsuleUrl"],
            submission.Body.Keys);
    }

    [Fact]
    public void ManualSearch_TrimsWithEncodeURIComponentAndKeepsItsOwnLifecycle()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetManualSearchQuery("   ");
        Assert.False(state.TryBeginManualSearch(out _));
        Assert.Equal("Enter a search query.", state.ManualSearchError);

        state.SetManualSearchQuery("  Hades II  ");
        Assert.True(state.TryBeginManualSearch(out var path));
        Assert.Equal("/manual/search?query=Hades%20II", path);
        Assert.True(state.IsManualSearchLoading);
        Assert.Empty(state.ManualSearchError);

        var result = new ManualMappingSearchResult(
            "local", "42", null, "Hades II", "steam", null, null);
        state.CompleteManualSearch(new ManualMappingSearchResponse("Hades II", false, [result]));
        Assert.False(state.IsManualSearchLoading);
        Assert.False(state.IsIgdbAvailable);
        Assert.Same(result, Assert.Single(state.ManualSearchResults));

        state.BeginManualSearch();
        state.FailManualSearch("Search unavailable.");
        Assert.False(state.IsManualSearchLoading);
        Assert.Empty(state.ManualSearchResults);
        Assert.Equal("Search unavailable.", state.ManualSearchError);
    }

    [Fact]
    public void ManualSearch_UsesTheEncodeURIComponentSpecialCharacterSet()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetManualSearchQuery("A! (Test)*~'");

        Assert.True(state.TryBeginManualSearch(out var path));
        Assert.Equal("/manual/search?query=A!%20(Test)*~'", path);
    }

    [Fact]
    public void ClosingManualEntry_ResetsTheSameDraftFieldsAsReactAndKeepsTheDate()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.OpenManualEntry(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.SetManualDuration("2", "15");
        state.SetManualSearchQuery("Hades");
        state.SelectManualResult(new ManualMappingSearchResult(
            "igdb", null, "123", "Hades II", null, null, null));
        state.SetManualPlatform("pc");
        state.FailManualSearch("Search unavailable.");
        state.BeginManualSearch();
        state.BeginManualSave();

        state.CloseManualEntry();

        Assert.False(state.IsManualEntryOpen);
        Assert.Equal("2026-07-13", state.ManualDate);
        Assert.Equal("1", state.ManualHours);
        Assert.Equal("0", state.ManualMinutes);
        Assert.Empty(state.ManualSearchQuery);
        Assert.Empty(state.ManualSearchResults);
        Assert.Empty(state.ManualSearchError);
        Assert.Null(state.ManualSelection);
        Assert.Empty(state.ManualPlatform);
        Assert.Empty(state.ManualError);
        Assert.False(state.IsManualSearchLoading);
        Assert.False(state.IsManualSaving);
    }

    [Fact]
    public void Delete_IsConfirmGatedUsesTheExactEndpointAndRefreshesOnlyOnSuccess()
    {
        var request = SessionsDeleteRequest.For(7);
        Assert.Equal("Remove this session entry?", request.Confirmation);
        Assert.Equal("/sessions/7", request.Path);
        Assert.Equal(HttpMethod.Delete, request.Method);

        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.BeginDelete(7);
        Assert.Contains(7, state.DeletingSessionIds);
        Assert.Empty(state.ListError);

        state.CompleteDelete();
        state.EndDelete(7);
        Assert.Equal(1, state.RefreshVersion);
        Assert.DoesNotContain(7, state.DeletingSessionIds);

        state.BeginDelete(8);
        state.FailDelete("Failed to remove session.");
        state.EndDelete(8);
        Assert.Equal("Failed to remove session.", state.ListError);
        Assert.Equal(1, state.RefreshVersion);
    }

    [Fact]
    public void RowView_UsesMappedPlatformCapsuleIgnoredFlagHashAndShortDuration()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var state = new SessionsState(now);
        var row = new AccountSession(7, now.AddMinutes(-65), now, "42", "manual", "Hades II",
            "https://images.test/hades.jpg", true, "psn", "igdb");
        state.CompleteListLoad(new AccountSessionsResponse(1, [row], 1, 10, 0));

        var view = Assert.Single(state.GetRowViews(now));

        Assert.Equal(7, view.SessionId);
        Assert.Equal("/games/3H4h3", view.GamePath);
        Assert.Equal("Hades II", view.Title);
        Assert.Equal("PS5", view.Platform);
        Assert.Equal("1h 05m", view.Duration);
        Assert.Equal("https://images.test/hades.jpg", view.ImageUrl);
        Assert.True(view.Ignored);
    }

    [Fact]
    public void FailedManualSave_LeavesTheModalOpenAndEndsOnlyItsSavingState()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));
        state.OpenManualEntry(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));

        state.BeginManualSave();
        Assert.True(state.IsManualSaving);
        Assert.Empty(state.ManualError);

        state.FailManualSave("Failed to create entry.");
        state.EndManualSave();

        Assert.True(state.IsManualEntryOpen);
        Assert.False(state.IsManualSaving);
        Assert.Equal("Failed to create entry.", state.ManualError);
        Assert.Equal(0, state.RefreshVersion);
    }

    [Fact]
    public void SessionsPage_WiresReactRequestsResponsiveRowsModalAndCancellationWithoutMappedBadges()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Pages", "Sessions.razor");
        var codePath = Path.Combine(root, "Pages", "Sessions.razor.cs");
        Assert.True(File.Exists(markupPath), "Missing Sessions Razor page.");

        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/sessions\"", markup);
        Assert.Contains("<AppLayout Title=\"Sessions\"", markup);
        Assert.Contains("<Overlay>", markup);
        Assert.Contains("<ManualEntryModal", markup);
        Assert.Contains("mt-6 space-y-3 md:hidden", markup);
        Assert.Contains("mt-6 hidden overflow-hidden", markup);
        Assert.True(markup.Split("@foreach (var session in rowViews)").Length - 1 >= 2);
        Assert.True(markup.Split("Ignored from stats").Length - 1 >= 2);
        Assert.DoesNotContain("Mapped", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No sessions match your filters.", markup);
        Assert.Contains("No sessions yet. Sync a provider to populate this list.", markup);
        Assert.Contains("Loading stats...", markup);
        Assert.Contains("Showing @_state.Start-@_state.End of @_state.Total sessions", markup);

        Assert.Contains("Task.Delay(300, debounce.Token)", code);
        Assert.Contains("ApplyDebouncedTitle(version)", code);
        Assert.Contains("_listRequestVersion", code);
        Assert.Contains("BeginStatsRequest()", code);
        Assert.Contains("SessionsRequestVersions", code);
        Assert.Contains("SetManualSearchQuery", code);
        Assert.Contains("BuildListRequest()", code);
        Assert.Contains("BuildStatsRequest()", code);
        Assert.DoesNotContain("onAuthFailure: LogoutAsync", code);
        Assert.Contains("Browser.ConfirmAsync(request.Confirmation)", code);
        Assert.Contains("SendAsync<object?>(request.Path, request.Method", code);
        Assert.Contains("SendAsync<ManualMappingSearchResponse>(path", code);
        Assert.Contains("SendAsync<ManualEntryCreateResponse>(submission!.Path", code);
        Assert.Contains("RefreshAllAsync()", code);
        Assert.Contains("_titleDebounce?.Cancel()", code);
        Assert.Contains("_listRequest?.Cancel()", code);
        Assert.Contains("_statsRequest?.Cancel()", code);
    }

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "apps", "web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate apps/web from the test output directory.");
    }
}
