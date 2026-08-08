using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Web.Infrastructure.Http;
using Web.Infrastructure.Auth;
using Web.Infrastructure.Browser;
using Web.Infrastructure.Models;
using Web.Routing;
using Web.Infrastructure.Common;

[assembly: InternalsVisibleTo("Web.Tests")]

namespace Web.Modules.Dashboard;

internal sealed record DashboardRequests(
    string TodaySessions,
    string TodayStats,
    string MonthlyStats,
    string YearlyStats,
    string CurrentSession)
{
    public IReadOnlyList<string> All =>
        [TodaySessions, TodayStats, MonthlyStats, YearlyStats, CurrentSession];

    public static DashboardRequests For(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        var today = Formatters.FormatUtcDateInput(now);
        var monthStart = $"{utc.Year:0000}-{utc.Month:00}-01";
        var yearStart = $"{utc.Year:0000}-01-01";

        return new DashboardRequests(
            $"/sessions?limit=10&offset=0&from={today}&to={today}&sort=date&direction=desc",
            $"/stats?from={today}&to={today}",
            $"/stats?from={monthStart}&to={today}",
            $"/stats?from={yearStart}&to={today}",
            "/sessions/current");
    }
}

internal sealed record DashboardSnapshot(
    IReadOnlyList<AccountSession> TodaySessions,
    IReadOnlyList<AccountPlatformStat> TodayPlatformTotals,
    AccountSession? CurrentSession,
    AccountStatsPeriod MonthlyStats,
    AccountStatsPeriod YearlyStats);

internal sealed record DashboardSessionCard(
    long SessionId,
    string GameId,
    string GamePath,
    string Title,
    string Duration,
    string Platform,
    string? ImageUrl);

internal sealed record DashboardPlatformView(string Label, double Value, string Hours);
internal sealed record DashboardTopGameView(string GameId, string Title, string Hours);
internal sealed record DashboardStatsView(
    string Hours,
    string Sessions,
    string TopTitle,
    IReadOnlyList<DashboardPlatformView> Platforms,
    IReadOnlyList<DashboardTopGameView> TopGames);
internal sealed record DashboardCurrentSessionView(
    string GameId,
    string GamePath,
    string Title,
    string Platform,
    string StartedAt,
    string Elapsed);

internal static class DashboardRequestFlow
{
    public static async Task WaitAsync(
        IReadOnlyCollection<Task> requests,
        CancellationTokenSource siblingCancellation)
    {
        var pending = requests.ToList();
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);

            try
            {
                await completed;
            }
            catch
            {
                siblingCancellation.Cancel();
                _ = ObserveAsync(pending);
                throw;
            }
        }
    }

    private static async Task ObserveAsync(IReadOnlyCollection<Task> requests)
    {
        try
        {
            await Task.WhenAll(requests);
        }
        catch
        {
        }
    }
}

internal sealed class DashboardState
{
    private const int TodaySessionsPageSize = 3;

    public IReadOnlyList<AccountSession> TodaySessions { get; private set; } = [];
    public IReadOnlyList<DashboardSessionCard> TodaySessionCards { get; private set; } = [];
    public IReadOnlyList<AccountPlatformStat> TodayPlatformTotals { get; private set; } = [];
    public AccountSession? CurrentSession { get; private set; }
    public AccountStatsPeriod? MonthlyStats { get; private set; }
    public AccountStatsPeriod? YearlyStats { get; private set; }
    public bool IsDashboardLoading { get; private set; }
    public string DashboardError { get; private set; } = string.Empty;
    public int TodaySessionsPage { get; private set; } = 1;
    public string TodayHours => Formatters.FormatHours(
        TodayPlatformTotals.Sum(item => item.TotalSeconds),
        1);

    public void BeginRefresh(bool silent)
    {
        if (!silent) IsDashboardLoading = true;
        DashboardError = string.Empty;
    }

    public void CompleteRefresh(DashboardSnapshot snapshot) =>
        CompleteRefresh(snapshot, DateTimeOffset.Now);

    public void CompleteRefresh(DashboardSnapshot snapshot, DateTimeOffset now)
    {
        TodaySessions = snapshot.TodaySessions;
        TodaySessionCards = CreateTodaySessionCards(snapshot.TodaySessions, now);
        TodayPlatformTotals = snapshot.TodayPlatformTotals;
        CurrentSession = snapshot.CurrentSession;
        MonthlyStats = snapshot.MonthlyStats;
        YearlyStats = snapshot.YearlyStats;
        IsDashboardLoading = false;
        TodaySessionsPage = 1;
    }

    public void FailRefresh(string error)
    {
        TodaySessions = [];
        TodaySessionCards = [];
        TodayPlatformTotals = [];
        CurrentSession = null;
        MonthlyStats = null;
        YearlyStats = null;
        IsDashboardLoading = false;
        DashboardError = error;
        TodaySessionsPage = 1;
    }

