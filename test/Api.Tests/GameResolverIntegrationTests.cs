using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class GameResolverIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AClientTitleNamesANewGameButNeverRenamesAnExistingOne()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var resolver = new GameResolver(db);
        var appId = $"resolver-{Guid.NewGuid():N}";

        var created = await resolver.Resolve(
            new GameResolutionRequest(GameExternalIdentityType.Steam, appId, "Original Title", null), CancellationToken.None);
        await resolver.Resolve(
            new GameResolutionRequest(GameExternalIdentityType.Steam, appId, "Renamed By A Client", null), CancellationToken.None);

        var game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == created.GameId);
        var identity = await db.GameExternalIdentities.AsNoTracking().SingleAsync(row => row.Id == created.GameExternalIdentityId);
        Assert.Equal("Original Title", game.Title);
        Assert.Equal("Original Title", identity.Title);
    }

    [Fact]
    public async Task AnAuthoritativeTitleRenamesAnUnlockedGame()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var resolver = new GameResolver(db);
        var appId = $"resolver-{Guid.NewGuid():N}";

        var created = await resolver.Resolve(
            new GameResolutionRequest(GameExternalIdentityType.Steam, appId, "Working Title", null), CancellationToken.None);
        await resolver.Resolve(
            new GameResolutionRequest(GameExternalIdentityType.Steam, appId, "Release Title", null, TitleIsAuthoritative: true),
            CancellationToken.None);

        var game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == created.GameId);
        Assert.Equal("Release Title", game.Title);
    }

    private PlaydexDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            }).Options);
}
