using Api.Modules.Manual.Drafts;
using Xunit;

namespace Api.Tests;

public sealed class DraftRulesTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, 60)]
    [InlineData("2026-02-30", 60)]
    [InlineData("2026-09-09", 0)]
    [InlineData("2026-09-09", -1)]
    [InlineData("9999-12-31", 2147483647)]
    public void IncompleteOrOverflowingDatesCannotBeAccepted(string? date, int minutes)
    {
        Assert.False(DraftRules.TryGetTimes(date, minutes, null, _now, out _, out _));
    }

    [Fact]
    public void HistoricalTimesUseUtcMidnightWithoutAKnownZone()
    {
        Assert.True(DraftRules.TryGetTimes("2026-09-09", 90, null, _now, out var start, out var end));
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 1, 30, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void HistoricalTimesStartAtMidnightInTheBatchZone()
    {
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        Assert.True(DraftRules.TryGetTimes("2026-09-09", 90, losAngeles, _now, out var start, out _));
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 7, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void SessionsLastAtMostADay()
    {
        Assert.True(DraftRules.TryGetTimes("2026-09-09", 24 * 60, null, _now, out _, out _));
        Assert.Equal(
            DraftRules.TooLongWarning,
            DraftRules.Problem("2026-09-09", 24 * 60 + 1, null, _now, out _, out _));
    }

    [Fact]
    public void FutureDatesWaitUntilTheyBegin()
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // 21:00 in Tokyo, 05:00 in Los Angeles on Sep 10.
        Assert.True(DraftRules.TryGetTimes("2026-09-10", 60, tokyo, _now, out _, out _));
        Assert.Equal(DraftRules.FutureDateWarning, DraftRules.Problem("2026-09-11", 60, tokyo, _now, out _, out _));
        Assert.Equal(DraftRules.FutureDateWarning, DraftRules.Problem("2026-09-11", 60, losAngeles, _now, out _, out _));
        // Without a known zone, today anywhere on Earth is allowed.
        Assert.True(DraftRules.TryGetTimes("2026-09-11", 60, null, _now, out _, out _));
        Assert.False(DraftRules.TryGetTimes("2026-09-12", 60, null, _now, out _, out _));
    }
}
