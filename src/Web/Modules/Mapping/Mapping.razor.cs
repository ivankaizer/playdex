using Microsoft.AspNetCore.Components;
using Web.Shared.Http;
using Web.Shared.Browser;
using Web.Shared.Models;
using Web.Routing;
using Web.Shared.Common;

namespace Web.Modules.Mapping;

internal sealed record MappingSubmission(
    string Path,
    IReadOnlyDictionary<string, object?> Body);

internal sealed record MappingConnectionSubmission(
    string Path,
    HttpMethod Method,
    IReadOnlyDictionary<string, object?> Body);

internal static class MappingInitialLoadFlow
{
    public static Task RunAsync(Func<Task> loadManualInputs, Func<Task> loadConnections)
    {
        var manualInputsTask = loadManualInputs();
        var connectionsTask = loadConnections();
        return Task.WhenAll(manualInputsTask, connectionsTask);
    }
}

internal sealed class MappingState
{
    public IReadOnlyList<ManualInput> ManualInputs { get; private set; } = [];
    public bool IsManualInputsLoading { get; private set; }
    public string ManualInputsError { get; private set; } = string.Empty;
    public long? ActiveMappingInputId { get; private set; }
    public string MappingSearchQuery { get; private set; } = string.Empty;
    public IReadOnlyList<ManualMappingSearchResult> MappingSearchResults { get; private set; } = [];
    public string MappingSearchError { get; private set; } = string.Empty;
    public bool IsMappingSearchLoading { get; private set; }
    public bool MappingIgdbAvailable { get; private set; } = true;
    public bool IsMappingUpdateLoading { get; private set; }
    public string MappingPlatformOverride { get; private set; } = "auto";
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

    public string BeginManualInputsLoad()
    {
        IsManualInputsLoading = true;
        ManualInputsError = string.Empty;
        return "/manual/inputs";
    }

    public void CompleteManualInputsLoad(ManualInputsResponse response)
    {
        ManualInputs = response.Inputs;
        IsManualInputsLoading = false;
    }

    public void FailManualInputsLoad(string error)
    {
        ManualInputs = [];
        IsManualInputsLoading = false;
        ManualInputsError = error;
    }

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

    public void OpenMappingRow(ManualInput input)
    {
        ActiveMappingInputId = input.Id;
        MappingSearchQuery = input.MappedGame?.Title ?? input.Input;
        MappingSearchResults = [];
        MappingSearchError = string.Empty;
        MappingPlatformOverride = !string.IsNullOrWhiteSpace(input.MappedGame?.Platform)
            ? input.MappedGame.Platform
            : "auto";
    }

    public void CloseMappingRow()
    {
        ActiveMappingInputId = null;
        MappingPlatformOverride = "auto";
    }

    public void SetMappingSearchQuery(string query) => MappingSearchQuery = query;

    public void SetMappingPlatformOverride(string platform) => MappingPlatformOverride = platform;

    public bool TryBeginMappingSearch(out string? path)
    {
        var query = MappingSearchQuery.Trim();
        if (query.Length == 0)
        {
            path = null;
            MappingSearchError = "Enter a search query.";
            return false;
        }

        IsMappingSearchLoading = true;
        MappingSearchError = string.Empty;
        path = $"/manual/search?query={EncodeUriComponent(query)}";
        return true;
    }

    public void CancelMappingSearch() => IsMappingSearchLoading = false;

    public void CompleteMappingSearch(ManualMappingSearchResponse response)
    {
        MappingIgdbAvailable = response.IgdbAvailable;
        MappingSearchResults = response.Results;
        IsMappingSearchLoading = false;
    }

    public void FailMappingSearch(string error)
    {
        MappingSearchResults = [];
        MappingSearchError = error;
        IsMappingSearchLoading = false;
    }

