using Microsoft.EntityFrameworkCore;
using Api.Modules.Polling;
using Api.Infrastructure.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PlayniteTimeoutIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CleanupEndsPlayniteSessionAtLastPingPlusThreeMinutes()
    {
        if (!postgres.IsConfigured) return;

        await using var db = new PlaydexDbContext(
            new DbContextOptionsBuilder<PlaydexDbContext>()
                .UseNpgsql(postgres.DataSource!)
                .Options);
        var suffix = Guid.NewGuid().ToString("n");
        var platform = await db.Platforms.SingleAsync(row => row.Key == "playnite");
        var unknown = await db.Platforms.SingleAsync(row => row.Key == "unknown");
        var user = new User { Email = $"timeout-{suffix}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var credential = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = platform.Id,
            ApiKey = suffix,
            Active = true,
        };
        var game = new Game
        {
            PlatformId = unknown.Id,
            Title = "Timeout Game",
            CanonicalKey = $"unknown:{suffix}",
        };
        db.AddRange(credential, game);
        await db.SaveChangesAsync();
        var lastPing = DateTimeOffset.UtcNow.AddMinutes(-5);
        var session = new PlaySession
        {
            PlatformCredentialId = credential.Id,
            GameId = game.Id,
            StartedAt = lastPing.AddMinutes(-10),
            CreatedAt = lastPing.AddMinutes(-10),
            UpdatedAt = lastPing,
        };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync();

        try
        {
            var transitions = await new PollingRepository(db)
                .EndTimedOutPlayniteSessions(
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(3),
                    CancellationToken.None);

            var transition = Assert.Single(transitions);
            Assert.Equal(SessionTransitionKind.Ended, transition.Kind);
            Assert.Equal(lastPing.AddMinutes(3), transition.EndedAt);
            await db.Entry(session).ReloadAsync();
            Assert.Equal(lastPing.AddMinutes(3), session.EndedAt);
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            db.Games.Remove(game);
            await db.SaveChangesAsync();
        }
    }
}
