namespace Api.Infrastructure.Common;

/// <summary>
/// Calendar days in a client's time zone, so stats bucket play into the days the viewer sees rather than UTC days.
/// </summary>
public static class LocalCalendar
{
    /// <summary>
    /// The time zone a client asked for: an IANA or Windows id (<paramref name="timeZone"/>), else a fixed UTC offset
    /// in minutes (clamped to ±14 h), else UTC.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZone, int? utcOffsetMinutes = null)
    {
        if (!string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out var zone))
        {
            return zone;
        }
        if (utcOffsetMinutes is { } minutes)
        {
            var offset = TimeSpan.FromMinutes(Math.Clamp(minutes, -14 * 60, 14 * 60));
            var name = $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
            return TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name);
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>The calendar day <paramref name="instant"/> falls on in <paramref name="zone"/>.</summary>
    public static DateOnly DateOf(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>
    /// The instant <paramref name="day"/> begins in <paramref name="zone"/>. A midnight skipped by a daylight-saving
    /// change begins at the first local time that exists; a repeated midnight begins at its first occurrence.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The day starts outside the range of <see cref="DateTimeOffset"/>.</exception>
    public static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Offsets are whole minutes and gaps last a few hours at most, so this ends quickly.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>The instant the day after the one <paramref name="instant"/> falls on begins in <paramref name="zone"/>.</summary>
    public static DateTimeOffset StartOfNextDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        StartOf(DateOf(instant, zone).AddDays(1), zone);
}
