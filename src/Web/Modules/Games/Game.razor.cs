using System.Globalization;
using Microsoft.AspNetCore.Components;
using Web.Infrastructure.Http;
using Web.Infrastructure.Auth;
using Web.Infrastructure.Models;
using Web.Infrastructure.Common;

namespace Web.Modules.Games;

internal sealed record GameRequests(
    string GameId,
    string GameHash,
    string Details,
    string SessionsBase,
    string Stats,
    string IgnoreStatus,
    string Rating)
{
    public static GameRequests For(string gameHash, DateTimeOffset now)
    {
        var gameId = Web.Infrastructure.Common.GameHash.DecodeGameHash(gameHash)
            ?? throw new ArgumentException("Invalid game hash.", nameof(gameHash));
        var to = Formatters.FormatUtcDateInput(now);
        var sessionsBase = $"/sessions?gameId={gameHash}&from=2025-01-01&to={to}&sort=date&direction=desc";
        return new GameRequests(
            gameId.ToString(CultureInfo.InvariantCulture),
            gameHash,
            $"/games/{gameHash}",
            sessionsBase,
            $"/games/{gameHash}/stats",
            $"/games/ignored?gameId={gameHash}",
            $"/games/rating?gameId={gameHash}");
    }

    public string SessionsPage(int page) => $"{SessionsBase}&limit=5&offset={(page - 1) * 5}";
}

internal sealed record GameMutationSubmission(
    string Path,
    HttpMethod Method,
    IReadOnlyDictionary<string, object?> Body);

internal sealed record GameChartBucket(string Key, string Label, int Year, int Month, double Seconds)
{
    public double Hours => Seconds / 3600;
}

internal static class GameInitialLoadFlow
{
    public static Task RunAsync(Func<Task> loadDetails, Func<Task> loadStats, Func<Task> loadFirstSessions) =>
        Task.WhenAll(loadDetails(), loadStats(), loadFirstSessions());
}

internal sealed class GameState
{
    private const int AllTimeStartYear = 2025;
    private const int RecentPageSize = 5;
    private DateTimeOffset _now;

