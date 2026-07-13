using Playdex.Web.Utilities;
using Xunit;

namespace Playdex.Web.Tests.Utilities;

public sealed class SessionDatesTests
{
    [Fact]
    public void GetDurationSeconds_UsesTheEndedTimeWhenPresent()
    {
        var started = DateTimeOffset.Parse("2026-07-13T12:00:00.250Z");
        var ended = DateTimeOffset.Parse("2026-07-13T13:00:00.249Z");
        var laterNow = DateTimeOffset.Parse("2026-07-14T12:00:00Z");

        Assert.Equal(3_599, SessionDates.GetDurationSeconds(started, ended, laterNow));
    }

    [Fact]
    public void GetDurationSeconds_UsesTheSuppliedNowForALiveSession()
    {
        var started = DateTimeOffset.Parse("2026-07-13T12:00:00Z");
        var now = DateTimeOffset.Parse("2026-07-13T13:00:00Z");

        Assert.Equal(3_600, SessionDates.GetDurationSeconds(started, null, now));
    }

    [Fact]
    public void GetDurationSeconds_ClampsFutureSessionsToZero()
    {
        var started = DateTimeOffset.Parse("2026-07-13T13:00:00Z");
        var now = DateTimeOffset.Parse("2026-07-13T12:59:59Z");

        Assert.Equal(0, SessionDates.GetDurationSeconds(started, null, now));
    }

    [Theory]
    [InlineData("2026-07-13T12:00:00Z")]
    [InlineData("2026-07-19T23:59:59Z")]
    public void CurrentUtcWeek_RunsFromMondayThroughSunday(string nowValue)
    {
        var range = SessionDates.CurrentUtcWeek(DateTimeOffset.Parse(nowValue));

        Assert.Equal(new DateOnly(2026, 7, 13), range.From);
        Assert.Equal(new DateOnly(2026, 7, 19), range.To);
    }

    [Fact]
    public void CurrentUtcWeek_UsesTheUtcDateAcrossAnOffsetBoundary()
    {
        var mondayLocalButSundayUtc = DateTimeOffset.Parse("2026-07-20T01:00:00+02:00");

        var range = SessionDates.CurrentUtcWeek(mondayLocalButSundayUtc);

        Assert.Equal(new DateOnly(2026, 7, 13), range.From);
        Assert.Equal(new DateOnly(2026, 7, 19), range.To);
    }
}
