using System.Globalization;
using Microsoft.AspNetCore.Components;
using Web.Infrastructure.Http;
using Web.Modules.Admin;

namespace Web.Modules.Admin;

internal static class AdminRequests
{
    public const int SessionPageSize = 25;

    public static readonly IReadOnlyList<string> Initial =
    [
        "/admin/sessions/recent",
        "/admin/providers/platforms",
        "/admin/credentials",
        "/admin/sessions?limit=25&offset=0",
    ];

    public static string Sessions(string query, int page)
    {
        var safePage = Math.Max(1, page);
        var offset = (safePage - 1) * SessionPageSize;
        return $"/admin/sessions?query={EncodeUriComponent(query)}&limit={SessionPageSize}&offset={offset}";
    }

    public static string Games(string query) =>
        $"/admin/games/search?query={EncodeUriComponent(query)}&limit=20";

    public static string GameUpdate(string gameId) =>
        $"/admin/games?gameId={EncodeUriComponent(gameId)}";

    private static string EncodeUriComponent(string value) =>
        Uri.EscapeDataString(value)
            .Replace("%21", "!", StringComparison.Ordinal)
            .Replace("%27", "'", StringComparison.Ordinal)
            .Replace("%28", "(", StringComparison.Ordinal)
            .Replace("%29", ")", StringComparison.Ordinal)
            .Replace("%2A", "*", StringComparison.Ordinal);
}

internal sealed record AdminSnapshot(
    AdminRecentSessionsResponse Recent,
    AdminPlatformStatusesResponse Platforms,
    AdminCredentialsResponse Credentials,
    AdminSessionsListResponse Movable);

internal sealed record AdminSubmission(
    string Path,
    HttpMethod Method,
    IReadOnlyDictionary<string, object?> Body);

internal sealed record CredentialDraft(string ApiKey, string AccountUser);
internal enum CredentialDraftField { ApiKey, AccountUser }
internal sealed record GameDraft(string Title, bool LockTitle, string ImageUrl, bool LockImage);
internal enum GameDraftField { Title, LockTitle, ImageUrl, LockImage }

internal static class AdminInitialLoadFlow
{
    public static async Task<AdminSnapshot> RunAsync(
        Func<CancellationToken, Task<AdminRecentSessionsResponse>> loadRecent,
        Func<CancellationToken, Task<AdminPlatformStatusesResponse>> loadPlatforms,
        Func<CancellationToken, Task<AdminCredentialsResponse>> loadCredentials,
        Func<CancellationToken, Task<AdminSessionsListResponse>> loadMovable,
        CancellationToken cancellationToken)
    {
        using var siblings = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var recentTask = loadRecent(siblings.Token);
        var platformsTask = loadPlatforms(siblings.Token);
        var credentialsTask = loadCredentials(siblings.Token);
        var movableTask = loadMovable(siblings.Token);
        var tasks = new Task[] { recentTask, platformsTask, credentialsTask, movableTask };

        var pending = tasks.ToList();
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
                siblings.Cancel();
                _ = ObserveAsync(pending);
                throw;
            }
        }

        return new AdminSnapshot(
            await recentTask,
            await platformsTask,
            await credentialsTask,
            await movableTask);
    }

    private static async Task ObserveAsync(IReadOnlyCollection<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }
    }
}

internal sealed class AdminState
{
    private readonly HashSet<string> _updatingPlatforms = new(StringComparer.Ordinal);
    private readonly HashSet<long> _savingCredentialIds = [];
    private readonly HashSet<long> _selectedSessionIds = [];
    private readonly HashSet<string> _savingGameIds = new(StringComparer.Ordinal);

