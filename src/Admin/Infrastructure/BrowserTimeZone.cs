using Microsoft.JSInterop;

namespace Admin.Infrastructure;

public sealed class BrowserTimeZone(IJSRuntime js)
{
    private TimeZoneInfo _timeZone = TimeZoneInfo.Local;
    private bool _loaded;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var ianaId = await js.InvokeAsync<string>("Playdex.browserTimeZone");
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch
        {
            _timeZone = TimeZoneInfo.Local;
        }
        finally
        {
            _loaded = true;
        }
    }

    public DateTimeOffset? ToLocal(DateTimeOffset? value) =>
        value is null ? null : TimeZoneInfo.ConvertTime(value.Value, _timeZone);

    public string Format(DateTimeOffset? value) =>
        ToLocal(value)?.ToString("yyyy-MM-dd HH:mm") ?? "-";
}
