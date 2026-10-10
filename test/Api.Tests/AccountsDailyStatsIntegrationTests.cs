using Api.Infrastructure.Http;
using Api.Modules.Accounts;
using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class AccountsDailyStatsIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DailyStatsBucketSessionsByUtcDayAndFillEmptyDays()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (user, steam, psn) = await SeedAsync(db);

        var service = CreateService(db);
        var days = await service.ComputeDailyStats(
            user.Id, null, "2026-09-28", "2026-10-01", null, 0, CancellationToken.None);

        Assert.Equal(["2026-09-28", "2026-09-29", "2026-09-30", "2026-10-01"], days.Select(day => day.Date));
        Assert.Equal([5400d, 0d, 1800d, 0d], days.Select(day => day.TotalSeconds));
        Assert.Equal([2, 0, 1, 0], days.Select(day => day.SessionCount));

        var steamOnly = await service.ComputeDailyStats(
            user.Id, null, "2026-09-28", "2026-10-01", steam.Key.ToUpperInvariant(), 0, CancellationToken.None);
        Assert.Equal([3600d, 0d, 1800d, 0d], steamOnly.Select(day => day.TotalSeconds));
        Assert.NotEqual(steam.Key, psn.Key);
    }

    [Fact]
    public async Task DailyStatsRejectRangesLongerThanAYear()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        var service = CreateService(db);

        await Assert.ThrowsAsync<AppProblemException>(() => service.ComputeDailyStats(
            1, null, "2025-01-01", "2026-01-02", null, 0, CancellationToken.None));
    }

    private PlaydexDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            }).Options);

    private static AccountsService CreateService(PlaydexDbContext db) =>
        new(new AccountsRepository(db, new GamesService(new GamesRepository(db))));

    private static async Task<(User User, Platform Steam, Platform Psn)> SeedAsync(PlaydexDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"daily-stats-{suffix}@example.test" };
        var game = new Game { Title = $"Daily stats {suffix}" };
        var steam = new Platform { Key = $"steam-{suffix}", Name = "Steam" };
        var psn = new Platform { Key = $"psn-{suffix}", Name = "PlayStation" };
        db.AddRange(user, game, steam, psn);
        await db.SaveChangesAsync();

        db.PlaySessions.AddRange(
            Session(user, game, steam, new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero), 3600),
            Session(user, game, psn, new DateTimeOffset(2026, 9, 28, 23, 30, 0, TimeSpan.Zero), 1800),
            Session(user, game, steam, new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero), 1800),
            // Outside the requested range on both ends.
            Session(user, game, steam, new DateTimeOffset(2026, 9, 27, 23, 0, 0, TimeSpan.Zero), 600),
            Session(user, game, steam, new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), 600));
        await db.SaveChangesAsync();
        return (user, steam, psn);
    }

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
}
