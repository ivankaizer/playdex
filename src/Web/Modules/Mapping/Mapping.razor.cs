using Microsoft.AspNetCore.Components;
using Web.Shared.Http;
using Web.Shared.Browser;
using Web.Shared.Models;
using Web.Routing;
using Web.Shared.Common;

namespace Web.Modules.Mapping;

internal sealed record MappingConnectionSubmission(
    string Path,
    HttpMethod Method,
    IReadOnlyDictionary<string, object?> Body);

internal static class MappingInitialLoadFlow
{
    public static Task RunAsync(Func<Task> loadConnections) => loadConnections();
}

internal sealed class MappingState
{
    public IReadOnlyList<GameConnection> GameConnections { get; private set; } = [];
    public string GameConnectionsError { get; private set; } = string.Empty;
    public bool IsGameConnectionsLoading { get; private set; }
    public string PrimaryGameQuery { get; private set; } = string.Empty;
    public IReadOnlyList<GameSearchResult> PrimaryGameResults { get; private set; } = [];
    public string PrimaryGameError { get; private set; } = string.Empty;
    public bool IsPrimaryGameLoading { get; private set; }
    public GameSearchResult? SelectedPrimaryGame { get; private set; }
    public string SecondaryGameQuery { get; private set; } = string.Empty;
    public IReadOnlyList<GameSearchResult> SecondaryGameResults { get; private set; } = [];
    public string SecondaryGameError { get; private set; } = string.Empty;
    public bool IsSecondaryGameLoading { get; private set; }
    public GameSearchResult? SelectedSecondaryGame { get; private set; }
    public bool IsConnectionSaving { get; private set; }
    public string ConnectionSaveError { get; private set; } = string.Empty;

    public string BeginGameConnectionsLoad()
    {
        IsGameConnectionsLoading = true;
        GameConnectionsError = string.Empty;
        return "/games/connections";
    }

    public void CompleteGameConnectionsLoad(GameConnectionsResponse response)
    {
        GameConnections = response.Connections;
        IsGameConnectionsLoading = false;
    }

    public void FailGameConnectionsLoad(string error)
    {
        GameConnections = [];
        IsGameConnectionsLoading = false;
        GameConnectionsError = error;
    }

    public void SetPrimaryGameQuery(string query) => PrimaryGameQuery = query;

    public void SetSecondaryGameQuery(string query) => SecondaryGameQuery = query;

    public bool TryBeginPrimarySearch(out string? path) =>
        TryBeginGameSearch(PrimaryGameQuery, primary: true, out path);

    public bool TryBeginSecondarySearch(out string? path) =>
        TryBeginGameSearch(SecondaryGameQuery, primary: false, out path);

    public void CancelPrimarySearch() => IsPrimaryGameLoading = false;

    public void CancelSecondarySearch() => IsSecondaryGameLoading = false;

    public void CompletePrimarySearch(GameSearchResponse response)
    {
        PrimaryGameResults = response.Results;
        IsPrimaryGameLoading = false;
    }

    public void CompleteSecondarySearch(GameSearchResponse response)
    {
        SecondaryGameResults = response.Results;
        IsSecondaryGameLoading = false;
    }

    public void FailPrimarySearch(string error)
    {
        PrimaryGameResults = [];
        PrimaryGameError = error;
        IsPrimaryGameLoading = false;
    }

    public void FailSecondarySearch(string error)
    {
        SecondaryGameResults = [];
        SecondaryGameError = error;
        IsSecondaryGameLoading = false;
    }

    public void SelectPrimaryGame(GameSearchResult game)
    {
        SelectedPrimaryGame = game;
        PrimaryGameQuery = game.Title;
        PrimaryGameResults = [];
        PrimaryGameError = string.Empty;
    }

    public void SelectSecondaryGame(GameSearchResult game)
    {
        SelectedSecondaryGame = game;
        SecondaryGameQuery = game.Title;
        SecondaryGameResults = [];
        SecondaryGameError = string.Empty;
    }

