using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Playdex.Web.Api;
using Playdex.Web.Auth;
using Playdex.Web.Browser;
using Playdex.Web.Models;
using Playdex.Web.Routing;
using Playdex.Web.Utilities;

namespace Playdex.Web.Pages;

internal sealed record RecapTab(string Key, string Label, string? From, string? To);

internal sealed record RecapWeekdayChart(
    double Width,
    double Height,
    string LinePoints,
    string AreaPath,
    IReadOnlyList<(double X, double Y)> Points)
{
    public static RecapWeekdayChart Build(IReadOnlyList<WeekdayStat> stats)
    {
        const double width = 240;
        const double height = 80;
        const double padding = 8;
        if (stats.Count == 0) return new(width, height, string.Empty, string.Empty, []);

        var maximum = Math.Max(1, stats.Max(item => item.SessionCount));
        var step = (width - padding * 2) / Math.Max(1, stats.Count - 1);
        var points = stats.Select((item, index) => (
            X: padding + index * step,
            Y: height - padding - item.SessionCount / (double)maximum * (height - padding * 2))).ToArray();
        var line = string.Join(" ", points.Select(point => $"{Number(point.X)},{Number(point.Y)}"));
        var area = $"M {Number(padding)},{Number(height - padding)} L {line} L {Number(width - padding)},{Number(height - padding)} Z";
        return new(width, height, line, area, points);
    }

    private static string Number(double value) => value.ToString("0.###############", CultureInfo.InvariantCulture);
}

internal sealed class RecapState
{
    private bool _defaultLoaded;

    public IReadOnlyList<int> Years { get; private set; } = [];
    public bool YearsLoaded { get; private set; }
    public string ActiveTab { get; private set; } = "all-time";
    public bool HasSearchRange { get; private set; }
    public string CustomFrom { get; private set; } = string.Empty;
    public string CustomTo { get; private set; } = string.Empty;
    public RecapResponse? Recap { get; private set; }
    public bool IsLoading { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public IReadOnlyList<RecapTab> Tabs =>
    [
        new("all-time", "All time", null, null),
        .. Years.Select(year => new RecapTab(
            $"year-{year}",
            year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{year:0000}-01-01",
            $"{year:0000}-12-31")),
        new("custom", "Custom", null, null),
    ];

    public string BeginYearsLoad()
    {
        YearsLoaded = false;
        return "/recap/years";
    }

    public void CompleteYears(RecapYearsResponse response)
    {
        Years = response.Years;
        YearsLoaded = true;
    }

    public void FailYears(string error)
    {
        Years = [];
        YearsLoaded = true;
        Error = error;
    }

    public string? TakeDefaultRequest()
    {
        if (!YearsLoaded || HasSearchRange || _defaultLoaded) return null;
        ActiveTab = "all-time";
        _defaultLoaded = true;
        return "/recap";
    }

    public string? ApplyLocationQuery(string query)
    {
        var values = ParseQuery(query);
        values.TryGetValue("from", out var from);
        values.TryGetValue("to", out var to);
        if (from?.Length > 0 && to?.Length > 0)
        {
            HasSearchRange = true;
            CustomFrom = from;
            CustomTo = to;
            ActiveTab = "custom";
            return BuildRangeRequest(from, to);
        }

        HasSearchRange = false;
        _defaultLoaded = false;
        return null;
    }

    public string? SelectTab(string key)
    {
        ActiveTab = key;
        if (key == "all-time") return "/recap";
        if (key == "custom")
        {
            return CustomFrom.Length > 0 && CustomTo.Length > 0
                ? BuildRangeRequest(CustomFrom, CustomTo)
                : null;
        }

        var tab = Tabs.First(item => item.Key == key);
        return BuildRangeRequest(tab.From!, tab.To!);
    }

    public void SetCustomFrom(string from) => CustomFrom = from;

    public void SetCustomTo(string to) => CustomTo = to;

    public string? BuildCustomNavigation() =>
        CustomFrom.Length > 0 && CustomTo.Length > 0
            ? BuildRangeRequest(CustomFrom, CustomTo)
            : null;

    public void BeginLoad()
    {
        IsLoading = true;
        Error = string.Empty;
    }

    public void CompleteLoad(RecapResponse recap)
    {
        Recap = recap;
        IsLoading = false;
    }

    public void FailLoad(string error)
    {
        Recap = null;
        IsLoading = false;
        Error = error;
    }

    private static string BuildRangeRequest(string from, string to) =>
        $"/recap?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = WebUtility.UrlDecode(pair[0]);
            var value = pair.Length == 2
                ? WebUtility.UrlDecode(pair[1])
                : string.Empty;
            values.TryAdd(key, value);
        }
        return values;
    }
}

