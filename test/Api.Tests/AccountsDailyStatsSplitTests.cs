using Api.Modules.Accounts;
using Xunit;

namespace Api.Tests;

public sealed class AccountsDailyStatsSplitTests
{
    private static readonly DateTimeOffset From = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SplitsSessionsAtMidnightAndClipsToTheRange()
    {
        var days = AccountsRepository.BuildDayStats(From, To,
        [
            // Starts before the range: only the hour after midnight counts.
            Session(new(2026, 9, 27, 23, 0, 0, TimeSpan.Zero), new(2026, 9, 28, 1, 0, 0, TimeSpan.Zero)),
            // Crosses midnight: two hours on each side.
            Session(new(2026, 9, 28, 22, 0, 0, TimeSpan.Zero), new(2026, 9, 29, 2, 0, 0, TimeSpan.Zero)),
        ], now: To);

        Assert.Equal(["2026-09-28", "2026-09-29", "2026-09-30", "2026-10-01"], days.Select(day => day.Date));
        Assert.Equal([3 * 3600d, 2 * 3600d, 0d, 0d], days.Select(day => day.TotalSeconds));
        Assert.Equal([2, 1, 0, 0], days.Select(day => day.SessionCount));
    }

    [Fact]
    public void SplitsEachDayByPlatform()
    {
        var days = AccountsRepository.BuildDayStats(From, To,
        [
            Session(new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero), new(2026, 9, 28, 11, 0, 0, TimeSpan.Zero)),
            Session(new(2026, 9, 28, 23, 0, 0, TimeSpan.Zero), new(2026, 9, 29, 1, 0, 0, TimeSpan.Zero), "psn", "PlayStation"),
        ], now: To);

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
        var days = AccountsRepository.BuildDayStats(From, To,
            [Session(new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero), endedAt: null)], now);

        Assert.Equal([0d, 4 * 3600d, 24 * 3600d, 6 * 3600d], days.Select(day => day.TotalSeconds));
        Assert.All(days, day => Assert.True(day.TotalSeconds <= 24 * 3600));
    }

    private static AccountSessionDto Session(
        DateTimeOffset startedAt, DateTimeOffset? endedAt, string platformKey = "steam", string platformName = "Steam") =>
        new(1, startedAt, endedAt, "g", platformKey, platformName, "Game", null, false, null, null);
}
