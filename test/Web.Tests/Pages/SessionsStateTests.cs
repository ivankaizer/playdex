using Web.Infrastructure.Models;
using Web.Modules.Sessions;
using Xunit;

namespace Web.Tests.Pages;

public sealed class SessionsStateTests
{
    [Fact]
    public void InitialStateUsesTheUtcMondayThroughSundayRangeAndExactReactUrls()
    {
        var state = new SessionsState(
            new DateTimeOffset(2026, 7, 13, 23, 30, 0, TimeSpan.FromHours(-7)));

        Assert.Equal("this-week", state.Preset);
        Assert.Equal("2026-07-13", state.From);
        Assert.Equal("2026-07-19", state.To);
        Assert.Equal(10, SessionsState.Limit);
        Assert.Equal(
            "/sessions?limit=10&offset=0&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
            state.BuildListRequest());
        Assert.Equal(
            "/stats?from=2026-07-13&to=2026-07-19",
            state.BuildStatsRequest());
    }

    [Fact]
    public void DebouncedTitleIsTrimmedResetsPagingAndOnlyChangesTheListUrl()
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
    public void DebouncedTitleUsesUrlSearchParamsFormEncodingForReactSpecialCharacters()
    {
        var state = new SessionsState(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));

        var version = state.SetTitle("A~B! (Test)*");
        Assert.True(state.ApplyDebouncedTitle(version));

        Assert.Equal(
            "/sessions?limit=10&offset=0&title=A%7EB%21+%28Test%29*&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
            state.BuildListRequest());
    }

    [Fact]
    public void DebouncedTitleRejectsAStaleVersionSoOlderWorkCannotWin()
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
    public void CustomDatesResetPagingAndStatsRequireBothBounds()
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
    public void PresetsUseUtcCalendarRangesAndResetPaging(string preset, string from, string to)
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
    public void SortResetsPagingTogglesTheActiveKeyAndDefaultsANewKeyToDescending()
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
    public void ListAndStatsHaveIndependentLoadingDataAndErrorLifecycles()
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
    public void CompletedListDrivesTheReactShowingAndFourPageWindow()
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
    public void StatsLoadIsSkippedAndClearedWhenEitherDateBoundIsMissing()
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
    public async Task RequestVersionsRejectDelayedStatsAfterTheRangeBecomesIncomplete()
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
    public void DeleteIsConfirmGatedUsesTheExactEndpointAndRefreshesOnlyOnSuccess()
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
    public void RowViewUsesMappedPlatformCapsuleIgnoredFlagHashAndShortDuration()
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
    public void SessionsPageWiresReactRequestsResponsiveRowsModalAndCancellationWithoutMappedBadges()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Modules", "Sessions", "Sessions.razor");
        var codePath = Path.Combine(root, "Modules", "Sessions", "Sessions.razor.cs");
        Assert.True(File.Exists(markupPath), "Missing Sessions Razor page.");

        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/sessions\"", markup);
        Assert.Contains("<AppLayout Title=\"Sessions\"", markup);
        Assert.DoesNotContain("<Overlay>", markup);
        Assert.DoesNotContain("<ManualEntryModal", markup);
        Assert.DoesNotContain("Add entry", markup);
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
        Assert.DoesNotContain("Manual", code);
        Assert.Contains("BuildListRequest()", code);
        Assert.Contains("BuildStatsRequest()", code);
        Assert.DoesNotContain("onAuthFailure: LogoutAsync", code);
        Assert.Contains("Browser.ConfirmAsync(request.Confirmation)", code);
        Assert.Contains("SendAsync<object?>(request.Path, request.Method", code);
        Assert.DoesNotContain("SendAsync<ManualMappingSearchResponse>", code);
        Assert.DoesNotContain("SendAsync<ManualEntryCreateResponse>", code);
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
            var candidate = Path.Combine(directory.FullName, "src", "Web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate src/Web from the test output directory.");
    }
}
