using System.Globalization;
using Api.Infrastructure.Common;
using Api.Modules.Games;
using Api.Modules.Metrics;
using Api.Modules.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

/// <summary>The poll failure backoff in <c>integration_poll_backoff</c>, shared by every API instance.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class CredentialBackoffTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-06T12:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>5 s doubling per failure, capped at 10 minutes.</summary>
    private static readonly int[] ExpectedDelaySeconds = [5, 10, 20, 40, 80, 160, 320, 600, 600];

    [Fact]
    public async Task DelaysDoubleUpToTheCapAndHonourRetryAfter()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var credentials = await Credentials.Create(postgres, count: 2);
        await using var db = CreateDb();
        var backoff = new CredentialBackoff(db);
        var first = credentials.Ids[0];

        var delays = new List<TimeSpan>();
        for (var attempt = 0; attempt < 9; attempt++)
        {
            delays.Add((await backoff.RecordFailure(first, Now, retryAfter: null, false, CancellationToken.None)).Delay);
        }

        Assert.Equal(ExpectedDelaySeconds.Select(seconds => TimeSpan.FromSeconds(seconds)), delays);
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            (await backoff.RecordFailure(credentials.Ids[1], Now, TimeSpan.FromMinutes(2), false, CancellationToken.None))
            .Delay);
    }

    [Fact]
    public async Task OnlyTheFailureThatEndsAHealthyStreakIsFirst()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var credentials = await Credentials.Create(postgres, count: 1);
        await using var db = CreateDb();
        var backoff = new CredentialBackoff(db);
        var id = credentials.Ids[0];

        var first = await backoff.RecordFailure(id, Now, null, authenticationFailure: true, CancellationToken.None);
        var second = await backoff.RecordFailure(id, Now.AddMinutes(1), null, false, CancellationToken.None);
        var third = await backoff.RecordFailure(id, Now.AddMinutes(2), null, true, CancellationToken.None);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
        Assert.False(third.IsFirst);
        Assert.Equal((3, 2), (third.Failures, third.AuthFailures));
        Assert.Equal(Now, third.FailingSince);

        await backoff.Reset(id, CancellationToken.None);

        var afterReset = await backoff.RecordFailure(id, Now.AddHours(1), null, false, CancellationToken.None);
        Assert.True(afterReset.IsFirst);
        Assert.Equal(Now.AddHours(1), afterReset.FailingSince);
    }

    [Fact]
    public async Task ACredentialInBackoffIsLoadedWithItsNextPollAndDeferringCountsNoFailure()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var credentials = await Credentials.Create(postgres, count: 2);
        await using var db = CreateDb();
        var backoff = new CredentialBackoff(db);
        var (failing, deferred) = (credentials.Ids[0], credentials.Ids[1]);
        var until = DateTimeOffset.UtcNow.AddMinutes(10);

        var failure = await backoff.RecordFailure(failing, DateTimeOffset.UtcNow, null, false, CancellationToken.None);
        await backoff.Defer(deferred, until, CancellationToken.None);
        var loaded = (await new PollingRepository(db, new UnusedGameResolver(), TestSessions.Lifecycle(db)).LoadActiveCredentials(CancellationToken.None))
            .Where(row => credentials.Ids.Contains(row.Credential.Id))
            .ToDictionary(row => row.Credential.Id, row => row.NextPollAt);

        Assert.NotNull(loaded[failing]);
        Assert.InRange(loaded[failing]!.Value - DateTimeOffset.UtcNow, TimeSpan.Zero, failure.Delay);
        Assert.Equal(until.ToUnixTimeMilliseconds(), loaded[deferred]!.Value.ToUnixTimeMilliseconds());
        Assert.True((await backoff.RecordFailure(deferred, DateTimeOffset.UtcNow, null, false, CancellationToken.None))
            .IsFirst);
    }

    [Fact]
    public async Task InstancesShareOneCountSoOnlyOneSeesTheFirstFailure()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var credentials = await Credentials.Create(postgres, count: 1);
        var id = credentials.Ids[0];

        var failures = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = CreateDb();
            return await new CredentialBackoff(db).RecordFailure(id, Now, null, false, CancellationToken.None);
        }));

        Assert.Single(failures, failure => failure.IsFirst);
        Assert.Equal(Enumerable.Range(1, 12), failures.Select(failure => failure.Failures).Order());
    }

    [Fact]
    public async Task AFailingCredentialNotifiesOnceAcrossInstances()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var credentials = await Credentials.Create(postgres, count: 1);
        var recorder = new RecordingPollFailedHandler();
        await using var firstReplica = CreateServices(recorder);
        await using var secondReplica = CreateServices(recorder);
        var replicas = new[] { CreatePolling(firstReplica), CreatePolling(secondReplica) };
        var active = new ActiveCredential(new IntegrationCredentials { Id = credentials.Ids[0] }, "steam");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await replicas[attempt % 2].PollCredential(active, CancellationToken.None);
        }

        var notification = Assert.Single(recorder.Events);
        Assert.Equal(credentials.Ids[0], notification.CredentialId);
        await using var db = CreateDb();
        Assert.Equal(4, await db.IntegrationPollBackoffs
            .Where(row => row.IntegrationId == credentials.Ids[0])
            .Select(row => row.Failures)
            .SingleAsync());
    }

    private ServiceProvider CreateServices(RecordingPollFailedHandler recorder) =>
        new ServiceCollection()
            .AddLogging()
            .AddDbContext<PlaydexDbContext>(options => options.UseNpgsql(postgres.DataSource!, MapEnums))
            .AddScoped<ICredentialBackoff, CredentialBackoff>()
            .AddScoped<EventBus>()
            .AddSingleton<IProviderClient>(new FailingClient())
            .AddSingleton<IEventSubscriber<PollFailed>>(recorder)
            .BuildServiceProvider();

    private PollingBackgroundService CreatePolling(IServiceProvider services) => new(
        services,
        Options.Create(new PollingOptions()),
        new MetricsCollector(),
        new PollingLease(postgres.DataSource!),
        new CapsuleLookupBackoff(),
        NullLogger<PollingBackgroundService>.Instance);

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, MapEnums)
            .Options);

    private static void MapEnums(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
        npgsql.MapEnum<IntegrationType>("integration_type");
        npgsql.MapEnum<CaptureMethod>("capture_method");
    }

    /// <summary>Steam integrations of a new user, deleted (with their backoff) afterwards.</summary>
    private sealed class Credentials(NpgsqlDataSource dataSource, long userId, long[] ids) : IAsyncDisposable
    {
        public long[] Ids { get; } = ids;

        public static async Task<Credentials> Create(PostgresFixture postgres, int count)
        {
            var dataSource = postgres.DataSource!;
            await using var command = dataSource.CreateCommand(
                $"insert into users (email) values ('backoff-{Guid.NewGuid():n}@example.test') returning id");
            var userId = (long)(await command.ExecuteScalarAsync())!;
            var ids = new long[count];
            for (var index = 0; index < count; index++)
            {
                await using var insert = dataSource.CreateCommand($"""
                    insert into integrations (user_id, type, platform_id, api_key, account_id)
                    values ({userId}, 'steam', (select id from platforms where key = 'steam'), 'key', '7656119{index:D10}')
                    returning id
                    """);
                ids[index] = (long)(await insert.ExecuteScalarAsync())!;
            }

            return new Credentials(dataSource, userId, ids);
        }

        public async ValueTask DisposeAsync()
        {
            await using var cleanup = dataSource.CreateCommand($"""
                delete from integrations where user_id = {userId};
                delete from users where id = {userId};
                """);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class FailingClient : IProviderClient
    {
        public string PlatformKey => "steam";

        public Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct) =>
            Task.FromException<ProviderPollResult?>(new ProviderPollException("Steam is down."));
    }

    private sealed class UnusedGameResolver : IGameResolver
    {
        public Task<GameResolution> Resolve(GameResolutionRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPollFailedHandler : IEventSubscriber<PollFailed>
    {
        private readonly Lock _gate = new();
        private readonly List<PollFailed> _events = [];

        public IReadOnlyList<PollFailed> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToArray();
                }
            }
        }

        public Task Handle(PollFailed domainEvent, CancellationToken ct)
        {
            lock (_gate)
            {
                _events.Add(domainEvent);
            }

            return Task.CompletedTask;
        }
    }
}
