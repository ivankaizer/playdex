using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class GamesServiceIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RatingOrIgnoringAnUnknownGameIsNotFound()
    {
        if (!postgres.IsConfigured) return;

        await using var db = new PlaydexDbContext(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!).Options);
        var user = new User { Email = $"games-missing-{Guid.NewGuid():n}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var missing = GameId.Encode(await db.Games.MaxAsync(game => (long?)game.Id) + 1_000_000 ?? 1_000_000);
        var service = new GamesService(new GamesRepository(db));

        try
        {
            var rating = await Assert.ThrowsAsync<AppProblemException>(() =>
                service.SetRating(user.Id, new GameRatingRequest(missing, 4), default));
            var ignore = await Assert.ThrowsAsync<AppProblemException>(() =>
                service.SetIgnore(user.Id, new GameIgnoreRequest(missing), ignored: true, default));

            Assert.Equal(404, rating.StatusCode);
            Assert.Equal(404, ignore.StatusCode);
            Assert.Equal(new GameIgnoreResponseDto(missing, false),
                await service.SetIgnore(user.Id, new GameIgnoreRequest(missing), ignored: false, default));
        }
        finally
        {
            await db.Users.Where(row => row.Id == user.Id).ExecuteDeleteAsync();
        }
    }
}
