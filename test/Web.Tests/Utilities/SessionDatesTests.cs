using System.Globalization;
using Web.Infrastructure.Common;
using Xunit;

namespace Web.Tests.Utilities;

public sealed class SessionDatesTests
{
    [Fact]
    public void GetDurationSecondsUsesTheEndedTimeWhenPresent()
    {
        var started = DateTimeOffset.Parse("2026-07-13T12:00:00.250Z", CultureInfo.InvariantCulture);
        var ended = DateTimeOffset.Parse("2026-07-13T13:00:00.249Z", CultureInfo.InvariantCulture);
        var laterNow = DateTimeOffset.Parse("2026-07-14T12:00:00Z", CultureInfo.InvariantCulture);

        Assert.Equal(3_599, SessionDates.GetDurationSeconds(started, ended, laterNow));
    }

    [Fact]
    public void GetDurationSecondsUsesTheSuppliedNowForALiveSession()
    {
        var started = DateTimeOffset.Parse("2026-07-13T12:00:00Z", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.Parse("2026-07-13T13:00:00Z", CultureInfo.InvariantCulture);

        Assert.Equal(3_600, SessionDates.GetDurationSeconds(started, null, now));
    }

    [Fact]
    public void GetDurationSecondsClampsFutureSessionsToZero()
    {
        var started = DateTimeOffset.Parse("2026-07-13T13:00:00Z", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.Parse("2026-07-13T12:59:59Z", CultureInfo.InvariantCulture);

        Assert.Equal(0, SessionDates.GetDurationSeconds(started, null, now));
    }

    [Theory]
    [InlineData("2026-07-13T12:00:00Z")]
    [InlineData("2026-07-19T23:59:59Z")]
    public void CurrentUtcWeekRunsFromMondayThroughSunday(string nowValue)
    {
        var range = SessionDates.CurrentUtcWeek(DateTimeOffset.Parse(nowValue, CultureInfo.InvariantCulture));

        Assert.Equal(new DateOnly(2026, 7, 13), range.From);
        Assert.Equal(new DateOnly(2026, 7, 19), range.To);
    }

    [Fact]
    public void CurrentUtcWeekUsesTheUtcDateAcrossAnOffsetBoundary()
    {
        var mondayLocalButSundayUtc = DateTimeOffset.Parse("2026-07-20T01:00:00+02:00", CultureInfo.InvariantCulture);

        var range = SessionDates.CurrentUtcWeek(mondayLocalButSundayUtc);

        Assert.Equal(new DateOnly(2026, 7, 13), range.From);
        Assert.Equal(new DateOnly(2026, 7, 19), range.To);
    }
}
