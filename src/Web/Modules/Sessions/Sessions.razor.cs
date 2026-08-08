using System.Globalization;
using Microsoft.AspNetCore.Components;
using Web.Shared.Http;
using Web.Shared.Browser;
using Web.Shared.Models;
using Web.Routing;
using Web.Shared.Common;

namespace Web.Modules.Sessions;

internal sealed record SessionsDeleteRequest(string Confirmation, string Path, HttpMethod Method)
{
    public static SessionsDeleteRequest For(long sessionId) =>
        new("Remove this session entry?", $"/sessions/{sessionId}", HttpMethod.Delete);
}

internal sealed class SessionsRequestVersions
{
    private long _stats;

    public long BeginStatsRequest() => ++_stats;

    public bool IsCurrentStatsRequest(long version) => version == _stats;
}

internal sealed record SessionsRowView(
    long SessionId,
    string GameId,
    string GamePath,
    string Title,
    string Platform,
    string Date,
    string Duration,
    string? ImageUrl,
    bool Ignored);

internal sealed class SessionsState
{
    private long _titleVersion;
    private readonly HashSet<long> _deletingSessionIds = [];

    public SessionsState(DateTimeOffset now)
    {
        var range = SessionDates.CurrentUtcWeek(now);
        From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public int Limit => 10;
    public int Offset { get; private set; }
    public string Preset { get; private set; } = "this-week";
    public string Title { get; private set; } = string.Empty;
    public string DebouncedTitle { get; private set; } = string.Empty;
    public string From { get; private set; }
    public string To { get; private set; }
    public string SortKey { get; private set; } = "date";
    public string SortDirection { get; private set; } = "desc";
    public IReadOnlyList<AccountSession> Rows { get; private set; } = [];
    public int Total { get; private set; }
    public bool IsListLoading { get; private set; }
    public string ListError { get; private set; } = string.Empty;
    public AccountStatsPeriod? Stats { get; private set; }
    public bool IsStatsLoading { get; private set; }
    public string StatsError { get; private set; } = string.Empty;
    public int RefreshVersion { get; private set; }
    public IReadOnlyCollection<long> DeletingSessionIds => _deletingSessionIds;
    public int Start => Total == 0 ? 0 : Offset + 1;
    public int End => Math.Min(Offset + Limit, Total);
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)Limit));
    public int CurrentPage => Math.Min(TotalPages, Offset / Limit + 1);
    public IReadOnlyList<int> PageNumbers
    {
        get
        {
            const int maxPages = 4;
            var startPage = Math.Max(1, Math.Min(CurrentPage - 1, TotalPages - maxPages + 1));
            return Enumerable.Range(startPage, Math.Min(TotalPages, maxPages)).ToArray();
        }
    }

    public void SetOffset(int offset) => Offset = Math.Max(0, offset);

    public long SetTitle(string title)
    {
        Title = title;
        return ++_titleVersion;
    }

    public bool ApplyDebouncedTitle(long version)
    {
        if (version != _titleVersion) return false;
        DebouncedTitle = Title.Trim();
        Offset = 0;
        return true;
    }

    public void SetFrom(string from)
    {
        From = from;
        Preset = "custom";
        Offset = 0;
    }

    public void SetTo(string to)
    {
        To = to;
        Preset = "custom";
        Offset = 0;
    }

    public void ApplyPreset(string preset, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        DateOnly from;
        DateOnly to;
        switch (preset)
        {
            case "today":
                from = to = today;
                break;
            case "yesterday":
                from = to = today.AddDays(-1);
                break;
            case "this-week":
            case "last-week":
                var week = SessionDates.CurrentUtcWeek(now);
                from = preset == "last-week" ? week.From.AddDays(-7) : week.From;
                to = from.AddDays(6);
                break;
            case "this-month":
                from = new DateOnly(today.Year, today.Month, 1);
                to = from.AddMonths(1).AddDays(-1);
                break;
            case "last-year":
                from = new DateOnly(today.Year - 1, 1, 1);
                to = new DateOnly(today.Year - 1, 12, 31);
                break;
            default:
                from = new DateOnly(today.Year, 1, 1);
                to = new DateOnly(today.Year, 12, 31);
                break;
        }

        From = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        To = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Preset = preset;
        Offset = 0;
    }

    public void SortBy(string key)
    {
        if (SortKey == key)
        {
            SortDirection = SortDirection == "asc" ? "desc" : "asc";
        }
        else
        {
            SortKey = key;
            SortDirection = "desc";
        }
        Offset = 0;
    }

    public void BeginListLoad()
    {
        IsListLoading = true;
        ListError = string.Empty;
    }

    public void FailListLoad(string error)
    {
        Rows = [];
        Total = 0;
        IsListLoading = false;
        ListError = error;
    }

    public void CompleteListLoad(AccountSessionsResponse response)
    {
        Rows = response.Sessions;
        Total = response.Total;
        IsListLoading = false;
    }

    public void BeginStatsLoad()
    {
        IsStatsLoading = true;
        StatsError = string.Empty;
    }

    public bool TryBeginStatsLoad()
    {
        if (From.Length == 0 || To.Length == 0)
        {
            Stats = null;
            IsStatsLoading = false;
            return false;
        }

        BeginStatsLoad();
        return true;
    }

    public void CompleteStatsLoad(AccountStatsRangeResponse response)
    {
        Stats = response.Stats;
        IsStatsLoading = false;
    }

    public void FailStatsLoad(string error)
    {
        Stats = null;
        IsStatsLoading = false;
        StatsError = error;
    }

    public void BeginDelete(long sessionId)
    {
        _deletingSessionIds.Add(sessionId);
        ListError = string.Empty;
    }

    public void CompleteDelete() => RefreshVersion++;

    public void FailDelete(string error) => ListError = error;

    public void EndDelete(long sessionId) => _deletingSessionIds.Remove(sessionId);

    public IReadOnlyList<SessionsRowView> GetRowViews(DateTimeOffset now) =>
        Rows.Select(session => new SessionsRowView(
            session.SessionId,
            session.GameId,
            $"/games/{GameHash.EncodeGameId(session.GameId)}",
            session.Game,
            Formatters.FormatSessionPlatformLabel(session.Platform, session.MappedPlatform),
            Formatters.FormatDateLabel(session.StartedAt),
            Formatters.FormatDuration(
                SessionDates.GetDurationSeconds(session.StartedAt, session.EndedAt, now),
                DurationFormat.Short),
            session.CapsuleUrl,
            session.Ignored)).ToArray();

    public string BuildListRequest()
    {
        var title = DebouncedTitle.Length == 0
            ? string.Empty
            : $"&title={EncodeQuery(DebouncedTitle)}";
        var from = From.Length == 0 ? string.Empty : $"&from={From}";
        var to = To.Length == 0 ? string.Empty : $"&to={To}";
        return $"/sessions?limit={Limit}&offset={Offset}{title}{from}{to}&sort={SortKey}&direction={SortDirection}";
    }

    public string? BuildStatsRequest() =>
        From.Length == 0 || To.Length == 0 ? null : $"/stats?from={From}&to={To}";

    private static string EncodeQuery(string value) =>
        Uri.EscapeDataString(value)
            .Replace("%20", "+", StringComparison.Ordinal)
            .Replace("~", "%7E", StringComparison.Ordinal)
            .Replace("%2A", "*", StringComparison.Ordinal);
}

