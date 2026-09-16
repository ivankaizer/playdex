using Microsoft.AspNetCore.Components;
using Web.Infrastructure.Models;

namespace Web.Infrastructure.Components;

public partial class LiveSessionControl : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private string _query = string.Empty;
    private string _platformKey = string.Empty;
    private IReadOnlyList<ManualMappingSearchResult> _results = [];
    private ManualMappingSearchResult? _selected;
    private bool _busy;

    [Parameter] public IReadOnlyList<ManualPlatform> Platforms { get; set; } = [];
    [Parameter] public ManualActiveSession? ActiveSession { get; set; }
    [Parameter] public bool Busy { get; set; }
    [Parameter] public string Error { get; set; } = string.Empty;
    [Parameter, EditorRequired] public Func<string, CancellationToken, Task<ManualMappingSearchResponse>> SearchGamesAsync { get; set; } = null!;
    [Parameter, EditorRequired] public EventCallback<ManualSessionStartRequest> OnStart { get; set; }
    [Parameter, EditorRequired] public EventCallback OnStop { get; set; }

    private void ChangeQuery(ChangeEventArgs args)
    {
        _query = args.Value?.ToString() ?? string.Empty;
        _results = [];
        _selected = null;
    }

    private void ChangePlatform(ChangeEventArgs args) => _platformKey = args.Value?.ToString() ?? string.Empty;

    private async Task SearchAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_query)) return;
        _busy = true;
        _request?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _request = request;
        try
        {
            var response = await SearchGamesAsync($"/manual/search?query={Uri.EscapeDataString(_query.Trim())}", request.Token);
            if (!request.IsCancellationRequested)
            {
                _results = response.Results;
                _selected = _results.Count == 1 ? _results[0] : null;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_request, request)) _request = null;
            _busy = false;
            request.Dispose();
        }
    }

    private void Select(ManualMappingSearchResult result) => _selected = result;

    private Task StartAsync() => _selected is null
        ? Task.CompletedTask
        : OnStart.InvokeAsync(new ManualSessionStartRequest(_selected.Title, _selected.GameId, _platformKey, _selected.IgdbId, _selected.CapsuleUrl));

    private Task StopAsync() => OnStop.InvokeAsync();

    public void Dispose()
    {
        _lifetime.Cancel();
        _request?.Cancel();
        _request?.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
