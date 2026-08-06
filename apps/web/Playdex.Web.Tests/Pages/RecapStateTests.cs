using System.Reflection;
using Microsoft.AspNetCore.Components;
using Playdex.Web.Modules.Recap;
using Xunit;

namespace Playdex.Web.Tests.Pages;

public sealed class RecapStateTests
{
    [Theory]
    [InlineData(2.55, 1, "2.5")]
    [InlineData(2.25, 1, "2.3")]
    [InlineData(-2.55, 1, "-2.5")]
    [InlineData(-2.25, 1, "-2.3")]
    [InlineData(2.0, 1, "2")]
    [InlineData(double.NaN, 1, "0")]
    [InlineData(double.PositiveInfinity, 1, "0")]
    [InlineData(double.NegativeInfinity, 1, "0")]
    [InlineData(0.1, 100, "0.1")]
    [InlineData(1e21, 1, "1e+21")]
    public void AverageFormatter_MatchesNumberToFixedThenNumberStringification(
        double value,
        int decimals,
        string expected)
    {
        var method = typeof(Recap).GetMethod(
            "FormatAverage",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [value, decimals]));
    }

    [Fact]
    public void YearsResolution_PreservesServerOrderThenRequestsAllTimeOnce()
    {
        var state = new RecapState();

        Assert.Equal("/recap/years", state.BeginYearsLoad());
        Assert.False(state.YearsLoaded);

        state.CompleteYears(new RecapYearsResponse([2024, 2026, 2025]));

        Assert.True(state.YearsLoaded);
        Assert.Equal([2024, 2026, 2025], state.Years);
        Assert.Equal("/recap", state.TakeDefaultRequest());
        Assert.Equal("all-time", state.ActiveTab);
        Assert.Null(state.TakeDefaultRequest());
    }

    [Fact]
    public void CompleteQueryRange_SelectsCustomAndBuildsTheExplicitReactUrl()
    {
        var state = new RecapState();

        var request = state.ApplyLocationQuery("?to=2026-07-19&from=2026-07-13");

        Assert.True(state.HasSearchRange);
        Assert.Equal("custom", state.ActiveTab);
        Assert.Equal("2026-07-13", state.CustomFrom);
        Assert.Equal("2026-07-19", state.CustomTo);
        Assert.Equal("/recap?from=2026-07-13&to=2026-07-19", request);
    }

    [Fact]
    public void DuplicateRangeKeys_UseTheFirstValueLikeUrlSearchParamsGet()
    {
        var state = new RecapState();

        var request = state.ApplyLocationQuery(
            "?from=2026-07-13&from=2025-01-01&to=2026-07-19&to=2025-12-31");

        Assert.Equal("2026-07-13", state.CustomFrom);
        Assert.Equal("2026-07-19", state.CustomTo);
        Assert.Equal("/recap?from=2026-07-13&to=2026-07-19", request);
    }

    [Fact]
    public void OneSidedQuery_IsIgnoredAndRearmsTheAllTimeRequest()
    {
        var state = new RecapState();
        state.CompleteYears(new RecapYearsResponse([2026]));
        Assert.Equal("/recap", state.TakeDefaultRequest());

        Assert.Null(state.ApplyLocationQuery("?from=2026-07-13"));

        Assert.False(state.HasSearchRange);
        Assert.Equal("all-time", state.ActiveTab);
        Assert.Empty(state.CustomFrom);
        Assert.Empty(state.CustomTo);
        Assert.Equal("/recap", state.TakeDefaultRequest());
    }

    [Fact]
    public async Task InitialCustomLoad_RouteBecomesOneSidedWhileYearsPending_LoadsOneFallbackAfterYearsSettle()
    {
        var state = new RecapState();
        var requests = new List<string>();
        var yearsCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var customRequest = state.ApplyLocationQuery("?from=2026-07-13&to=2026-07-19")!;

        var flow = RecapInitialCustomFlow.RunAsync(
            state,
            customRequest,
            async () =>
            {
                state.BeginYearsLoad();
                await yearsCompletion.Task;
                state.CompleteYears(new RecapYearsResponse([2026]));
            },
            path =>
            {
                requests.Add(path);
                return Task.CompletedTask;
            });

        Assert.Equal(["/recap?from=2026-07-13&to=2026-07-19"], requests);
        Assert.Null(state.ApplyLocationQuery("?from=2026-07-13"));
        Assert.Null(state.TakeDefaultRequest());

        yearsCompletion.SetResult();
        await flow;

        Assert.Equal(
            ["/recap?from=2026-07-13&to=2026-07-19", "/recap"],
            requests);
        Assert.Null(state.TakeDefaultRequest());
    }

    [Fact]
    public async Task InitialCustomLoad_RouteLosesRange_StartsFallbackBeforeStaleCustomCompletes()
    {
        var state = new RecapState();
        var requests = new List<string>();
        var yearsCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var customCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var customRequest = state.ApplyLocationQuery("?from=2026-07-13&to=2026-07-19")!;

        var flow = RecapInitialCustomFlow.RunAsync(
            state,
            customRequest,
            async () =>
            {
                state.BeginYearsLoad();
                await yearsCompletion.Task;
                state.CompleteYears(new RecapYearsResponse([2026]));
            },
            path =>
            {
                requests.Add(path);
                if (path == customRequest) return customCompletion.Task;
                fallbackStarted.SetResult();
                return Task.CompletedTask;
            });

        Assert.Equal([customRequest], requests);
        Assert.Null(state.ApplyLocationQuery("?from=2026-07-13"));

        yearsCompletion.SetResult();
        try
        {
            await fallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(customCompletion.Task.IsCompleted);
            Assert.False(flow.IsCompleted);
            Assert.Equal([customRequest, "/recap"], requests);
        }
        finally
        {
            customCompletion.TrySetResult();
            await flow;
        }
    }

    [Fact]
    public async Task InitialCustomLoad_CustomRemainsActive_DoesNotDoubleLoadAfterYearsSettle()
    {
        var state = new RecapState();
        var requests = new List<string>();
        var yearsCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var customRequest = state.ApplyLocationQuery("?from=2026-07-13&to=2026-07-19");

        var flow = RecapInitialCustomFlow.RunAsync(
            state,
            customRequest!,
            async () =>
            {
                state.BeginYearsLoad();
                await yearsCompletion.Task;
                state.CompleteYears(new RecapYearsResponse([2026]));
            },
            path =>
            {
                requests.Add(path);
                return Task.CompletedTask;
            });

        yearsCompletion.SetResult();
        await flow;

        Assert.Equal(["/recap?from=2026-07-13&to=2026-07-19"], requests);
        Assert.True(state.HasSearchRange);
        Assert.Null(state.TakeDefaultRequest());
    }

    [Fact]
    public void YearTabs_PreserveServerOrderAndLoadWithoutChangingLocation()
    {
        var state = new RecapState();
        state.CompleteYears(new RecapYearsResponse([2024, 2026, 2025]));

        Assert.Equal(
            ["all-time", "year-2024", "year-2026", "year-2025", "custom"],
            state.Tabs.Select(tab => tab.Key));

        var request = state.SelectTab("year-2026");

        Assert.Equal("year-2026", state.ActiveTab);
        Assert.Equal("/recap?from=2026-01-01&to=2026-12-31", request);
    }

    [Fact]
    public void CustomRange_RequiresBothBoundsForTabLoadAndBrowserNavigation()
    {
        var state = new RecapState();
        Assert.Null(state.SelectTab("custom"));
        Assert.Equal("custom", state.ActiveTab);

        state.SetCustomFrom("2026-07-13");
        Assert.Null(state.BuildCustomNavigation());
        Assert.Null(state.SelectTab("custom"));

        state.SetCustomTo("2026-07-19");
        Assert.Equal("/recap?from=2026-07-13&to=2026-07-19", state.SelectTab("custom"));
        Assert.Equal("/recap?from=2026-07-13&to=2026-07-19", state.BuildCustomNavigation());
    }

    [Fact]
    public void RecapLoad_PreservesOldContentWhileLoadingAndClearsItOnFailure()
    {
        var state = new RecapState();
        var existing = Recap(totalSessions: 7);
        state.CompleteLoad(existing);

        state.BeginLoad();

        Assert.True(state.IsLoading);
        Assert.Same(existing, state.Recap);
        Assert.Empty(state.Error);

        state.FailLoad("Failed to load recap.");

        Assert.False(state.IsLoading);
        Assert.Null(state.Recap);
        Assert.Equal("Failed to load recap.", state.Error);

        var replacement = Recap(totalSessions: 9);
        state.BeginLoad();
        state.CompleteLoad(replacement);
        Assert.Same(replacement, state.Recap);
        Assert.False(state.IsLoading);
        Assert.Empty(state.Error);
    }

    [Fact]
    public void YearsFailure_StillResolvesAndAllowsTheDefaultRecapLoad()
    {
        var state = new RecapState();

        state.FailYears("Failed to load recap years.");

        Assert.True(state.YearsLoaded);
        Assert.Empty(state.Years);
        Assert.Equal("Failed to load recap years.", state.Error);
        Assert.Equal("/recap", state.TakeDefaultRequest());

        state.BeginLoad();
        Assert.Empty(state.Error);
    }

    [Fact]
    public void WeekdayChart_UsesTheSameInlineSvgCoordinatesAsReact()
    {
        var chart = RecapWeekdayChart.Build(
            [new WeekdayStat(1, 1), new WeekdayStat(2, 4), new WeekdayStat(3, 2)]);

        Assert.Equal(240, chart.Width);
        Assert.Equal(80, chart.Height);
        Assert.Equal("8,56 120,8 232,40", chart.LinePoints);
        Assert.Equal("M 8,72 L 8,56 120,8 232,40 L 232,72 Z", chart.AreaPath);
        Assert.Equal([(8d, 56d), (120d, 8d), (232d, 40d)], chart.Points);
    }

    [Fact]
    public void RecapPage_WiresQueryStateInlineSectionsAndGameNavigationInReactOrder()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Modules", "Recap", "Recap.razor");
        var codePath = Path.Combine(root, "Modules", "Recap", "Recap.razor.cs");
        Assert.True(File.Exists(markupPath), "Missing Recap Razor page.");

        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/recap\"", markup);
        Assert.Contains("<AppLayout Title=\"Recap\"", markup);
        Assert.DoesNotContain("StatsRecapSection", markup);
        Assert.Contains("weekdayGradient", markup);
        Assert.Contains("Loading recap...", markup);
        Assert.Contains("No game data available yet.", markup);
        Assert.Contains("No platform data available yet.", markup);
        Assert.Contains("No games logged yet.", markup);
        Assert.Contains("recap.TopGamesByTime", markup);
        Assert.Contains("recap.PlatformTotals", markup);
        Assert.Contains("recap.GamesTable", markup);

        string[] orderedHeadings =
        [
            "Top 5 games",
            "Volume and session length",
            "Days played",
            "Sessions by weekday",
            "Heatmap by hour",
            "Weekend vs weekday",
            "Share of playtime",
            "How you explore games",
            "Game leaders",
            "Summed playtime by game",
        ];
        var previous = -1;
        foreach (var heading in orderedHeadings)
        {
            var index = markup.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(index > previous, $"{heading} must follow the prior React section.");
            previous = index;
        }

        Assert.Contains("SendAsync<RecapYearsResponse>(\"/recap/years\"", code);
        Assert.Contains("SendAsync<RecapResponse>(path", code);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"from\")]", code);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"to\")]", code);
        Assert.Contains("ApplyLocationQuery(new Uri(Navigation.Uri).Query)", code);
        Assert.Contains("var request = _state.SelectTab(key);", code);
        Assert.Contains("if (request is not null) await LoadRecapAsync(request);", code);
        Assert.Contains("var target = _state.BuildCustomNavigation();", code);
        Assert.Contains("NavigateAsync(target)", code);
        Assert.Contains("GameHash.EncodeGameId(gameId)", code);

        AssertQueryBinding("QueryFrom", "from");
        AssertQueryBinding("QueryTo", "to");
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

    private static void AssertQueryBinding(string propertyName, string queryName)
    {
        var property = typeof(Recap).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        var attribute = property?.GetCustomAttribute<SupplyParameterFromQueryAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(queryName, attribute.Name);
    }

    private static RecapResponse Recap(int totalSessions) => new(
        Range: new RecapRange(null, null),
        TotalSessions: totalSessions,
        TotalSeconds: 3600,
        AverageSessionSeconds: 3600,
        MedianSessionSeconds: 3600,
        LongestSessionSeconds: 3600,
        ShortestSessionSeconds: 3600,
        AverageSessionsPerDay: 1,
        AverageSessionsPerWeek: 1,
        AverageSessionsPerMonth: 1,
        MostActiveDay: null,
        MostActiveMonth: null,
        LongestStreakDays: 1,
        DaysPlayed: 1,
        DaysNotPlayed: 0,
        SessionsByWeekday: [],
        SessionsByHour: [],
        PeakHour: null,
        WeekendSeconds: 0,
        WeekdaySeconds: 3600,
        UniqueGames: 1,
        TopGameBySessions: null,
        TopGameByTime: null,
        TopGamesByTime: [],
        TopGameByAverageSession: null,
        GameWithLongestSession: null,
        NewGamesTried: 1,
        AbandonedGames: 0,
        ReturnRateGames: 0,
        TimeOnTop3Percent: 100,
        MostBingedGame: null,
        PlatformTotals: [],
        MostUsedPlatform: null,
        GamesTable: []);
}
