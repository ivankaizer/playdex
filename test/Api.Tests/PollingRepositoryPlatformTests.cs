using Api.Modules.Polling;
using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PollingRepositoryPlatformTests(PostgresFixture postgres)
{
    [Fact]
    public void EffectiveSessionPlatformPrefersOverrideAndFallsBackToMain()
    {
        Assert.Equal(15L, PollingRepository.GetEffectiveSessionPlatformId(
            new Integration { PlatformId = 10, SessionPlatformId = 15 }));
        Assert.Equal(10L, PollingRepository.GetEffectiveSessionPlatformId(
            new Integration { PlatformId = 10 }));
    }

    [Theory]
    [InlineData(IntegrationType.Steam, "steam")]
    [InlineData(IntegrationType.Psn, "psn")]
    [InlineData(IntegrationType.Xbox, "xbox")]
    public void PollingClientKeyComesFromIntegrationType(IntegrationType type, string expected)
    {
        Assert.Equal(expected, PollingRepository.GetProviderKey(type));
    }

    [Fact]
    public async Task PsnOverrideStillRoutesToPsnAndRecordsSessionsOnTheOverride()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("n");
        var psn = await db.Platforms.SingleAsync(platform => platform.Key == "psn");
        var ps5 = await db.Platforms.SingleAsync(platform => platform.Key == "ps5");
        var user = new User
        {
            Email = $"polling-platform-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var game = new Game { Title = $"Polling platform {suffix}", CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Psn,
            PlatformId = psn.Id,
            SessionPlatformId = ps5.Id,
            ApiKey = "test-token",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        try
        {
            var repository = new PollingRepository(db, new FixedGameResolver(game.Id));
            var active = (await repository.LoadActiveCredentials(default))
                .Single(item => item.Credential.Id == integration.Id);

            Assert.Equal("psn", active.PlatformKey);

            await repository.ApplySuccessfulPoll(
                integration.Id,
                new CurrentlyPlayingResult("Game", "PPSA123", null, "psn"),
                now,
                null,
                default);

            Assert.Equal(ps5.Id, await db.PlaySessions.AsNoTracking()
                .Where(session => session.IntegrationId == integration.Id)
                .Select(session => session.PlatformId)
                .SingleAsync());
        }
        finally
        {
            db.PollResults.RemoveRange(db.PollResults.Where(item => item.IntegrationId == integration.Id));
            db.PlaySessions.RemoveRange(db.PlaySessions.Where(item => item.IntegrationId == integration.Id));
            db.Integrations.Remove(integration);
            db.Games.Remove(game);
            db.Users.Remove(user);
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

    private sealed class FixedGameResolver(long gameId) : IGameResolver
    {
        public Task<GameResolution> Resolve(GameResolutionRequest request, CancellationToken ct) =>
            Task.FromResult(new GameResolution(gameId, null, NeedsCapsule: false));
    }
}
