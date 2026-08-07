using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Playdex.Web.Shared.Http;
using Playdex.Web.Shared.Auth;
using Playdex.Web.Shared.Browser;
using Playdex.Web.Shared.Models;
using Playdex.Web.Routing;
using Playdex.Web.Shared.Common;

[assembly: InternalsVisibleTo("Playdex.Web.Tests")]

namespace Playdex.Web.Modules.Dashboard;

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
    string Elapsed,
    bool IsManual);

internal enum DashboardRefreshMode
{
    Silent,
}

internal static class DashboardManualStartFlow
{
    public static async Task CompleteAsync(
        Action closeModal,
        Func<Task> renderClosedModal,
        Func<Task> loadManualInputs,
        Func<Task> refreshDashboard,
        CancellationToken cancellationToken)
    {
        closeModal();
        await renderClosedModal();
        cancellationToken.ThrowIfCancellationRequested();
        await loadManualInputs();
        cancellationToken.ThrowIfCancellationRequested();
        await refreshDashboard();
    }
}

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
    public IReadOnlyList<AccountSession> TodaySessions { get; private set; } = [];
    public IReadOnlyList<DashboardSessionCard> TodaySessionCards { get; private set; } = [];
    public IReadOnlyList<AccountPlatformStat> TodayPlatformTotals { get; private set; } = [];
    public AccountSession? CurrentSession { get; private set; }
    public AccountStatsPeriod? MonthlyStats { get; private set; }
    public AccountStatsPeriod? YearlyStats { get; private set; }
    public bool IsDashboardLoading { get; private set; }
    public string DashboardError { get; private set; } = string.Empty;
    public IReadOnlyList<ManualInput> ManualInputs { get; private set; } = [];
    public bool IsManualInputsLoading { get; private set; }
    public string ManualInputsError { get; private set; } = string.Empty;
    public bool IsManualModalOpen { get; private set; }
    public string ManualInputSelection { get; private set; } = string.Empty;
    public string ManualInputText { get; private set; } = string.Empty;
    public string ManualStartError { get; private set; } = string.Empty;
    public bool IsManualStartLoading { get; private set; }
    public bool IsManualStopLoading { get; private set; }
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
    }

    private static IReadOnlyList<DashboardSessionCard> CreateTodaySessionCards(
        IReadOnlyList<AccountSession> sessions,
        DateTimeOffset now) =>
        sessions.Select(session => new DashboardSessionCard(
            session.SessionId,
            session.GameId,
            $"/games/{GameHash.EncodeGameId(session.GameId)}",
            session.Game,
            Formatters.FormatDuration(
                SessionDates.GetDurationSeconds(session.StartedAt, session.EndedAt, now),
                DurationFormat.Long),
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
                DurationFormat.Long),
            CurrentSession.Platform == "manual");
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

    public void OpenManualModal() => IsManualModalOpen = true;

    public void CloseManualModal()
    {
        IsManualModalOpen = false;
        ManualInputSelection = string.Empty;
        ManualInputText = string.Empty;
        ManualStartError = string.Empty;
    }

    public void BeginManualInputs()
    {
        IsManualInputsLoading = true;
        ManualInputsError = string.Empty;
    }

    public void CompleteManualInputs(IReadOnlyList<ManualInput> inputs)
    {
        ManualInputs = inputs;
        IsManualInputsLoading = false;
    }

    public void FailManualInputs(string error)
    {
        ManualInputs = [];
        IsManualInputsLoading = false;
        ManualInputsError = error;
    }

    public void SelectManualInput(string value)
    {
        ManualInputSelection = value;
        ManualInputText = value;
    }

    public void SetManualInputText(string value) => ManualInputText = value;

    public bool TryBeginManualStart(out string title)
    {
        title = ManualInputText.Trim();
        if (title.Length == 0)
        {
            ManualStartError = "Enter a title to start a session.";
            return false;
        }

        IsManualStartLoading = true;
        ManualStartError = string.Empty;
        return true;
    }

    public DashboardRefreshMode ManualStartSucceeded()
    {
        CloseManualModal();
        return DashboardRefreshMode.Silent;
    }

    public void FailManualStart(string error) => ManualStartError = error;
    public void EndManualStart() => IsManualStartLoading = false;

    public void BeginManualStop()
    {
        IsManualStopLoading = true;
        DashboardError = string.Empty;
    }

    public DashboardRefreshMode ManualStopSucceeded() => DashboardRefreshMode.Silent;
    public void FailManualStop(string error) => DashboardError = error;
    public void EndManualStop() => IsManualStopLoading = false;

    private static IReadOnlyList<DashboardPlatformView> CreatePlatforms(
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
    private string MonthlyHighlightsLabel => $"{DateTime.Now.ToString("MMMM", EnUs)} highlights";
    private string YearlyRecapLabel => $"{DateTime.Now.Year} recap";

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

    private async Task OpenManualModalAsync()
    {
        _state.OpenManualModal();
        await LoadManualInputsAsync();
    }

    private async Task LoadManualInputsAsync()
    {
        _state.BeginManualInputs();
        try
        {
            var response = await Api.SendAsync<ManualInputsResponse>("/manual/inputs",
                cancellationToken: _lifetime.Token);
            _state.CompleteManualInputs(response.Inputs);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailManualInputs(exception.Message);
        }
    }

    private async Task StartManualSessionAsync()
    {
        if (!_state.TryBeginManualStart(out var title)) return;

        try
        {
            await Api.SendAsync<ManualSessionStartResponse>("/manual/sessions/start",
                HttpMethod.Post,
                new ManualSessionStartRequest(title),
                cancellationToken: _lifetime.Token);
            await DashboardManualStartFlow.CompleteAsync(
                () => { _state.ManualStartSucceeded(); },
                RenderManualCloseAsync,
                LoadManualInputsAsync,
                () => RefreshDashboardAsync(silent: true),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailManualStart(exception.Message);
        }
        finally
        {
            _state.EndManualStart();
        }
    }

    private async Task RenderManualCloseAsync()
    {
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    private async Task StopManualSessionAsync()
    {
        _state.BeginManualStop();
        try
        {
            await Api.SendAsync<ManualSessionStopResponse>("/manual/sessions/stop",
                HttpMethod.Post,
                cancellationToken: _lifetime.Token);
            _state.ManualStopSucceeded();
            await RefreshDashboardAsync(silent: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailManualStop(exception.Message);
        }
        finally
        {
            _state.EndManualStop();
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

    private string RecapPath(DateRangeKind range)
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
    }

    private enum DateRangeKind
    {
        Today,
        Month,
        Year,
    }
}