    public bool TryBuildMappingSubmission(
        long inputId,
        ManualMappingSearchResult result,
        out MappingSubmission? submission)
    {
        submission = null;
        if (result.Source == "local" && !string.IsNullOrEmpty(result.GameId))
        {
            submission = new MappingSubmission(
                $"/manual/inputs/{inputId}/map",
                new Dictionary<string, object?>
                {
                    ["gameId"] = GameHash.EncodeGameId(result.GameId),
                });
            return true;
        }

        if (result.Source == "igdb" && !string.IsNullOrEmpty(result.IgdbId))
        {
            submission = new MappingSubmission(
                $"/manual/inputs/{inputId}/map",
                new Dictionary<string, object?>
                {
                    ["igdbId"] = result.IgdbId,
                    ["title"] = result.Title,
                    ["platformId"] = result.PlatformId,
                    ["platformKey"] = MappingPlatformOverride == "auto"
                        ? null
                        : MappingPlatformOverride,
                    ["capsuleUrl"] = result.CapsuleUrl,
                });
            return true;
        }

        MappingSearchError = "Invalid mapping selection.";
        return false;
    }

    public void BeginMappingUpdate()
    {
        IsMappingUpdateLoading = true;
        MappingSearchError = string.Empty;
    }

    public void CompleteMappingUpdate()
    {
        IsMappingUpdateLoading = false;
        ActiveMappingInputId = null;
        MappingSearchQuery = string.Empty;
        MappingSearchResults = [];
        MappingPlatformOverride = "auto";
    }

    public void FailMappingUpdate(string error)
    {
        IsMappingUpdateLoading = false;
        MappingSearchError = error;
    }

    public void EndMappingUpdate() => IsMappingUpdateLoading = false;

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
    private static readonly IReadOnlyList<(string Key, string Label)> ManualPlatformOptions =
    [
        ("auto", "Use IGDB platform"),
        ("ps5", "PS5"),
        ("pc", "PC"),
        ("steam", "Steam"),
        ("playnite", "Playnite"),
        ("switch-1", "Nintendo Switch 1"),
        ("switch-2", "Nintendo Switch 2"),
        ("xbox-series-xs", "Xbox Series X|S"),
        ("unknown", "Unknown"),
    ];

    private readonly CancellationTokenSource _lifetime = new();
    private readonly MappingState _state = new();
    private CancellationTokenSource? _manualInputsRequest;
    private CancellationTokenSource? _connectionsRequest;
    private CancellationTokenSource? _mappingSearchRequest;
    private CancellationTokenSource? _mappingUpdateRequest;
    private CancellationTokenSource? _primarySearchRequest;
    private CancellationTokenSource? _secondarySearchRequest;
    private long _manualInputsRequestVersion;
    private long _connectionsRequestVersion;
    private long _mappingSearchRequestVersion;
    private long _mappingUpdateRequestVersion;
    private long _primarySearchRequestVersion;
    private long _secondarySearchRequestVersion;

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override Task OnInitializedAsync() =>
        MappingInitialLoadFlow.RunAsync(LoadManualInputsAsync, LoadGameConnectionsAsync);

