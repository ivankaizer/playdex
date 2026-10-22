using Api.Infrastructure.Common;
using Api.Modules.Recap;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class RecapRangeTests
{
    [Fact]
    public void ExplicitEndIsCutToTheStartOfItsUtcDay()
    {
        var end = RecapRange.ResolveEndExclusive(
            new DateTimeOffset(2026, 10, 22, 15, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 5, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void WithoutAnEndTheRangeRunsThroughToday()
    {
        var end = RecapRange.ResolveEndExclusive(new DateTimeOffset(2026, 10, 22, 23, 59, 0, TimeSpan.Zero), null);

        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), end);
    }
}

[Collection(PostgresCollectionDefinition.Name)]
public sealed class RecapServiceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset RangeStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeEndExclusive = new(2026, 3, 11, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecapForARangeAddsUpSessionsDaysStreaksAndGames()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var scenario = await SeedAsync(db);
        var service = new RecapService(new RecapRepository(db));

        var recap = await service.GetRecap(
            scenario.User.Id, "2026-03-01", "2026-03-10", RangeStart, RangeEndExclusive, CancellationToken.None);

        // Six counted sessions: the 5-minute one falls under the user's 10-minute threshold, two are outside.
        Assert.Equal(new RecapRangeDto("2026-03-01", "2026-03-10"), recap.Range);
        Assert.Equal(6, recap.TotalSessions);
        Assert.Equal(19_800, recap.TotalSeconds);
        Assert.Equal(3_300, recap.AverageSessionSeconds);
        Assert.Equal(3_150, recap.MedianSessionSeconds);
        Assert.Equal(7_200, recap.LongestSessionSeconds);
        Assert.Equal(900, recap.ShortestSessionSeconds);

        // Ten days in range, five with play; 2-4 March is the longest run.
        Assert.Equal(5, recap.DaysPlayed);
        Assert.Equal(5, recap.DaysNotPlayed);
        Assert.Equal(3, recap.LongestStreakDays);
        Assert.Equal(0.6, recap.AverageSessionsPerDay, 6);
        Assert.Equal(4.2, recap.AverageSessionsPerWeek, 6);
        Assert.Equal(6, recap.AverageSessionsPerMonth, 6);
        Assert.Equal(new MostActiveDayDto("2026-03-02", 2), recap.MostActiveDay);
        Assert.Equal(new MostActiveMonthDto("2026-03", 6), recap.MostActiveMonth);

        // Monday first: two on Monday 2nd, one each on Tue, Wed, Sat and Sun.
        Assert.Equal([2, 1, 1, 0, 0, 1, 1], recap.SessionsByWeekday.Select(day => day.SessionCount));
        Assert.Equal(24, recap.SessionsByHour.Count);
        Assert.Equal(new HourStatDto(9, 1), recap.PeakHour);
        Assert.Equal(3_600, recap.WeekendSeconds);
        Assert.Equal(16_200, recap.WeekdaySeconds);

        // Connected games A and B count as one game, shown as A (the one with more play time).
        Assert.Equal(3, recap.UniqueGames);
        Assert.Equal(
            [(scenario.A.Title, 12_600d, 3), (scenario.C.Title, 6_300d, 2), (scenario.D.Title, 900d, 1)],
            recap.TopGamesByTime.Select(game => (game.Title, game.TotalSeconds, game.SessionCount)));
        Assert.Equal(GameId.Encode(scenario.A.Id), recap.TopGameByTime?.GameId);
        Assert.Equal(scenario.A.Title, recap.TopGameBySessions?.Title);
        Assert.Equal(scenario.A.Title, recap.TopGameByAverageSession?.Title);
        Assert.Equal(4_200, recap.TopGameByAverageSession!.AverageSessionSeconds);
        Assert.Equal(recap.TopGamesByTime, recap.GamesTable);
        Assert.Equal(1, recap.AbandonedGames);
        Assert.True(recap.GamesTable.Single(game => game.Title == scenario.D.Title).IsAbandoned);
        Assert.Equal(100, recap.TopThreeGamesSharePercent, 6);
        Assert.Equal(new LongestSessionGameDto(GameId.Encode(scenario.A.Id), scenario.A.Title, 7_200), recap.GameWithLongestSession);

        // A's three sessions (Mon 10:00, Mon 20:00, Tue 09:00) fall within 24 hours.
        Assert.Equal(new BingedGameDto(GameId.Encode(scenario.A.Id), scenario.A.Title, 3), recap.MostBingedGame);

        // A was first played in February, so only C and D are new in this range; nothing spans two months here.
        Assert.Equal(2, recap.NewGamesTried);
        Assert.Equal(0, recap.ReturnRateGames);

        Assert.Equal(
            [(scenario.Pc.Key, 4, 17_100d), (scenario.Console.Key, 2, 2_700d)],
            recap.PlatformTotals.Select(platform => (platform.PlatformKey, platform.SessionCount, platform.TotalSeconds)));
        Assert.Equal(new PlatformSummaryDto(scenario.Pc.Key, scenario.Pc.Name, 17_100, 4), recap.MostUsedPlatform);
    }

    [Fact]
    public async Task AllTimeRecapSeesEverySessionAndGamesPlayedInSeveralMonths()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var scenario = await SeedAsync(db);
        var service = new RecapService(new RecapRepository(db));

        var recap = await service.GetRecap(scenario.User.Id, null, null, null, null, CancellationToken.None);

        Assert.Equal(new RecapRangeDto(null, null), recap.Range);
        Assert.Equal(8, recap.TotalSessions);
        Assert.Equal(1, recap.ReturnRateGames); // A: February and March
        Assert.Equal(3, recap.NewGamesTried);
        Assert.Equal([2026], (await service.GetYears(scenario.User.Id, CancellationToken.None)).Years);
    }

    [Fact]
    public async Task EmptyRangeIsAllZeroes()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var scenario = await SeedAsync(db);
        var service = new RecapService(new RecapRepository(db));

        var recap = await service.GetRecap(
            scenario.User.Id, "2025-06-01", "2025-06-30",
            new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(0, recap.TotalSessions);
        Assert.Equal(0, recap.TotalSeconds);
        Assert.Equal(0, recap.DaysPlayed);
        Assert.Equal(30, recap.DaysNotPlayed);
        Assert.Null(recap.MostActiveDay);
        Assert.Null(recap.PeakHour);
        Assert.Null(recap.TopGameByTime);
        Assert.Null(recap.MostUsedPlatform);
        Assert.Equal(0, recap.TopThreeGamesSharePercent);
        Assert.Empty(recap.GamesTable);
    }

    private sealed record Scenario(User User, Platform Pc, Platform Console, Game A, Game B, Game C, Game D);

    private static async Task<Scenario> SeedAsync(PlaydexDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"recap-{suffix}@example.test" };
        var pc = new Platform { Key = $"recap-pc-{suffix}", Name = "Recap PC" };
        var console = new Platform { Key = $"recap-console-{suffix}", Name = "Recap Console" };
        var a = new Game { Title = $"Recap A {suffix}" };
        var b = new Game { Title = $"Recap B {suffix}" };
        var c = new Game { Title = $"Recap C {suffix}" };
        var d = new Game { Title = $"Recap D {suffix}" };
        db.AddRange(user, pc, console, a, b, c, d);
        await db.SaveChangesAsync();

        db.UserSettings.Add(new UserSettings { UserId = user.Id, IgnoreShortSessionsMinutes = 10 });
        db.GameConnections.Add(new GameConnection { UserId = user.Id, GameId = a.Id, ConnectedGameId = b.Id });
        db.PlaySessions.AddRange(
            Session(user, a, pc, At(2, 20, 10), 3_600),   // before the range: A's first session
            Session(user, a, pc, At(3, 2, 10), 7_200),    // Monday
            Session(user, a, pc, At(3, 2, 20), 3_600),
            Session(user, b, console, At(3, 3, 9), 1_800),
            Session(user, c, pc, At(3, 4, 18), 3_600),
            Session(user, d, console, At(3, 7, 12), 900),  // Saturday
            Session(user, c, pc, At(3, 8, 22), 2_700),     // Sunday
            Session(user, d, console, At(3, 9, 12), 300),  // under the 10-minute threshold: not counted
            Session(user, c, pc, At(3, 15, 12), 600));     // after the range
        await db.SaveChangesAsync();
        return new Scenario(user, pc, console, a, b, c, d);
    }

    private static DateTimeOffset At(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    private static PlaySession Session(User user, Game game, Platform platform, DateTimeOffset start, int seconds) =>
        new()
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = platform.Id,
            CaptureMethod = CaptureMethod.Manual,
            StartedAt = start,
            EndedAt = start.AddSeconds(seconds),
        };

    private PlaydexDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            }).Options);
}
