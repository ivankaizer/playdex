using Api.Modules.Accounts;
using Xunit;

namespace Api.Tests;

public sealed class AccountsDailyStatsSplitTests
{
    private static readonly DateTimeOffset _from = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _to = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SplitsSessionsAtMidnightAndClipsToTheRange()
    {
        var days = AccountsRepository.BuildDayStats(_from, _to,
        [
            // Starts before the range: only the hour after midnight counts.
            Session(new(2026, 9, 27, 23, 0, 0, TimeSpan.Zero), new(2026, 9, 28, 1, 0, 0, TimeSpan.Zero)),
            // Crosses midnight: two hours on each side.
            Session(new(2026, 9, 28, 22, 0, 0, TimeSpan.Zero), new(2026, 9, 29, 2, 0, 0, TimeSpan.Zero)),
        ], now: _to, TimeZoneInfo.Utc);

        Assert.Equal(["2026-09-28", "2026-09-29", "2026-09-30", "2026-10-01"], days.Select(day => day.Date));
        Assert.Equal([3 * 3600d, 2 * 3600d, 0d, 0d], days.Select(day => day.TotalSeconds));
        Assert.Equal([2, 1, 0, 0], days.Select(day => day.SessionCount));
    }

    [Fact]
    public void SplitsEachDayByPlatform()
    {
        var days = AccountsRepository.BuildDayStats(_from, _to,
        [
            Session(new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero), new(2026, 9, 28, 11, 0, 0, TimeSpan.Zero)),
            Session(new(2026, 9, 28, 23, 0, 0, TimeSpan.Zero), new(2026, 9, 29, 1, 0, 0, TimeSpan.Zero), "psn", "PlayStation"),
        ], now: _to, TimeZoneInfo.Utc);

        Assert.Equal(
            [new AccountDayPlatformDto("psn", "PlayStation", 3600), new AccountDayPlatformDto("steam", "Steam", 3600)],
            days[0].Platforms);
        Assert.Equal([new AccountDayPlatformDto("psn", "PlayStation", 3600)], days[1].Platforms);
        Assert.Empty(days[2].Platforms);
    }

    [Fact]
    public void CountsRunningSessionsUpToNowWithoutExceedingADay()
    {
        var now = new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);
        var days = AccountsRepository.BuildDayStats(_from, _to,
            [Session(new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero), endedAt: null)], now, TimeZoneInfo.Utc);

        Assert.Equal([0d, 4 * 3600d, 24 * 3600d, 6 * 3600d], days.Select(day => day.TotalSeconds));
        Assert.All(days, day => Assert.True(day.TotalSeconds <= 24 * 3600));
    }

    [Fact]
    public void BucketsByTheClientsCalendarDaysIncludingDaylightSavingChanges()
    {
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        // Oct 31 – Nov 2, 2026 in Los Angeles; Nov 1 has 25 hours (PDT -> PST).
        var from = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.FromHours(-7));
        var to = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.FromHours(-8));
        var now = to;
        var days = AccountsRepository.BuildDayStats(from, to,
        [
            // 23:30 PDT on Oct 31, though Nov 1 in UTC.
            Session(new(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero)),
            // Runs from before the range to now: the whole of each local day.
            Session(new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero), endedAt: null, "psn", "PlayStation"),
        ], now, losAngeles);

        Assert.Equal(["2026-10-31", "2026-11-01", "2026-11-02"], days.Select(day => day.Date));
        Assert.Equal([24 * 3600d + 1800, 25 * 3600d, 24 * 3600d], days.Select(day => day.TotalSeconds));
        Assert.Equal([2, 1, 1], days.Select(day => day.SessionCount));
    }

    private static AccountSessionDto Session(
        DateTimeOffset startedAt, DateTimeOffset? endedAt, string platformKey = "steam", string platformName = "Steam") =>
        new(1, startedAt, endedAt, "g", platformKey, platformName, "Game", null, false);
}
