using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Accounts;
using Api.Modules.Games;
using Api.Modules.Manual;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Hades", "%Hades%")]
    [InlineData("50%", @"%50\%%")]
    [InlineData("A_B", @"%A\_B%")]
    [InlineData(@"C:\Games", @"%C:\\Games%")]
    public void PatternsMatchWildcardsLiterally(string text, string pattern) =>
        Assert.Equal(pattern, SearchText.ContainsPattern(text));

    [Fact]
    public void SearchesAreTrimmedAndBounded()
    {
        Assert.Equal("Hades", SearchText.Bounded("  Hades "));
        Assert.Equal(new string('a', 200), SearchText.Bounded(new string('a', 200)));
        var exception = Assert.Throws<AppProblemException>(() => SearchText.Bounded(new string('a', 201)));
        Assert.Equal("query must be at most 200 characters.", exception.Message);
        Assert.Null(SearchText.OptionalFilter("   ", "title"));
        Assert.Equal("title must be at most 200 characters.",
            Assert.Throws<AppProblemException>(() => SearchText.OptionalFilter(new string('a', 201), "title")).Message);
    }

    [Fact]
    public async Task GameSearchRejectsOverlongQueriesBeforeQuerying()
    {
        var games = new GamesService(new GamesRepository(null!));
        await Assert.ThrowsAsync<AppProblemException>(() => games.Search(new string('a', 201), 10, CancellationToken.None));
    }
}

[Collection(PostgresCollectionDefinition.Name)]
public sealed class SearchTextIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PercentAndUnderscoreInASearchMatchOnlyThemselves()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var underscore = new Game { Title = $"A_B {suffix}" };
        var anyCharacter = new Game { Title = $"AxB {suffix}" };
        var percent = new Game { Title = $"50% {suffix}" };
        var anything = new Game { Title = $"500 and more {suffix}" };
        var user = new User { Email = $"search-{suffix}@example.test" };
        var platform = new Platform { Key = $"search-{suffix}", Name = "Search platform" };
        db.AddRange(underscore, anyCharacter, percent, anything, user, platform);
        await db.SaveChangesAsync();
        foreach (var game in new[] { underscore, anyCharacter })
        {
            db.PlaySessions.Add(new PlaySession
            {
                UserId = user.Id, GameId = game.Id, PlatformId = platform.Id, CaptureMethod = CaptureMethod.Manual,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-2), EndedAt = DateTimeOffset.UtcNow.AddHours(-1),
            });
        }
        await db.SaveChangesAsync();

        var games = new GamesRepository(db);
        Assert.Equal([underscore.Title], (await games.SearchGames($"A_B {suffix}", 10, default)).Select(game => game.Title));
        Assert.Equal([percent.Title], (await games.SearchGames($"50% {suffix}", 10, default)).Select(game => game.Title));

        var manual = new ManualRepository(db);
        Assert.Equal([percent.Title], (await manual.SearchLocalGames($"50% {suffix}", 5, default)).Select(game => game.Title));

        var sessions = await new AccountsRepository(db, new GamesService(games)).ListSessions(
            user.Id, null, $"A_B {suffix}", null, null, null, null, "date", "desc", 10, 0, 0, default);
        Assert.Equal([underscore.Title], sessions.Sessions.Select(session => session.Game));
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            })
            .Options);
}
