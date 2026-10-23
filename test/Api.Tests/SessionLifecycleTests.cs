using Api.Infrastructure.Common;
using Api.Modules.ApiCapture;
using Api.Modules.Games;
using Api.Modules.Notifications;
using Api.Modules.Polling;
using Api.Modules.Posters;
using Api.Modules.Providers;
using Api.Modules.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class SessionLifecycleTests(PostgresFixture postgres)
{
    private static readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task PollAfterAGapEndsTheSessionOneIntervalAfterItWasLastSeen()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var lastSeen = Now().AddHours(-6);
        var stale = await world.AddOpenSession(world.Steam, world.GameA, lastSeen.AddHours(-1), lastSeen);
        var recorder = new SessionEventRecorder();
        var lifecycle = TestSessions.Lifecycle(world.Db, recorder);
        var polledAt = Now();

        await world.Poll(lifecycle, world.GameA, polledAt);
        await lifecycle.PublishPending(default);

        Assert.Equal(lastSeen + _pollingInterval, (await world.Session(stale.Id)).EndedAt);
        var current = Assert.Single(await world.OpenSessions(world.Steam));
        Assert.Equal(polledAt, current.StartedAt);
        Assert.Collection(
            recorder.Events,
            ended => Assert.Equal(
                new SessionEnded(stale.Id, world.Steam.Id, world.GameA.Id, stale.StartedAt, lastSeen + _pollingInterval),
                ended),
            started => Assert.Equal(
                new SessionStarted(current.Id, world.Steam.Id, world.GameA.Id, polledAt),
                started));
    }

    [Fact]
    public async Task PollOfTheSameGameWithinTheGapContinuesTheSession()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var lastSeen = Now().AddMinutes(-2);
        var open = await world.AddOpenSession(world.Steam, world.GameA, lastSeen.AddHours(-1), lastSeen);
        var recorder = new SessionEventRecorder();
        var lifecycle = TestSessions.Lifecycle(world.Db, recorder);
        var polledAt = Now();

        await world.Poll(lifecycle, world.GameA, polledAt);
        await lifecycle.PublishPending(default);

        var current = Assert.Single(await world.OpenSessions(world.Steam));
        Assert.Equal(open.Id, current.Id);
        Assert.Equal(polledAt, current.UpdatedAt);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task SwitchingToAnIgnoredGameEndsAndNotifiesThePreviousSession()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        await world.AddTelegramChannel();
        await world.Ignore(world.GameB);
        var recorder = new SessionEventRecorder();
        var lifecycle = TestSessions.Lifecycle(world.Db, recorder);
        var startedAt = Now().AddMinutes(-1);
        var switchedAt = Now();

        await world.Poll(lifecycle, world.GameA, startedAt);
        await world.Poll(lifecycle, world.GameB, switchedAt);
        await lifecycle.PublishPending(default);

        Assert.Empty(await world.OpenSessions(world.Steam));
        var ended = Assert.Single(recorder.Ended);
        Assert.Equal((world.GameA.Id, switchedAt), (ended.GameId, ended.EndedAt));
        var notifications = new NotificationsRepository(world.Db);
        Assert.NotNull(await notifications.GetSessionEndedTarget(ended.SessionId, default));

        var ignoredSession = await world.AddOpenSession(world.Steam, world.GameB, switchedAt, switchedAt);
        Assert.Null(await notifications.GetSessionStartedTarget(ignoredSession.Id, default));
    }

    [Fact]
    public async Task PausingAnIntegrationEndsItsSessionAtTheLastSighting()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var lastSeen = Now().AddMinutes(-30);
        var open = await world.AddOpenSession(world.Steam, world.GameA, lastSeen.AddHours(-1), lastSeen);
        var recorder = new SessionEventRecorder();

        var status = await new ProvidersRepository(world.Db, TestSessions.Lifecycle(world.Db, recorder))
            .UpdateProviderStatus(world.User.Id, world.Steam.Id, "paused", default);

        Assert.Equal("paused", status!.Status);
        Assert.Equal(lastSeen + _pollingInterval, (await world.Session(open.Id)).EndedAt);
        Assert.Equal(open.Id, Assert.Single(recorder.Ended).SessionId);
    }

    [Fact]
    public async Task SweepEndsStaleSessionsOnceAndAnnouncesIntegrationSessions()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var now = Now();
        var polling = await world.AddOpenSession(world.Steam, world.GameA, now.AddHours(-3), now.AddHours(-2));
        var timedOut = await world.AddOpenSession(world.Api, world.GameA, now.AddMinutes(-30), now.AddMinutes(-10));
        var pinged = await world.AddOpenSession(world.Api, world.GameB, now.AddMinutes(-30), now.AddMinutes(-1));
        var manual = await world.AddManualTimer(world.GameA, now.AddHours(-13));
        var recorder = new SessionEventRecorder();
        var lifecycle = TestSessions.Lifecycle(world.Db, recorder);

        await lifecycle.SweepStaleSessions(default);
        await lifecycle.SweepStaleSessions(default);

        Assert.Equal(now.AddHours(-2) + _pollingInterval, (await world.Session(polling.Id)).EndedAt);
        Assert.Equal(now.AddMinutes(-10) + SessionLifecycle.CaptureTimeout, (await world.Session(timedOut.Id)).EndedAt);
        Assert.Null((await world.Session(pinged.Id)).EndedAt);
        Assert.Equal(now.AddHours(-13) + SessionLifecycle.ManualTimerLimit, (await world.Session(manual.Id)).EndedAt);
        Assert.Equal(
            [polling.Id, timedOut.Id],
            recorder.Ended.Where(row => world.OwnsSession(row.SessionId)).Select(row => row.SessionId).Order());
        Assert.Empty(recorder.Started);
    }

    [Fact]
    public async Task CaptureKeepsOneSessionPerRunningGame()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var recorder = new SessionEventRecorder();
        var capture = world.Capture(recorder);

        var a = await capture.StartSession(world.ApiKey, "A", "a", "steam", "steam", default);
        var b = await capture.StartSession(world.ApiKey, "B", "b", "steam", "steam", default);
        var pingA = await capture.PingSession(world.ApiKey, "A", "a", "steam", "steam", default);
        var pingB = await capture.PingSession(world.ApiKey, "B", "b", "steam", "steam", default);

        Assert.Equal((a!.SessionId, false, 0), (pingA!.SessionId, pingA.Started, pingA.EndedSessionCount));
        Assert.Equal((b!.SessionId, false, 0), (pingB!.SessionId, pingB.Started, pingB.EndedSessionCount));
        Assert.Equal(2, (await world.OpenSessions(world.Api)).Count);
        Assert.Equal([a.SessionId, b.SessionId], recorder.Started.Select(row => row.SessionId));
        Assert.Empty(recorder.Ended);

        var end = await capture.EndSessions(world.ApiKey, "A", "a", "steam", "steam", default);

        Assert.Equal(1, end!.EndedSessionCount);
        Assert.Equal(a.SessionId, Assert.Single(recorder.Ended).SessionId);
        Assert.Equal(b.SessionId, Assert.Single(await world.OpenSessions(world.Api)).Id);
    }

    [Fact]
    public async Task CaptureRestartsAGameStartedAgainOrPingedOnAnotherPlatform()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var recorder = new SessionEventRecorder();
        var capture = world.Capture(recorder);

        var first = await capture.StartSession(world.ApiKey, "A", "a", "steam", "steam", default);
        var second = await capture.StartSession(world.ApiKey, "A", "a", "steam", "steam", default);
        var moved = await capture.PingSession(world.ApiKey, "A", "a", "steam", "gog", default);

        Assert.Equal((true, 1), (moved!.Started, moved.EndedSessionCount));
        Assert.Equal(
            [first!.SessionId, second!.SessionId, moved.SessionId],
            recorder.Started.Select(row => row.SessionId));
        Assert.Equal([first.SessionId, second.SessionId], recorder.Ended.Select(row => row.SessionId));
        Assert.Equal(moved.SessionId, Assert.Single(await world.OpenSessions(world.Api)).Id);
    }

    [Fact]
    public async Task CapturePingEndsTimedOutSessionsAtTheirLastPingPlusTheTimeout()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var lastPing = Now().AddMinutes(-10);
        var forgotten = await world.AddOpenSession(world.Api, world.GameB, lastPing.AddHours(-1), lastPing);
        var recorder = new SessionEventRecorder();

        var ping = await world.Capture(recorder).PingSession(world.ApiKey, "A", "a", "steam", "steam", default);

        Assert.Equal((true, 1), (ping!.Started, ping.EndedSessionCount));
        Assert.Equal(lastPing + SessionLifecycle.CaptureTimeout, (await world.Session(forgotten.Id)).EndedAt);
        var ended = Assert.Single(recorder.Ended);
        Assert.Equal((forgotten.Id, lastPing + SessionLifecycle.CaptureTimeout), (ended.SessionId, ended.EndedAt));
    }

    [Fact]
    public async Task RecordedCaptureSessionIsAnnouncedAsEnded()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var recorder = new SessionEventRecorder();

        var recorded = await world.Capture(recorder)
            .RecordSession(world.ApiKey, 3600, null, "A", "a", "steam", "steam", default);

        var session = await world.Session(recorded!.SessionId!.Value);
        Assert.Equal(TimeSpan.FromHours(1), session.EndedAt - session.StartedAt);
        var ended = Assert.IsType<SessionEnded>(Assert.Single(recorder.Events));
        Assert.Equal(
            (session.Id, world.Api.Id, world.GameA.Id, TimeSpan.FromHours(1)),
            (ended.SessionId, ended.CredentialId, ended.GameId, ended.EndedAt - ended.StartedAt));
    }

    [Fact]
    public async Task ConcurrentPingsForOneGameOpenOneSession()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var world = await World.Create(CreateDb());
        var recorder = new SessionEventRecorder();
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();

        var pings = await Task.WhenAll(Enumerable.Range(0, 2).Select(index =>
            world.Capture(recorder, index == 0 ? firstDb : secondDb)
                .PingSession(world.ApiKey, "A", "a", "steam", "steam", default)));

        var open = Assert.Single(await world.OpenSessions(world.Api));
        Assert.All(pings, ping => Assert.Equal(open.Id, ping!.SessionId));
        Assert.Equal(open.Id, Assert.Single(recorder.Started).SessionId);
    }

    private static DateTimeOffset Now() =>
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

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

    /// <summary>A user with a Steam polling integration, an API integration and two games; removed on dispose.</summary>
    private sealed class World(PlaydexDbContext db, User user, Game gameA, Game gameB, Integration steam, Integration api)
        : IAsyncDisposable
    {
        public PlaydexDbContext Db => db;
        public User User => user;
        public Game GameA => gameA;
        public Game GameB => gameB;
        public Integration Steam => steam;
        public Integration Api => api;
        public string ApiKey => api.ApiKey!;

        public static async Task<World> Create(PlaydexDbContext db)
        {
            var now = DateTimeOffset.UtcNow;
            var suffix = Guid.NewGuid().ToString("n");
            var steamPlatform = await db.Platforms.SingleAsync(row => row.Key == "steam");
            var user = new User { Email = $"lifecycle-{suffix}@example.test", CreatedAt = now, UpdatedAt = now };
            var gameA = new Game { Title = $"Lifecycle A {suffix}", CreatedAt = now, UpdatedAt = now };
            var gameB = new Game { Title = $"Lifecycle B {suffix}", CreatedAt = now, UpdatedAt = now };
            db.AddRange(user, gameA, gameB);
            await db.SaveChangesAsync();
            var steam = new Integration
            {
                UserId = user.Id,
                Type = IntegrationType.Steam,
                PlatformId = steamPlatform.Id,
                ApiKey = "steam-key",
                AccountId = "steam-account",
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var api = new Integration
            {
                UserId = user.Id,
                Type = IntegrationType.Api,
                ApiKey = $"lifecycle-{suffix}",
                AccountId = "api",
                Active = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(steam, api);
            await db.SaveChangesAsync();
            return new World(db, user, gameA, gameB, steam, api);
        }

        public bool OwnsSession(long sessionId) =>
            db.PlaySessions.AsNoTracking().Any(row => row.Id == sessionId && row.UserId == user.Id);

        public async Task<PlaySession> AddOpenSession(
            Integration integration, Game game, DateTimeOffset startedAt, DateTimeOffset lastSeen)
        {
            var session = new PlaySession
            {
                UserId = user.Id,
                GameId = game.Id,
                PlatformId = integration.PlatformId ?? (await db.Platforms.SingleAsync(row => row.Key == "steam")).Id,
                CaptureMethod = integration.Type == IntegrationType.Api ? CaptureMethod.Api : CaptureMethod.Polling,
                IntegrationId = integration.Id,
                StartedAt = startedAt,
                CreatedAt = startedAt,
                UpdatedAt = lastSeen,
            };
            db.PlaySessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<PlaySession> AddManualTimer(Game game, DateTimeOffset startedAt)
        {
            var session = new PlaySession
            {
                UserId = user.Id,
                GameId = game.Id,
                PlatformId = (await db.Platforms.SingleAsync(row => row.Key == "pc")).Id,
                CaptureMethod = CaptureMethod.Manual,
                StartedAt = startedAt,
                CreatedAt = startedAt,
                UpdatedAt = startedAt,
            };
            db.PlaySessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task AddTelegramChannel()
        {
            db.NotificationChannels.Add(new NotificationChannel
            {
                UserId = user.Id,
                Transport = NotificationChannel.Telegram,
                Destination = "4242",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task Ignore(Game game)
        {
            db.IgnoredGames.Add(new IgnoredGame
            {
                UserId = user.Id,
                GameId = game.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public Task<SuccessfulPollOutcome> Poll(SessionLifecycle lifecycle, Game game, DateTimeOffset polledAt) =>
            new PollingRepository(db, new TitleGameResolver(this), lifecycle).ApplySuccessfulPoll(
                steam.Id,
                new CurrentlyPlayingResult(game.Title, game.Title, null, "steam"),
                polledAt,
                null,
                default);

        public ApiCaptureService Capture(SessionEventRecorder recorder, PlaydexDbContext? context = null)
        {
            var target = context ?? db;
            return new ApiCaptureService(
                new ApiCaptureRepository(target),
                target,
                new TitleGameResolver(this),
                TestSessions.Lifecycle(target, recorder),
                new PosterUrls(Options.Create(new PostersOptions())));
        }

        public async Task<PlaySession> Session(long id) =>
            await db.PlaySessions.AsNoTracking().SingleAsync(row => row.Id == id);

        public async Task<List<PlaySession>> OpenSessions(Integration integration) =>
            await db.PlaySessions.AsNoTracking()
                .Where(row => row.IntegrationId == integration.Id && row.EndedAt == null)
                .ToListAsync();

        public async ValueTask DisposeAsync()
        {
            db.ChangeTracker.Clear();
            await db.PlaySessions.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
            await db.PollResults.Where(row => row.IntegrationId == steam.Id).ExecuteDeleteAsync();
            await db.IgnoredGames.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
            await db.NotificationChannels.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
            await db.Integrations.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
            await db.Games.Where(row => row.Id == gameA.Id || row.Id == gameB.Id).ExecuteDeleteAsync();
            await db.Users.Where(row => row.Id == user.Id).ExecuteDeleteAsync();
            await db.DisposeAsync();
        }
    }

    /// <summary>Resolves "A"/"a" to game A and anything else to game B.</summary>
    private sealed class TitleGameResolver(World world) : IGameResolver
    {
        public Task<GameResolution> Resolve(GameResolutionRequest request, CancellationToken ct)
        {
            var game = string.Equals(request.ExternalId, "a", StringComparison.OrdinalIgnoreCase) ||
                       request.ExternalId == world.GameA.Title
                ? world.GameA
                : world.GameB;
            return Task.FromResult(new GameResolution(game.Id, null, false));
        }
    }
}
