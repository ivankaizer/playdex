using Api.Infrastructure.Persistence;
using Api.Modules.Accounts;
using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class AccountsPlatformNameTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SessionAndStatsResponsesUsePersistedPlatformName()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var platform = new Platform
        {
            Key = $"test-{suffix}",
            Name = $"Display Name {suffix}",
        };
        var user = new User { Email = $"platform-name-{suffix}@example.test" };
        var game = new Game { Title = $"Platform Name Game {suffix}" };
        db.AddRange(platform, user, game);
        await db.SaveChangesAsync();

        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Api,
            Active = true,
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var session = new PlaySession
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = platform.Id,
            CaptureMethod = CaptureMethod.Api,
            IntegrationId = integration.Id,
            StartedAt = startedAt,
            EndedAt = startedAt.AddMinutes(20),
        };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync();

        try
        {
            var repository = new AccountsRepository(
                db,
                new GamesService(new GamesRepository(db)));

            var listed = await repository.ListSessions(
                user.Id, null, null, null, platform.Key,
                null, null, "date", "desc", 10, 0, 0,
                CancellationToken.None);
            var listedSession = Assert.Single(listed.Sessions);
            Assert.Equal(platform.Key, listedSession.PlatformKey);
            Assert.Equal(platform.Name, listedSession.PlatformName);

            var stats = await repository.ComputeStats(
                user.Id, null, startedAt.AddMinutes(-1), startedAt.AddHours(1),
                platform.Key, 0, CancellationToken.None);
            var platformStat = Assert.Single(stats.Platforms);
            Assert.Equal(platform.Key, platformStat.PlatformKey);
            Assert.Equal(platform.Name, platformStat.PlatformName);
        }
        finally
        {
            db.PlaySessions.Remove(session);
            db.Integrations.Remove(integration);
            db.Users.Remove(user);
            db.Games.Remove(game);
            db.Platforms.Remove(platform);
            await db.SaveChangesAsync();
        }
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(
                postgres.DataSource!,
                npgsql =>
                {
                    npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                    npgsql.MapEnum<IntegrationType>("integration_type");
                    npgsql.MapEnum<CaptureMethod>("capture_method");
                })
            .Options);
}
