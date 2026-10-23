using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Accounts;
using Api.Modules.Recap;
using Xunit;

namespace Api.Tests;

public sealed class LocalCalendarTests
{
    private static readonly TimeZoneInfo _losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly TimeZoneInfo _warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly TimeZoneInfo _tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    /// <summary>UTC-3, with clocks jumping 00:00 -> 01:00 on Mar 10 and falling back 01:00 -> 00:00 on Nov 10.</summary>
    private static readonly TimeZoneInfo _midnightChanges = TimeZoneInfo.CreateCustomTimeZone(
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
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero), LocalCalendar.StartOf(new DateOnly(2026, 10, 1), _losAngeles));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero), LocalCalendar.StartOf(new DateOnly(2026, 10, 1), _tokyo));
        Assert.Equal(TimeSpan.Zero, LocalCalendar.StartOf(new DateOnly(2026, 10, 1), _tokyo).Offset);
        Assert.Equal(new DateOnly(2026, 9, 30), LocalCalendar.DateOf(new DateTimeOffset(2026, 10, 1, 6, 59, 0, TimeSpan.Zero), _losAngeles));
        Assert.Equal(new DateOnly(2026, 10, 1), LocalCalendar.DateOf(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero), _losAngeles));
    }

    [Fact]
    public void SkippedAndRepeatedMidnightsStartAtTheFirstLocalTimeThatExists()
    {
        // Mar 10 00:00 does not exist: the day starts at 01:00 daylight time, the same instant as 00:00 standard.
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 3, 0, 0, TimeSpan.Zero), LocalCalendar.StartOf(new DateOnly(2026, 3, 10), _midnightChanges));
        // Nov 10 00:00 happens twice: the day starts at the first one, so it lasts 25 hours.
        var repeated = LocalCalendar.StartOf(new DateOnly(2026, 11, 10), _midnightChanges);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero), repeated);
        Assert.Equal(TimeSpan.FromHours(25), LocalCalendar.StartOf(new DateOnly(2026, 11, 11), _midnightChanges) - repeated);
        Assert.Equal(new DateOnly(2026, 11, 9), LocalCalendar.DateOf(repeated.AddTicks(-1), _midnightChanges));
    }

    [Fact]
    public void ResolvesTheClientsZoneOrFallsBackToUtc()
    {
        Assert.Equal(_warsaw.Id, LocalCalendar.ResolveTimeZone(" Europe/Warsaw ").Id);
        Assert.Equal(TimeZoneInfo.Utc, LocalCalendar.ResolveTimeZone("Not/AZone"));
        Assert.Equal(TimeZoneInfo.Utc, LocalCalendar.ResolveTimeZone(null));
    }

    [Fact]
    public void DateOnlyBoundsAreTheClientsDays()
    {
        var range = AccountDateRange.ParseRequired("2026-10-01", "2026-10-31", _losAngeles);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 7, 0, 0, TimeSpan.Zero), range.From);
        // Nov 1 starts in daylight time; the change to standard time happens at 02:00.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero), range.ToExclusive);

        var utc = AccountDateRange.ParseOptional("2026-10-01T10:00:00Z", null, _losAngeles);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), utc.From);
        Assert.Null(utc.ToExclusive);
    }

    [Theory]
    [InlineData("2026-10-02", "2026-10-01")]
    [InlineData("2026-10-01", "9999-12-31")]
    [InlineData("0001-01-01", "0001-01-02")]
    [InlineData("2026-13-01", "2026-12-01")]
    public void InvalidRangesAreRejected(string from, string to) =>
        Assert.Throws<AppProblemException>(() => AccountDateRange.ParseRequired(from, to, _tokyo));

    [Fact]
    public void RecapEndsTodayAtTheLatest()
    {
        // 01:30 on Oct 23 in Warsaw (UTC+2): tomorrow starts at 22:00 UTC.
        var now = new DateTimeOffset(2026, 10, 22, 23, 30, 0, TimeSpan.Zero);
        var tomorrow = new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero);
        var yearEnd = LocalCalendar.StartOf(new DateOnly(2027, 1, 1), _warsaw);
        var october = LocalCalendar.StartOf(new DateOnly(2026, 10, 1), _warsaw);

        Assert.Equal(tomorrow, RecapRange.ResolveEndExclusive(now, yearEnd, _warsaw));
        Assert.Equal(tomorrow, RecapRange.ResolveEndExclusive(now, null, _warsaw));
        Assert.Equal(october, RecapRange.ResolveEndExclusive(now, october, _warsaw));
    }

    [Fact]
    public void RecapCountsWholeLocalDaysAndMonths()
    {
        var jan1 = LocalCalendar.StartOf(new DateOnly(2026, 1, 1), _warsaw);
        // Oct 25 has 25 hours in Warsaw.
        Assert.Equal(3, RecapRange.CountDays(
            LocalCalendar.StartOf(new DateOnly(2026, 10, 24), _warsaw),
            LocalCalendar.StartOf(new DateOnly(2026, 10, 27), _warsaw),
            _warsaw));
        Assert.Equal(296, RecapRange.CountDays(jan1, LocalCalendar.StartOf(new DateOnly(2026, 10, 24), _warsaw), _warsaw));
        Assert.Equal(10, RecapRange.CountMonths(jan1, LocalCalendar.StartOf(new DateOnly(2026, 10, 24), _warsaw), _warsaw));
        Assert.Equal(0, RecapRange.CountDays(LocalCalendar.StartOf(new DateOnly(2027, 1, 1), _warsaw), jan1, _warsaw));
        Assert.Equal(0, RecapRange.CountMonths(LocalCalendar.StartOf(new DateOnly(2027, 1, 1), _warsaw), jan1, _warsaw));
    }
}