    private async Task LoadManualInputsAsync()
    {
        var path = _state.BeginManualInputsLoad();
        _manualInputsRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _manualInputsRequest = requestCancellation;
        var version = ++_manualInputsRequestVersion;
        try
        {
            var response = await Api.SendAsync<ManualInputsResponse>(path,
                cancellationToken: requestCancellation.Token);
            if (version == _manualInputsRequestVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteManualInputsLoad(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _manualInputsRequestVersion) _state.FailManualInputsLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_manualInputsRequest, requestCancellation)) _manualInputsRequest = null;
            requestCancellation.Dispose();
        }
    }

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

    private void OpenMappingRow(ManualInput input)
    {
        InvalidateMappingUpdate();
        InvalidateMappingSearch();
        _state.OpenMappingRow(input);
    }

    private void CloseMappingRow()
    {
        InvalidateMappingUpdate();
        InvalidateMappingSearch();
        _state.CloseMappingRow();
    }

    private void ChangeMappingSearchQuery(ChangeEventArgs eventArgs)
    {
        _state.SetMappingSearchQuery(eventArgs.Value?.ToString() ?? string.Empty);
        InvalidateMappingSearch();
    }

    private void ChangeMappingPlatformOverride(ChangeEventArgs eventArgs) =>
        _state.SetMappingPlatformOverride(eventArgs.Value?.ToString() ?? "auto");

    private async Task SearchMappingAsync()
    {
        if (!_state.TryBeginMappingSearch(out var path)) return;
        _mappingSearchRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _mappingSearchRequest = requestCancellation;
        var version = ++_mappingSearchRequestVersion;
        try
        {
            var response = await Api.SendAsync<ManualMappingSearchResponse>(path!,
                cancellationToken: requestCancellation.Token);
            if (version == _mappingSearchRequestVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteMappingSearch(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _mappingSearchRequestVersion) _state.FailMappingSearch(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_mappingSearchRequest, requestCancellation)) _mappingSearchRequest = null;
            requestCancellation.Dispose();
        }
    }

    private async Task SelectMappingResultAsync(long inputId, ManualMappingSearchResult result)
    {
        _state.BeginMappingUpdate();
        if (!_state.TryBuildMappingSubmission(inputId, result, out var submission))
        {
            _state.EndMappingUpdate();
            return;
        }

        _mappingUpdateRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _mappingUpdateRequest = requestCancellation;
        var version = ++_mappingUpdateRequestVersion;
        try
        {
            await Api.SendAsync<ManualMappingResponse>(submission!.Path,
                HttpMethod.Post,
                submission.Body,
                cancellationToken: requestCancellation.Token);
            if (version != _mappingUpdateRequestVersion || requestCancellation.IsCancellationRequested) return;

            _state.CompleteMappingUpdate();
            await LoadManualInputsAsync();
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _mappingUpdateRequestVersion) _state.FailMappingUpdate(exception.Message);
        }
        finally
        {
            if (version == _mappingUpdateRequestVersion) _state.EndMappingUpdate();
            if (ReferenceEquals(_mappingUpdateRequest, requestCancellation)) _mappingUpdateRequest = null;
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

    private Task NavigateMappingResultAsync(ManualMappingSearchResult result) =>
        string.IsNullOrEmpty(result.GameId)
            ? Task.CompletedTask
            : NavigateToGameAsync(result.GameId);

    private string MappingResultPlatform(ManualMappingSearchResult result)
    {
        if (result.Source == "igdb")
        {
            return _state.MappingPlatformOverride == "auto"
                ? result.Platform ?? "Unknown platform"
                : Formatters.FormatPlatformLabel(_state.MappingPlatformOverride);
        }

        return string.IsNullOrEmpty(result.Platform)
            ? "Unknown platform"
            : Formatters.FormatPlatformLabel(result.Platform);
    }

    private static string MappingButtonClass(ManualInput input) =>
        "w-full rounded-full px-3 py-2 text-xs font-semibold uppercase tracking-[0.3em] transition sm:w-auto " +
        (input.MappedGame is not null
            ? "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200 hover:border-emerald-300/60 dark:hover:border-emerald-400/60 hover:text-emerald-700 dark:hover:text-emerald-100"
            : "border border-emerald-300/60 dark:border-emerald-400/60 text-emerald-700 dark:text-emerald-200 hover:border-emerald-300 dark:hover:border-emerald-400");

    private void InvalidateMappingSearch()
    {
        ++_mappingSearchRequestVersion;
        _mappingSearchRequest?.Cancel();
        _state.CancelMappingSearch();
    }

    private void InvalidateMappingUpdate()
    {
        ++_mappingUpdateRequestVersion;
        _mappingUpdateRequest?.Cancel();
        _state.EndMappingUpdate();
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
        _manualInputsRequest?.Cancel();
        _connectionsRequest?.Cancel();
        _mappingSearchRequest?.Cancel();
        _mappingUpdateRequest?.Cancel();
        _primarySearchRequest?.Cancel();
        _secondarySearchRequest?.Cancel();
        _manualInputsRequest?.Dispose();
        _connectionsRequest?.Dispose();
        _mappingSearchRequest?.Dispose();
        _mappingUpdateRequest?.Dispose();
        _primarySearchRequest?.Dispose();
        _secondarySearchRequest?.Dispose();
        _lifetime.Dispose();
    }
}