internal static class RecapInitialCustomFlow
{
    public static async Task RunAsync(
        RecapState state,
        string customRequest,
        Func<Task> loadYears,
        Func<string, Task> loadRecap)
    {
        var yearsAndFallbackTask = LoadYearsAndFallbackAsync();
        var customTask = loadRecap(customRequest);
        await Task.WhenAll(yearsAndFallbackTask, customTask);

        async Task LoadYearsAndFallbackAsync()
        {
            await loadYears();
            var fallbackRequest = state.TakeDefaultRequest();
            if (fallbackRequest is not null) await loadRecap(fallbackRequest);
        }
    }
}

public partial class Recap : ComponentBase, IDisposable
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly IReadOnlyList<string> WeekdayLabels =
        ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    private static readonly IReadOnlyList<string> PlatformBarClasses =
    [
        "bg-emerald-400",
        "bg-teal-400",
        "bg-cyan-400",
        "bg-sky-400",
        "bg-indigo-400",
        "bg-violet-400",
    ];
    private static readonly IReadOnlyList<string> PlatformDotClasses =
    [
        "bg-emerald-400/80",
        "bg-teal-400/80",
        "bg-cyan-400/80",
        "bg-sky-400/80",
        "bg-indigo-400/80",
        "bg-violet-400/80",
    ];

    private readonly CancellationTokenSource _lifetime = new();
    private readonly RecapState _state = new();
    private CancellationTokenSource? _recapRequest;
    private long _recapRequestVersion;
    private bool _initialized;
    private string _theme = "light";

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private AuthSession AuthSession { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "from")]
    private string? QueryFrom { get; set; }

    [SupplyParameterFromQuery(Name = "to")]
    private string? QueryTo { get; set; }

    private RecapResponse? CurrentRecap => _state.Recap;
    private IReadOnlyList<RecapTab> Tabs => _state.Tabs;
    private RecapWeekdayChart WeekdayChart => RecapWeekdayChart.Build(CurrentRecap?.SessionsByWeekday ?? []);
    private int HourMaximum => Math.Max(1, CurrentRecap?.SessionsByHour.Select(item => item.SessionCount).DefaultIfEmpty().Max() ?? 0);
    private int DaysPlayed => CurrentRecap?.DaysPlayed ?? 0;
    private int DaysNotPlayed => CurrentRecap?.DaysNotPlayed ?? 0;
    private int TotalDays => DaysPlayed + DaysNotPlayed;
    private double DaysPlayedPercent => TotalDays > 0 ? DaysPlayed / (double)TotalDays * 100 : 0;
    private int DaysPlayedPercentRounded => RoundPercent(DaysPlayedPercent);
    private double PlatformTotalSeconds => CurrentRecap?.PlatformTotals.Sum(item => item.TotalSeconds) ?? 0;
    private double TimeSplitTotalSeconds =>
        (CurrentRecap?.WeekendSeconds ?? 0) + (CurrentRecap?.WeekdaySeconds ?? 0);
    private double WeekendShare => TimeSplitTotalSeconds > 0
        ? (CurrentRecap?.WeekendSeconds ?? 0) / TimeSplitTotalSeconds
        : 0;
    private double WeekdayShare => TimeSplitTotalSeconds > 0
        ? (CurrentRecap?.WeekdaySeconds ?? 0) / TimeSplitTotalSeconds
        : 0;
    private int WeekendSharePercent => RoundPercent(WeekendShare * 100);
    private int WeekdaySharePercent => RoundPercent(WeekdayShare * 100);
    private string DaysStrokeDasharray =>
        $"{Number(DaysPlayedPercent)} {Number(100 - DaysPlayedPercent)}";
    private string WeekdayViewBox =>
        $"0 0 {Number(WeekdayChart.Width)} {Number(WeekdayChart.Height)}";
    private string RangeLabel
    {
        get
        {
            var range = CurrentRecap?.Range;
            return range?.From is not { Length: > 0 } from || range.To is not { Length: > 0 } to
                ? "All time"
                : $"{FormatDate(from)} — {FormatDate(to)}";
        }
    }

    private IReadOnlyList<(string Label, string Value)> VolumeStats => CurrentRecap is { } recap
        ?
        [
            ("Total sessions", recap.TotalSessions.ToString(CultureInfo.InvariantCulture)),
            ("Total time", Formatters.FormatHours(recap.TotalSeconds, 1)),
        ]
        : [];

    private IReadOnlyList<(string Label, string Value)> SessionLengthStats => CurrentRecap is { } recap
        ?
        [
            ("Avg session length", Formatters.FormatDuration(recap.AverageSessionSeconds, DurationFormat.Long)),
            ("Median session length", Formatters.FormatDuration(recap.MedianSessionSeconds, DurationFormat.Long)),
            ("Longest session", Formatters.FormatDuration(recap.LongestSessionSeconds, DurationFormat.Long)),
            ("Shortest session", Formatters.FormatDuration(recap.ShortestSessionSeconds, DurationFormat.Long)),
        ]
        : [];

    private IReadOnlyList<(string Label, string Value)> CadenceStats => CurrentRecap is { } recap
        ?
        [
            ("Avg sessions / day", FormatAverage(recap.AverageSessionsPerDay)),
            ("Avg sessions / week", FormatAverage(recap.AverageSessionsPerWeek)),
            ("Avg sessions / month", FormatAverage(recap.AverageSessionsPerMonth)),
            ("Most active day", recap.MostActiveDay is { } day
                ? $"{FormatDate(day.Date)} · {day.SessionCount}"
                : "—"),
            ("Most active month", recap.MostActiveMonth is { } month
                ? $"{FormatMonthLabel(month.Month)} · {month.SessionCount}"
                : "—"),
            ("Longest streak", $"{recap.LongestStreakDays} days"),
        ]
        : [];

    private IReadOnlyList<(string Label, string Value)> GameMixStats => CurrentRecap is { } recap
        ?
        [
            ("Unique games played", recap.UniqueGames.ToString(CultureInfo.InvariantCulture)),
            ("New games tried", recap.NewGamesTried.ToString(CultureInfo.InvariantCulture)),
            ("Games abandoned", recap.AbandonedGames.ToString(CultureInfo.InvariantCulture)),
            ("Return rate games", recap.ReturnRateGames.ToString(CultureInfo.InvariantCulture)),
            ("Top 3 time share", $"{RoundPercent(recap.TimeOnTop3Percent)}%"),
            ("Most binged game", recap.MostBingedGame is { } game
                ? $"{game.Title} · {game.SessionCount}"
                : "—"),
        ]
        : [];

    private IReadOnlyList<(string Label, string Value)> GameHighlightStats => CurrentRecap is { } recap
        ?
        [
            ("Top game by sessions", recap.TopGameBySessions is { } sessions
                ? $"{sessions.Title} · {sessions.SessionCount}"
                : "—"),
            ("Top game by time", recap.TopGameByTime is { } time
                ? $"{time.Title} · {Formatters.FormatHours(time.TotalSeconds, 1)}"
                : "—"),
            ("Top game by avg session", recap.TopGameByAverageSession is { } average
                ? $"{average.Title} · {Formatters.FormatDuration(average.AverageSessionSeconds, DurationFormat.Long)}"
                : "—"),
            ("Longest session game", recap.GameWithLongestSession is { } longest
                ? $"{longest.Title} · {Formatters.FormatDuration(longest.SessionSeconds, DurationFormat.Long)}"
                : "—"),
        ]
        : [];

    protected override async Task OnParametersSetAsync()
    {
        var firstRender = !_initialized;
        _initialized = true;
        var request = _state.ApplyLocationQuery(new Uri(Navigation.Uri).Query);

        if (firstRender)
        {
            if (request is not null)
            {
                await RecapInitialCustomFlow.RunAsync(
                    _state,
                    request,
                    LoadYearsAsync,
                    LoadRecapAsync);
                return;
            }

            await LoadYearsAsync();
            var defaultRequest = _state.TakeDefaultRequest();
            if (defaultRequest is not null) await LoadRecapAsync(defaultRequest);
            return;
        }

        if (request is not null)
        {
            await LoadRecapAsync(request);
            return;
        }

        var fallbackRequest = _state.TakeDefaultRequest();
        if (fallbackRequest is not null) await LoadRecapAsync(fallbackRequest);
    }

    private async Task LoadYearsAsync()
    {
        _state.BeginYearsLoad();
        try
        {
            var response = await Api.SendAsync<RecapYearsResponse>("/recap/years",
                onAuthFailure: LogoutAsync,
                cancellationToken: _lifetime.Token);
            _state.CompleteYears(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailYears(exception.Message);
        }
    }

    private async Task LoadRecapAsync(string path)
    {
        _recapRequest?.Cancel();
        _recapRequest?.Dispose();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _recapRequest = requestCancellation;
        var version = ++_recapRequestVersion;
        _state.BeginLoad();

        try
        {
            var response = await Api.SendAsync<RecapResponse>(path,
                onAuthFailure: LogoutAsync,
                cancellationToken: requestCancellation.Token);
            if (version == _recapRequestVersion) _state.CompleteLoad(response);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _recapRequestVersion) _state.FailLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_recapRequest, requestCancellation)) _recapRequest = null;
            requestCancellation.Dispose();
        }
    }

    private async Task SelectTabAsync(string key)
    {
        var request = _state.SelectTab(key);
        if (request is not null) await LoadRecapAsync(request);
    }

    private void SetCustomFrom(ChangeEventArgs eventArgs) =>
        _state.SetCustomFrom(eventArgs.Value?.ToString() ?? string.Empty);

    private void SetCustomTo(ChangeEventArgs eventArgs) =>
        _state.SetCustomTo(eventArgs.Value?.ToString() ?? string.Empty);

    private async Task ApplyCustomAsync()
    {
        var target = _state.BuildCustomNavigation();
        if (target is null) return;
        await NavigateAsync(target);
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

    private Task NavigateToGameAsync(string gameId) =>
        NavigateAsync($"/games/{GameHash.EncodeGameId(gameId)}");

    private void SetTheme(string theme) => _theme = theme;

    private string TabClass(string key) =>
        $"rounded-full border px-3 py-2 text-[10px] font-semibold uppercase tracking-[0.25em] transition " +
        (_state.ActiveTab == key
            ? "border-emerald-300 bg-emerald-400 text-slate-900"
            : "border-slate-200 dark:border-slate-800 text-slate-600 dark:text-slate-200 hover:border-emerald-300/60");

    private async Task LogoutAsync()
    {
        await AuthSession.LogoutAsync();
        Navigation.NavigateTo("/", forceLoad: true);
    }

    private static string FormatDate(string date) => Formatters.FormatDateLabel(
        DateTimeOffset.Parse($"{date}T00:00:00Z", CultureInfo.InvariantCulture));

    private static string FormatMonthLabel(string monthKey)
    {
        var parts = monthKey.Split('-');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            year == 0 || month is < 1 or > 12)
        {
            return monthKey;
        }

        return new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("MMM yyyy", EnUs);
    }

    private static string FormatAverage(double value, int decimals = 1)
        => Formatters.FormatJavaScriptFixedNumber(value, decimals);

    private static int RoundPercent(double value) => (int)Math.Floor(value + 0.5);

    private static string Number(double value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);

    private static string PercentStyle(double value) => $"width: {Number(value)}%";

    private static string OpacityStyle(double value) => $"opacity: {Number(value)}";

    public void Dispose()
    {
        _lifetime.Cancel();
        _recapRequest?.Cancel();
        _recapRequest?.Dispose();
        _lifetime.Dispose();
    }
}