    public bool TryBuildConnectionSubmission(out MappingConnectionSubmission? submission)
    {
        submission = null;
        if (SelectedPrimaryGame is null || SelectedSecondaryGame is null)
        {
            ConnectionSaveError = "Select two games to connect.";
            return false;
        }
        if (SelectedPrimaryGame.GameId == SelectedSecondaryGame.GameId)
        {
            ConnectionSaveError = "Pick two different games to connect.";
            return false;
        }

        submission = BuildConnectionSubmission(
            SelectedPrimaryGame.GameId,
            SelectedSecondaryGame.GameId,
            HttpMethod.Post);
        return true;
    }

    public static MappingConnectionSubmission BuildDisconnectSubmission(
        string gameId,
        string connectedGameId) =>
        BuildConnectionSubmission(gameId, connectedGameId, HttpMethod.Delete);

    public void BeginConnectionSave()
    {
        IsConnectionSaving = true;
        ConnectionSaveError = string.Empty;
    }

    public void CompleteConnect()
    {
        SelectedPrimaryGame = null;
        SelectedSecondaryGame = null;
        PrimaryGameQuery = string.Empty;
        SecondaryGameQuery = string.Empty;
        PrimaryGameResults = [];
        SecondaryGameResults = [];
    }

    public void FailConnectionSave(string error) => ConnectionSaveError = error;

    public void EndConnectionSave() => IsConnectionSaving = false;

    public void ClearConnectionDraft()
    {
        SelectedPrimaryGame = null;
        SelectedSecondaryGame = null;
        PrimaryGameQuery = string.Empty;
        SecondaryGameQuery = string.Empty;
        PrimaryGameResults = [];
        SecondaryGameResults = [];
        PrimaryGameError = string.Empty;
        SecondaryGameError = string.Empty;
        ConnectionSaveError = string.Empty;
    }

    private bool TryBeginGameSearch(string query, bool primary, out string? path)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            path = null;
            if (primary) PrimaryGameError = "Enter a search query.";
            else SecondaryGameError = "Enter a search query.";
            return false;
        }

        if (primary)
        {
            IsPrimaryGameLoading = true;
            PrimaryGameError = string.Empty;
        }
        else
        {
            IsSecondaryGameLoading = true;
            SecondaryGameError = string.Empty;
        }

        path = $"/games/search?query={EncodeUriComponent(trimmed)}";
        return true;
    }

    private static MappingConnectionSubmission BuildConnectionSubmission(
        string gameId,
        string connectedGameId,
        HttpMethod method) =>
        new(
            "/games/connections",
            method,
            new Dictionary<string, object?>
            {
                ["gameId"] = GameHash.EncodeGameId(gameId),
                ["connectedGameId"] = GameHash.EncodeGameId(connectedGameId),
            });

    private static string EncodeUriComponent(string value) =>
        Uri.EscapeDataString(value)
            .Replace("%21", "!", StringComparison.Ordinal)
            .Replace("%27", "'", StringComparison.Ordinal)
            .Replace("%28", "(", StringComparison.Ordinal)
            .Replace("%29", ")", StringComparison.Ordinal)
            .Replace("%2A", "*", StringComparison.Ordinal);
}

