using Api.Infrastructure.Common;
using Api.Modules.Accounts;
using Api.Modules.Games;
using Api.Modules.Recap;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

/// <summary>Stats, Today and the recap bucket play by the client's calendar days (the <c>tz</c> parameter).</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ClientTimeZoneIntegrationTests(PostgresFixture postgres)
{
    private static readonly TimeZoneInfo _losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private static readonly TimeZoneInfo _tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    [Fact]
    public async Task RangeStatsUseTheClientsMonthsAndTheTitleFilter()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await Seed(db);
        var hades = await AddGame(db, $"Hades {seed.Suffix}");
        var control = await AddGame(db, $"Control {seed.Suffix}");
        // 20:00 on Oct 31 in Los Angeles, though already November in UTC.
        AddSession(db, seed, hades, new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero), 3600);
        AddSession(db, seed, control, new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.Zero), 1800);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var stats = await service.ComputeStatsForRange(
            seed.User.Id, null, "2026-10-01", "2026-11-30", null, null, 0, _losAngeles, CancellationToken.None);
        Assert.Equal(
            [(2026, 10, 3600d), (2026, 11, 1800d)],
            stats.Months.Select(month => (month.Year, month.Month, month.TotalSeconds)));

        var hadesOnly = await service.ComputeStatsForRange(
            seed.User.Id, null, "2026-10-01", "2026-11-30", null, "HADES", 0, _losAngeles, CancellationToken.None);
        Assert.Equal(1, hadesOnly.SessionCount);
        Assert.Equal(3600d, hadesOnly.TotalSeconds);
        Assert.Equal(hades.Title, hadesOnly.TopGameTitle);

        var hadesDays = await service.ComputeDailyStats(
            seed.User.Id, null, "2026-10-31", "2026-11-10", null, "hades", 0, _losAngeles, CancellationToken.None);
        Assert.Equal(3600d, hadesDays[0].TotalSeconds);
        Assert.Equal(3600d, hadesDays.Sum(day => day.TotalSeconds));

        // In UTC the Hades session falls in November, outside an October-only range.
        var octoberUtc = await service.ComputeStatsForRange(
            seed.User.Id, null, "2026-10-01", "2026-10-31", null, null, 0, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal(0, octoberUtc.SessionCount);
    }

    [Fact]
    public async Task TodayIncludesSessionsThatCrossedMidnightOrAreStillRunning()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await Seed(db);
        var game = await AddGame(db, $"Today {seed.Suffix}");
        var now = DateTimeOffset.UtcNow;
        var todayStart = LocalCalendar.StartOf(LocalCalendar.DateOf(now, _tokyo), _tokyo);
        var yesterday = AddSession(db, seed, game, todayStart.AddHours(-3), 3600);
        var running = AddSession(db, seed, game, todayStart.AddHours(-2), null);
        var crossed = AddSession(db, seed, game, todayStart.AddMinutes(-30), 3600);
        await db.SaveChangesAsync();

        var today = await CreateService(db).ListSessionsToday(seed.User.Id, null, 0, _tokyo, CancellationToken.None);

        Assert.Equal([crossed.Id, running.Id], today.Sessions.Select(session => session.SessionId));
        Assert.DoesNotContain(yesterday.Id, today.Sessions.Select(session => session.SessionId));
        Assert.Equal(2, today.Total);
    }

    [Fact]
    public async Task TodayListsTheNewestSessionsAndCountsThemAll()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await Seed(db);
        var game = await AddGame(db, $"Busy day {seed.Suffix}");
        var todayStart = LocalCalendar.StartOf(LocalCalendar.DateOf(DateTimeOffset.UtcNow, _tokyo), _tokyo);
        var sessions = Enumerable.Range(0, AccountsRepository.TodaySessionLimit + 2)
            .Select(index => AddSession(db, seed, game, todayStart.AddMinutes(index), 30))
            .ToList();
        await db.SaveChangesAsync();

        var today = await CreateService(db).ListSessionsToday(seed.User.Id, null, 0, _tokyo, CancellationToken.None);

        Assert.Equal(AccountsRepository.TodaySessionLimit + 2, today.Total);
        Assert.Equal(
            sessions.Select(session => session.Id).Reverse().Take(AccountsRepository.TodaySessionLimit),
            today.Sessions.Select(session => session.SessionId));
    }

    [Fact]
    public async Task GameStatsStartAtTheFirstYearPlayedInTheClientsCalendar()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await Seed(db);
        var game = await AddGame(db, $"Years {seed.Suffix}");
        // 20:00 UTC on Dec 31, 2022 is already 2023 in Tokyo.
        AddSession(db, seed, game, new DateTimeOffset(2022, 12, 31, 20, 0, 0, TimeSpan.Zero), 3600);
        AddSession(db, seed, game, new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero), 1800);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var currentYear = LocalCalendar.DateOf(DateTimeOffset.UtcNow, _tokyo).Year;

        var stats = await service.ComputeGameStats(seed.User.Id, GameId.Encode(game.Id), _tokyo, CancellationToken.None);

        Assert.Equal(Enumerable.Range(2023, currentYear - 2022), stats.Years.Select(year => year.Year));
        Assert.Equal(3600d, stats.Years[0].Seconds);
        Assert.Equal(3600d, stats.Months.Single(month => month.Year == 2023 && month.Month == 1).Seconds);
        Assert.Equal((currentYear - 2022) * 12, stats.Months.Count);
        Assert.Equal(5400d, stats.TotalSeconds);

        var utc = await service.ComputeGameStats(seed.User.Id, GameId.Encode(game.Id), TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal(2022, utc.Years[0].Year);
    }

    [Fact]
    public async Task RecapBucketsDaysHoursAndYearsInTheClientsZone()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await Seed(db);
        var game = await AddGame(db, $"Recap {seed.Suffix}");
        // 23:30 UTC on Mar 2 and 00:30 UTC on Mar 3 are both Mar 3 in Tokyo, at 8:30 and 9:30.
        AddSession(db, seed, game, new DateTimeOffset(2025, 3, 2, 23, 30, 0, TimeSpan.Zero), 1800);
        AddSession(db, seed, game, new DateTimeOffset(2025, 3, 3, 0, 30, 0, TimeSpan.Zero), 1800);
        AddSession(db, seed, game, new DateTimeOffset(2025, 3, 10, 3, 0, 0, TimeSpan.Zero), 1800);
        // Already 2023 in Tokyo.
        AddSession(db, seed, game, new DateTimeOffset(2022, 12, 31, 20, 0, 0, TimeSpan.Zero), 1800);
        await db.SaveChangesAsync();
        var repository = new RecapRepository(db);
        var service = new RecapService(repository);

        var recap = await service.GetRecap(
            seed.User.Id,
            "2025-03-01",
            "2025-03-31",
            LocalCalendar.StartOf(new DateOnly(2025, 3, 1), _tokyo),
            LocalCalendar.StartOf(new DateOnly(2025, 4, 1), _tokyo),
            _tokyo,
            CancellationToken.None);

        Assert.Equal(3, recap.TotalSessions);
        Assert.Equal(new MostActiveDayDto("2025-03-03", 2), recap.MostActiveDay);
        Assert.Equal(2, recap.DaysPlayed);
        Assert.Equal(29, recap.DaysNotPlayed);
        Assert.Equal(1, recap.SessionsByHour[8].SessionCount);
        Assert.Equal(1, recap.SessionsByHour[9].SessionCount);
        Assert.Equal(0, recap.SessionsByHour[23].SessionCount);
        Assert.Equal(3, recap.AverageSessionsPerMonth, 6);

        Assert.Equal([2025, 2023], await repository.ListRecapYears(seed.User.Id, _tokyo, CancellationToken.None));
        Assert.Equal([2025, 2022], await repository.ListRecapYears(seed.User.Id, TimeZoneInfo.Utc, CancellationToken.None));
    }

    [Fact]
    public async Task CurrentYearRecapCountsDaysOnlyUpToToday()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await Seed(db);
        var game = await AddGame(db, $"This year {seed.Suffix}");
        var now = DateTimeOffset.UtcNow;
        AddSession(db, seed, game, now.AddHours(-1), 600);
        await db.SaveChangesAsync();
        var today = LocalCalendar.DateOf(now, _losAngeles);
        var yearStart = new DateOnly(today.Year, 1, 1);

        var recap = await new RecapService(new RecapRepository(db)).GetRecap(
            seed.User.Id,
            $"{today.Year}-01-01",
            $"{today.Year}-12-31",
            LocalCalendar.StartOf(yearStart, _losAngeles),
            LocalCalendar.StartOf(yearStart.AddYears(1), _losAngeles),
            _losAngeles,
            CancellationToken.None);

        var elapsedDays = today.DayNumber - yearStart.DayNumber + 1;
        Assert.Equal(elapsedDays, recap.DaysPlayed + recap.DaysNotPlayed);
        Assert.Equal(recap.TotalSessions / (double)elapsedDays, recap.AverageSessionsPerDay, 10);
        Assert.Equal(recap.TotalSessions / (double)today.Month, recap.AverageSessionsPerMonth, 10);
    }

    private static PlaySession AddSession(
        PlaydexDbContext db, SeedData seed, Game game, DateTimeOffset startedAt, int? seconds)
    {
        var session = new PlaySession
        {
            UserId = seed.User.Id,
            GameId = game.Id,
            PlatformId = seed.Platform.Id,
            CaptureMethod = CaptureMethod.Manual,
            StartedAt = startedAt,
            EndedAt = seconds is { } value ? startedAt.AddSeconds(value) : null,
        };
        db.PlaySessions.Add(session);
        return session;
    }

    private static async Task<Game> AddGame(PlaydexDbContext db, string title)
    {
        var game = new Game { Title = title };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private static async Task<SeedData> Seed(PlaydexDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"client-zone-{suffix}@example.test" };
        var platform = new Platform { Key = $"client-zone-{suffix}", Name = "Test platform" };
        db.AddRange(user, platform);
        await db.SaveChangesAsync();
        return new SeedData(suffix, user, platform);
    }

    private static AccountsService CreateService(PlaydexDbContext db) =>
        new(new AccountsRepository(db, new GamesService(new GamesRepository(db))));

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            })
            .Options);

    private sealed record SeedData(string Suffix, User User, Platform Platform);
}
