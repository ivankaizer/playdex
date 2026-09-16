using Web.Infrastructure.Models;
using Web.Modules.Dashboard;
using Xunit;

namespace Web.Tests.Pages;

public sealed class DashboardStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DashboardStateTypesAreInternalImplementationDetails()
    {
        Type[] types =
        [
            typeof(DashboardRequests),
            typeof(DashboardSnapshot),
            typeof(DashboardSessionCard),
            typeof(DashboardPlatformView),
            typeof(DashboardTopGameView),
            typeof(DashboardStatsView),
            typeof(DashboardCurrentSessionView),
            typeof(DashboardRequestFlow),
            typeof(DashboardState),
        ];

        Assert.All(types, type => Assert.False(type.IsPublic, $"{type.Name} must remain internal."));
    }

    [Fact]
    public void RequestsIncludeManualLiveSessionAlongsideDashboardData()
    {
        var requests = DashboardRequests.For(Now);

        Assert.Equal(
            [
                "/sessions?limit=10&offset=0&from=2026-07-13&to=2026-07-13&sort=date&direction=desc",
                "/stats?from=2026-07-13&to=2026-07-13",
                "/stats?from=2026-07-01&to=2026-07-13",
                "/stats?from=2026-01-01&to=2026-07-13",
                "/sessions/current",
                "/manual/sessions/current",
            ],
            requests.All);
    }

    [Fact]
    public void RefreshIsAllOrNothingAndSilentRefreshDoesNotShowTheSkeleton()
    {
        var state = new DashboardState();
        var snapshot = Snapshot();

        state.BeginRefresh(silent: false);
        Assert.True(state.IsDashboardLoading);
        state.CompleteRefresh(snapshot);

        Assert.False(state.IsDashboardLoading);
        Assert.Single(state.TodaySessions);
        Assert.Same(snapshot.CurrentSession, state.CurrentSession);
        Assert.Same(snapshot.ActiveManualSession, state.ActiveManualSession);
        Assert.Same(snapshot.MonthlyStats, state.MonthlyStats);
        Assert.Same(snapshot.YearlyStats, state.YearlyStats);

        state.BeginRefresh(silent: true);
        Assert.False(state.IsDashboardLoading);
        state.FailRefresh("Dashboard unavailable.");

        Assert.Empty(state.TodaySessions);
        Assert.Empty(state.TodayPlatformTotals);
        Assert.Null(state.CurrentSession);
        Assert.Null(state.ActiveManualSession);
        Assert.Null(state.MonthlyStats);
        Assert.Null(state.YearlyStats);
        Assert.Equal("Dashboard unavailable.", state.DashboardError);
    }

    [Fact]
    public async Task AggregateRefreshReportsTheFirstFailureWithoutWaitingForAnUnresolvedSibling()
    {
        using var siblingCancellation = new CancellationTokenSource();
        var unresolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("first request failed");
        var aggregate = DashboardRequestFlow.WaitAsync(
            [unresolved.Task, Task.FromException(failure)],
            siblingCancellation);

        try
        {
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(
                () => aggregate.WaitAsync(TimeSpan.FromSeconds(1)));

            Assert.Same(failure, observed);
            Assert.True(siblingCancellation.IsCancellationRequested);
            Assert.False(unresolved.Task.IsCompleted);
        }
        finally
        {
            unresolved.TrySetResult();
        }
    }

    [Fact]
    public void DerivedCardsTotalsAndCurrentSessionMatchReactFormattingAndRounding()
    {
        var state = new DashboardState();
        state.CompleteRefresh(Snapshot(), Now);

        var card = Assert.Single(state.TodaySessionCards);
        Assert.Equal("Elden Ring", card.Title);
        Assert.Equal("1h 1m", card.Duration);
        Assert.Equal("Steam", card.Platform);
        Assert.Equal("/games/3H4h3", card.GamePath);

        Assert.Equal("1h", state.TodayHours);
        Assert.Equal(13, state.GetTodayPlatforms()[0].Value);

        var current = state.GetCurrentSession(Now);
        Assert.NotNull(current);
        Assert.Equal("PC", current.Platform);
        Assert.Equal("1h 1m", current.Elapsed);
        Assert.True(current.CanStop);

        var monthly = DashboardState.GetStatsView(state.MonthlyStats);
        Assert.Equal("2h", monthly.Hours);
        Assert.Equal("2", monthly.Sessions);
        Assert.Equal("Elden Ring", monthly.TopTitle);
    }

    [Fact]
    public void SyncedCurrentSessionRemainsVisibleWithoutManualStopAction()
    {
        var state = new DashboardState();
        state.CompleteRefresh(Snapshot() with { ActiveManualSession = null }, Now);

        var current = Assert.IsType<DashboardCurrentSessionView>(state.GetCurrentSession(Now));

        Assert.Equal("Hades II", current.Title);
        Assert.False(current.CanStop);
    }

    [Fact]
    public void TodaySessionCardsStayFrozenWhileCurrentElapsedAdvances()
    {
        var snapshot = Snapshot();
        var openTodaySession = snapshot.TodaySessions[0] with
        {
            StartedAt = Now.AddMinutes(-10),
            EndedAt = null,
        };
        snapshot = snapshot with { TodaySessions = [openTodaySession] };
        var state = new DashboardState();
        state.CompleteRefresh(snapshot, Now);
        var cardsBefore = state.TodaySessionCards;

        Assert.Same(cardsBefore, state.TodaySessionCards);
        Assert.Equal("10 min", Assert.Single(state.TodaySessionCards).Duration);
        Assert.NotEqual(
            state.GetCurrentSession(Now)?.Elapsed,
            state.GetCurrentSession(Now.AddMinutes(5))?.Elapsed);
    }

    [Fact]
    public void TodaySessionCardsArePaginatedByThreeWithClampedPage()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            TodaySessions = Enumerable.Range(1, 8)
                .Select(index => new AccountSession(
                    index,
                    Now.AddHours(-index),
                    Now.AddHours(-index).AddSeconds(3600),
                    "42",
                    "steam",
                    "Steam",
                    $"Game {index}",
                    null,
                    false,
                    null,
                    null))
                .ToArray(),
        };
        var state = new DashboardState();
        state.CompleteRefresh(snapshot, Now);

        Assert.Equal(8, state.TodaySessionCards.Count);
        Assert.Equal(3, state.TodaySessionsTotalPages);
        Assert.Equal([1L, 2, 3], state.TodaySessionPageItems.Select(item => item.SessionId));

        state.SetTodaySessionsPage(2);
        Assert.Equal([4L, 5, 6], state.TodaySessionPageItems.Select(item => item.SessionId));
        state.SetTodaySessionsPage(3);
        Assert.Equal([7L, 8], state.TodaySessionPageItems.Select(item => item.SessionId));
        state.SetTodaySessionsPage(99);
        Assert.Equal(3, state.TodaySessionsPage);
    }

    [Fact]
    public void PlatformRenderingUsesNamesWhilePreservingDistinctKeys()
    {
        var stats = new AccountStatsPeriod(
            7200,
            2,
            null,
            [
                new AccountPlatformStat("psn", "PlayStation Network", 3600, null),
                new AccountPlatformStat("ps5", "PlayStation 5", 3600, null),
            ],
            [],
            []);

        var view = DashboardState.GetStatsView(stats);

        Assert.Equal(
            ["PlayStation Network", "PlayStation 5"],
            view.Platforms.Select(item => item.Label));

        var root = FindWebRoot();
        var dashboard = File.ReadAllText(Path.Combine(root, "Modules", "Dashboard", "Dashboard.razor"));
        var recap = File.ReadAllText(Path.Combine(root, "Infrastructure", "Components", "StatsRecapSection.razor"));
        var pie = File.ReadAllText(Path.Combine(root, "Infrastructure", "Components", "StatsPieChart.razor"));
        Assert.DoesNotContain("@key=\"item.Label\"", dashboard);
        Assert.DoesNotContain("@key=\"item.Label\"", recap);
        Assert.DoesNotContain("@key=\"segment.Label\"", pie);
    }

    [Fact]
    public void DashboardPageWiresConcurrentRefreshAndLiteralRegionsWithoutPolling()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Modules", "Dashboard", "Dashboard.razor");
        Assert.True(File.Exists(markupPath), "Missing Dashboard Razor page.");

        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(Path.Combine(root, "Modules", "Dashboard", "Dashboard.razor.cs"));

        Assert.Contains("@page \"/dashboard\"", markup);
        Assert.Contains("<AppLayout Title=\"Dashboard\"", markup);
        Assert.Contains("Current session", markup);
        Assert.Contains("Today stats", markup);
        Assert.Contains("Today sessions", markup);
        Assert.Contains("Title=\"Monthly stats\"", markup);
        Assert.Contains("Title=\"Yearly stats\"", markup);
        Assert.DoesNotContain("<ManualSessionModal", markup);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token)", code);
        Assert.Contains("DashboardRequestFlow.WaitAsync", code);
        Assert.True(code.Split("cancellationToken: refreshToken").Length - 1 >= 5);
        Assert.Contains("SendAsync<AccountSessionsResponse>(requests.TodaySessions", code);
        Assert.Contains("SendAsync<AccountStatsRangeResponse>(requests.TodayStats", code);
        Assert.Contains("SendAsync<AccountStatsRangeResponse>(requests.MonthlyStats", code);
        Assert.Contains("SendAsync<AccountStatsRangeResponse>(requests.YearlyStats", code);
        Assert.Contains("SendAsync<AccountCurrentSessionResponse>(requests.CurrentSession", code);
        Assert.Contains("SendAsync<ManualActiveSessionResponse>(requests.ActiveManualSession", code);
        Assert.DoesNotContain("SendAsync<ManualInputsResponse>", code);
        Assert.DoesNotContain("SendAsync<ManualSessionStartResponse>", code);
        Assert.Contains("SendAsync<ManualSessionStopResponse>", code);
        Assert.Contains("\"/manual/sessions/stop\"", code);
        Assert.Contains("RefreshDashboardAsync(silent: true)", code);
        Assert.Contains("Stop session", markup);
        Assert.Contains("disabled=\"@_isStoppingManualSession\"", markup);
        Assert.Contains("role=\"alert\"", markup);
        Assert.Contains("_lifetime.Token.ThrowIfCancellationRequested()", code);
        Assert.Contains("catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)", code);
        Assert.Contains("_lifetime.Cancel()", code);
        Assert.DoesNotContain("PeriodicTimer", code);
        Assert.DoesNotContain("Task.Delay", code);
    }

    [Fact]
    public void DashboardPiePaletteFollowsEveryAppLayoutThemeChange()
    {
        var root = FindWebRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Layout", "AppLayout.razor"));
        var markup = File.ReadAllText(Path.Combine(root, "Modules", "Dashboard", "Dashboard.razor"));
        var code = File.ReadAllText(Path.Combine(root, "Modules", "Dashboard", "Dashboard.razor.cs"));

        Assert.Contains("public EventCallback<string> OnThemeChanged", layout);
        Assert.Contains("OnThemeChanged.InvokeAsync(_theme)", layout);
        Assert.Contains("OnThemeChanged=\"SetTheme\"", markup);
        Assert.DoesNotContain("Storage.GetAsync(ThemeKey)", code);
    }

    [Fact]
    public void AppLayoutRendersOverlayInsideMainAfterTheContentWrapperFooter()
    {
        var root = FindWebRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Layout", "AppLayout.razor"));
        var footerIndex = layout.IndexOf("<AppFooter", StringComparison.Ordinal);
        var overlayIndex = layout.IndexOf("@Overlay", StringComparison.Ordinal);
        var mainCloseIndex = layout.IndexOf("</main>", StringComparison.Ordinal);

        Assert.True(footerIndex >= 0, "AppLayout must render its footer.");
        Assert.True(overlayIndex > footerIndex, "The overlay must follow the content wrapper footer.");
        Assert.True(overlayIndex < mainCloseIndex, "The overlay must remain inside the main element.");
    }

    private static DashboardSnapshot Snapshot()
    {
        var completed = new AccountSession(
            1,
            Now.AddHours(-2),
            Now.AddSeconds(-3539),
            "42",
            "steam",
            "Steam",
            "Elden Ring",
            "https://images.test/elden.jpg",
            false,
            "psn",
            "local");
        var current = new AccountSession(
            2,
            Now.AddSeconds(-3661),
            null,
            "43",
            "manual",
            "Manual",
            "Hades II",
            null,
            false,
            null,
            null);
        var todayStats = new AccountStatsPeriod(
            3600,
            1,
            "Elden Ring",
            [
                new AccountPlatformStat("steam", "Steam", 450, null),
                new AccountPlatformStat("psn", "PlayStation Network", 3150, null),
            ],
            [new AccountTopGame("42", "Elden Ring", 3600)],
            []);
        var monthly = new AccountStatsPeriod(
            7200,
            2,
            "Elden Ring",
            [new AccountPlatformStat("steam", "Steam", 7200, null)],
            [new AccountTopGame("42", "Elden Ring", 7200)],
            []);
        var yearly = new AccountStatsPeriod(
            10800,
            3,
            "Hades II",
            [new AccountPlatformStat("manual", "Manual", 10800, null)],
            [new AccountTopGame("43", "Hades II", 10800)],
            []);

        var activeManualSession = new ManualActiveSession(
            2,
            "43",
            "Hades II",
            "pc",
            "PC",
            Now.AddSeconds(-3661),
            null);

        return new DashboardSnapshot(
            [completed], todayStats.Platforms, current, activeManualSession, monthly, yearly);
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
