using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    [Fact]
    public async Task WithoutAnExternalIdTheSameTitleFindsTheSameGame()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var resolver = new GameResolver(db);
        var suffix = Guid.NewGuid().ToString("N");

        var first = await resolver.Resolve(
            new GameResolutionRequest(null, null, $"Half-Life {suffix}", null), CancellationToken.None);
        var again = await resolver.Resolve(
            new GameResolutionRequest(null, null, $"Half-Life {suffix}", null), CancellationToken.None);
        var respelled = await resolver.Resolve(
            new GameResolutionRequest(GameExternalIdentityType.Psn, "  ", $"  HALF LIFE: {suffix.ToUpperInvariant()}™ ", null),
            CancellationToken.None);

        Assert.Equal(first.GameId, again.GameId);
        Assert.Equal(first.GameId, respelled.GameId);
        Assert.Null(respelled.GameExternalIdentityId);
        Assert.Equal(1, await db.Games.CountAsync(row => EF.Functions.ILike(row.Title, $"%{suffix}%")));
    }

    [Fact]
    public async Task TitlesInOtherScriptsKeepTheirLetters()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var resolver = new GameResolver(db);
        var number = Random.Shared.Next(1_000_000, 9_999_999);

        var witcher = await resolver.Resolve(new GameResolutionRequest(null, null, $"Ведьмак {number}", null), CancellationToken.None);
        var persona = await resolver.Resolve(new GameResolutionRequest(null, null, $"ペルソナ{number}", null), CancellationToken.None);

        Assert.NotEqual(witcher.GameId, persona.GameId);
    }

    [Fact]
    public async Task AGameFoundByTitleKeepsItsTitleAndPoster()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var resolver = new GameResolver(db);
        var title = $"Poster Game {Guid.NewGuid():N}";
        var withoutPoster = await resolver.Resolve(new GameResolutionRequest(null, null, title, null), CancellationToken.None);

        await resolver.Resolve(
            new GameResolutionRequest(null, null, title.ToUpperInvariant(), "https://example.com/first.jpg", TitleIsAuthoritative: true),
            CancellationToken.None);
        var later = await resolver.Resolve(
            new GameResolutionRequest(null, null, title, "https://example.com/second.jpg"), CancellationToken.None);

        var game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == withoutPoster.GameId);
        Assert.True(withoutPoster.NeedsCapsule);
        Assert.False(later.NeedsCapsule);
        Assert.Equal(title, game.Title);
        Assert.Equal("https://example.com/first.jpg", game.CapsuleUrl);
    }

    [Fact]
    public async Task AnUpToDateGameIsNeitherWrittenNorLocked()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var resolver = new GameResolver(db);
        var appId = $"resolver-{Guid.NewGuid():N}";
        var stamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var before = new Game { Title = "Steady Title", CapsuleUrl = "https://example.com/c.jpg", CreatedAt = stamp, UpdatedAt = stamp };
        db.Games.Add(before);
        await db.SaveChangesAsync();
        var identity = new GameExternalIdentity
        {
            GameId = before.Id, Type = GameExternalIdentityType.Steam, ExternalId = appId, Title = "Steady Title",
            CreatedAt = stamp, UpdatedAt = stamp,
        };
        db.GameExternalIdentities.Add(identity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Another player resolving the same game holds its lock; an up-to-date resolve must not wait for it.
        await using var otherPlayer = await postgres.DataSource!.OpenConnectionAsync();
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@key, 0))", otherPlayer))
        {
            hold.Parameters.AddWithValue("key", $"identity:steam:{appId}");
            await hold.ExecuteNonQueryAsync();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var again = await resolver.Resolve(
            new GameResolutionRequest(GameExternalIdentityType.Steam, appId, "Steady Title", "https://example.com/c.jpg", TitleIsAuthoritative: true),
            timeout.Token);

        var after = await db.Games.AsNoTracking().SingleAsync(row => row.Id == before.Id);
        var identityAfter = await db.GameExternalIdentities.AsNoTracking().SingleAsync(row => row.Id == identity.Id);
        Assert.Equal(new GameResolution(before.Id, identity.Id, NeedsCapsule: false), again);
        Assert.Equal(stamp, after.UpdatedAt);
        Assert.Equal(stamp, identityAfter.UpdatedAt);
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
