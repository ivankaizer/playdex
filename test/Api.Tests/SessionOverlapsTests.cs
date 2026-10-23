using Api.Infrastructure.Common;
using Xunit;

namespace Api.Tests;

public sealed class SessionOverlapsTests
{
    private static readonly DateTimeOffset _day = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static PlatformSession Taken(double startHours, double? endHours) => new(
        1, "Hades", "PlayStation 5", _day.AddHours(startHours), endHours is { } end ? _day.AddHours(end) : null);

    private static DateTimeOffset? FirstFree(double hours, params PlatformSession[] taken) =>
        SessionOverlaps.FirstFreeStart(taken, _day, _day.AddDays(1), TimeSpan.FromHours(hours), _now);

    [Fact]
    public void AnEmptyDayStartsAtMidnight()
    {
        Assert.Equal(_day, FirstFree(2));
        Assert.Equal(_day, FirstFree(24));
    }

    [Fact]
    public void AnEntryFollowsTheSessionsItWouldOverlap()
    {
        Assert.Equal(_day.AddHours(1), FirstFree(2, Taken(0, 1)));
        // Taken out of order: 00:00-01:00 and 01:00-03:00 are back to back.
        Assert.Equal(_day.AddHours(3), FirstFree(2, Taken(1, 3), Taken(0, 1)));
    }

    [Fact]
    public void AnEntryTakesTheFirstGapItFits()
    {
        // 30 minutes fit before 00:30, two hours only between 04:00 and 20:00.
        Assert.Equal(_day, FirstFree(0.5, Taken(0.5, 4), Taken(20, 22)));
        Assert.Equal(_day.AddHours(4), FirstFree(2, Taken(0.5, 4), Taken(20, 22)));
        // A session from the day before that runs past midnight takes the start of the day.
        Assert.Equal(_day.AddHours(1), FirstFree(1, Taken(-2, 1)));
        // Back to back with the next session is not an overlap.
        Assert.Equal(_day, FirstFree(3, Taken(3, 5)));
    }

    [Fact]
    public void ARunningSessionIsTakenUntilNow()
    {
        // Today: running since 10:00, now 12:00, so 00:00-10:00 and 12:00-24:00 are free.
        var today = _now.AddHours(-12);
        var running = new PlatformSession(1, "Hades", "PC", _now.AddHours(-2), null);
        DateTimeOffset? Free(int hours) =>
            SessionOverlaps.FirstFreeStart([running], today, today.AddDays(1), TimeSpan.FromHours(hours), _now);
        Assert.Equal(today, Free(10));
        Assert.Equal(_now, Free(11));
        Assert.Null(Free(13));
    }

    [Fact]
    public void ADayWithoutAGapThatLongHasNoStart()
    {
        Assert.Null(FirstFree(2, Taken(0.5, 23)));
        Assert.Null(FirstFree(24, Taken(12, 12.5)));
        Assert.Null(FirstFree(1.5, Taken(0, 1), Taken(2, 23.5)));
        // A 23-hour day (spring forward) cannot hold 24 hours.
        Assert.Null(SessionOverlaps.FirstFreeStart([], _day, _day.AddHours(23), TimeSpan.FromHours(24), _now));
    }

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h")]
    [InlineData(150, "2 h 30 min")]
    [InlineData(1440, "24 h")]
    public void DurationsReadAsHoursAndMinutes(int minutes, string expected)
    {
        Assert.Equal(expected, SessionOverlaps.FormatDuration(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void TheConflictNamesTheSessionInTheClientsOffset()
    {
        var finished = new PlatformSession(
            1, "Hades", "PlayStation 5", new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 9, 19, 30, 0, TimeSpan.Zero));
        Assert.Equal(
            "This would overlap your Hades session on PlayStation 5 (from 2026-09-09 20:00 to 21:30). "
                + "Sessions on the same platform cannot overlap.",
            SessionOverlaps.ConflictMessage(finished, TimeSpan.FromHours(2)));

        var overnight = finished with { EndedAt = new DateTimeOffset(2026, 9, 9, 23, 0, 0, TimeSpan.Zero) };
        Assert.Contains("(from 2026-09-09 20:00 to 2026-09-10 01:00)", SessionOverlaps.ConflictMessage(overnight, TimeSpan.FromHours(2)));

        var running = finished with { EndedAt = null };
        Assert.Contains("(started 2026-09-09 18:00, still running)", SessionOverlaps.ConflictMessage(running, TimeSpan.Zero));
    }
}
