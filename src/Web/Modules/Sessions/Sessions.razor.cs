using System.Globalization;
using Microsoft.AspNetCore.Components;
using Web.Infrastructure.Http;
using Web.Infrastructure.Browser;
using Web.Infrastructure.Models;
using Web.Routing;
using Web.Infrastructure.Common;

namespace Web.Modules.Sessions;

internal sealed record SessionsDeleteRequest(string Confirmation, string Path, HttpMethod Method)
{
    public static SessionsDeleteRequest For(long sessionId) =>
        new("Remove this session entry?", $"/sessions/{sessionId}", HttpMethod.Delete);
}

internal sealed record SessionUpdateSubmission(
    string Path,
    HttpMethod Method,
    AccountSessionUpdateRequest Body);

internal sealed class SessionsRequestVersions
{
    private long _stats;
    private long _activeManualSession;

    public long BeginStatsRequest() => ++_stats;

    public bool IsCurrentStatsRequest(long version) => version == _stats;

    public long BeginActiveManualSessionRequest() => ++_activeManualSession;

    public bool IsCurrentActiveManualSessionRequest(long version) =>
        version == _activeManualSession;
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
    bool Ignored,
    bool CanEdit);

internal sealed class SessionEditState
{
    private const string LocalDateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

    public AccountSession? Session { get; private set; }
    public string StartedAt { get; private set; } = string.Empty;
    public string EndedAt { get; private set; } = string.Empty;
    public bool IsOpen => Session is not null;
    public string Error { get; private set; } = string.Empty;
    public bool IsSaving { get; private set; }

    public bool TryOpen(AccountSession session)
    {
        if (session.EndedAt is null) return false;
        Session = session;
        StartedAt = session.StartedAt.ToLocalTime().ToString(LocalDateTimeFormat, CultureInfo.InvariantCulture);
        EndedAt = session.EndedAt.Value.ToLocalTime().ToString(LocalDateTimeFormat, CultureInfo.InvariantCulture);
        return true;
    }

    public void BeginSave()
    {
        IsSaving = true;
        Error = string.Empty;
    }

    public void FailSave(string error)
    {
        IsSaving = false;
        Error = error;
    }

    public void CompleteSave()
    {
        Session = null;
        StartedAt = string.Empty;
        EndedAt = string.Empty;
        IsSaving = false;
        Error = string.Empty;
    }

    public void Cancel() => CompleteSave();

    public void SetStartedAt(string value)
    {
        StartedAt = value;
        Error = string.Empty;
    }

    public void SetEndedAt(string value)
    {
        EndedAt = value;
        Error = string.Empty;
    }

    public bool TryBuildSubmission(out SessionUpdateSubmission? submission)
    {
        submission = null;
        if (Session is null ||
            !TryParseLocal(StartedAt, out var startedAt) ||
            !TryParseLocal(EndedAt, out var endedAt))
        {
            Error = "Enter a valid start and end date and time.";
            return false;
        }
        if (endedAt <= startedAt)
        {
            Error = "End time must be later than start time.";
            return false;
        }
        submission = new SessionUpdateSubmission(
            $"/sessions/{Session.SessionId}",
            HttpMethod.Put,
            new AccountSessionUpdateRequest(startedAt, endedAt));
        return true;
    }

    private static bool TryParseLocal(string value, out DateTimeOffset result)
    {
        result = default;
        if (!DateTime.TryParseExact(
                value,
                LocalDateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local) ||
            TimeZoneInfo.Local.IsInvalidTime(local))
        {
            return false;
        }

        result = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return true;
    }
}

internal sealed class SessionsState
{
    public const string PlatformsRequest = "/manual/platforms";
    public const string ActiveManualSessionRequest = "/manual/sessions/current";
    private long _titleVersion;
    private readonly HashSet<long> _deletingSessionIds = [];

