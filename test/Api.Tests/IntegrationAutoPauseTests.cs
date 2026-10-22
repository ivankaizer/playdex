using Api.Infrastructure.Common;
using Api.Modules.Games;
using Api.Modules.Metrics;
using Api.Modules.Polling;
using Api.Modules.Providers;
using Api.Modules.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

/// <summary>An integration whose credential the provider keeps rejecting is paused instead of polled forever.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class IntegrationAutoPauseTests(PostgresFixture postgres)
{
    private const string Rejected = "PSN authorize request returned 302 without an access code. " +
        "The NPSSO token may be invalid or expired.";

    [Fact]
    public async Task RepeatedRejectionsPauseTheIntegrationOnceWithTheReason()
    {
        if (!postgres.IsConfigured) return;

        await using var scope = await Scope.Create(postgres, new ProviderPollException(Rejected, authenticationFailure: true));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);
        }

        // A poll that was already running when the integration was paused announces nothing more.
        await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);

        await using var db = scope.CreateDb();
        var integration = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == scope.IntegrationId);
        Assert.False(integration.Active);
        Assert.Equal(Rejected, integration.PausedReason);
        Assert.NotNull(integration.PausedAt);
        var paused = Assert.Single(scope.Recorder.Paused);
        Assert.Equal((scope.IntegrationId, 3, Rejected), (paused.CredentialId, paused.AuthFailures, paused.Reason));
        Assert.Single(scope.Recorder.Failed);
        Assert.DoesNotContain(
            await new PollingRepository(db, new GameResolver(db), TestSessions.Lifecycle(db)).LoadActiveCredentials(CancellationToken.None),
            row => row.Credential.Id == scope.IntegrationId);
    }

    [Fact]
    public async Task OtherFailuresNeverPause()
    {
        if (!postgres.IsConfigured) return;

        await using var scope = await Scope.Create(postgres, new ProviderPollException("PSN presence request returned 503."));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);
        }

        await using var db = scope.CreateDb();
        Assert.True(await db.Integrations.Where(row => row.Id == scope.IntegrationId).Select(row => row.Active).SingleAsync());
        Assert.Empty(scope.Recorder.Paused);
    }

    [Fact]
    public async Task ResumingClearsTheReasonAndTheBackoff()
    {
        if (!postgres.IsConfigured) return;

        await using var scope = await Scope.Create(postgres, new ProviderPollException(Rejected, authenticationFailure: true));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);
        }

        await using var db = scope.CreateDb();
        var repository = new ProvidersRepository(db, TestSessions.Lifecycle(db));
        var paused = Assert.Single(await repository.ListProviders(scope.UserId, CancellationToken.None));
        Assert.Equal(("paused", Rejected), (paused.Status, paused.PausedReason));
        Assert.NotNull(paused.PausedAt);

        var resumed = await repository.UpdateProviderStatus(scope.UserId, scope.IntegrationId, "active", CancellationToken.None);

        Assert.Equal("active", resumed!.Status);
        Assert.Null(resumed.PausedReason);
        Assert.Null(resumed.PausedAt);
        Assert.False(await db.IntegrationPollBackoffs.AnyAsync(row => row.IntegrationId == scope.IntegrationId));
    }

    [Fact]
    public async Task ANewCredentialIsPolledOnTheNextCycle()
    {
        if (!postgres.IsConfigured) return;

        await using var scope = await Scope.Create(postgres, new ProviderPollException(Rejected, authenticationFailure: true));
        await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);

        await using var db = scope.CreateDb();
        await new ProvidersRepository(db, TestSessions.Lifecycle(db)).UpdateCredential(
            scope.UserId, scope.IntegrationId, true, "new-npsso", false, null, ProvidersService.ValidateCredential,
            CancellationToken.None);

        Assert.False(await db.IntegrationPollBackoffs.AnyAsync(row => row.IntegrationId == scope.IntegrationId));
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly NpgsqlDataSource _dataSource;

        private Scope(ServiceProvider services, NpgsqlDataSource dataSource, long userId, long integrationId)
        {
            _services = services;
            _dataSource = dataSource;
            UserId = userId;
            IntegrationId = integrationId;
            Credential = new ActiveCredential(
                new IntegrationCredentials { Id = integrationId, UserId = userId, ApiKey = "npsso" }, "psn");
            Polling = new PollingBackgroundService(
                services,
                Options.Create(new PollingOptions { AuthFailuresBeforePause = 3 }),
                new MetricsCollector(),
                new PollingLease(dataSource),
                new CapsuleLookupBackoff(),
                NullLogger<PollingBackgroundService>.Instance);
        }

        public long UserId { get; }
        public long IntegrationId { get; }
        public ActiveCredential Credential { get; }
        public PollingBackgroundService Polling { get; }
        public EventRecorder Recorder => _services.GetRequiredService<EventRecorder>();

        public static async Task<Scope> Create(PostgresFixture postgres, Exception failure)
        {
            var dataSource = postgres.DataSource!;
            await using var insert = dataSource.CreateCommand($"""
                with new_user as (
                    insert into users (email) values ('auto-pause-{Guid.NewGuid():n}@example.test') returning id)
                insert into integrations (user_id, type, platform_id, api_key, account_id, active)
                select id, 'psn', (select id from platforms where key = 'psn'), 'npsso', null, true from new_user
                returning user_id, id
                """);
            await using var reader = await insert.ExecuteReaderAsync();
            await reader.ReadAsync();
            var (userId, integrationId) = (reader.GetInt64(0), reader.GetInt64(1));

            var recorder = new EventRecorder();
            var services = new ServiceCollection()
                .AddLogging()
                .AddDbContext<PlaydexDbContext>(options => options.UseNpgsql(dataSource, MapEnums))
                .AddScoped<IGameResolver, GameResolver>()
                .AddScoped<PollingRepository>()
                .AddScoped<SessionLifecycle>()
                .AddScoped<ICredentialBackoff, CredentialBackoff>()
                .AddScoped<EventBus>()
                .AddSingleton<IProviderClient>(new FailingClient(failure))
                .AddSingleton(recorder)
                .AddSingleton<IEventSubscriber<PollFailed>>(recorder)
                .AddSingleton<IEventSubscriber<IntegrationPaused>>(recorder)
                .BuildServiceProvider();
            return new Scope(services, dataSource, userId, integrationId);
        }

        public PlaydexDbContext CreateDb() => new(
            new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql(_dataSource, MapEnums).Options);

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await using var cleanup = _dataSource.CreateCommand($"""
                delete from integrations where id = {IntegrationId};
                delete from users where id = {UserId};
                """);
            await cleanup.ExecuteNonQueryAsync();
        }

        private static void MapEnums(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
        {
            npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            npgsql.MapEnum<IntegrationType>("integration_type");
            npgsql.MapEnum<CaptureMethod>("capture_method");
        }
    }

    private sealed class FailingClient(Exception failure) : IProviderClient
    {
        public string PlatformKey => "psn";

        public Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct) =>
            Task.FromException<ProviderPollResult?>(failure);
    }

    private sealed class EventRecorder : IEventSubscriber<PollFailed>, IEventSubscriber<IntegrationPaused>
    {
        public List<PollFailed> Failed { get; } = [];
        public List<IntegrationPaused> Paused { get; } = [];

        public Task Handle(PollFailed domainEvent, CancellationToken ct)
        {
            Failed.Add(domainEvent);
            return Task.CompletedTask;
        }

        public Task Handle(IntegrationPaused domainEvent, CancellationToken ct)
        {
            Paused.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