    public IReadOnlyList<AdminUserRecentSessions> RecentUsers { get; private set; } = [];
    public bool IsSessionsLoading { get; private set; }
    public string SessionsError { get; private set; } = string.Empty;
    public IReadOnlyList<AdminPlatformStatus> Platforms { get; private set; } = [];
    public bool IsPlatformsLoading { get; private set; }
    public string PlatformsError { get; private set; } = string.Empty;
    public IReadOnlyList<AdminCredential> Credentials { get; private set; } = [];
    public Dictionary<long, CredentialDraft> CredentialDrafts { get; private set; } = [];
    public bool IsCredentialsLoading { get; private set; }
    public string CredentialsError { get; private set; } = string.Empty;
    public string SelectedCredentialPlatform { get; private set; } = string.Empty;
    public string SessionQuery { get; private set; } = string.Empty;
    public IReadOnlyList<AdminMovableSession> MovableSessions { get; private set; } = [];
    public int SessionTotal { get; private set; }
    public int SessionPage { get; private set; } = 1;
    public string TargetCredentialId { get; private set; } = string.Empty;
    public bool IsSessionListLoading { get; private set; }
    public bool IsMovingSessions { get; private set; }
    public string MoveSessionsError { get; private set; } = string.Empty;
    public string MoveSessionsNotice { get; private set; } = string.Empty;
    public string GameQuery { get; private set; } = string.Empty;
    public IReadOnlyList<AdminGame> Games { get; private set; } = [];
    public Dictionary<string, GameDraft> GameDrafts { get; private set; } = new(StringComparer.Ordinal);
    public bool HasGameSearch { get; private set; }
    public bool IsGamesLoading { get; private set; }
    public string GamesError { get; private set; } = string.Empty;
    public bool IsAnyInitialLoading =>
        IsSessionsLoading || IsPlatformsLoading || IsCredentialsLoading || IsSessionListLoading;
    public IReadOnlyList<string> CredentialPlatforms => Credentials
        .Select(item => item.Platform)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    public IReadOnlyDictionary<string, int> CredentialCountsByPlatform => Credentials
        .GroupBy(item => item.Platform, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    public IReadOnlyList<AdminCredential> FilteredCredentials =>
        SelectedCredentialPlatform.Length == 0
            ? Credentials
            : Credentials.Where(item => item.Platform == SelectedCredentialPlatform).ToArray();
    public IReadOnlyCollection<long> SelectedSessionIds => _selectedSessionIds;
    public bool AllVisibleSessionsSelected =>
        MovableSessions.Count > 0 && MovableSessions.All(item => _selectedSessionIds.Contains(item.SessionId));
    public int SessionPageCount => Math.Max(1, (int)Math.Ceiling(SessionTotal / (double)AdminRequests.SessionPageSize));
    public int SessionRangeStart => SessionTotal == 0 ? 0 : (SessionPage - 1) * AdminRequests.SessionPageSize + 1;
    public int SessionRangeEnd => SessionTotal == 0 || MovableSessions.Count == 0
        ? 0
        : SessionRangeStart + MovableSessions.Count - 1;

    public void BeginInitialLoad()
    {
        IsSessionsLoading = true;
        IsPlatformsLoading = true;
        IsCredentialsLoading = true;
        IsSessionListLoading = true;
        SessionsError = string.Empty;
        PlatformsError = string.Empty;
        CredentialsError = string.Empty;
        MoveSessionsError = string.Empty;
        MoveSessionsNotice = string.Empty;
    }

    public void CompleteInitialLoad(AdminSnapshot snapshot)
    {
        RecentUsers = snapshot.Recent.Users;
        Platforms = snapshot.Platforms.Platforms;
        Credentials = snapshot.Credentials.Credentials;
        CredentialDrafts = Credentials.ToDictionary(
            item => item.Id,
            item => new CredentialDraft(item.ApiKey ?? string.Empty, item.AccountUser ?? string.Empty));
        MovableSessions = snapshot.Movable.Sessions;
        SessionTotal = snapshot.Movable.Total;
        SessionPage = 1;
        _selectedSessionIds.Clear();
        SelectedCredentialPlatform = CredentialPlatforms.Count > 0 ? CredentialPlatforms[0] : string.Empty;
        EndInitialLoading();
    }

    public void FailInitialLoad(string error)
    {
        SessionsError = error;
        PlatformsError = error;
        CredentialsError = error;
        MoveSessionsError = error;
        EndInitialLoading();
    }

    private void EndInitialLoading()
    {
        IsSessionsLoading = false;
        IsPlatformsLoading = false;
        IsCredentialsLoading = false;
        IsSessionListLoading = false;
    }

    public bool IsPlatformUpdating(string platform) => _updatingPlatforms.Contains(platform);

    public AdminSubmission BeginPlatformUpdate(AdminPlatformStatus platform)
    {
        _updatingPlatforms.Add(platform.Platform);
        PlatformsError = string.Empty;
        return new AdminSubmission(
            "/admin/providers/platforms/status",
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["platform"] = platform.Platform,
                ["enabled"] = !platform.Enabled,
            });
    }

    public void CompletePlatformUpdate(AdminPlatformStatusUpdateResponse response) =>
        Platforms = Platforms.Select(item => item.Platform == response.Platform
            ? item with
            {
                Enabled = response.Enabled,
                ActiveCredentials = response.Enabled ? item.TotalCredentials : 0,
            }
            : item).ToArray();

