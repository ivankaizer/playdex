using Web.Infrastructure.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Web.Infrastructure.Components;

internal sealed class GameSearchBoxState
{
    public string Query { get; private set; } = string.Empty;
    public IReadOnlyList<GameSearchResult> Results { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public bool IsOpen { get; private set; }
    public int ActiveIndex { get; private set; } = -1;
    public string Error { get; private set; } = string.Empty;

    public void SetQuery(string query)
    {
        Query = query;
        Results = [];
        IsLoading = false;
        IsOpen = false;
        ActiveIndex = -1;
        Error = string.Empty;
    }

    public bool TryBeginSearch(out string? path)
    {
        var query = Query.Trim();
        if (query.Length == 0)
        {
            path = null;
            return false;
        }

        IsLoading = true;
        IsOpen = true;
        Error = string.Empty;
        path = $"/games/search?query={Uri.EscapeDataString(query)}&limit=5";
        return true;
    }

    public void CompleteSearch(GameSearchResponse response)
    {
        Results = response.Results.Take(5).ToArray();
        ActiveIndex = -1;
        IsLoading = false;
        IsOpen = true;
        Error = string.Empty;
    }

    public void FailSearch(string error)
    {
        Results = [];
        ActiveIndex = -1;
        IsLoading = false;
        IsOpen = true;
        Error = error;
    }

    public void MoveActive(int direction)
    {
        if (Results.Count == 0) return;
        ActiveIndex = Math.Clamp(ActiveIndex + direction, 0, Results.Count - 1);
    }

    public GameSearchResult? TakeActiveChoice()
    {
        if (ActiveIndex < 0 || ActiveIndex >= Results.Count) return null;
        var selected = Results[ActiveIndex];
        Reset();
        return selected;
    }

    public string TakeChoice(GameSearchResult choice)
    {
        Reset();
        return $"/games/{choice.GameId}";
    }

    public void Dismiss()
    {
        IsOpen = false;
        ActiveIndex = -1;
    }

    private void Reset()
    {
        Query = string.Empty;
        Results = [];
        IsLoading = false;
        IsOpen = false;
        ActiveIndex = -1;
        Error = string.Empty;
    }
}

public partial class GameSearchBox : ComponentBase, IDisposable
{
    private readonly GameSearchBoxState _state = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _searchRequest;
    private long _searchVersion;

    [Parameter, EditorRequired]
    public Func<string, CancellationToken, Task<GameSearchResponse>> SearchAsync { get; set; } = null!;

    [Parameter]
    public EventCallback<string> OnNavigate { get; set; }

    private string? ActiveOptionId =>
        _state.ActiveIndex < 0 ? null : OptionId(_state.ActiveIndex);

    private static string OptionId(int index) => $"game-search-option-{index}";

    private async Task HandleInputAsync(ChangeEventArgs eventArgs)
    {
        InvalidateSearch();
        _state.SetQuery(eventArgs.Value?.ToString() ?? string.Empty);
        if (_state.Query.Trim().Length == 0) return;

        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchRequest = request;
        var version = _searchVersion;

        try
        {
            await Task.Delay(250, request.Token);
            if (!_state.TryBeginSearch(out var path)) return;
            StateHasChanged();

            var response = await SearchAsync(path!, request.Token);
            if (version == _searchVersion && !request.IsCancellationRequested)
            {
                _state.CompleteSearch(response);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
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

    private async Task HandleKeyDownAsync(KeyboardEventArgs eventArgs)
    {
        switch (eventArgs.Key)
        {
            case "ArrowDown":
                _state.MoveActive(1);
                break;
            case "ArrowUp":
                _state.MoveActive(-1);
                break;
            case "Enter":
                var selected = _state.TakeActiveChoice();
                if (selected is not null) await NavigateAsync($"/games/{selected.GameId}");
                break;
            case "Escape":
                Dismiss();
                break;
        }
    }

    private void SetActive(int index)
    {
        var direction = index - _state.ActiveIndex;
        _state.MoveActive(direction);
    }

    private Task SelectAsync(GameSearchResult result) => NavigateAsync(_state.TakeChoice(result));

    private async Task NavigateAsync(string path)
    {
        InvalidateSearch();
        await OnNavigate.InvokeAsync(path);
    }

    private void Dismiss()
    {
        InvalidateSearch();
        _state.Dismiss();
    }

    private void InvalidateSearch()
    {
        ++_searchVersion;
        _searchRequest?.Cancel();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _searchRequest?.Cancel();
        _searchRequest?.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