    public GameRequests? Requests { get; private set; }
    public GameResponse? GameDetails { get; private set; }
    public string GameDetailsError { get; private set; } = string.Empty;
    public IReadOnlyList<AccountSession> RecentSessions { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public string GameError { get; private set; } = string.Empty;
    public bool SessionsLoaded { get; private set; }
    public bool IsIgnoreChecking { get; private set; }
    public bool IsIgnoreSaving { get; private set; }
    public bool IsIgnored { get; private set; }
    public string IgnoreError { get; private set; } = string.Empty;
    public int? Rating { get; private set; }
    public bool IsRatingLoading { get; private set; }
    public bool IsRatingSaving { get; private set; }
    public string RatingError { get; private set; } = string.Empty;
    public int SelectedYear { get; private set; }
    public int RecentPage { get; private set; } = 1;
    public int RecentTotal { get; private set; }
    public bool IsRecentPageLoading { get; private set; }
    public GameStatsResponse? Stats { get; private set; }
    public string StatsError { get; private set; } = string.Empty;
    public string DisplayTitle => GameDetails?.Title ??
        (GameDetailsError.Length > 0 ? "Unknown game" : "Loading game...");
    public string? GameImage =>
        RecentSessions.FirstOrDefault(session => session.CapsuleUrl is not null)?.CapsuleUrl ??
        GameDetails?.CapsuleUrl;
    public double TotalSeconds => Stats?.TotalSeconds ?? 0;
    public string LastPlayedLabel => Stats?.LastPlayedAt is { } lastPlayed
        ? Formatters.FormatDateLabel(lastPlayed)
        : "—";
    public IReadOnlyList<GameChartBucket> YearlyStats => BuildYearlyStats();
    public IReadOnlyList<int> AvailableYears => YearlyStats.Select(item => item.Year).ToArray();
    public int ActiveYear => AvailableYears.Contains(SelectedYear)
        ? SelectedYear
        : AvailableYears.LastOrDefault(SelectedYear);
    public IReadOnlyList<GameChartBucket> MonthlyStats => BuildMonthlyStats();
    public int RecentTotalPages => Math.Max(1, (int)Math.Ceiling(RecentTotal / (double)RecentPageSize));
    public string RatingLabel => Rating switch
    {
        1 => "Rough",
        2 => "Okay",
        3 => "Good",
        4 => "Great",
        5 => "Legendary",
        _ => "Not rated",
    };
    public bool RatingDisabled => IsRatingSaving || IsRatingLoading || Requests is null;

    public bool TryBegin(string? gameHash, DateTimeOffset now, out GameRequests? requests)
    {
        _now = now;
        SelectedYear = now.UtcDateTime.Year;
        RecentPage = 1;
        RecentTotal = 0;
        GameDetails = null;
        RecentSessions = [];
        Rating = null;
        Stats = null;
        StatsError = string.Empty;
        IsIgnored = false;
        GameDetailsError = string.Empty;
        GameError = string.Empty;
        IgnoreError = string.Empty;
        RatingError = string.Empty;
        IsIgnoreSaving = false;
        IsRatingSaving = false;

        if (string.IsNullOrWhiteSpace(gameHash) || !Web.Infrastructure.Common.GameHash.DecodeGameHash(gameHash).HasValue)
        {
            Requests = null;
            requests = null;
            GameDetailsError = "Invalid game link.";
            GameError = "Invalid game link.";
            IgnoreError = "Invalid game link.";
            RatingError = "Invalid game link.";
            IsLoading = false;
            SessionsLoaded = true;
            IsIgnoreChecking = false;
            IsRatingLoading = false;
            return false;
        }

        Requests = GameRequests.For(gameHash, now);
        requests = Requests;
        IsLoading = true;
        SessionsLoaded = false;
        IsIgnoreChecking = true;
        IsRatingLoading = true;
        return true;
    }

    public void CompleteDetails(GameResponse response) => GameDetails = response;

    public void FailDetails(string error)
    {
        GameDetails = null;
        GameDetailsError = error;
    }

    public void BeginFirstSessionsLoad()
    {
        IsLoading = true;
        GameError = string.Empty;
    }

    public void CompleteFirstSessions(AccountSessionsResponse response)
    {
        RecentSessions = response.Sessions;
        RecentTotal = response.Total;
        RecentPage = 1;
        IsLoading = false;
        IsRecentPageLoading = false;
        SessionsLoaded = true;
    }

    public void CompleteSessionsPage(int page, AccountSessionsResponse response)
    {
        RecentSessions = response.Sessions;
        RecentTotal = response.Total;
        RecentPage = page;
        IsRecentPageLoading = false;
    }

    public void BeginRecentPageLoad()
    {
        IsRecentPageLoading = true;
        GameError = string.Empty;
    }

    public void FailSessions(string error)
    {
        RecentSessions = [];
        RecentTotal = 0;
        IsLoading = false;
        IsRecentPageLoading = false;
        SessionsLoaded = true;
        GameError = error;
    }

    public void BeginStatsLoad()
    {
        StatsError = string.Empty;
    }

    public void CompleteStats(GameStatsResponse response)
    {
        Stats = response;
        StatsError = string.Empty;
    }

    public void FailStats(string error) => StatsError = error;

    public void CompleteIgnore(GameIgnoreResponse response)
    {
        IsIgnored = response.Ignored;
        IgnoreError = string.Empty;
        IsIgnoreChecking = false;
    }

    public void FailIgnore(string error)
    {
        IsIgnored = false;
        IgnoreError = error;
        IsIgnoreChecking = false;
    }

    public void CompleteRating(GameRatingResponse response)
    {
        Rating = response.Rating;
        RatingError = string.Empty;
        IsRatingLoading = false;
    }

    public void FailRating(string error)
    {
        Rating = null;
        RatingError = error;
        IsRatingLoading = false;
    }

    public GameMutationSubmission BuildIgnoreSubmission(bool isIgnored)
    {
        var requests = Requests ?? throw new InvalidOperationException("No game is loaded.");
        return new GameMutationSubmission(
            "/games/ignored",
            isIgnored ? HttpMethod.Delete : HttpMethod.Post,
            new Dictionary<string, object?> { ["gameId"] = requests.GameHash });
    }

    public GameMutationSubmission BuildRatingSubmission(int? value)
    {
        var requests = Requests ?? throw new InvalidOperationException("No game is loaded.");
        var body = new Dictionary<string, object?> { ["gameId"] = requests.GameHash };
        if (value.HasValue) body["rating"] = value.Value;
        return new GameMutationSubmission(
            "/games/rating",
            value.HasValue ? HttpMethod.Post : HttpMethod.Delete,
            body);
    }

    public void BeginIgnoreSave()
    {
        IsIgnoreSaving = true;
        IgnoreError = string.Empty;
    }

    public void EndIgnoreSave() => IsIgnoreSaving = false;

    public void BeginRatingSave()
    {
        IsRatingSaving = true;
        RatingError = string.Empty;
    }

    public void EndRatingSave() => IsRatingSaving = false;

    public void SetSelectedYear(int year) => SelectedYear = year;

    private GameChartBucket[] BuildYearlyStats()
    {
        if (Stats is null) return [];
        var endYear = Math.Max(AllTimeStartYear, _now.UtcDateTime.Year);
        var totals = Enumerable.Range(AllTimeStartYear, endYear - AllTimeStartYear + 1)
            .ToDictionary(year => year, _ => 0d);
        foreach (var item in Stats.Years)
        {
            if (totals.ContainsKey(item.Year)) totals[item.Year] = item.Seconds;
        }

        return totals.Select(item => new GameChartBucket(
            item.Key.ToString(CultureInfo.InvariantCulture),
            item.Key.ToString(CultureInfo.InvariantCulture),
            item.Key,
            -1,
            item.Value)).ToArray();
    }

    private GameChartBucket[] BuildMonthlyStats()
    {
        var year = ActiveYear;
        var totals = new double[12];
        foreach (var item in Stats?.Months ?? [])
        {
            if (item.Year == year && item.Month is >= 1 and <= 12)
            {
                totals[item.Month - 1] = item.Seconds;
            }
        }

        return Enumerable.Range(0, 12).Select(month => new GameChartBucket(
            $"{year}-{month}",
            new DateTime(year, month + 1, 1).ToString("MMM", CultureInfo.GetCultureInfo("en-US")),
            year,
            month,
            totals[month])).ToArray();
    }
}

public partial class Game : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly GameState _state = new();
    private CancellationTokenSource? _pageRequest;
    private long _loadVersion;

