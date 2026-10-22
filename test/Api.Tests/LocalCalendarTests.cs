using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Accounts;
using Api.Modules.Recap;
using Xunit;

namespace Api.Tests;

public sealed class LocalCalendarTests
{
    private static readonly TimeZoneInfo LosAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    /// <summary>UTC-3, with clocks jumping 00:00 -> 01:00 on Mar 10 and falling back 01:00 -> 00:00 on Nov 10.</summary>
    private static readonly TimeZoneInfo MidnightChanges = TimeZoneInfo.CreateCustomTimeZone(
        "Test/MidnightChanges",
        TimeSpan.FromHours(-3),
        "Midnight changes",
        "Standard",
        "Daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 10),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 10)),
        ]);

    [Fact]
    public void DaysStartAtLocalMidnight()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero), LocalCalendar.StartOf(new DateOnly(2026, 10, 1), LosAngeles));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero), LocalCalendar.StartOf(new DateOnly(2026, 10, 1), Tokyo));
        Assert.Equal(TimeSpan.Zero, LocalCalendar.StartOf(new DateOnly(2026, 10, 1), Tokyo).Offset);
        Assert.Equal(new DateOnly(2026, 9, 30), LocalCalendar.DateOf(new DateTimeOffset(2026, 10, 1, 6, 59, 0, TimeSpan.Zero), LosAngeles));
        Assert.Equal(new DateOnly(2026, 10, 1), LocalCalendar.DateOf(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero), LosAngeles));
    }

    [Fact]
    public void SkippedAndRepeatedMidnightsStartAtTheFirstLocalTimeThatExists()
    {
        // Mar 10 00:00 does not exist: the day starts at 01:00 daylight time, the same instant as 00:00 standard.
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 3, 0, 0, TimeSpan.Zero), LocalCalendar.StartOf(new DateOnly(2026, 3, 10), MidnightChanges));
        // Nov 10 00:00 happens twice: the day starts at the first one, so it lasts 25 hours.
        var repeated = LocalCalendar.StartOf(new DateOnly(2026, 11, 10), MidnightChanges);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero), repeated);
        Assert.Equal(TimeSpan.FromHours(25), LocalCalendar.StartOf(new DateOnly(2026, 11, 11), MidnightChanges) - repeated);
        Assert.Equal(new DateOnly(2026, 11, 9), LocalCalendar.DateOf(repeated.AddTicks(-1), MidnightChanges));
    }

    [Fact]
    public void ResolvesTheClientsZoneOrFallsBackToUtc()
    {
        Assert.Equal(Warsaw.Id, LocalCalendar.ResolveTimeZone(" Europe/Warsaw ").Id);
        Assert.Equal(TimeZoneInfo.Utc, LocalCalendar.ResolveTimeZone("Not/AZone"));
        Assert.Equal(TimeZoneInfo.Utc, LocalCalendar.ResolveTimeZone(null));
    }

    [Fact]
    public void DateOnlyBoundsAreTheClientsDays()
    {
        var range = AccountDateRange.ParseRequired("2026-10-01", "2026-10-31", LosAngeles);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero), range.From);
        // Nov 1 starts in daylight time; the change to standard time happens at 02:00.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), range.ToExclusive);

        var utc = AccountDateRange.ParseOptional("2026-10-01T10:00:00Z", null, LosAngeles);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), utc.From);
        Assert.Null(utc.ToExclusive);
    }

    [Theory]
    [InlineData("2026-10-02", "2026-10-01")]
    [InlineData("2026-10-01", "9999-12-31")]
    [InlineData("0001-01-01", "0001-01-02")]
    [InlineData("2026-13-01", "2026-12-01")]
    public void InvalidRangesAreRejected(string from, string to) =>
        Assert.Throws<AppProblemException>(() => AccountDateRange.ParseRequired(from, to, Tokyo));

    [Fact]
    public void RecapEndsTodayAtTheLatest()
    {
        // 01:30 on Oct 23 in Warsaw (UTC+2): tomorrow starts at 22:00 UTC.
        var now = new DateTimeOffset(2026, 10, 22, 23, 30, 0, TimeSpan.Zero);
        var tomorrow = new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero);
        var yearEnd = LocalCalendar.StartOf(new DateOnly(2027, 1, 1), Warsaw);
        var october = LocalCalendar.StartOf(new DateOnly(2026, 10, 1), Warsaw);

        Assert.Equal(tomorrow, RecapRange.ResolveEndExclusive(now, yearEnd, Warsaw));
        Assert.Equal(tomorrow, RecapRange.ResolveEndExclusive(now, null, Warsaw));
        Assert.Equal(october, RecapRange.ResolveEndExclusive(now, october, Warsaw));
    }

    [Fact]
    public void RecapCountsWholeLocalDaysAndMonths()
    {
        var jan1 = LocalCalendar.StartOf(new DateOnly(2026, 1, 1), Warsaw);
        // Oct 25 has 25 hours in Warsaw.
        Assert.Equal(3, RecapRange.CountDays(
            LocalCalendar.StartOf(new DateOnly(2026, 10, 24), Warsaw),
            LocalCalendar.StartOf(new DateOnly(2026, 10, 27), Warsaw),
            Warsaw));
        Assert.Equal(296, RecapRange.CountDays(jan1, LocalCalendar.StartOf(new DateOnly(2026, 10, 24), Warsaw), Warsaw));
        Assert.Equal(10, RecapRange.CountMonths(jan1, LocalCalendar.StartOf(new DateOnly(2026, 10, 24), Warsaw), Warsaw));
        Assert.Equal(0, RecapRange.CountDays(LocalCalendar.StartOf(new DateOnly(2027, 1, 1), Warsaw), jan1, Warsaw));
        Assert.Equal(0, RecapRange.CountMonths(LocalCalendar.StartOf(new DateOnly(2027, 1, 1), Warsaw), jan1, Warsaw));
    }
}