    public SessionsState(DateTimeOffset now)
    {
        var range = SessionDates.CurrentUtcWeek(now);
        From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static int Limit => 10;
    public int Offset { get; private set; }
    public string Preset { get; private set; } = "this-week";
    public string Title { get; private set; } = string.Empty;
    public string DebouncedTitle { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
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
    public IReadOnlyList<ManualPlatform> Platforms { get; private set; } = [];
    public ManualActiveSession? ActiveManualSession { get; private set; }
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

    public void SetPlatform(string? platform)
    {
        Platform = platform?.Trim() ?? string.Empty;
        Offset = 0;
    }

    public void TogglePlatform(string platform) =>
        SetPlatform(Platform == platform ? null : platform);

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

    public void CompletePlatforms(ManualPlatformsResponse response) => Platforms = response.Platforms;

    public void CompleteActiveManualSession(ManualActiveSessionResponse response) =>
        ActiveManualSession = response.Session;

    public IReadOnlyList<SessionsRowView> GetRowViews(DateTimeOffset now) =>
        Rows.Select(session => new SessionsRowView(
            session.SessionId,
            session.GameId,
            $"/games/{GameHash.EncodeGameId(session.GameId)}",
            session.Game,
            session.PlatformName,
            Formatters.FormatDateLabel(session.StartedAt),
            Formatters.FormatDuration(
                SessionDates.GetDurationSeconds(session.StartedAt, session.EndedAt, now),
                DurationFormat.Compact),
            session.CapsuleUrl,
            session.Ignored,
            session.EndedAt is not null)).ToArray();

    public string BuildListRequest()
    {
        var title = DebouncedTitle.Length == 0
            ? string.Empty
            : $"&title={EncodeQuery(DebouncedTitle)}";
        var platform = Platform.Length == 0
            ? string.Empty
            : $"&platform={EncodeQuery(Platform)}";
        var from = From.Length == 0 ? string.Empty : $"&from={From}";
        var to = To.Length == 0 ? string.Empty : $"&to={To}";
        return $"/sessions?limit={Limit}&offset={Offset}{title}{platform}{from}{to}&sort={SortKey}&direction={SortDirection}";
    }

    public string? BuildStatsRequest() =>
        From.Length == 0 || To.Length == 0
            ? null
            : $"/stats?from={From}&to={To}{(Platform.Length == 0 ? string.Empty : $"&platform={EncodeQuery(Platform)}")}";

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
    private readonly SessionEditState _editState = new();
    private readonly SessionCreateState _createState = new();
    private readonly SessionsRequestVersions _requestVersions = new();
    private CancellationTokenSource? _titleDebounce;
    private CancellationTokenSource? _listRequest;
    private CancellationTokenSource? _statsRequest;
    private CancellationTokenSource? _manualSearchRequest;
    private CancellationTokenSource? _manualSaveRequest;
    private CancellationTokenSource? _activeManualSessionRequest;
    private long _listRequestVersion;
    private long _manualSearchVersion;
    private string _theme = "light";
    private bool _isStoppingManualSession;
    private string _manualActionError = string.Empty;

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    private IReadOnlyList<SessionsRowView> RowViews => _state.GetRowViews(DateTimeOffset.Now);
    private bool HasSessionFilters =>
        _state.Title.Trim().Length > 0 || _state.Platform.Length > 0 ||
        _state.From.Length > 0 || _state.To.Length > 0;
    private IReadOnlyList<string> PieColors => _theme == "dark" ? DarkPieColors : LightPieColors;
    private IReadOnlyList<(string Label, string Value)> StatsSummary =>
    [
        ("Hours played", Formatters.FormatHours(_state.Stats?.TotalSeconds ?? 0, 1)),
        ("Sessions", (_state.Stats?.SessionCount ?? 0).ToString(CultureInfo.InvariantCulture)),
        ("Top title", _state.Stats?.TopGameTitle ?? "—"),
    ];
    private IReadOnlyList<(string Key, string Label, double Value, string Hours)> StatsPlatforms
    {
        get
        {
            if (_state.Stats is null) return [];
            var total = Math.Max(_state.Stats.Platforms.Sum(item => item.TotalSeconds), 1);
            return _state.Stats.Platforms.Select(item => (
                item.PlatformKey,
                item.PlatformName,
                Math.Floor(item.TotalSeconds / total * 100 + 0.5),
                Formatters.FormatHours(item.TotalSeconds, 1))).ToArray();
        }
    }
    private IReadOnlyList<(string GameId, string Title, string Hours)> StatsTopGames =>
        _state.Stats?.TopGames.Select(game => (
            game.GameId,
            game.Title,
            Formatters.FormatHours(game.TotalSeconds, 1))).ToArray() ?? [];
    private IReadOnlyList<AccountMonthStat> StatsMonths => _state.Stats?.Months ?? [];
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

    protected override Task OnInitializedAsync() => Task.WhenAll(RefreshAllAsync(), LoadPlatformsAsync());

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

    private async Task ChangePlatformAsync(ChangeEventArgs eventArgs)
    {
        _state.SetPlatform(eventArgs.Value?.ToString());
        await RefreshAllAsync();
    }

    private async Task TogglePlatformAsync(string platform)
    {
        _state.TogglePlatform(platform);
        await RefreshAllAsync();
    }

    private async Task SortAsync(string key)
    {
        _state.SortBy(key);
        await LoadListAsync();
    }

    private async Task SetPageAsync(int page)
    {
        _state.SetOffset((page - 1) * SessionsState.Limit);
        await LoadListAsync();
    }

    private async Task PreviousPageAsync()
    {
        _state.SetOffset(_state.Offset - SessionsState.Limit);
        await LoadListAsync();
    }

    private async Task NextPageAsync()
    {
        _state.SetOffset(_state.Offset + SessionsState.Limit);
        await LoadListAsync();
    }

    private Task RefreshAllAsync() =>
        Task.WhenAll(LoadListAsync(), LoadStatsAsync(), LoadActiveManualSessionAsync());

    private async Task LoadPlatformsAsync()
    {
        try
        {
            var response = await Api.SendAsync<ManualPlatformsResponse>(
                SessionsState.PlatformsRequest,
                cancellationToken: _lifetime.Token);
            _state.CompletePlatforms(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _manualActionError = exception.Message;
        }
    }

    private async Task LoadActiveManualSessionAsync()
    {
        var version = _requestVersions.BeginActiveManualSessionRequest();
        _activeManualSessionRequest?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _activeManualSessionRequest = request;
        try
        {
            var response = await Api.SendAsync<ManualActiveSessionResponse>(
                SessionsState.ActiveManualSessionRequest,
                cancellationToken: request.Token);
            if (_requestVersions.IsCurrentActiveManualSessionRequest(version) &&
                !request.IsCancellationRequested)
            {
                _state.CompleteActiveManualSession(response);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_requestVersions.IsCurrentActiveManualSessionRequest(version))
            {
                _manualActionError = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_activeManualSessionRequest, request))
            {
                _activeManualSessionRequest = null;
            }
            request.Dispose();
        }
    }

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

    private void OpenSessionEditor(long sessionId)
    {
        var session = _state.Rows.FirstOrDefault(row => row.SessionId == sessionId);
        if (session is not null) _editState.TryOpen(session);
    }

    private void CancelSessionEdit() => _editState.Cancel();

    private void OpenSessionCreator()
    {
        _manualActionError = string.Empty;
        _createState.Open(SessionCreateMode.Live, DateTimeOffset.Now);
    }

    private void CloseSessionCreator()
    {
        InvalidateManualSearch();
        _manualSaveRequest?.Cancel();
        _createState.Close();
    }

    private void ManualQueryChanged(string _)
    {
        InvalidateManualSearch();
        _createState.CancelSearch();
    }

    private async Task SearchManualGamesAsync()
    {
        if (!_createState.TryBeginSearch(out var path)) return;
        InvalidateManualSearch();
        var version = _manualSearchVersion;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _manualSearchRequest = request;
        try
        {
            var response = await Api.SendAsync<ManualMappingSearchResponse>(
                path!,
                cancellationToken: request.Token);
            if (version == _manualSearchVersion && !request.IsCancellationRequested)
            {
                _createState.CompleteSearch(response);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _manualSearchVersion) _createState.FailSearch(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_manualSearchRequest, request)) _manualSearchRequest = null;
            request.Dispose();
        }
    }

    private async Task SaveCreatedSessionAsync()
    {
        if (_createState.IsSaving) return;
        if (!_createState.TryBuildSubmission(out var submission)) return;
        if (!_createState.TryBeginSave()) return;
        _manualSaveRequest?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _manualSaveRequest = request;
        try
        {
            switch (submission!.Body)
            {
                case ManualSessionStartRequest start:
                    await Api.SendAsync<ManualSessionStartResponse>(
                        submission.Path, submission.Method, start, cancellationToken: request.Token);
                    break;
                case ManualEntryCreateRequest entry:
                    await Api.SendAsync<ManualEntryCreateResponse>(
                        submission.Path, submission.Method, entry, cancellationToken: request.Token);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported session creation request.");
            }

            _createState.CompleteSave();
            await RefreshAllAsync();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _createState.FailSave(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_manualSaveRequest, request)) _manualSaveRequest = null;
            request.Dispose();
        }
    }