    [Parameter]
    public string? Hash { get; set; }

    [CascadingParameter(Name = "NormalizedGameHash")]
    public string? NormalizedGameHash { get; set; }

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private AuthSession AuthSession { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnParametersSetAsync()
    {
        var version = ++_loadVersion;
        _pageRequest?.Cancel();
        _pageRequest?.Dispose();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _pageRequest = requestCancellation;

        if (!_state.TryBegin(NormalizedGameHash, DateTimeOffset.UtcNow, out var requests)) return;

        await GameInitialLoadFlow.RunAsync(
            () => LoadDetailsAsync(requests!, version, requestCancellation.Token),
            () => LoadStatsAsync(requests!, version, requestCancellation.Token),
            () => LoadFirstSessionsAsync(requests!, version, requestCancellation.Token));
    }

    private async Task LoadDetailsAsync(GameRequests requests, long version, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Api.SendAsync<GameResponse>(requests.Details,
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (IsCurrent(version, cancellationToken)) _state.CompleteDetails(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, cancellationToken)) _state.FailDetails(exception.Message);
        }
    }

    private async Task LoadStatsAsync(GameRequests requests, long version, CancellationToken cancellationToken)
    {
        _state.BeginStatsLoad();
        try
        {
            var response = await Api.SendAsync<GameStatsResponse>(requests.Stats,
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (IsCurrent(version, cancellationToken)) _state.CompleteStats(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, cancellationToken)) _state.FailStats(exception.Message);
        }
    }

