using Microsoft.EntityFrameworkCore;
using Playdex.Api.Modules.Polling;
using Playdex.Api.Shared.Persistence;
using Xunit;

namespace Playdex.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PollingLifecycleIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SuccessfulPolls_ApplyStartUnchangedSwitchStopIgnoreAndEvidenceAtomically()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var platform = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User { Email = $"polling-{suffix}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var credential = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = platform.Id,
            AccountId = suffix,
            ApiKey = suffix,
            Active = true,
        };
        db.PlatformCredentials.Add(credential);
        await db.SaveChangesAsync();

        try
        {
            var repository = new PollingRepository(db);
            var firstAt = DateTimeOffset.UtcNow.AddMinutes(-4);
            var first = await repository.ApplySuccessfulPoll(
                credential.Id,
                new CurrentlyPlayingResult("First Game", $"first-{suffix}", "https://example.test/first.jpg", "steam"),
                firstAt,
                "{\"state\":\"first\"}",
                CancellationToken.None);

            Assert.NotNull(first.GameId);
            Assert.False(first.Ignored);
            Assert.Equal(SessionTransitionKind.Started, Assert.Single(first.Transitions).Kind);

            var unchangedAt = firstAt.AddMinutes(1);
            var unchanged = await repository.ApplySuccessfulPoll(
                credential.Id,
                new CurrentlyPlayingResult("First Game Renamed", $"first-{suffix}", "https://example.test/updated.jpg", "steam"),
                unchangedAt,
                "{\"state\":\"same\"}",
                CancellationToken.None);

            Assert.Empty(unchanged.Transitions);
            var active = await db.PlaySessions.SingleAsync(row => row.EndedAt == null);
            Assert.Equal(unchangedAt, active.UpdatedAt);
            var updatedGame = await db.Games.SingleAsync(row => row.Id == first.GameId);
            Assert.Equal("First Game Renamed", updatedGame.Title);
            Assert.Equal("https://example.test/updated.jpg", updatedGame.CapsuleUrl);

            var switchedAt = firstAt.AddMinutes(2);
            var switched = await repository.ApplySuccessfulPoll(
                credential.Id,
                new CurrentlyPlayingResult("Second Game", $"second-{suffix}", null, "steam"),
                switchedAt,
                "{\"state\":\"second\"}",
                CancellationToken.None);

            Assert.Collection(
                switched.Transitions,
                transition => Assert.Equal(SessionTransitionKind.Ended, transition.Kind),
                transition => Assert.Equal(SessionTransitionKind.Started, transition.Kind));

            var stoppedAt = firstAt.AddMinutes(3);
            var stopped = await repository.ApplySuccessfulPoll(
                credential.Id, null, stoppedAt, "{\"state\":\"idle\"}", CancellationToken.None);

            Assert.Equal(SessionTransitionKind.Ended, Assert.Single(stopped.Transitions).Kind);
            Assert.Empty(await db.PlaySessions.Where(row => row.EndedAt == null).ToListAsync());

            db.IgnoredGames.Add(new IgnoredGame { UserId = user.Id, GameId = first.GameId!.Value });
            await db.SaveChangesAsync();
            var ignoredAt = firstAt.AddMinutes(4);
            var ignored = await repository.ApplySuccessfulPoll(
                credential.Id,
                new CurrentlyPlayingResult("First Game", $"first-{suffix}", null, "steam"),
                ignoredAt,
                "{\"state\":\"ignored\"}",
                CancellationToken.None);

            Assert.True(ignored.Ignored);
            Assert.Empty(ignored.Transitions);
            Assert.Empty(await db.PlaySessions.Where(row => row.EndedAt == null).ToListAsync());

            var polls = await db.PollResults
                .Where(row => row.PlatformCredentialId == credential.Id)
                .OrderBy(row => row.PolledAt)
                .ToListAsync();
            Assert.Equal(5, polls.Count);
            Assert.Equal(first.GameId, polls[0].GameId);
            Assert.Equal("{\"state\":\"first\"}", polls[0].RawPayload);
            Assert.Null(polls[3].GameId);
            Assert.Equal(first.GameId, polls[4].GameId);
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            await db.Games.Where(row => row.CanonicalKey.EndsWith(suffix)).ExecuteDeleteAsync();
        }
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!)
            .Options);
}
