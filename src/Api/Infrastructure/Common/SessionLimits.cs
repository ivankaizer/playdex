namespace Api.Infrastructure.Common;

/// <summary>
/// Bounds every play session keeps, however it is recorded: by hand, from an AI draft, by an edit or by a capture
/// client. A longer session or one in the future is a typo or a broken client, and it would skew every stat.
/// </summary>
public static class SessionLimits
{
    public const int MaxDurationMinutes = 24 * 60;
    public const int MaxDurationSeconds = MaxDurationMinutes * 60;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(MaxDurationMinutes);

    /// <summary>How far a client-supplied time may run ahead of the server clock.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The latest date a session may be on: today in <paramref name="zone"/>, or without a known zone, today where
    /// clocks are furthest ahead (UTC+14), so no client's today is refused.
    /// </summary>
    public static DateOnly LatestDate(DateTimeOffset now, TimeZoneInfo? zone) =>
        zone is null ? DateOnly.FromDateTime(now.UtcDateTime.AddHours(14)) : LocalCalendar.DateOf(now, zone);
}
