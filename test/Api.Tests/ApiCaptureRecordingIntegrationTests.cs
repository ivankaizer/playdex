using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Api.Infrastructure.Common;
using Playdex.Persistence;
using Api.Modules.ApiCapture;
using Api.Modules.Games;
using Api.Modules.Posters;
using Xunit;

namespace Api.Tests;

/// <summary>POST /capture/sessions with a startedAt records only the time the server doesn't have yet.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ApiCaptureRecordingIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ASessionSentTwiceIsRecordedOnce()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await WithApiKey(async (db, service, seed) =>
        {
            var startedAt = seed.Now.AddHours(-2);

            var first = await service.RecordSession(seed.ApiKey, 600, startedAt, "Hades", "1145360", "steam", "steam", CancellationToken.None);
            // The answer was lost, so the offline queue sends it again.
            var again = await service.RecordSession(seed.ApiKey, 600, startedAt, "Hades", "1145360", "steam", "steam", CancellationToken.None);

            Assert.NotNull(first?.SessionId);
            Assert.Equal(600, first.RecordedSeconds);
            Assert.Null(again?.SessionId);
            Assert.Equal(0, again!.RecordedSeconds);
            var session = Assert.Single(await Sessions(db, seed));
            Assert.Equal((startedAt, startedAt.AddMinutes(10)), session);
        });
    }

    [Fact]
    public async Task PlayAfterThePingTimeoutIsRecordedOnlyWhereTheServerHasNoSession()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await WithApiKey(async (db, service, seed) =>
        {
            // Online from an hour ago; pings stopped reaching the server, which closed the session 36 minutes ago (its
            // last ping plus 3 minutes). The game ran on: a ping a minute ago opened a new session.
            await Insert(db, seed, seed.Now.AddHours(-1), seed.Now.AddMinutes(-36));
            await Insert(db, seed, seed.Now.AddMinutes(-1), null);

            // The client's idea of the close is a minute early, and it sends up to its reconnecting ping.
            var response = await service.RecordSession(
                seed.ApiKey, 36 * 60, seed.Now.AddMinutes(-37), "Hades", "1145360", "steam", "steam", CancellationToken.None);

            Assert.Equal(35 * 60, response!.RecordedSeconds);
            Assert.Equal(
                [
                    (seed.Now.AddHours(-1), (DateTimeOffset?)seed.Now.AddMinutes(-36)),
                    (seed.Now.AddMinutes(-36), seed.Now.AddMinutes(-1)),
                    (seed.Now.AddMinutes(-1), null),
                ],
                await Sessions(db, seed));
        });
    }

    [Fact]
    public async Task ASessionWithoutAStartIsRecordedAsBefore()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await WithApiKey(async (db, service, seed) =>
        {
            await Insert(db, seed, seed.Now.AddMinutes(-30), seed.Now.AddMinutes(-20));

            var response = await service.RecordSession(seed.ApiKey, 3600, null, "Hades", "1145360", "steam", "steam", CancellationToken.None);

            Assert.Equal(3600, response!.RecordedSeconds);
            Assert.Equal(2, (await Sessions(db, seed)).Count);
        });
    }

    private sealed record Seed(DateTimeOffset Now, string ApiKey, long UserId, long IntegrationId, long GameId, long PlatformId);

    private async Task WithApiKey(Func<PlaydexDbContext, ApiCaptureService, Seed, Task> test)
    {
        await using var db = CreateDb();
        var clock = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(clock.Ticks - (clock.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("n");
        var platform = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User { Email = $"capture-record-{suffix}@example.test", CreatedAt = now, UpdatedAt = now };
        var game = new Game { Title = "Record Test", CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Api,
            ApiKey = $"record-{suffix}",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        try
        {
            var service = new ApiCaptureService(
                new ApiCaptureRepository(db),
                db,
                new FixedGameResolver(game.Id),
                TestSessions.Lifecycle(db),
                new PosterUrls(Options.Create(new PostersOptions())));
            await test(db, service, new Seed(now, integration.ApiKey, user.Id, integration.Id, game.Id, platform.Id));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.PlaySessions.Where(row => row.IntegrationId == integration.Id).ExecuteDeleteAsync();
            await db.Integrations.Where(row => row.Id == integration.Id).ExecuteDeleteAsync();
            await db.Users.Where(row => row.Id == user.Id).ExecuteDeleteAsync();
            await db.Games.Where(row => row.Id == game.Id).ExecuteDeleteAsync();
        }
    }

    private static async Task Insert(PlaydexDbContext db, Seed seed, DateTimeOffset startedAt, DateTimeOffset? endedAt)
    {
        db.PlaySessions.Add(new PlaySession
        {
            UserId = seed.UserId,
            GameId = seed.GameId,
            PlatformId = seed.PlatformId,
            CaptureMethod = CaptureMethod.Api,
            IntegrationId = seed.IntegrationId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            CreatedAt = seed.Now,
            UpdatedAt = endedAt ?? seed.Now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<(DateTimeOffset StartedAt, DateTimeOffset? EndedAt)>> Sessions(PlaydexDbContext db, Seed seed)
    {
        db.ChangeTracker.Clear();
        var rows = await db.PlaySessions
            .AsNoTracking()
            .Where(row => row.IntegrationId == seed.IntegrationId)
            .OrderBy(row => row.StartedAt)
            .Select(row => new { row.StartedAt, row.EndedAt })
            .ToListAsync();
        return rows.ConvertAll(row => (row.StartedAt, row.EndedAt));
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
            Task.FromResult(new GameResolution(gameId, null, false));
    }
}
