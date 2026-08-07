namespace Playdex.Web.Shared.Common;

public readonly record struct DateRange(DateOnly From, DateOnly To);

public static class SessionDates
{
    public static long GetDurationSeconds(
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        DateTimeOffset now)
    {
        var elapsedMilliseconds =
            (endedAt ?? now).ToUnixTimeMilliseconds() - startedAt.ToUnixTimeMilliseconds();
        return Math.Max(0, elapsedMilliseconds / 1000);
    }

    public static DateRange CurrentUtcWeek(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var mondayOffset = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-mondayOffset);
        return new DateRange(monday, monday.AddDays(6));
    }
}
