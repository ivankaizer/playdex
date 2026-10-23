using Api.Modules.Manual;
using Api.Modules.Games;
using Api.Modules.Polling;
using Api.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Globalization;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class ManualRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PerUserStartLockSerializesCompetingTransactions()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var firstDb = CreateDb();
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        await TestSessions.Lifecycle(firstDb).LockManualTimer(42, CancellationToken.None);

        await using var secondDb = CreateDb();
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
        var secondLock = TestSessions.Lifecycle(secondDb)
            .LockManualTimer(42, CancellationToken.None);

        Assert.NotSame(secondLock, await Task.WhenAny(secondLock, Task.Delay(250)));

        await firstTransaction.CommitAsync();
        await secondLock.WaitAsync(TimeSpan.FromSeconds(5));
        await secondTransaction.CommitAsync();
    }

    [Fact]
    public async Task StopWaitsForTheSamePerUserStartLock()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var firstDb = CreateDb();
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        await TestSessions.Lifecycle(firstDb).LockManualTimer(43, CancellationToken.None);

        await using var stopDb = CreateDb();
        var stop = CreateService(stopDb).StopSession(43, CancellationToken.None);

        Assert.NotSame(stop, await Task.WhenAny(stop, Task.Delay(250)));

        await firstTransaction.CommitAsync();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SavedGameCanBeStartedStoppedAndLoggedHistorically()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("n");
        var platform = await db.Platforms.SingleAsync(item => item.Key == "pc");
        var user = new User
        {
            Email = $"manual-session-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var game = new Game
        {
            Title = $"Manual game {suffix}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        try
        {
            var started = await service.StartSession(
                user.Id,
                new ManualSessionStartRequest(game.Title, GameId.Encode(game.Id), "pc"),
                CancellationToken.None);

            var active = await db.PlaySessions.SingleAsync(item => item.Id == started.SessionId);
            Assert.Null(active.EndedAt);
            Assert.Equal(CaptureMethod.Manual, active.CaptureMethod);
            Assert.Equal(platform.Id, active.PlatformId);

            Assert.Equal(1, (await service.StopSession(user.Id, CancellationToken.None)).EndedSessionCount);

            var historical = await service.CreateEntry(
                user.Id,
                new ManualEntryCreateRequest(
                    "2026-09-07", 75, GameId.Encode(game.Id), null, game.Title, "pc", null),
                CancellationToken.None);

            Assert.Equal(2, await db.PlaySessions.CountAsync(item => item.UserId == user.Id));
            Assert.NotNull((await db.PlaySessions.SingleAsync(item => item.Id == historical.SessionId)).EndedAt);
        }
        finally
        {
            db.PlaySessions.RemoveRange(db.PlaySessions.Where(item => item.UserId == user.Id));
            db.Games.Remove(game);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task VerifiedIgdbSelectionCreatesOnceAndReusesTheIdentity()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("n");
        var igdbId = Random.Shared.Next(100_000_000, int.MaxValue)
            .ToString(CultureInfo.InvariantCulture);
        var user = new User
        {
            Email = $"manual-igdb-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var handler = new QueueHandler(
            Json("""{"access_token":"token","expires_in":3600}"""),
            IgdbGame(igdbId),
            IgdbGame(igdbId));
        var options = Options.Create(new IgdbOptions { ClientId = "client", ClientSecret = "secret" });
        var service = new ManualService(
            new ManualRepository(db),
            new IgdbClient(new HttpClient(handler), options, new IgdbTokenCache()),
            options,
            new GameResolver(db),
            TestSessions.Lifecycle(db));

        try
        {
            await service.StartSession(
                user.Id,
                new ManualSessionStartRequest(
                    "Spoofed title", null, "pc", igdbId, "https://images.test/spoofed.jpg"),
                CancellationToken.None);
            await service.StopSession(user.Id, CancellationToken.None);
            await service.CreateEntry(
                user.Id,
                new ManualEntryCreateRequest(
                    "2026-09-07", 60, null, igdbId, "Spoofed again", "pc", null),
                CancellationToken.None);

            var identity = await db.GameExternalIdentities.SingleAsync(item =>
                item.Type == GameExternalIdentityType.Igdb && item.ExternalId == igdbId);
            var game = await db.Games.SingleAsync(item => item.Id == identity.GameId);
            Assert.Equal("Authoritative Hades II", game.Title);
            Assert.Equal(
                "https://images.igdb.com/igdb/image/upload/t_cover_big/cover-id.jpg",
                game.CapsuleUrl);
            Assert.Equal(2, await db.PlaySessions.CountAsync(item => item.UserId == user.Id));
        }
        finally
        {
            db.PlaySessions.RemoveRange(db.PlaySessions.Where(item => item.UserId == user.Id));
            var identities = db.GameExternalIdentities.Where(item =>
                item.Type == GameExternalIdentityType.Igdb && item.ExternalId == igdbId);
            var gameIds = await identities.Select(item => item.GameId).ToListAsync();
            db.GameExternalIdentities.RemoveRange(identities);
            db.Games.RemoveRange(db.Games.Where(item => gameIds.Contains(item.Id)));
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    private static ManualService CreateService(PlaydexDbContext db) => new(
        new ManualRepository(db),
        new IgdbClient(new HttpClient(), Options.Create(new IgdbOptions()), new IgdbTokenCache()),
        Options.Create(new IgdbOptions()),
        new GameResolver(db),
        TestSessions.Lifecycle(db));

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage IgdbGame(string id) => Json(
        $$"""[{"id":{{id}},"name":"Authoritative Hades II","cover":{"image_id":"cover-id"},"platforms":[{"id":6,"name":"PC"}]}]""");

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
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
}
