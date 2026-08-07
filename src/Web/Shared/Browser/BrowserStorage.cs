using Microsoft.JSInterop;

namespace Web.Shared.Browser;

public sealed class BrowserStorage(IJSRuntime js) : IBrowserStorage
{
    public ValueTask<string?> GetAsync(string key) =>
        js.InvokeAsync<string?>("playdex.storage.get", key);

    public ValueTask SetAsync(string key, string value) =>
        js.InvokeVoidAsync("playdex.storage.set", key, value);

    public ValueTask RemoveAsync(string key) =>
        js.InvokeVoidAsync("playdex.storage.remove", key);
}