    public void FailPlatformUpdate(string error) => PlatformsError = error;
    public void EndPlatformUpdate(string platform) => _updatingPlatforms.Remove(platform);

    public void SelectCredentialPlatform(string platform) => SelectedCredentialPlatform = platform;

    public void SetCredentialDraft(long id, CredentialDraftField field, string value)
    {
        var draft = CredentialDrafts.GetValueOrDefault(id) ?? new CredentialDraft(string.Empty, string.Empty);
        CredentialDrafts[id] = field == CredentialDraftField.ApiKey
            ? draft with { ApiKey = value }
            : draft with { AccountUser = value };
    }

    public bool IsCredentialSaving(long id) => _savingCredentialIds.Contains(id);

    public AdminSubmission? BeginCredentialSave(long id)
    {
        if (!CredentialDrafts.TryGetValue(id, out var draft)) return null;
        _savingCredentialIds.Add(id);
        CredentialsError = string.Empty;
        return new AdminSubmission(
            $"/admin/credentials/{id.ToString(CultureInfo.InvariantCulture)}",
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["apiKey"] = EmptyToNull(draft.ApiKey),
                ["user"] = EmptyToNull(draft.AccountUser),
            });
    }

    public void CompleteCredentialSave(AdminCredentialUpdateResponse response)
    {
        Credentials = Credentials.Select(item => item.Id == response.Credential.Id
            ? response.Credential
            : item).ToArray();
        CredentialDrafts[response.Credential.Id] = new CredentialDraft(
            response.Credential.ApiKey ?? string.Empty,
            response.Credential.AccountUser ?? string.Empty);
    }

    public void FailCredentialSave(string error) => CredentialsError = error;
    public void EndCredentialSave(long id) => _savingCredentialIds.Remove(id);

    public void SetSessionQuery(string value) => SessionQuery = value;
    public void SetTargetCredentialId(string value) => TargetCredentialId = value;

    public string BeginMovableSessionsLoad(string query, int page)
    {
        IsSessionListLoading = true;
        MoveSessionsError = string.Empty;
        return AdminRequests.Sessions(query, page);
    }

    public void CompleteMovableSessionsLoad(AdminSessionsListResponse response, int page)
    {
        MovableSessions = response.Sessions;
        SessionTotal = response.Total;
        SessionPage = Math.Max(1, page);
        _selectedSessionIds.RemoveWhere(id => MovableSessions.All(item => item.SessionId != id));
        IsSessionListLoading = false;
    }

    public void FailMovableSessionsLoad(string error)
    {
        IsSessionListLoading = false;
        MoveSessionsError = error;
    }

    public void ToggleSessionSelection(long id)
    {
        if (!_selectedSessionIds.Add(id)) _selectedSessionIds.Remove(id);
    }

    public void ToggleVisibleSessions()
    {
        if (AllVisibleSessionsSelected)
        {
            foreach (var session in MovableSessions) _selectedSessionIds.Remove(session.SessionId);
            return;
        }

        foreach (var session in MovableSessions) _selectedSessionIds.Add(session.SessionId);
    }

    public bool TryBuildMoveSubmission(out AdminSubmission? submission)
    {
        submission = null;
        if (!long.TryParse(TargetCredentialId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target) || target <= 0)
        {
            MoveSessionsError = "Select a target account.";
            return false;
        }

        if (_selectedSessionIds.Count == 0)
        {
            MoveSessionsError = "Select at least one session.";
            return false;
        }

        IsMovingSessions = true;
        MoveSessionsError = string.Empty;
        MoveSessionsNotice = string.Empty;
        submission = new AdminSubmission(
            "/admin/sessions/move",
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["sessionIds"] = _selectedSessionIds.ToArray(),
                ["targetCredentialId"] = target,
            });
        return true;
    }

    public void CompleteMove(AdminSessionMoveResponse response)
    {
        MoveSessionsNotice = $"Moved {response.MovedSessions} session(s).";
        _selectedSessionIds.Clear();
    }

    public void FailMove(string error) => MoveSessionsError = error;
    public void EndMove() => IsMovingSessions = false;

    public void SetGameQuery(string value) => GameQuery = value;

    public bool TryBeginGameSearch(out string? path)
    {
        var query = GameQuery.Trim();
        if (query.Length == 0)
        {
            HasGameSearch = false;
            Games = [];
            GameDrafts.Clear();
            GamesError = "Enter a game title to search.";
            path = null;
            return false;
        }

        HasGameSearch = true;
        IsGamesLoading = true;
        GamesError = string.Empty;
        path = AdminRequests.Games(query);
        return true;
    }

    public void CompleteGameSearch(AdminGameSearchResponse response)
    {
        Games = response.Games;
        GameDrafts = Games.ToDictionary(
            item => item.Id,
            item => new GameDraft(item.Title, item.TitleLocked, item.ImageUrl ?? string.Empty, item.ImageLocked),
            StringComparer.Ordinal);
        IsGamesLoading = false;
    }

    public void FailGameSearch(string error)
    {
        IsGamesLoading = false;
        GamesError = error;
    }

    public void SetGameDraft(string id, GameDraftField field, string value)
    {
        var draft = ResolveGameDraft(id);
        GameDrafts[id] = field switch
        {
            GameDraftField.Title => draft with { Title = value },
            GameDraftField.ImageUrl => draft with { ImageUrl = value },
            _ => draft,
        };
    }

    public void SetGameDraft(string id, GameDraftField field, bool value)
    {
        var draft = ResolveGameDraft(id);
        GameDrafts[id] = field switch
        {
            GameDraftField.LockTitle => draft with { LockTitle = value },
            GameDraftField.LockImage => draft with { LockImage = value },
            _ => draft,
        };
    }

    public bool IsGameSaving(string id) => _savingGameIds.Contains(id);

    public AdminSubmission? BeginGameSave(string id)
    {
        if (!GameDrafts.TryGetValue(id, out var draft)) return null;
        _savingGameIds.Add(id);
        GamesError = string.Empty;
        return new AdminSubmission(
            AdminRequests.GameUpdate(id),
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["gameId"] = id,
                ["id"] = id,
                ["title"] = draft.Title.Trim(),
                ["lockTitle"] = draft.LockTitle,
                ["imageUrl"] = EmptyToNull(draft.ImageUrl),
                ["lockImage"] = draft.LockImage,
            });
    }

    public void CompleteGameSave(AdminGameUpdateResponse response)
    {
        Games = Games.Select(item => item.Id == response.Game.Id ? response.Game : item).ToArray();
        GameDrafts[response.Game.Id] = new GameDraft(
            response.Game.Title,
            response.Game.TitleLocked,
            response.Game.ImageUrl ?? string.Empty,
            response.Game.ImageLocked);
    }

    public void FailGameSave(string error) => GamesError = error;
    public void EndGameSave(string id) => _savingGameIds.Remove(id);

    private GameDraft ResolveGameDraft(string id)
    {
        if (GameDrafts.TryGetValue(id, out var draft)) return draft;
        var game = Games.FirstOrDefault(item => item.Id == id);
        return game is null
            ? new GameDraft(string.Empty, true, string.Empty, true)
            : new GameDraft(game.Title, game.TitleLocked, game.ImageUrl ?? string.Empty, game.ImageLocked);
    }

    private static string? EmptyToNull(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

public partial class Admin : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AdminState _state = new();
    private CancellationTokenSource? _initialRequest;
    private CancellationTokenSource? _sessionListRequest;
    private CancellationTokenSource? _gameSearchRequest;
    private long _initialVersion;
    private long _sessionListVersion;
    private long _gameSearchVersion;

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        _state.BeginInitialLoad();
        var version = ++_initialVersion;
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _initialRequest = requestCancellation;
        try
        {
            var snapshot = await AdminInitialLoadFlow.RunAsync(
                token => Api.SendAsync<AdminRecentSessionsResponse>(AdminRequests.Initial[0], cancellationToken: token),
                token => Api.SendAsync<AdminPlatformStatusesResponse>(AdminRequests.Initial[1], cancellationToken: token),
                token => Api.SendAsync<AdminCredentialsResponse>(AdminRequests.Initial[2], cancellationToken: token),
                token => Api.SendAsync<AdminSessionsListResponse>(AdminRequests.Initial[3], cancellationToken: token),
                requestCancellation.Token);
            if (version == _initialVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteInitialLoad(snapshot);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _initialVersion) _state.FailInitialLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_initialRequest, requestCancellation)) _initialRequest = null;
            requestCancellation.Dispose();
        }
    }

    private async Task TogglePlatformAsync(AdminPlatformStatus platform)
    {
        var submission = _state.BeginPlatformUpdate(platform);
        try
        {
            var response = await Api.SendAsync<AdminPlatformStatusUpdateResponse>(
                submission.Path, submission.Method, submission.Body, cancellationToken: _lifetime.Token);
            _state.CompletePlatformUpdate(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailPlatformUpdate(exception.Message);
        }
        finally
        {
            _state.EndPlatformUpdate(platform.Platform);
        }
    }

    private void ChangeCredentialDraft(long id, CredentialDraftField field, ChangeEventArgs eventArgs) =>
        _state.SetCredentialDraft(id, field, eventArgs.Value?.ToString() ?? string.Empty);

    private async Task SaveCredentialAsync(long id)
    {
        var submission = _state.BeginCredentialSave(id);
        if (submission is null) return;
        try
        {
            var response = await Api.SendAsync<AdminCredentialUpdateResponse>(
                submission.Path, submission.Method, submission.Body, cancellationToken: _lifetime.Token);
            _state.CompleteCredentialSave(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailCredentialSave(exception.Message);
        }
        finally
        {
            _state.EndCredentialSave(id);
        }
    }

    private async Task LoadMovableSessionsAsync(string query, int page)
    {
        var version = ++_sessionListVersion;
        _sessionListRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _sessionListRequest = requestCancellation;
        var safePage = Math.Max(1, page);
        var path = _state.BeginMovableSessionsLoad(query, safePage);
        try
        {
            var response = await Api.SendAsync<AdminSessionsListResponse>(path,
                cancellationToken: requestCancellation.Token);
            if (version == _sessionListVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteMovableSessionsLoad(response, safePage);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _sessionListVersion) _state.FailMovableSessionsLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_sessionListRequest, requestCancellation)) _sessionListRequest = null;
            requestCancellation.Dispose();
        }
    }

    private Task SearchSessionsAsync()
    {
        _state.SetSessionQuery(_state.SessionQuery);
        return LoadMovableSessionsAsync(_state.SessionQuery.Trim(), 1);
    }

    private void ChangeSessionQuery(ChangeEventArgs eventArgs) =>
        _state.SetSessionQuery(eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeTargetCredential(ChangeEventArgs eventArgs) =>
        _state.SetTargetCredentialId(eventArgs.Value?.ToString() ?? string.Empty);

    private async Task MoveSessionsAsync()
    {
        if (!_state.TryBuildMoveSubmission(out var submission)) return;
        try
        {
            var response = await Api.SendAsync<AdminSessionMoveResponse>(
                submission!.Path, submission.Method, submission.Body, cancellationToken: _lifetime.Token);
            _state.CompleteMove(response);
            await LoadMovableSessionsAsync(_state.SessionQuery.Trim(), _state.SessionPage);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailMove(exception.Message);
        }
        finally
        {
            _state.EndMove();
        }
    }

    private async Task SearchGamesAsync()
    {
        if (!_state.TryBeginGameSearch(out var path)) return;
        var version = ++_gameSearchVersion;
        _gameSearchRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _gameSearchRequest = requestCancellation;
        try
        {
            var response = await Api.SendAsync<AdminGameSearchResponse>(path!,
                cancellationToken: requestCancellation.Token);
            if (version == _gameSearchVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteGameSearch(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _gameSearchVersion) _state.FailGameSearch(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_gameSearchRequest, requestCancellation)) _gameSearchRequest = null;
            requestCancellation.Dispose();
        }
    }

    private void ChangeGameQuery(ChangeEventArgs eventArgs) =>
        _state.SetGameQuery(eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeGameDraft(string id, GameDraftField field, ChangeEventArgs eventArgs) =>
        _state.SetGameDraft(id, field, eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeGameDraft(string id, GameDraftField field, bool value) =>
        _state.SetGameDraft(id, field, value);

    private async Task SaveGameAsync(string id)
    {
        var submission = _state.BeginGameSave(id);
        if (submission is null) return;
        try
        {
            var response = await Api.SendAsync<AdminGameUpdateResponse>(
                submission.Path, submission.Method, submission.Body, cancellationToken: _lifetime.Token);
            _state.CompleteGameSave(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailGameSave(exception.Message);
        }
        finally
        {
            _state.EndGameSave(id);
        }
    }

    public void Dispose()
    {
        _initialVersion++;
        _sessionListVersion++;
        _gameSearchVersion++;
        _initialRequest?.Cancel();
        _sessionListRequest?.Cancel();
        _gameSearchRequest?.Cancel();
        _initialRequest?.Dispose();
        _sessionListRequest?.Dispose();
        _gameSearchRequest?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