public partial class Sessions : ComponentBase, IDisposable
{
    private static readonly IReadOnlyList<(string Key, string Label)> Presets =
    [
        ("today", "Today"),
        ("yesterday", "Yesterday"),
        ("this-week", "This week"),
        ("last-week", "Last week"),
        ("this-month", "This month"),
        ("this-year", "This year"),
        ("last-year", "Last year"),
    ];
    private static readonly IReadOnlyList<string> LightPieColors =
        ["#059669", "#0e7490", "#7c3aed", "#d97706"];
    private static readonly IReadOnlyList<string> DarkPieColors =
        ["#34d399", "#22d3ee", "#a78bfa", "#f59e0b"];

    private readonly CancellationTokenSource _lifetime = new();
    private readonly SessionsState _state = new(DateTimeOffset.Now);
    private readonly SessionsRequestVersions _requestVersions = new();
    private CancellationTokenSource? _titleDebounce;
    private CancellationTokenSource? _listRequest;
    private CancellationTokenSource? _statsRequest;
    private long _listRequestVersion;
    private string _theme = "light";

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    private IReadOnlyList<SessionsRowView> RowViews => _state.GetRowViews(DateTimeOffset.Now);
    private bool HasSessionFilters =>
        _state.Title.Trim().Length > 0 || _state.From.Length > 0 || _state.To.Length > 0;
    private IReadOnlyList<string> PieColors => _theme == "dark" ? DarkPieColors : LightPieColors;
    private IReadOnlyList<(string Label, string Value)> StatsSummary =>
    [
        ("Hours played", Formatters.FormatHours(_state.Stats?.TotalSeconds ?? 0, 1)),
        ("Sessions", (_state.Stats?.SessionCount ?? 0).ToString(CultureInfo.InvariantCulture)),
        ("Top title", _state.Stats?.TopGameTitle ?? "—"),
    ];
    private IReadOnlyList<(string Label, double Value, string Hours)> StatsPlatforms
    {
        get
        {
            if (_state.Stats is null) return [];
            var total = Math.Max(_state.Stats.TotalSeconds, 1);
            return _state.Stats.Platforms.Select(item => (
                Formatters.FormatStatsPlatformLabel(item.Platform),
                Math.Floor(item.TotalSeconds / total * 100 + 0.5),
                Formatters.FormatHours(item.TotalSeconds, 1))).ToArray();
        }
    }
    private IReadOnlyList<(string GameId, string Title, string Hours)> StatsTopGames =>
        _state.Stats?.TopGames.Select(game => (
            game.GameId,
            game.Title,
            Formatters.FormatHours(game.TotalSeconds, 1))).ToArray() ?? [];
    private string StatsRangeLabel
    {
        get
        {
            if (_state.From.Length == 0 && _state.To.Length == 0) return "All time";
            if (_state.From.Length > 0 && _state.To.Length > 0)
            {
                return $"{FormatDate(_state.From)} to {FormatDate(_state.To)}";
            }
            return _state.From.Length > 0
                ? $"From {FormatDate(_state.From)}"
                : $"Until {FormatDate(_state.To)}";
        }
    }

