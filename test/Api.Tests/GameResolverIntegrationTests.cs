using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;
using Api.Modules.Games;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class GameResolverIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ExactIdentityReusesGameAcrossPlatformsAndPreservesLocks()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var psn = await db.Platforms.SingleAsync(row => row.Key == "psn");
        var now = DateTimeOffset.UtcNow;
        var game = new Game
        {
            Title = "Locked title",
            ExternalId = suffix,
            CanonicalKey = $"steam:{suffix}",
            PlatformId = steam.Id,
            CapsuleUrl = "https://example.test/locked.jpg",
            TitleLocked = true,
            CapsuleUrlLocked = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        db.GameExternalIdentities.Add(new GameExternalIdentity
        {
            GameId = game.Id,
            Type = GameExternalIdentityType.Steam,
            ExternalId = suffix,
            Title = "Original report",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        try
        {
            var result = await new GameResolver(db).Resolve(
                new GameResolutionRequest(
                    GameExternalIdentityType.Steam,
                    suffix,
                    "New reported title",
                    "psn",
                    "https://example.test/new.jpg"),
                CancellationToken.None);

            Assert.Equal(game.Id, result.GameId);
            var updatedGame = await db.Games.AsNoTracking().SingleAsync(row => row.Id == game.Id);
            Assert.Equal("Locked title", updatedGame.Title);
            Assert.Equal("https://example.test/locked.jpg", updatedGame.CapsuleUrl);
            Assert.Equal(psn.Id, updatedGame.PlatformId);
            var identity = await db.GameExternalIdentities.AsNoTracking()
                .SingleAsync(row => row.GameId == game.Id);
            Assert.Equal("New reported title", identity.Title);
        }
        finally
        {
            await db.GameExternalIdentities.Where(row => row.GameId == game.Id).ExecuteDeleteAsync();
            await db.Games.Where(row => row.Id == game.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task NewIdentityDualWritesAndMissingIdUsesLegacyFallback()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var resolver = new GameResolver(db);
        var identified = await resolver.Resolve(
            new GameResolutionRequest(
                GameExternalIdentityType.Xbox,
                $"xbox-{suffix}",
                "Identified Game",
                "xbox",
                null),
            CancellationToken.None);
        var fallback = await resolver.Resolve(
            new GameResolutionRequest(
                GameExternalIdentityType.Steam,
                null,
                $"Fallback {suffix}",
                "steam",
                null),
            CancellationToken.None);

        try
        {
            var identifiedGame = await db.Games.AsNoTracking()
                .SingleAsync(row => row.Id == identified.GameId);
            Assert.Equal($"xbox:xbox-{suffix}", identifiedGame.CanonicalKey);
            var identity = await db.GameExternalIdentities.AsNoTracking()
                .SingleAsync(row => row.GameId == identified.GameId);
            Assert.Equal(GameExternalIdentityType.Xbox, identity.Type);

            var fallbackGame = await db.Games.AsNoTracking().SingleAsync(row => row.Id == fallback.GameId);
            Assert.Equal($"steam:fallback {suffix}", fallbackGame.CanonicalKey);
            Assert.False(await db.GameExternalIdentities.AnyAsync(row => row.GameId == fallback.GameId));
        }
        finally
        {
            await db.GameExternalIdentities
                .Where(row => row.ExternalId == $"xbox-{suffix}")
                .ExecuteDeleteAsync();
            await db.Games
                .Where(row => row.Id == identified.GameId || row.Id == fallback.GameId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ConcurrentResolutionCreatesOneGameAndIdentity()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var externalId = $"concurrent-{Guid.NewGuid():n}";
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var request = new GameResolutionRequest(
            GameExternalIdentityType.Steam,
            externalId,
            "Concurrent Game",
            "steam",
            null);

        var resolutions = await Task.WhenAll(
            new GameResolver(firstDb).Resolve(request, CancellationToken.None),
            new GameResolver(secondDb).Resolve(request, CancellationToken.None));

        Assert.Equal(resolutions[0].GameId, resolutions[1].GameId);
        await using var verifyDb = CreateDb();
        var identity = await verifyDb.GameExternalIdentities
            .SingleAsync(row => row.Type == GameExternalIdentityType.Steam && row.ExternalId == externalId);
        Assert.Equal(resolutions[0].GameId, identity.GameId);
        Assert.Equal(1, await verifyDb.Games.CountAsync(row => row.Id == identity.GameId));

        await verifyDb.GameExternalIdentities.Where(row => row.Id == identity.Id).ExecuteDeleteAsync();
        await verifyDb.Games.Where(row => row.Id == identity.GameId).ExecuteDeleteAsync();
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(
                postgres.DataSource!,
                npgsql => npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type"))
            .Options);
}
