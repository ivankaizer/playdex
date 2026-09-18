using Api.Modules.Accounts;
using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class AccountsSessionUpdateIntegrationTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(120, 120)]
    [InlineData(-330, -330)]
    [InlineData(120, 60)]
    [InlineData(0, 0)]
    public async Task EditingSessionPersistsOffsetTimestampsAsUtc(int startOffsetMinutes, int endOffsetMinutes)
    {
        if (!postgres.IsConfigured) return;

        await using var db = new PlaydexDbContext(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            }).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"session-update-{suffix}@example.test" };
        var game = new Game { Title = $"Session update {suffix}" };
        var platform = new Platform { Key = $"session-update-{suffix}", Name = "Test platform" };
        db.AddRange(user, game, platform);
        await db.SaveChangesAsync();
        var session = new PlaySession
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = platform.Id,
            CaptureMethod = CaptureMethod.Manual,
            StartedAt = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 9, 17, 13, 0, 0, TimeSpan.Zero),
        };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync();

        var expectedStart = new DateTimeOffset(2026, 9, 17, 23, 30, 0, TimeSpan.Zero);
        var expectedEnd = new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero);
        var request = new AccountSessionUpdateRequest(
            expectedStart.ToOffset(TimeSpan.FromMinutes(startOffsetMinutes)),
            expectedEnd.ToOffset(TimeSpan.FromMinutes(endOffsetMinutes)));
        var service = new AccountsService(new AccountsRepository(db, new GamesService(new GamesRepository(db))));

        var response = await service.UpdateSession(session.Id, user.Id, request, CancellationToken.None);
        await db.Entry(session).ReloadAsync();

        Assert.Equal(expectedStart, session.StartedAt);
        Assert.Equal(expectedEnd, session.EndedAt);
        Assert.Equal(TimeSpan.Zero, session.StartedAt.Offset);
        Assert.Equal(TimeSpan.Zero, session.EndedAt!.Value.Offset);
        Assert.Equal(expectedStart, response.StartedAt);
        Assert.Equal(expectedEnd, response.EndedAt);
        Assert.Equal(TimeSpan.Zero, response.StartedAt.Offset);
        Assert.Equal(TimeSpan.Zero, response.EndedAt.Offset);
    }
}