    public int TodaySessionsTotalPages =>
        Math.Max(1, (int)Math.Ceiling(TodaySessionCards.Count / (double)TodaySessionsPageSize));

    public IReadOnlyList<DashboardSessionCard> TodaySessionPageItems => TodaySessionCards
        .Skip((TodaySessionsPage - 1) * TodaySessionsPageSize)
        .Take(TodaySessionsPageSize)
        .ToArray();

    public void SetTodaySessionsPage(int page) =>
        TodaySessionsPage = Math.Clamp(page, 1, TodaySessionsTotalPages);

    private static DashboardSessionCard[] CreateTodaySessionCards(
        IReadOnlyList<AccountSession> sessions,
        DateTimeOffset now) =>
        sessions.Select(session => new DashboardSessionCard(
            session.SessionId,
            session.GameId,
            $"/games/{GameHash.EncodeGameId(session.GameId)}",
            session.Game,
            Formatters.FormatDuration(
                SessionDates.GetDurationSeconds(session.StartedAt, session.EndedAt, now),
                DurationFormat.Full),
            Formatters.FormatSessionPlatformLabel(session.Platform, session.MappedPlatform),
            session.CapsuleUrl)).ToArray();

    public IReadOnlyList<DashboardPlatformView> GetTodayPlatforms() =>
        CreatePlatforms(TodayPlatformTotals, TodayPlatformTotals.Sum(item => item.TotalSeconds));

    public DashboardCurrentSessionView? GetCurrentSession(DateTimeOffset now)
    {
        if (CurrentSession is null) return null;

        return new DashboardCurrentSessionView(
            CurrentSession.GameId,
            $"/games/{GameHash.EncodeGameId(CurrentSession.GameId)}",
            CurrentSession.Game,
            Formatters.FormatSessionPlatformLabel(CurrentSession.Platform, CurrentSession.MappedPlatform),
            Formatters.FormatTimeLabel(CurrentSession.StartedAt),
            Formatters.FormatDuration(
                SessionDates.GetDurationSeconds(CurrentSession.StartedAt, CurrentSession.EndedAt, now),
                DurationFormat.Full));
    }

    public static DashboardStatsView GetStatsView(AccountStatsPeriod? stats)
    {
        if (stats is null)
        {
            return new DashboardStatsView("0h", "0", "—", [], []);
        }

        return new DashboardStatsView(
            Formatters.FormatHours(stats.TotalSeconds, 1),
            stats.SessionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            stats.TopGameTitle ?? "—",
            CreatePlatforms(stats.Platforms, stats.TotalSeconds),
            stats.TopGames.Select(game => new DashboardTopGameView(
                game.GameId,
                game.Title,
                Formatters.FormatHours(game.TotalSeconds, 1))).ToArray());
    }

    private static DashboardPlatformView[] CreatePlatforms(
        IReadOnlyList<AccountPlatformStat> platforms,
        double totalSeconds)
    {
        var safeTotal = Math.Max(totalSeconds, 1);
        return platforms.Select(item => new DashboardPlatformView(
            Formatters.FormatStatsPlatformLabel(item.Platform),
            Math.Floor(item.TotalSeconds / safeTotal * 100 + 0.5),
            Formatters.FormatHours(item.TotalSeconds, 1))).ToArray();
    }
}

