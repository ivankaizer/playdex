using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Api.Infrastructure.Common;
using Api.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Pending notifications in <c>notification_outbox</c>. Each test runs on its own clock in the past, so it only sees
/// the rows it queued (claims take rows due by "now").
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class NotificationOutboxTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions _webJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AnEventPublishedTwiceIsQueuedOnceAndEveryKindRoundTrips()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await Scope.Create(postgres);
        var started = new SessionStarted(scope.Id(1), scope.IntegrationId, 3, scope.Clock.Now);
        var ended = new SessionEnded(scope.Id(2), scope.IntegrationId, 3, scope.Clock.Now, scope.Clock.Now.AddHours(1));
        var paused = new IntegrationPaused(
            scope.IntegrationId, "steam", "Steam rejected the Web API key (403).", 10, scope.Clock.Now);

        for (var replica = 0; replica < 2; replica++)
        {
            await scope.Publish(started);
            await scope.Publish(ended);
            await scope.Publish(paused);
        }

        var claimed = new List<object?>();
        await using var db = scope.CreateDb();
        var outbox = new NotificationOutbox(db, scope.Clock);
        while (await outbox.ClaimNext(TimeSpan.FromMinutes(5), CancellationToken.None) is { } next)
        {
            claimed.Add(NotificationOutbox.Deserialize(next));
        }

        Assert.Equal([started, ended, paused], claimed);
    }

    [Fact]
    public async Task AQueuedNotificationIsSentAfterARestartExactlyOnce()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await Scope.Create(postgres);
        await scope.Publish(new IntegrationPaused(scope.IntegrationId, "steam", "down", 10, scope.Clock.Now));

        // A new dispatcher, as after a restart: the queued notification was not in its memory.
        Assert.Equal(1, await scope.CreateDispatcher().DeliverDue(CancellationToken.None));
        Assert.Equal(0, await scope.CreateDispatcher().DeliverDue(CancellationToken.None));

        var sent = Assert.Single(scope.Apprise.Requests);
        Assert.EndsWith($"/notify/telegram_{scope.ChatId}", sent);
        await using var db = scope.CreateDb();
        var row = await scope.Rows(db).SingleAsync();
        Assert.Equal((1, scope.Clock.Now), (row.Attempts, row.DeliveredAt));
    }

    [Fact]
    public async Task TwoInstancesSendEachNotificationOnce()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await Scope.Create(postgres, appriseDelay: TimeSpan.FromMilliseconds(30));
        for (var index = 0; index < 8; index++)
        {
            await scope.Publish(new IntegrationPaused(
                scope.IntegrationId, "steam", $"down {index}", 10, scope.Clock.Now.AddTicks(index * 10)));
        }

        var handled = await Task.WhenAll(
            scope.CreateDispatcher().DeliverDue(CancellationToken.None),
            scope.CreateDispatcher().DeliverDue(CancellationToken.None));

        Assert.Equal(8, handled.Sum());
        Assert.Equal(8, scope.Apprise.Requests.Length);
        await using var db = scope.CreateDb();
        Assert.True(await scope.Rows(db).AllAsync(row => row.DeliveredAt != null && row.Attempts == 1));
    }

    [Fact]
    public async Task AFailedDeliveryIsRetriedWithBackoffThenGivenUp()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await Scope.Create(postgres, appriseStatus: HttpStatusCode.ServiceUnavailable);
        await scope.Publish(new IntegrationPaused(scope.IntegrationId, "steam", "down", 10, scope.Clock.Now));
        var dispatcher = scope.CreateDispatcher();

        Assert.True(await dispatcher.DeliverNext(CancellationToken.None));
        Assert.False(await dispatcher.DeliverNext(CancellationToken.None));
        scope.Clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(await dispatcher.DeliverNext(CancellationToken.None));
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await dispatcher.DeliverNext(CancellationToken.None));

        for (var attempt = 3; attempt <= NotificationOutbox.MaxAttempts; attempt++)
        {
            scope.Clock.Advance(TimeSpan.FromHours(1));
            Assert.True(await dispatcher.DeliverNext(CancellationToken.None));
        }

        scope.Clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await dispatcher.DeliverNext(CancellationToken.None));
        Assert.Equal(NotificationOutbox.MaxAttempts, scope.Apprise.Requests.Length);
        await using var db = scope.CreateDb();
        var row = await scope.Rows(db).SingleAsync();
        Assert.NotNull(row.FailedAt);
        Assert.Null(row.DeliveredAt);
        Assert.Contains("503", row.LastError);
    }

    [Fact]
    public async Task ANotificationClaimedByAnInstanceThatDiedIsSentOnceTheClaimExpires()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await Scope.Create(postgres);
        await scope.Publish(new IntegrationPaused(scope.IntegrationId, "steam", "down", 10, scope.Clock.Now));
        await using (var db = scope.CreateDb())
        {
            Assert.NotNull(await new NotificationOutbox(db, scope.Clock).ClaimNext(
                NotificationDispatcher.ClaimDuration, CancellationToken.None));
        }

        var dispatcher = scope.CreateDispatcher();
        Assert.False(await dispatcher.DeliverNext(CancellationToken.None));
        scope.Clock.Advance(NotificationDispatcher.ClaimDuration);
        Assert.True(await dispatcher.DeliverNext(CancellationToken.None));

        Assert.Single(scope.Apprise.Requests);
    }

    [Fact]
    public async Task ShutdownHandsAnUnsentNotificationBack()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        using var stopping = new CancellationTokenSource();
        await using var scope = await Scope.Create(postgres, onSend: stopping.Cancel);
        await scope.Publish(new IntegrationPaused(scope.IntegrationId, "steam", "down", 10, scope.Clock.Now));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.CreateDispatcher().DeliverNext(stopping.Token));

        await using var db = scope.CreateDb();
        var row = await scope.Rows(db).SingleAsync();
        Assert.Equal((0, scope.Clock.Now, (DateTimeOffset?)null), (row.Attempts, row.NextAttemptAt, row.DeliveredAt));
    }

    [Fact]
    public async Task UnknownKindsAreGivenUpAndFinishedRowsAreForgottenAfterTheRetention()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await Scope.Create(postgres);
        await using (var insert = postgres.DataSource!.CreateCommand("""
            insert into notification_outbox (dedupe_key, kind, payload, next_attempt_at, created_at)
            values (@key, 'FromANewerRelease', '{}', @now, @now)
            """))
        {
            insert.Parameters.AddWithValue("key", $"{scope.KeyPrefix}unknown");
            insert.Parameters.AddWithValue("now", scope.Clock.Now);
            await insert.ExecuteNonQueryAsync();
        }

        await scope.Publish(new IntegrationPaused(scope.IntegrationId, "steam", "down", 10, scope.Clock.Now));
        Assert.Equal(2, await scope.CreateDispatcher().DeliverDue(CancellationToken.None));
        Assert.Single(scope.Apprise.Requests);

        await using var db = scope.CreateDb();
        var outbox = new NotificationOutbox(db, scope.Clock);
        Assert.Equal(0, await outbox.DeleteFinished(scope.Clock.Now - NotificationDispatcher.Retention, CancellationToken.None));
        scope.Clock.Advance(NotificationDispatcher.Retention + TimeSpan.FromSeconds(1));
        Assert.Equal(2, await outbox.DeleteFinished(scope.Clock.Now - NotificationDispatcher.Retention, CancellationToken.None));
    }

    [Fact]
    public async Task PollFailuresAreNotQueuedAndOnesQueuedBeforeAreSkipped()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await Scope.Create(postgres);
        var failed = new PollFailed(scope.IntegrationId, "steam", "down", TimeSpan.FromSeconds(5), scope.Clock.Now);
        await using (var db = scope.CreateDb())
        {
            Assert.False(await new NotificationOutbox(db, scope.Clock).Enqueue(failed, CancellationToken.None));
        }

        // A row an earlier release queued: drained without telling the user.
        await using (var insert = postgres.DataSource!.CreateCommand("""
            insert into notification_outbox (dedupe_key, kind, payload, next_attempt_at, created_at)
            values (@key, 'PollFailed', cast(@payload as jsonb), @now, @now)
            """))
        {
            insert.Parameters.AddWithValue("key", $"{scope.KeyPrefix}poll-failed");
            insert.Parameters.AddWithValue("payload", JsonSerializer.Serialize(failed, _webJson));
            insert.Parameters.AddWithValue("now", scope.Clock.Now);
            await insert.ExecuteNonQueryAsync();
        }

        Assert.Equal(1, await scope.CreateDispatcher().DeliverDue(CancellationToken.None));
        Assert.Empty(scope.Apprise.Requests);
        await using var rows = scope.CreateDb();
        Assert.NotNull((await scope.Rows(rows).SingleAsync()).DeliveredAt);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly NpgsqlDataSource _dataSource;
        private readonly long _userId;
        private readonly long _idBase = Random.Shared.NextInt64(1L << 40, 1L << 50);

        private Scope(ServiceProvider services, NpgsqlDataSource dataSource, long userId, long integrationId, string chatId,
            ManualClock clock, RecordingApprise apprise)
        {
            _services = services;
            _dataSource = dataSource;
            _userId = userId;
            IntegrationId = integrationId;
            ChatId = chatId;
            Clock = clock;
            Apprise = apprise;
        }

        public long IntegrationId { get; }
        public string ChatId { get; }
        public ManualClock Clock { get; }
        public RecordingApprise Apprise { get; }
        public string KeyPrefix => $"test-{_idBase}-";

        public static async Task<Scope> Create(
            PostgresFixture postgres,
            HttpStatusCode appriseStatus = HttpStatusCode.OK,
            TimeSpan appriseDelay = default,
            Action? onSend = null)
        {
            var dataSource = postgres.DataSource!;
            var chatId = Random.Shared.NextInt64(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await using var insert = dataSource.CreateCommand($"""
                with new_user as (
                    insert into users (email) values ('outbox-{Guid.NewGuid():n}@example.test') returning id),
                channel as (
                    insert into notification_channels (user_id, transport, destination)
                    select id, 'telegram', '{chatId}' from new_user)
                insert into integrations (user_id, type, platform_id, api_key, account_id)
                select id, 'steam', (select id from platforms where key = 'steam'), 'key', '76561190000000077'
                from new_user
                returning user_id, id
                """);
            await using var reader = await insert.ExecuteReaderAsync();
            await reader.ReadAsync();
            var (userId, integrationId) = (reader.GetInt64(0), reader.GetInt64(1));

            // A clock years in the past, different for every test: claims only see this test's rows.
            var clock = new ManualClock(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMinutes(Random.Shared.Next(0, 5_000_000)));
            var apprise = new RecordingApprise(appriseStatus, appriseDelay, onSend);
            var services = new ServiceCollection()
                .AddLogging()
                .AddDbContext<PlaydexDbContext>(options => options.UseNpgsql(dataSource, MapEnums))
                .AddSingleton<TimeProvider>(clock)
                .AddScoped<NotificationOutbox>()
                .AddScoped<NotificationsRepository>()
                .AddSingleton(new AppriseClient(new HttpClient(apprise) { BaseAddress = new Uri("https://apprise.test/") }))
                .AddSingleton(new NotificationRenderer(new HttpClient(), new CoverUrlPolicy([])))
                .AddScoped<NotificationSender>()
                .AddSingleton<NotificationSignal>()
                .AddScoped<NotificationEventHandlers>()
                .AddScoped<IEventSubscriber<SessionStarted>>(provider => provider.GetRequiredService<NotificationEventHandlers>())
                .AddScoped<IEventSubscriber<SessionEnded>>(provider => provider.GetRequiredService<NotificationEventHandlers>())
                .AddScoped<IEventSubscriber<IntegrationPaused>>(provider => provider.GetRequiredService<NotificationEventHandlers>())
                .AddScoped<EventBus>()
                .BuildServiceProvider();
            return new Scope(services, dataSource, userId, integrationId, chatId, clock, apprise);
        }

        /// <summary>A session id no other test uses (events about it are only queued, never resolved).</summary>
        public long Id(int offset) => _idBase + offset;

        public async Task Publish<T>(T domainEvent)
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<EventBus>().Publish(domainEvent);
        }

        public NotificationDispatcher CreateDispatcher() => new(
            _services.GetRequiredService<NotificationSignal>(),
            _services.GetRequiredService<IServiceScopeFactory>(),
            Clock,
            NullLogger<NotificationDispatcher>.Instance);

        public PlaydexDbContext CreateDb() => new(
            new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql(_dataSource, MapEnums).Options);

        /// <summary>This test's rows: queued on its clock, or inserted with its key prefix.</summary>
        public IQueryable<NotificationOutboxEntry> Rows(PlaydexDbContext db)
        {
            var from = Clock.Start;
            var to = Clock.Start.AddDays(30);
            var prefix = KeyPrefix;
            return db.NotificationOutbox.AsNoTracking()
                .Where(row => (row.CreatedAt >= from && row.CreatedAt < to) || row.DedupeKey.StartsWith(prefix));
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = CreateDb())
            {
                await Rows(db).ExecuteDeleteAsync();
            }

            await _services.DisposeAsync();
            await using var cleanup = _dataSource.CreateCommand($"""
                delete from integrations where id = {IntegrationId};
                delete from users where id = {_userId};
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

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Start { get; } = start;
        public DateTimeOffset Now { get; private set; } = start;

        public void Advance(TimeSpan by) => Now += by;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingApprise(HttpStatusCode status, TimeSpan delay, Action? onSend) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public string[] Requests => _requests.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Enqueue(request.RequestUri!.ToString());
            onSend?.Invoke();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }
}
