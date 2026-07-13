using Microsoft.JSInterop;

namespace Playdex.Web.Browser;

public sealed class BrowserInterop(IJSRuntime js)
{
    public ValueTask ApplyThemeAsync(string theme) =>
        js.InvokeVoidAsync("playdex.applyTheme", theme);

    public ValueTask<bool> PrefersDarkAsync() =>
        js.InvokeAsync<bool>("playdex.prefersDark");

    public ValueTask<bool> ConfirmAsync(string message) =>
        js.InvokeAsync<bool>("playdex.confirm", message);

    public ValueTask FocusAsync(string id) =>
        js.InvokeVoidAsync("playdex.focus", id);

    public ValueTask ScrollToTopAsync() =>
        js.InvokeVoidAsync("playdex.scrollToTop");
}
