using Api.Infrastructure.Common;
using Api.Modules.Launcher;
using Xunit;

namespace Api.Tests;

public sealed class LauncherSummaryBuilderTests
{
    // Saturday 2026-10-10, 18:00 in Prague (UTC+2 in summer time).
    private static readonly DateTimeOffset _now = new(2026, 10, 10, 16, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo _prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    private static readonly Dictionary<long, LauncherGameRow> _games = new()
    {
        [1] = new LauncherGameRow(1, "Control", "https://cdn/1.webp"),
        [2] = new LauncherGameRow(2, "Hades II", null),
        [3] = new LauncherGameRow(3, "Balatro", null),
    };

    [Fact]
    public void DaysAreLocalCalendarDaysAndASessionCountsTowardTheDayItStarted()
    {
        // 23:30 UTC on the 9th is 01:30 on the 10th in Prague: today, not yesterday.
        var sessions = new[]
        {
            Session(1, 1, new DateTimeOffset(2026, 10, 9, 23, 30, 0, TimeSpan.Zero), minutes: 60),
            Session(2, 2, new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero), minutes: 30),
        };

        var summary = LauncherSummaryBuilder.Build(sessions, _games, [], _now, _prague);

        Assert.Equal(7, summary.Days.Count);
        Assert.Equal("2026-10-04", summary.Days[0].Date);
        Assert.Equal("2026-10-10", summary.Days[^1].Date);
        Assert.Equal(new LauncherPeriodDto(3600, 1), summary.Today);
        Assert.Equal(new LauncherDayDto("2026-10-09", 1800, 1), summary.Days[^2]);
        Assert.Equal(new LauncherPeriodDto(5400, 2), summary.Week);
    }

    [Fact]
    public void ARunningSessionCountsUpToNow()
    {
        var running = new LauncherSessionRow(1, 1, "steam-deck", "Steam Deck", _now.AddMinutes(-25), null);

        var summary = LauncherSummaryBuilder.Build([running], _games, [], _now, _prague);

        Assert.Equal(25 * 60, summary.Today.TotalSeconds);
        Assert.Null(summary.RecentSessions.Single().EndedAt);
        Assert.Equal("steam-deck", summary.RecentSessions.Single().PlatformKey);
        Assert.Equal("Steam Deck", summary.RecentSessions.Single().PlatformName);
        Assert.Equal(25 * 60, summary.RecentSessions.Single().DurationSeconds);
    }

    [Fact]
    public void TopGamesAreTheWeeksMostPlayedAndOlderSessionsOnlyCountAllTime()
    {
        var sessions = new[]
        {
            Session(1, 1, _now.AddDays(-1), minutes: 40),
            Session(2, 2, _now.AddDays(-2), minutes: 90),
            Session(3, 1, _now.AddDays(-3), minutes: 30),
            Session(4, 3, _now.AddDays(-10), minutes: 500), // Before the 7 days.
        };

        var summary = LauncherSummaryBuilder.Build(sessions, _games, [], _now, _prague);

        Assert.Equal(["Hades II", "Control"], summary.TopGames.Select(game => game.Title));
        Assert.Equal(new LauncherTopGameDto(GameId.Encode(1), "Control", 70 * 60, 2), summary.TopGames[1]);
        Assert.Equal(new LauncherPeriodDto(160 * 60, 3), summary.Week);
        Assert.Equal(new LauncherPeriodDto(660 * 60, 4), summary.AllTime);
    }

    [Fact]
    public void PeriodsDetailTodayTheWeekAndTheCalendarMonthByGameAndPlatform()
    {
        // Now is Saturday 2026-10-10 in Prague.
        var deck = _now.AddHours(-3);
        var sessions = new[]
        {
            new LauncherSessionRow(1, 1, "steam-deck", "Steam Deck", deck, deck.AddMinutes(60)), // today
            Session(2, 2, _now.AddDays(-2), minutes: 30), // this week
            Session(3, 2, _now.AddDays(-8), minutes: 45), // 2 October: this month, not the last 7 days
            Session(4, 1, _now.AddDays(-20), minutes: 100), // September
        };

        var periods = LauncherSummaryBuilder.Build(sessions, _games, [], _now, _prague).Periods;

        Assert.Equal(60 * 60, periods.Today.TotalSeconds);
        Assert.Equal([new LauncherPlatformDto("steam-deck", "Steam Deck", 60 * 60, 1)], periods.Today.Platforms);
        Assert.Equal((90 * 60, 2), (periods.Week.TotalSeconds, periods.Week.SessionCount));
        Assert.Equal((135 * 60, 3), (periods.Month.TotalSeconds, periods.Month.SessionCount));
        Assert.Equal(["Hades II", "Control"], periods.Month.TopGames.Select(game => game.Title));
        Assert.Equal(
            [new LauncherPlatformDto("pc", "PC", 75 * 60, 2), new LauncherPlatformDto("steam-deck", "Steam Deck", 60 * 60, 1)],
            periods.Month.Platforms);
    }

    [Fact]
    public void MonthDaysRunFromTheFirstToToday()
    {
        var days = LauncherSummaryBuilder.Build([Session(1, 1, _now.AddHours(-2), minutes: 20)], _games, [], _now, _prague).MonthDays;

        Assert.Equal(10, days.Count);
        Assert.Equal("2026-10-01", days[0].Date);
        Assert.Equal(new LauncherDayDto("2026-10-10", 20 * 60, 1), days[^1]);
    }

