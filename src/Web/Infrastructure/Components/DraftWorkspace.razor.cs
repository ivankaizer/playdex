using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Web.Infrastructure.Models;
using Web.Modules.Sessions;

namespace Web.Infrastructure.Components;

public partial class DraftWorkspace
{
    [Parameter] public DraftCapabilities? Capabilities { get; set; }
    [Parameter] public IReadOnlyList<ManualPlatform> Platforms { get; set; } = [];
    [Parameter] public bool Embedded { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnAccepted { get; set; }
    private IReadOnlyList<DraftBatchDto> _batches = [];
    private DraftBatchDto? _batch;
    private readonly List<DraftRowEdit> _rows = [];
    private string _text = "", _error = "", _outcome = "", _preview = "", _mediaType = "", _fileName = "screenshot.png";
    private byte[]? _image;
    private bool _busy;
    private Guid _requestId = Guid.NewGuid();
    private GenerationSource? _generationSource;
    private Guid? _generatedBatchId;
    private bool _generationFailed;
    private sealed record GenerationSource(string Text, byte[]? Image, string MediaType, string FileName, string Timezone);
    private readonly string _pasteId = $"draft-paste-{Guid.NewGuid():N}";
    private DotNetObjectReference<DraftWorkspace>? _reference;
    private readonly CancellationTokenSource _lifetime = new();
    private static string ReviewFieldClass(bool needsReview) => needsReview
        ? "border-rose-500 bg-rose-50 focus:border-rose-600 focus:ring-2 focus:ring-rose-200 dark:border-rose-400 dark:bg-rose-950/30 dark:focus:border-rose-300 dark:focus:ring-rose-900"
        : "border-slate-300 bg-white focus:border-emerald-400 dark:border-slate-700 dark:bg-slate-950";
    private static string AriaInvalid(bool invalid) => invalid ? "true" : "false";
    protected override async Task OnInitializedAsync() => await LoadBatchesAsync();
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_batch is null && Capabilities?.Available == true)
        {
            _reference ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("playdex.drafts.attachPaste", _pasteId, _reference, Capabilities.MaxImageBytes, Capabilities.MaxImagePixels);
        }
    }
    private async Task LoadBatchesAsync()
    {
        try { _batches = await Api.SendAsync<DraftBatchDto[]>("/manual/draft-batches", cancellationToken: _lifetime.Token); }
        catch (Exception exception) { _error = exception.Message; }
    }
    private async Task ResumeAsync(Guid id)
    {
        _busy = true; _error = ""; _generationFailed = false;
        try { Show(await Api.SendAsync<DraftBatchDto>($"/manual/draft-batches/{id}", cancellationToken: _lifetime.Token)); }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }
    private void Show(DraftBatchDto batch) { _outcome = ""; _batch = batch; _rows.Clear(); _rows.AddRange(batch.Rows.Where(r => r.Status != "deleted").Select(r => new DraftRowEdit(r))); }
    private async Task<bool> SaveAllAsync()
    {
        foreach (var row in _rows.Where(r => !r.IsAccepted)) if (!await SaveAsync(row)) return false;
        return true;
    }
    private async Task CloseAsync()
    {
        _busy = true;
        try { if (await SaveAllAsync()) await OnClose.InvokeAsync(); }
        finally { _busy = false; }
    }
    private async Task BackAsync()
    {
        _busy = true;
        try { if (!await SaveAllAsync()) return; _batch = null; _rows.Clear(); _generationFailed = false; _error = ""; await LoadBatchesAsync(); }
        finally { _busy = false; }
    }
    private async Task LoadImageAsync(InputFileChangeEventArgs args)
    {
        if (Capabilities is null || _busy) return;
        try
        {
            var file = args.File;
            if (file.ContentType is not ("image/png" or "image/jpeg" or "image/webp")) throw new InvalidOperationException("Choose a PNG, JPEG or WebP screenshot.");
            await using var stream = file.OpenReadStream(Capabilities.MaxImageBytes, _lifetime.Token);
            using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, _lifetime.Token);
            var bytes = buffer.ToArray();
            var preview = $"data:{file.ContentType};base64,{Convert.ToBase64String(bytes)}";
            if (!await JS.InvokeAsync<bool>("playdex.drafts.validateImage", preview, Capabilities.MaxImagePixels)) throw new InvalidOperationException("The image is invalid or exceeds the pixel limit.");
            _image = bytes; _mediaType = file.ContentType; _fileName = file.Name; _preview = preview; _error = "";
        }
        catch (Exception exception) { _error = exception.Message; }
    }
    [JSInvokable] public Task PasteImage(string dataUrl, string mediaType)
    {
        if (_busy || Capabilities is null) return Task.CompletedTask;
        try
        {
            var bytes = Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]);
            if (bytes.LongLength > Capabilities.MaxImageBytes || mediaType is not ("image/png" or "image/jpeg" or "image/webp")) throw new InvalidOperationException("Unsupported or oversized screenshot.");
            _image = bytes; _mediaType = mediaType; _fileName = "pasted-screenshot"; _preview = dataUrl; _error = "";
        }
        catch (Exception exception) { _error = exception.Message; }
        return InvokeAsync(StateHasChanged);
    }
    [JSInvokable] public Task PasteError(string error) { _error = error; return InvokeAsync(StateHasChanged); }
    private void RemoveImage() { _image = null; _preview = ""; }
    private async Task GenerateAsync()
    {
        if (_busy || Capabilities?.Available != true) return;
        _busy = true; _error = ""; _generationFailed = false;
        try
        {
            if (_text.Length > Capabilities.MaxTextCharacters) throw new InvalidOperationException("Text exceeds the character limit.");
            var timezone = await JS.InvokeAsync<string>("playdex.drafts.timezone");
            _requestId = Guid.NewGuid();
            _generationSource = new(_text, _image, _mediaType, _fileName, timezone);
            await RunGenerationAsync();
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task RetryGenerationAsync()
    {
        if (_busy || !_generationFailed || _generationSource is null || Capabilities?.Available != true) return;
        await RunGenerationAsync();
    }

    private async Task RegenerateAsync()
    {
        if (_busy || Capabilities?.Available != true) return;
        _busy = true; _error = ""; _generationFailed = false;
        try
        {
            if (!await SaveAllAsync()) return;
            if (_generationSource is null || _generatedBatchId != _batch?.Id)
            {
                _batch = null;
                _rows.Clear();
                _text = "";
                RemoveImage();
                _outcome = "Add the original description or screenshot to generate again. Saved batches cannot reload the original input.";
                await LoadBatchesAsync();
                return;
            }
            _requestId = Guid.NewGuid();
            await RunGenerationAsync();
        }
        finally { _busy = false; }
    }

    private async Task RunGenerationAsync()
    {
        var source = _generationSource!;
        _busy = true; _error = ""; _generationFailed = false;
        try
        {
            HttpContent BuildContent()
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(_requestId.ToString()), "requestId");
                content.Add(new StringContent(source.Timezone), "timezone");
                if (!string.IsNullOrWhiteSpace(source.Text)) content.Add(new StringContent(source.Text), "text");
                if (source.Image is not null)
                {
                    var file = new ByteArrayContent(source.Image); file.Headers.ContentType = new MediaTypeHeaderValue(source.MediaType);
                    content.Add(file, "file", source.FileName);
                }
                return content;
            }
            Show(await CreateBatchAsync(BuildContent));
            _generatedBatchId = _batch!.Id;
            _text = ""; RemoveImage();
        }
        catch (Exception exception) { _error = exception.Message; _generationFailed = true; }
        finally { _busy = false; }
    }

    private async Task<DraftBatchDto> CreateBatchAsync(Func<HttpContent> buildContent)
    {
        try
        {
            return await Api.SendAsync<DraftBatchDto>("/manual/draft-batches", HttpMethod.Post, buildContent, cancellationToken: _lifetime.Token);
        }
        catch (HttpRequestException) when (!_lifetime.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), _lifetime.Token);
            return await Api.SendAsync<DraftBatchDto>("/manual/draft-batches", HttpMethod.Post, buildContent, cancellationToken: _lifetime.Token);
        }
    }

    private string RowPath(DraftRowEdit row) => $"/manual/draft-batches/{_batch!.Id}/rows/{row.Id}";
    private async Task<bool> SaveAsync(DraftRowEdit row)
    {
        if (row.IsAccepted || !row.IsDirty) return true;
        try { row.Apply(await Api.SendAsync<DraftRowDto>(RowPath(row), HttpMethod.Patch, row.Update, cancellationToken: _lifetime.Token)); return true; }
        catch (Exception exception) { row.Error = $"{exception.Message} Your edits are still here. Retry saving, or reopen this batch to review newer server changes."; return false; }
    }
    private async Task SaveActionAsync(DraftRowEdit row) { _busy = true; try { await SaveAsync(row); } finally { _busy = false; } }
    private async Task SearchAsync(DraftRowEdit row)
    {
        _busy = true;
        try
        {
            if (!await SaveAsync(row)) return;
            var result = await Api.SendAsync<ManualMappingSearchResponse>($"/manual/search?query={Uri.EscapeDataString(row.Title)}", cancellationToken: _lifetime.Token);
            row.Results = result.Results;
            row.Error = !result.IgdbAvailable ? "IGDB is unavailable. You can still choose a saved game." : result.Results.Count == 0 ? "No matches. Correct the title and search again, or delete this draft." : "";
        }
        catch (Exception exception) { row.Error = exception.Message; }
        finally { _busy = false; }
    }
    private async Task ChooseAsync(DraftRowEdit row, ManualMappingSearchResult selection)
    {
        _busy = true;
        try { row.Selection = selection; if (await SaveAsync(row)) row.Results = []; }
        finally { _busy = false; }
    }
    private async Task DeleteAsync(DraftRowEdit row)
    {
        _busy = true;
        try
        {
            await Api.SendAsync<object?>(RowPath(row) + $"?revision={row.Revision}", HttpMethod.Delete, cancellationToken: _lifetime.Token);
            _rows.Remove(row);
            if (!_rows.Any(item => !item.IsAccepted)) await CompleteBatchAsync("Draft batch completed; it was removed from saved review.");
        }
        catch (Exception exception) { row.Error = exception.Message; }
        finally { _busy = false; }
    }
    private async Task AcceptAsync(IReadOnlyList<DraftRowEdit> visible)
    {
        if (_busy) return;
        _busy = true; _error = "";
        try
        {
            var saved = new List<DraftRowEdit>();
            foreach (var row in visible.Where(r => r.CanAccept)) if (await SaveAsync(row) && row.CanAccept) saved.Add(row);
            if (saved.Count == 0) return;
            var response = await Api.SendAsync<DraftAcceptResponse>($"/manual/draft-batches/{_batch!.Id}/accept", HttpMethod.Post, new DraftAcceptRequest(saved.Select(r => r.Acceptance).ToArray()), cancellationToken: _lifetime.Token);
            foreach (var result in response.Results)
            {
                var row = _rows.FirstOrDefault(r => r.Id == result.Id); if (row is null) continue;
                if (result.SessionId is {} sessionId) row.MarkAccepted(sessionId); else row.Error = result.Error ?? "Acceptance failed. Retry this row.";
            }
            if (response.Results.Any(r => r.SessionId is not null)) await OnAccepted.InvokeAsync();
            if (!_rows.Any(row => !row.IsAccepted)) await CompleteBatchAsync("All ready drafts were accepted; the batch was removed from saved review.");
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private async Task CompleteBatchAsync(string outcome)
    {
        _batch = null;
        _rows.Clear();
        _outcome = outcome;
        await LoadBatchesAsync();
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _generationSource = null;
        _image = null;
        try { await JS.InvokeVoidAsync("playdex.drafts.detachPaste", _pasteId); } catch (JSDisconnectedException) { }
        _reference?.Dispose(); _lifetime.Dispose(); GC.SuppressFinalize(this);
    }
}
