using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Api.Infrastructure.Common;
using Shared.Persistence;
using Api.Modules.ApiCapture;
using Api.Modules.Games;
using Api.Modules.Polling;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PollingSessionTimeoutTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PollCycleFinalizesTimedOutApiSessions()
    {
        var cleaner = new RecordingTimeoutCleaner(endedCount: 2);
        var services = new ServiceCollection()
            .AddScoped<IApiSessionTimeoutCleaner>(_ => cleaner)
            .BuildServiceProvider();

        var endedCount = await PollingBackgroundService.EndTimedOutApiSessions(
            services,
            CancellationToken.None);

        Assert.Equal(2, endedCount);
        Assert.Equal(1, cleaner.CallCount);
    }

    [Fact]
    public async Task CleanupEndsOnlyExpiredApiSessionsAndPublishesTheirEffectiveEndTime()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("n");
        var platform = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User
        {
            Email = $"session-timeout-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var game = new Game
        {
            Title = "Timeout Test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(user, game);
        await db.SaveChangesAsync();

        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Api,
            ApiKey = $"timeout-{suffix}",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        var expiredLastPing = now.AddMinutes(-5);
        var expired = new PlaySession
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = platform.Id,
            CaptureMethod = CaptureMethod.Api,
            IntegrationId = integration.Id,
            StartedAt = now.AddMinutes(-10),
            CreatedAt = now.AddMinutes(-10),
            UpdatedAt = expiredLastPing,
        };
        var current = new PlaySession
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = platform.Id,
            CaptureMethod = CaptureMethod.Api,
            IntegrationId = integration.Id,
            StartedAt = now.AddMinutes(-2),
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-1),
        };
        db.AddRange(expired, current);
        await db.SaveChangesAsync();

        try
        {
            var recorder = new RecordingSessionEndedHandler();
            await using var eventServices = new ServiceCollection()
                .AddSingleton(recorder)
                .AddScoped<IEventSubscriber<SessionEnded>>(services =>
                    services.GetRequiredService<RecordingSessionEndedHandler>())
                .BuildServiceProvider();
            var eventBus = new EventBus(
                eventServices.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<EventBus>.Instance);
            var service = new ApiCaptureService(
                new ApiCaptureRepository(db),
                db,
                new UnusedGameResolver(),
                eventBus,
                NullLogger<ApiCaptureService>.Instance);

            var endedCount = await service.EndTimedOutSessions(CancellationToken.None);

            db.ChangeTracker.Clear();
            var savedExpired = await db.PlaySessions.SingleAsync(row => row.Id == expired.Id);
            var savedCurrent = await db.PlaySessions.SingleAsync(row => row.Id == current.Id);
            var endedEvent = Assert.Single(recorder.Events);
            Assert.Equal(1, endedCount);
            Assert.Equal(expiredLastPing.AddMinutes(3), savedExpired.EndedAt);
            Assert.Null(savedCurrent.EndedAt);
            Assert.Equal(expired.Id, endedEvent.SessionId);
            Assert.Equal(savedExpired.EndedAt, endedEvent.EndedAt);
        }
        finally
        {
            db.PlaySessions.RemoveRange(
                await db.PlaySessions.Where(row => row.IntegrationId == integration.Id).ToListAsync());
            db.Integrations.Remove(integration);
            db.Users.Remove(user);
            db.Games.Remove(game);
            await db.SaveChangesAsync();
        }
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

    private sealed class RecordingTimeoutCleaner(int endedCount) : IApiSessionTimeoutCleaner
    {
        public int CallCount { get; private set; }

        public Task<int> EndTimedOutSessions(CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(endedCount);
        }
    }

    private sealed class RecordingSessionEndedHandler : IEventSubscriber<SessionEnded>
    {
        public List<SessionEnded> Events { get; } = [];

        public Task Handle(SessionEnded domainEvent, CancellationToken ct)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedGameResolver : IGameResolver
    {
        public Task<GameResolution> Resolve(GameResolutionRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