public partial class Dashboard : ComponentBase, IDisposable
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly IReadOnlyList<string> LightPieColors =
        ["#059669", "#0e7490", "#7c3aed", "#d97706"];
    private static readonly IReadOnlyList<string> DarkPieColors =
        ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"];

    private readonly CancellationTokenSource _lifetime = new();
    private readonly DashboardState _state = new();
    private string _theme = "light";

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private AuthSession AuthSession { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    private IReadOnlyList<string> PlatformPieColors =>
        _theme == "dark" ? DarkPieColors : LightPieColors;
    private IReadOnlyList<DashboardSessionCard> TodaySessionCards => _state.TodaySessionCards;
    private IReadOnlyList<DashboardPlatformView> TodayPlatforms => _state.GetTodayPlatforms();
    private DashboardCurrentSessionView? CurrentSession => _state.GetCurrentSession(DateTimeOffset.Now);
    private DashboardStatsView Monthly => DashboardState.GetStatsView(_state.MonthlyStats);
    private DashboardStatsView Yearly => DashboardState.GetStatsView(_state.YearlyStats);
    private IReadOnlyList<(string Label, double Value)> TodayPieSegments =>
        TodayPlatforms.Select(item => (item.Label, item.Value)).ToArray();
    private IReadOnlyList<(string Label, string Value)> MonthlySummary =>
    [
        ("Hours played", Monthly.Hours),
        ("Sessions", Monthly.Sessions),
        ("Top title", Monthly.TopTitle),
    ];
    private IReadOnlyList<(string Label, string Value)> YearlySummary =>
    [
        ("Hours played", Yearly.Hours),
        ("Sessions", Yearly.Sessions),
        ("Top title", Yearly.TopTitle),
    ];
    private IReadOnlyList<(string Label, double Value, string Hours)> MonthlyPlatforms =>
        Monthly.Platforms.Select(item => (item.Label, item.Value, item.Hours)).ToArray();
    private IReadOnlyList<(string Label, double Value, string Hours)> YearlyPlatforms =>
        Yearly.Platforms.Select(item => (item.Label, item.Value, item.Hours)).ToArray();
    private IReadOnlyList<(string GameId, string Title, string Hours)> MonthlyTopGames =>
        Monthly.TopGames.Select(item => (item.GameId, item.Title, item.Hours)).ToArray();
    private IReadOnlyList<(string GameId, string Title, string Hours)> YearlyTopGames =>
        Yearly.TopGames.Select(item => (item.GameId, item.Title, item.Hours)).ToArray();
    private static string MonthlyHighlightsLabel => $"{DateTime.Now.ToString("MMMM", EnUs)} highlights";
    private static string YearlyRecapLabel => $"{DateTime.Now.Year} recap";

    protected override async Task OnInitializedAsync()
    {
        _state.BeginRefresh(silent: false);
        await RefreshDashboardAsync();
    }

    private void SetTheme(string theme) => _theme = theme;

    private async Task RefreshDashboardAsync(bool silent = false)
    {
        _state.BeginRefresh(silent);
        try
        {
            var requests = DashboardRequests.For(DateTimeOffset.Now);
            using var siblingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var refreshToken = siblingCancellation.Token;
            var todaySessionsTask = Api.SendAsync<AccountSessionsResponse>(requests.TodaySessions,
                onAuthFailure: LogoutAsync,
                cancellationToken: refreshToken);
            var todayStatsTask = Api.SendAsync<AccountStatsRangeResponse>(requests.TodayStats,
                onAuthFailure: LogoutAsync,
                cancellationToken: refreshToken);
            var monthlyStatsTask = Api.SendAsync<AccountStatsRangeResponse>(requests.MonthlyStats,
                onAuthFailure: LogoutAsync,
                cancellationToken: refreshToken);
            var yearlyStatsTask = Api.SendAsync<AccountStatsRangeResponse>(requests.YearlyStats,
                onAuthFailure: LogoutAsync,
                cancellationToken: refreshToken);
            var currentSessionTask = Api.SendAsync<AccountCurrentSessionResponse>(requests.CurrentSession,
                onAuthFailure: LogoutAsync,
                cancellationToken: refreshToken);

            await DashboardRequestFlow.WaitAsync(
                [todaySessionsTask, todayStatsTask, monthlyStatsTask, yearlyStatsTask, currentSessionTask],
                siblingCancellation);
            _lifetime.Token.ThrowIfCancellationRequested();

            _state.CompleteRefresh(
                new DashboardSnapshot(
                    (await todaySessionsTask).Sessions,
                    (await todayStatsTask).Stats.Platforms,
                    (await currentSessionTask).Session,
                    (await monthlyStatsTask).Stats,
                    (await yearlyStatsTask).Stats),
                DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailRefresh(exception.Message);
        }
    }

    private async Task NavigateAsync(string target)
    {
        var instruction = NavigationGuard.PlanNavigation(
            new Uri(Navigation.Uri).PathAndQuery,
            target,
            replaceHistory: false);
        if (instruction.Kind == NavigationInstructionKind.NoOp) return;

        Navigation.NavigateTo(instruction.Target!);
        if (instruction.ScrollToTop) await Browser.ScrollToTopAsync();
    }

    private Task NavigateToTodayRecapAsync() => NavigateAsync(RecapPath(DateRangeKind.Today));
    private Task NavigateToMonthlyRecapAsync() => NavigateAsync(RecapPath(DateRangeKind.Month));
    private Task NavigateToYearlyRecapAsync() => NavigateAsync(RecapPath(DateRangeKind.Year));
    private Task NavigateToSessionsAsync() => NavigateAsync("/sessions");
    private Task NavigateToGameAsync(string gameId) => NavigateAsync($"/games/{GameHash.EncodeGameId(gameId)}");

    private static string RecapPath(DateRangeKind range)
    {
        var now = DateTimeOffset.Now;
        var utc = now.UtcDateTime;
        var today = Formatters.FormatUtcDateInput(now);
        var from = range switch
        {
            DateRangeKind.Today => today,
            DateRangeKind.Month => $"{utc.Year:0000}-{utc.Month:00}-01",
            _ => $"{utc.Year:0000}-01-01",
        };
        return $"/recap?from={from}&to={today}";
    }

    private async Task LogoutAsync()
    {
        await AuthSession.LogoutAsync();
        Navigation.NavigateTo("/", forceLoad: true);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private enum DateRangeKind
    {
        Today,
        Month,
        Year,
    }
}
