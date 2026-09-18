using System.Globalization;
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
        catch (InvalidOperationException)
        {
            // JavaScript is unavailable during prerendering; retry once interactive.
            return;
        }
        catch
        {
            _timeZone = TimeZoneInfo.Local;
        }
        _loaded = true;
    }

    public DateTimeOffset? ToLocal(DateTimeOffset? value) =>
        value is null ? null : TimeZoneInfo.ConvertTime(value.Value, _timeZone);

    public string Format(DateTimeOffset? value) =>
        ToLocal(value)?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";

    public (DateTimeOffset Start, DateTimeOffset End) DayRange(DateTimeOffset now)
    {
        var day = TimeZoneInfo.ConvertTime(now, _timeZone).Date;
        return (Boundary(day), Boundary(day.AddDays(1)));
    }

    private DateTimeOffset Boundary(DateTime day)
    {
        day = DateTime.SpecifyKind(day, DateTimeKind.Unspecified);
        // Some zones advance their clocks at midnight, or repeat midnight.
        while (_timeZone.IsInvalidTime(day)) day = day.AddMinutes(1);
        var offset = _timeZone.IsAmbiguousTime(day)
            ? _timeZone.GetAmbiguousTimeOffsets(day).Max() : _timeZone.GetUtcOffset(day);
        return new DateTimeOffset(day, offset).ToUniversalTime();
    }
}