    protected override Task OnInitializedAsync() => RefreshAllAsync();

    private void SetTheme(string theme) => _theme = theme;

    private async Task HandleTitleChanged(ChangeEventArgs eventArgs)
    {
        var version = _state.SetTitle(eventArgs.Value?.ToString() ?? string.Empty);
        _titleDebounce?.Cancel();
        _titleDebounce?.Dispose();
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _titleDebounce = debounce;
        try
        {
            await Task.Delay(300, debounce.Token);
            if (_state.ApplyDebouncedTitle(version)) await LoadListAsync();
        }
        catch (OperationCanceledException) when (debounce.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_titleDebounce, debounce)) _titleDebounce = null;
            debounce.Dispose();
        }
    }

    private async Task SelectPresetAsync(string preset)
    {
        _state.ApplyPreset(preset, DateTimeOffset.Now);
        await RefreshAllAsync();
    }

    private async Task ChangeFromAsync(ChangeEventArgs eventArgs)
    {
        _state.SetFrom(eventArgs.Value?.ToString() ?? string.Empty);
        await RefreshAllAsync();
    }

    private async Task ChangeToAsync(ChangeEventArgs eventArgs)
    {
        _state.SetTo(eventArgs.Value?.ToString() ?? string.Empty);
        await RefreshAllAsync();
    }

    private async Task SortAsync(string key)
    {
        _state.SortBy(key);
        await LoadListAsync();
    }

    private async Task SetPageAsync(int page)
    {
        _state.SetOffset((page - 1) * _state.Limit);
        await LoadListAsync();
    }

    private async Task PreviousPageAsync()
    {
        _state.SetOffset(_state.Offset - _state.Limit);
        await LoadListAsync();
    }

    private async Task NextPageAsync()
    {
        _state.SetOffset(_state.Offset + _state.Limit);
        await LoadListAsync();
    }

    private Task RefreshAllAsync() => Task.WhenAll(LoadListAsync(), LoadStatsAsync());

    private async Task LoadListAsync()
    {
        _listRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _listRequest = requestCancellation;
        var requestVersion = ++_listRequestVersion;
        _state.BeginListLoad();
        try
        {
            var response = await Api.SendAsync<AccountSessionsResponse>(
                _state.BuildListRequest(),
                cancellationToken: requestCancellation.Token);
            if (requestVersion == _listRequestVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteListLoad(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (requestVersion == _listRequestVersion) _state.FailListLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_listRequest, requestCancellation)) _listRequest = null;
            requestCancellation.Dispose();
        }
    }

    private async Task LoadStatsAsync()
    {
        var requestVersion = _requestVersions.BeginStatsRequest();
        _statsRequest?.Cancel();
        if (!_state.TryBeginStatsLoad()) return;

        var path = _state.BuildStatsRequest()!;
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _statsRequest = requestCancellation;
        try
        {
            var response = await Api.SendAsync<AccountStatsRangeResponse>(
                path,
                cancellationToken: requestCancellation.Token);
            if (_requestVersions.IsCurrentStatsRequest(requestVersion) &&
                !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteStatsLoad(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_requestVersions.IsCurrentStatsRequest(requestVersion))
            {
                _state.FailStatsLoad(exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_statsRequest, requestCancellation)) _statsRequest = null;
            requestCancellation.Dispose();
        }
    }

    private async Task RemoveSessionAsync(long sessionId)
    {
        var request = SessionsDeleteRequest.For(sessionId);
        if (!await Browser.ConfirmAsync(request.Confirmation)) return;

        _state.BeginDelete(sessionId);
        try
        {
            await Api.SendAsync<object?>(request.Path, request.Method,
                cancellationToken: _lifetime.Token);
            _state.CompleteDelete();
            await RefreshAllAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailDelete(exception.Message);
        }
        finally
        {
            _state.EndDelete(sessionId);
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

    private Task NavigateToGameAsync(string gameId) =>
        NavigateAsync($"/games/{GameHash.EncodeGameId(gameId)}");

    private Task OpenRecapAsync() => NavigateAsync(
        _state.From.Length > 0 && _state.To.Length > 0
            ? $"/recap?from={_state.From}&to={_state.To}"
            : "/recap");

    private static string FormatDate(string date) => Formatters.FormatDateLabel(
        DateTimeOffset.Parse($"{date}T00:00:00Z", CultureInfo.InvariantCulture));

    private string SortIconPath(string key) => _state.SortKey == key
        ? _state.SortDirection == "asc" ? "M10 4l4 4H6l4-4z" : "M10 16l-4-4h8l-4 4z"
        : "M6 8l4-4 4 4H6zM14 12l-4 4-4-4h8z";

    public void Dispose()
    {
        _listRequestVersion++;
        _requestVersions.BeginStatsRequest();
        _lifetime.Cancel();
        _titleDebounce?.Cancel();
        _listRequest?.Cancel();
        _statsRequest?.Cancel();
        _titleDebounce?.Dispose();
        _listRequest?.Dispose();
        _statsRequest?.Dispose();
        _lifetime.Dispose();
    }
}
