using Microsoft.AspNetCore.Components;
using Web.Infrastructure.Http;
using Web.Infrastructure.Models;

namespace Web.Modules.Sessions;

public partial class SessionCreatePage : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SessionCreateState _state = new();
    private IReadOnlyList<ManualPlatform> _platforms = [];
    private DraftCapabilities? _draftCapabilities;
    private CancellationTokenSource? _searchRequest;
    private CancellationTokenSource? _saveRequest;
    private long _searchVersion;
    private bool _draftsOpen;
    private string _theme = "light";

    [Inject] private PlaydexApiClient Api { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var mode = new Uri(Navigation.Uri).Query.Contains("mode=live", StringComparison.OrdinalIgnoreCase)
            ? SessionCreateMode.Live
            : SessionCreateMode.Historical;
        _state.Open(mode, DateTimeOffset.Now);
        await Task.WhenAll(LoadPlatformsAsync(), LoadCapabilitiesAsync());
    }

    private void SetTheme(string theme) => _theme = theme;

    private async Task LoadPlatformsAsync()
    {
        try { _platforms = (await Api.SendAsync<ManualPlatformsResponse>("/manual/platforms", cancellationToken: _lifetime.Token)).Platforms; }
        catch { _platforms = []; }
    }

    private async Task LoadCapabilitiesAsync()
    {
        try { _draftCapabilities = await Api.SendAsync<DraftCapabilities>("/manual/draft-capabilities", cancellationToken: _lifetime.Token); }
        catch { _draftCapabilities = null; }
    }

    private void OpenDrafts()
    {
        _state.Close();
        InvalidateSearch();
        _draftsOpen = true;
    }

    private void ShowManual()
    {
        if (_draftsOpen) CloseDrafts();
    }

    private void CloseDrafts()
    {
        _draftsOpen = false;
        _state.Open(SessionCreateMode.Historical, DateTimeOffset.Now);
    }

    private static Task DraftsAcceptedAsync() => Task.CompletedTask;

    private string WorkspaceTabSelected(bool drafts) => (_draftsOpen == drafts).ToString().ToLowerInvariant();

    private string WorkspaceTabClass(bool drafts) =>
        $"flex-1 rounded-xl px-5 py-2.5 text-xs font-semibold uppercase tracking-[0.18em] transition sm:flex-none {(_draftsOpen == drafts ? "bg-white text-emerald-700 shadow-sm dark:bg-slate-800 dark:text-emerald-200" : "text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200")}";

    private void QueryChanged(string _)
    {
        InvalidateSearch();
        _state.CancelSearch();
    }

    private async Task SearchAsync()
    {
        if (!_state.TryBeginSearch(out var path)) return;
        InvalidateSearch();
        var version = _searchVersion;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchRequest = request;
        try
        {
            var response = await Api.SendAsync<ManualMappingSearchResponse>(path!, cancellationToken: request.Token);
            if (version == _searchVersion && !request.IsCancellationRequested) _state.CompleteSearch(response);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (version == _searchVersion) _state.FailSearch(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_searchRequest, request)) _searchRequest = null;
            request.Dispose();
        }
    }

    private async Task SaveAsync()
    {
        if (_state.IsSaving || !_state.TryBuildSubmission(out var submission) || !_state.TryBeginSave()) return;
        _saveRequest?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _saveRequest = request;
        try
        {
            switch (submission!.Body)
            {
                case ManualEntryCreateRequest entry:
                    await Api.SendAsync<ManualEntryCreateResponse>(submission.Path, submission.Method, entry, cancellationToken: request.Token);
                    break;
                case ManualSessionStartRequest start:
                    await Api.SendAsync<ManualSessionStartResponse>(submission.Path, submission.Method, start, cancellationToken: request.Token);
                    break;
                default: throw new InvalidOperationException("Unsupported session creation request.");
            }
            _state.CompleteSave();
            Navigation.NavigateTo("/sessions");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception) { _state.FailSave(exception.Message); }
        finally
        {
            if (ReferenceEquals(_saveRequest, request)) _saveRequest = null;
            request.Dispose();
        }
    }

    private Task BackToSessionsAsync()
    {
        Navigation.NavigateTo("/sessions");
        return Task.CompletedTask;
    }

    private void InvalidateSearch()
    {
        ++_searchVersion;
        _searchRequest?.Cancel();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        InvalidateSearch();
        _saveRequest?.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