    private async Task StopManualSessionAsync()
    {
        if (_isStoppingManualSession) return;
        _isStoppingManualSession = true;
        _manualActionError = string.Empty;
        try
        {
            await Api.SendAsync<ManualSessionStopResponse>(
                "/manual/sessions/stop",
                HttpMethod.Post,
                cancellationToken: _lifetime.Token);
            await RefreshAllAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _manualActionError = exception.Message;
        }
        finally
        {
            _isStoppingManualSession = false;
        }
    }

    private void InvalidateManualSearch()
    {
        ++_manualSearchVersion;
        _manualSearchRequest?.Cancel();
    }

    private async Task SaveSessionEditAsync()
    {
        if (!_editState.TryBuildSubmission(out var submission)) return;
        _editState.BeginSave();
        try
        {
            await Api.SendAsync<AccountSessionUpdateResponse>(
                submission!.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _editState.CompleteSave();
            await RefreshAllAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _editState.FailSave(exception.Message);
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
        _requestVersions.BeginActiveManualSessionRequest();
        _lifetime.Cancel();
        _titleDebounce?.Cancel();
        _listRequest?.Cancel();
        _statsRequest?.Cancel();
        _manualSearchRequest?.Cancel();
        _manualSaveRequest?.Cancel();
        _activeManualSessionRequest?.Cancel();
        _titleDebounce?.Dispose();
        _listRequest?.Dispose();
        _statsRequest?.Dispose();
        _manualSearchRequest?.Dispose();
        _manualSaveRequest?.Dispose();
        _activeManualSessionRequest?.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
