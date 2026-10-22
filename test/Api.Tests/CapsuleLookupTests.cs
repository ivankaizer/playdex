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

[Collection(PostgresCollectionDefinition.Name)]
public sealed class CapsuleLookupTests(PostgresFixture postgres)
{
    [Fact]
    public void MissesAndFailuresAreRetriedLater()
    {
        var lookups = new CapsuleLookupBackoff();
        var now = DateTimeOffset.UnixEpoch;

        lookups.RecordMiss(1, now);
        lookups.RecordFailure(2, now, retryAfter: null);
        lookups.RecordFailure(3, now, retryAfter: TimeSpan.FromHours(2));

        Assert.False(lookups.IsDue(1, now + CapsuleLookupBackoff.MissRetry - TimeSpan.FromSeconds(1)));
        Assert.True(lookups.IsDue(1, now + CapsuleLookupBackoff.MissRetry));
        Assert.True(lookups.IsDue(2, now + CapsuleLookupBackoff.FailureRetry));
        Assert.False(lookups.IsDue(3, now + TimeSpan.FromHours(1)));
        Assert.True(lookups.IsDue(4, now));

        lookups.Forget(1);
        Assert.True(lookups.IsDue(1, now));
    }

    [Fact]
    public async Task AGameWithoutACapsuleIsLookedUpOnceNotOnEveryPoll()
    {
        if (!postgres.IsConfigured) return;

        var client = new XboxClient(_ => Task.FromResult<string?>(null));
        await using var scope = await Scope.Create(postgres, client);

        await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);
        await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);
        await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);

        Assert.Equal(3, client.PresenceCalls);
        Assert.Equal(1, client.CapsuleCalls);
    }

    [Fact]
    public async Task ARateLimitedLookupHoldsTheCredentialBackWithoutCountingAFailure()
    {
        if (!postgres.IsConfigured) return;

        var client = new XboxClient(_ => throw new ProviderPollException("rate limited", TimeSpan.FromMinutes(10)));
        await using var scope = await Scope.Create(postgres, client);
        var before = DateTimeOffset.UtcNow;

        await scope.Polling.PollCredential(scope.Credential, CancellationToken.None);

        Assert.False(scope.Polling.Backoff.IsEligible(scope.Credential.Credential.Id, before.AddMinutes(9), out _));
        Assert.True(scope.Polling.Backoff.IsEligible(scope.Credential.Credential.Id, before.AddMinutes(11), out _));
        Assert.True(scope.Polling.Backoff.RecordFailure(scope.Credential.Credential.Id, before).IsFirst);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly NpgsqlDataSource _dataSource;
        private readonly long _userId;
        private readonly long _integrationId;
        private readonly string _titleId;

        private Scope(
            ServiceProvider services,
            NpgsqlDataSource dataSource,
            long userId,
            long integrationId,
            string titleId,
            PollingBackgroundService polling)
        {
            _services = services;
            _dataSource = dataSource;
            _userId = userId;
            _integrationId = integrationId;
            _titleId = titleId;
            Polling = polling;
            Credential = new ActiveCredential(
                new IntegrationCredentials { Id = integrationId, UserId = userId, ApiKey = "key" }, "xbox");
        }

        public PollingBackgroundService Polling { get; }
        public ActiveCredential Credential { get; }

        public static async Task<Scope> Create(PostgresFixture postgres, XboxClient client)
        {
            var dataSource = postgres.DataSource!;
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var insert = new NpgsqlCommand($"""
                with new_user as (
                    insert into users (email) values ('capsule-{Guid.NewGuid():n}@example.test') returning id)
                insert into integrations (user_id, type, platform_id, api_key, account_id)
                select id, 'xbox', (select id from platforms where key = 'xbox'), 'key', null from new_user
                returning user_id, id
                """, connection);
            await using var reader = await insert.ExecuteReaderAsync();
            await reader.ReadAsync();
            var userId = reader.GetInt64(0);
            var integrationId = reader.GetInt64(1);

            var services = new ServiceCollection()
                .AddLogging()
                .AddDbContext<PlaydexDbContext>(options => options.UseNpgsql(dataSource, npgsql =>
                {
                    npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                    npgsql.MapEnum<IntegrationType>("integration_type");
                    npgsql.MapEnum<CaptureMethod>("capture_method");
                }))
                .AddScoped<IGameResolver, GameResolver>()
                .AddScoped<PollingRepository>()
                .AddScoped<EventBus>()
                .AddSingleton<IProviderClient>(client)
                .BuildServiceProvider();
            var polling = new PollingBackgroundService(
                services,
                Options.Create(new PollingOptions()),
                new MetricsCollector(),
                new PollingLease(dataSource),
                new CapsuleLookupBackoff(),
                NullLogger<PollingBackgroundService>.Instance);
            return new Scope(services, dataSource, userId, integrationId, client.TitleId, polling);
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await using var cleanup = _dataSource.CreateCommand($"""
                delete from poll_results where integration_id = {_integrationId};
                delete from play_sessions where integration_id = {_integrationId};
                delete from integrations where id = {_integrationId};
                delete from users where id = {_userId};
                with identity as (
                    delete from game_external_identities where type = 'xbox' and external_id = '{_titleId}'
                    returning game_id)
                delete from games where id in (select game_id from identity);
                """);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class XboxClient(Func<string, Task<string?>> capsule) : IProviderClient, IProviderGameMetadataClient
    {
        // Unique per test run so the shared game starts without a capsule.
        public string TitleId { get; } = $"capsule-test-{Guid.NewGuid():n}";
        public int PresenceCalls { get; private set; }
        public int CapsuleCalls { get; private set; }
        public string PlatformKey => "xbox";

        public Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct)
        {
            PresenceCalls++;
            return Task.FromResult<ProviderPollResult?>(new ProviderPollResult(
                new CurrentlyPlayingResult("Capsule Test", TitleId, CapsuleUrl: null, "xbox"), "{}"));
        }

        public Task<string?> GetCapsuleUrl(IntegrationCredentials credential, string externalId, CancellationToken ct)
        {
            CapsuleCalls++;
            return capsule(externalId);
        }
    }
}