    private async Task LoadFirstSessionsAsync(
        GameRequests requests,
        long version,
        CancellationToken cancellationToken)
    {
        _state.BeginFirstSessionsLoad();
        try
        {
            var response = await Api.SendAsync<AccountSessionsResponse>(requests.SessionsPage(1),
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (!IsCurrent(version, cancellationToken)) return;
            _state.CompleteFirstSessions(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            if (!IsCurrent(version, cancellationToken)) return;
            _state.FailSessions(exception.Message);
        }

        if (!IsCurrent(version, cancellationToken)) return;
        await Task.WhenAll(
            LoadIgnoreAsync(requests, version, cancellationToken),
            LoadRatingAsync(requests, version, cancellationToken));
    }

    private async Task LoadSessionsPageAsync(int page)
    {
        if (_state.Requests is null) return;
        var version = _loadVersion;
        var cancellationToken = CurrentCancellationToken;
        _state.BeginRecentPageLoad();
        try
        {
            var response = await Api.SendAsync<AccountSessionsResponse>(
                _state.Requests.SessionsPage(page),
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (IsCurrent(version, cancellationToken)) _state.CompleteSessionsPage(page, response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, cancellationToken)) _state.FailSessions(exception.Message);
        }
    }

    private async Task LoadIgnoreAsync(GameRequests requests, long version, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Api.SendAsync<GameIgnoreResponse>(requests.IgnoreStatus,
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (IsCurrent(version, cancellationToken)) _state.CompleteIgnore(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, cancellationToken)) _state.FailIgnore(exception.Message);
        }
    }

    private async Task LoadRatingAsync(GameRequests requests, long version, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Api.SendAsync<GameRatingResponse>(requests.Rating,
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (IsCurrent(version, cancellationToken)) _state.CompleteRating(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, cancellationToken)) _state.FailRating(exception.Message);
        }
    }

    private async Task ToggleIgnoreAsync()
    {
        if (_state.Requests is null)
        {
            _state.FailIgnore("Cannot toggle without a game id.");
            return;
        }

        var submission = _state.BuildIgnoreSubmission(_state.IsIgnored);
        var version = _loadVersion;
        var cancellationToken = CurrentCancellationToken;
        _state.BeginIgnoreSave();
        try
        {
            var response = await Api.SendAsync<GameIgnoreResponse>(
                submission.Path,
                submission.Method,
                submission.Body,
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (IsCurrent(version, cancellationToken)) _state.CompleteIgnore(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, cancellationToken)) _state.FailIgnore(exception.Message);
        }
        finally
        {
            if (IsCurrent(version, cancellationToken)) _state.EndIgnoreSave();
        }
    }

    private async Task RateGameAsync(int? value)
    {
        if (_state.Requests is null)
        {
            _state.FailRating("Cannot rate without a game id.");
            return;
        }

        var submission = _state.BuildRatingSubmission(value);
        var version = _loadVersion;
        var cancellationToken = CurrentCancellationToken;
        _state.BeginRatingSave();
        try
        {
            var response = await Api.SendAsync<GameRatingResponse>(
                submission.Path,
                submission.Method,
                submission.Body,
                onAuthFailure: LogoutAsync,
                cancellationToken: cancellationToken);
            if (IsCurrent(version, cancellationToken)) _state.CompleteRating(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, cancellationToken)) _state.FailRating(exception.Message);
        }
        finally
        {
            if (IsCurrent(version, cancellationToken)) _state.EndRatingSave();
        }
    }

    private void ChangeYear(ChangeEventArgs eventArgs)
    {
        if (int.TryParse(eventArgs.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            _state.SetSelectedYear(year);
        }
    }

    private bool IsCurrent(long version, CancellationToken cancellationToken) =>
        version == _loadVersion && !cancellationToken.IsCancellationRequested;

    private CancellationToken CurrentCancellationToken => _pageRequest?.Token ?? _lifetime.Token;

    private async Task LogoutAsync()
    {
        await AuthSession.LogoutAsync();
        Navigation.NavigateTo("/", forceLoad: true);
    }

    public void Dispose()
    {
        _loadVersion++;
        _pageRequest?.Cancel();
        _pageRequest?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