public partial class Mapping : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly MappingState _state = new();
    private CancellationTokenSource? _connectionsRequest;
    private CancellationTokenSource? _primarySearchRequest;
    private CancellationTokenSource? _secondarySearchRequest;
    private long _connectionsRequestVersion;
    private long _primarySearchRequestVersion;
    private long _secondarySearchRequestVersion;

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override Task OnInitializedAsync() =>
        MappingInitialLoadFlow.RunAsync(LoadGameConnectionsAsync);

    private async Task LoadGameConnectionsAsync()
    {
        var path = _state.BeginGameConnectionsLoad();
        _connectionsRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _connectionsRequest = requestCancellation;
        var version = ++_connectionsRequestVersion;
        try
        {
            var response = await Api.SendAsync<GameConnectionsResponse>(path,
                cancellationToken: requestCancellation.Token);
            if (version == _connectionsRequestVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteGameConnectionsLoad(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _connectionsRequestVersion) _state.FailGameConnectionsLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_connectionsRequest, requestCancellation)) _connectionsRequest = null;
            requestCancellation.Dispose();
        }
    }

    private void ChangePrimaryGameQuery(ChangeEventArgs eventArgs)
    {
        _state.SetPrimaryGameQuery(eventArgs.Value?.ToString() ?? string.Empty);
        InvalidatePrimarySearch();
    }

    private void ChangeSecondaryGameQuery(ChangeEventArgs eventArgs)
    {
        _state.SetSecondaryGameQuery(eventArgs.Value?.ToString() ?? string.Empty);
        InvalidateSecondarySearch();
    }

    private Task SearchPrimaryGamesAsync() => SearchGamesAsync(primary: true);

    private Task SearchSecondaryGamesAsync() => SearchGamesAsync(primary: false);

    private async Task SearchGamesAsync(bool primary)
    {
        string? path;
        var canSearch = primary
            ? _state.TryBeginPrimarySearch(out path)
            : _state.TryBeginSecondarySearch(out path);
        if (!canSearch) return;

        var previous = primary ? _primarySearchRequest : _secondarySearchRequest;
        previous?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var version = primary ? ++_primarySearchRequestVersion : ++_secondarySearchRequestVersion;
        if (primary) _primarySearchRequest = requestCancellation;
        else _secondarySearchRequest = requestCancellation;

        try
        {
            var response = await Api.SendAsync<GameSearchResponse>(path!,
                cancellationToken: requestCancellation.Token);
            var isCurrent = primary
                ? version == _primarySearchRequestVersion
                : version == _secondarySearchRequestVersion;
            if (!isCurrent || requestCancellation.IsCancellationRequested) return;
            if (primary) _state.CompletePrimarySearch(response);
            else _state.CompleteSecondarySearch(response);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var isCurrent = primary
                ? version == _primarySearchRequestVersion
                : version == _secondarySearchRequestVersion;
            if (isCurrent)
            {
                if (primary) _state.FailPrimarySearch(exception.Message);
                else _state.FailSecondarySearch(exception.Message);
            }
        }
        finally
        {
            if (primary && ReferenceEquals(_primarySearchRequest, requestCancellation))
            {
                _primarySearchRequest = null;
            }
            if (!primary && ReferenceEquals(_secondarySearchRequest, requestCancellation))
            {
                _secondarySearchRequest = null;
            }
            requestCancellation.Dispose();
        }
    }

    private async Task ConnectGamesAsync()
    {
        if (!_state.TryBuildConnectionSubmission(out var submission)) return;
        _state.BeginConnectionSave();
        try
        {
            await Api.SendAsync<GameConnectionChangeResponse>(submission!.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _state.CompleteConnect();
            await LoadGameConnectionsAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailConnectionSave(exception.Message);
        }
        finally
        {
            _state.EndConnectionSave();
        }
    }

    private async Task DisconnectGamesAsync(string gameId, string connectedGameId)
    {
        var submission = MappingState.BuildDisconnectSubmission(gameId, connectedGameId);
        _state.BeginConnectionSave();
        try
        {
            await Api.SendAsync<GameConnectionChangeResponse>(submission.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            await LoadGameConnectionsAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailConnectionSave(exception.Message);
        }
        finally
        {
            _state.EndConnectionSave();
        }
    }

    private async Task NavigateToGameAsync(string gameId)
    {
        var target = $"/games/{GameHash.EncodeGameId(gameId)}";
        var instruction = NavigationGuard.PlanNavigation(
            new Uri(Navigation.Uri).PathAndQuery,
            target,
            replaceHistory: false);
        if (instruction.Kind == NavigationInstructionKind.NoOp) return;

        Navigation.NavigateTo(instruction.Target!);
        if (instruction.ScrollToTop) await Browser.ScrollToTopAsync();
    }

    private void InvalidatePrimarySearch()
    {
        ++_primarySearchRequestVersion;
        _primarySearchRequest?.Cancel();
        _state.CancelPrimarySearch();
    }

    private void InvalidateSecondarySearch()
    {
        ++_secondarySearchRequestVersion;
        _secondarySearchRequest?.Cancel();
        _state.CancelSecondarySearch();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _connectionsRequest?.Cancel();
        _primarySearchRequest?.Cancel();
        _secondarySearchRequest?.Cancel();
        _connectionsRequest?.Dispose();
        _primarySearchRequest?.Dispose();
        _secondarySearchRequest?.Dispose();
        _lifetime.Dispose();
    }
}