    [Fact]
    public void GamesCarryTotalsAndIdentitiesMostRecentFirst()
    {
        var sessions = new[]
        {
            Session(1, 1, _now.AddDays(-20), minutes: 120),
            Session(2, 2, _now.AddDays(-1), minutes: 15),
            Session(3, 1, _now.AddDays(-5), minutes: 60),
        };
        LauncherIdentityRow[] identities = [new(1, "steam", "3669870"), new(2, "steam", "split-fiction")];

        var summary = LauncherSummaryBuilder.Build(sessions, _games, identities, _now, _prague);

        Assert.Equal([GameId.Encode(2), GameId.Encode(1)], summary.Games.Select(game => game.GameId));
        var control = summary.Games[1];
        Assert.Equal(180 * 60, control.TotalSeconds);
        Assert.Equal(2, control.SessionCount);
        Assert.Equal(_now.AddDays(-5), control.LastPlayedAt);
        Assert.Equal("https://cdn/1.webp", control.CapsuleUrl);
        Assert.Equal([new LauncherGameIdentityDto("steam", "3669870")], control.Identities);
    }

    [Fact]
    public void GamesPlayedInTheLastFourteenDaysCarryThoseDaysAndTheirNewestFiveSessions()
    {
        var sessions = new[]
        {
            Session(1, 1, _now.AddHours(-2), minutes: 30), // today
            Session(2, 1, _now.AddDays(-1), minutes: 20),
            Session(3, 1, _now.AddDays(-1).AddHours(-3), minutes: 10),
            Session(4, 1, _now.AddDays(-13), minutes: 40), // the first of the 14 days
            Session(5, 1, _now.AddDays(-14), minutes: 50), // before them
            Session(6, 1, _now.AddDays(-2), minutes: 5),
            Session(7, 1, _now.AddDays(-3), minutes: 5),
            Session(8, 2, _now.AddDays(-20), minutes: 60), // Hades II: not in the 14 days
        };

        var summary = LauncherSummaryBuilder.Build(sessions, _games, [], _now, _prague);

        var control = summary.Games.Single(game => game.GameId == GameId.Encode(1)).Recent!;
        Assert.Equal(14, control.DaySeconds.Count);
        Assert.Equal(40 * 60, control.DaySeconds[0]);
        Assert.Equal(30 * 60, control.DaySeconds[^1]);
        Assert.Equal(30 * 60, control.DaySeconds[^2]);
        Assert.Equal(6, control.SessionCount);
        Assert.Equal(["1", "2", "3", "6", "7"], control.Sessions.Select(session => session.SessionId));
        Assert.Null(summary.Games.Single(game => game.GameId == GameId.Encode(2)).Recent);
    }

    [Fact]
    public void RecentSessionsAreTheNewestTwenty()
    {
        var sessions = Enumerable.Range(1, 25).Select(i => Session(i, 1, _now.AddHours(-i), minutes: 10)).ToArray();

        var summary = LauncherSummaryBuilder.Build(sessions, _games, [], _now, _prague);

        Assert.Equal(20, summary.RecentSessions.Count);
        Assert.Equal("1", summary.RecentSessions[0].SessionId);
        Assert.Equal("20", summary.RecentSessions[^1].SessionId);
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, 3)] // Today and the two days before.
    [InlineData(new[] { 1, 2 }, 2)] // Nothing yet today: the streak up to yesterday still counts.
    [InlineData(new[] { 0, 2, 3 }, 1)] // A gap ends it.
    [InlineData(new[] { 2, 3 }, 0)]
    public void StreakCountsConsecutiveDaysEndingTodayOrYesterday(int[] daysAgo, int streak)
    {
        var sessions = daysAgo.Select((days, i) => Session(i + 1, 1, _now.AddDays(-days), minutes: 10)).ToArray();

        Assert.Equal(streak, LauncherSummaryBuilder.Build(sessions, _games, [], _now, _prague).CurrentStreakDays);
    }

    [Fact]
    public void NoSessionsIsAnEmptySummary()
    {
        var summary = LauncherSummaryBuilder.Build([], _games, [], _now, _prague);

        Assert.Equal(new LauncherPeriodDto(0, 0), summary.AllTime);
        Assert.All(summary.Days, day => Assert.Equal(0, day.TotalSeconds));
        Assert.Empty(summary.TopGames);
        Assert.Empty(summary.Games);
        Assert.Equal(0, summary.CurrentStreakDays);
    }

    [Fact]
    public void TimeZoneIsAnIanaOrWindowsIdThenAnOffsetThenUtc()
    {
        Assert.Equal(_prague.BaseUtcOffset, LauncherSummaryBuilder.ResolveTimeZone("Europe/Prague", null).BaseUtcOffset);
        Assert.Equal(
            _prague.GetUtcOffset(_now),
            LauncherSummaryBuilder.ResolveTimeZone("Central Europe Standard Time", null).GetUtcOffset(_now));

        var fromOffset = LauncherSummaryBuilder.ResolveTimeZone("Nowhere/Unknown", 330);
        Assert.Equal(TimeSpan.FromMinutes(330), fromOffset.BaseUtcOffset);
        Assert.Equal("UTC+05:30", fromOffset.Id);
        Assert.Equal(TimeSpan.FromHours(-14), LauncherSummaryBuilder.ResolveTimeZone(null, -100_000).BaseUtcOffset);

        Assert.Equal(TimeZoneInfo.Utc, LauncherSummaryBuilder.ResolveTimeZone("Nowhere/Unknown", null));
        Assert.Equal(TimeZoneInfo.Utc, LauncherSummaryBuilder.ResolveTimeZone(null, null));
    }

    private static LauncherSessionRow Session(long id, long gameId, DateTimeOffset startedAt, int minutes) =>
        new(id, gameId, "pc", "PC", startedAt, startedAt.AddMinutes(minutes));
}
