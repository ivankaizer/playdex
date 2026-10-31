using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Api.Infrastructure.Common;
using Api.Modules.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Api.Tests;

public sealed class PollingBackgroundServiceTests
{
    [Fact]
    public async Task RepeatedFailuresOfAWorkingCredentialNotifyOnce()
    {
        var recorder = new RecordingPollFailedHandler();
        using var polls = new PollCounter(42);
        await using var services = CreateServices(new ThrowingClient(new ProviderPollException("token expired")), recorder);
        var polling = CreatePolling(services);
        var active = new ActiveCredential(new IntegrationCredentials { Id = 42 }, "steam");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await polling.PollCredential(active, CancellationToken.None);
        }

        var notification = Assert.Single(recorder.Events);
        Assert.Equal(42, notification.CredentialId);
        Assert.Equal("token expired", notification.Message);
        Assert.Equal(3, polls.Failures);
    }

    [Fact]
    public async Task ShutdownDuringAPollIsNotRecordedAsAFailure()
    {
        using var stopping = new CancellationTokenSource();
        var recorder = new RecordingPollFailedHandler();
        using var polls = new PollCounter(7);
        await using var services = CreateServices(new CancellingClient(stopping), recorder);
        var polling = CreatePolling(services);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => polling.PollCredential(
            new ActiveCredential(new IntegrationCredentials { Id = 7 }, "steam"), stopping.Token));

        Assert.Empty(recorder.Events);
        Assert.Equal(0, polls.Failures);
    }

    [Fact]
    public async Task CredentialsArePolledInParallelUpToTheLimit()
    {
        var client = new SlowFailingClient(TimeSpan.FromMilliseconds(150));
        await using var services = CreateServices(client, new RecordingPollFailedHandler());
        var polling = CreatePolling(services, maxConcurrentPolls: 3);
        var due = Enumerable.Range(1, 7)
            .Select(id => new ActiveCredential(new IntegrationCredentials { Id = id }, "steam"))
            .ToArray();

        await polling.PollAll(due, CancellationToken.None);

        Assert.Equal(7, client.Calls);
        Assert.Equal(3, client.MaxInFlight);
    }

    [Fact]
    public async Task AFailureWhileRecordingTheBackoffDoesNotEscape()
    {
        var recorder = new RecordingPollFailedHandler();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<EventBus>()
            .AddSingleton<IProviderClient>(new ThrowingClient(new ProviderPollException("down")))
            .AddSingleton<ICredentialBackoff>(new UnavailableBackoff())
            .AddSingleton<IEventSubscriber<PollFailed>>(recorder)
            .BuildServiceProvider();
        using var polls = new PollCounter(3);

        await CreatePolling(services).PollCredential(
            new ActiveCredential(new IntegrationCredentials { Id = 3 }, "steam"), CancellationToken.None);

        Assert.Empty(recorder.Events);
        Assert.Equal(1, polls.Failures);
    }

    private static ServiceProvider CreateServices(IProviderClient client, RecordingPollFailedHandler recorder) =>
        new ServiceCollection()
            .AddLogging()
            .AddScoped<EventBus>()
            .AddSingleton(client)
            .AddSingleton<ICredentialBackoff>(new InMemoryBackoff())
            .AddSingleton<IEventSubscriber<PollFailed>>(recorder)
            .BuildServiceProvider();

    private static PollingBackgroundService CreatePolling(
        IServiceProvider services,
        int maxConcurrentPolls = 4) => new(
        services,
        Options.Create(new PollingOptions { MaxConcurrentPolls = maxConcurrentPolls }),
        new IntegrationHealthGauges(TimeProvider.System, Options.Create(new PollingOptions())),
        new PollingLease(NpgsqlDataSource.Create("Host=unused")),
        new CapsuleLookupBackoff(),
        NullLogger<PollingBackgroundService>.Instance);

    /// <summary>Counts the <c>playdex.polls</c> measurements of one integration that failed.</summary>
    private sealed class PollCounter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _failures;

        public PollCounter(long integrationId)
        {
            var id = integrationId.ToString(CultureInfo.InvariantCulture);
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PollingMetrics.MeterName && instrument.Name == "playdex.polls")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? integration = null;
                string? outcome = null;
                foreach (var tag in tags)
                {
                    switch (tag.Key)
                    {
                        case "integration_id":
                            integration = tag.Value as string;
                            break;
                        case "outcome":
                            outcome = tag.Value as string;
                            break;
                    }
                }

                if (integration == id && outcome is "failure" or "auth_failure")
                {
                    Interlocked.Add(ref _failures, value);
                }
            });
            _listener.Start();
        }

        public long Failures => Interlocked.Read(ref _failures);

        public void Dispose() => _listener.Dispose();
    }

    private sealed class ThrowingClient(Exception exception) : IProviderClient
    {
        public string PlatformKey => "steam";

        public Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct) =>
            Task.FromException<ProviderPollResult?>(exception);
    }

    private sealed class SlowFailingClient(TimeSpan duration) : IProviderClient
    {
        private int _calls;
        private int _inFlight;
        private int _maxInFlight;

        public string PlatformKey => "steam";
        public int Calls => _calls;
        public int MaxInFlight => _maxInFlight;

        public async Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            var inFlight = Interlocked.Increment(ref _inFlight);
            int observed;
            while (inFlight > (observed = _maxInFlight) &&
                   Interlocked.CompareExchange(ref _maxInFlight, inFlight, observed) != observed)
            {
            }

            await Task.Delay(duration, ct);
            Interlocked.Decrement(ref _inFlight);
            throw new ProviderPollException("unavailable");
        }
    }

    private sealed class CancellingClient(CancellationTokenSource stopping) : IProviderClient
    {
        public string PlatformKey => "steam";

        public async Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct)
        {
            await stopping.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    private sealed class RecordingPollFailedHandler : IEventSubscriber<PollFailed>
    {
        public List<PollFailed> Events { get; } = [];

        public Task Handle(PollFailed domainEvent, CancellationToken ct)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>Counts failures like the database does; CredentialBackoffTests covers the real one.</summary>
    private sealed class InMemoryBackoff : ICredentialBackoff
    {
        private readonly ConcurrentDictionary<long, int> _failures = new();

        public Task<CredentialFailure> RecordFailure(
            long credentialId,
            DateTimeOffset now,
            TimeSpan? retryAfter,
            bool authenticationFailure,
            CancellationToken ct)
        {
            var failures = _failures.AddOrUpdate(credentialId, 1, (_, count) => count + 1);
            return Task.FromResult(new CredentialFailure(TimeSpan.FromSeconds(5), failures, 0, now));
        }

        public Task Defer(long credentialId, DateTimeOffset until, CancellationToken ct) => Task.CompletedTask;

        public Task Reset(long credentialId, CancellationToken ct)
        {
            _failures.TryRemove(credentialId, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableBackoff : ICredentialBackoff
    {
        public Task<CredentialFailure> RecordFailure(
            long credentialId,
            DateTimeOffset now,
            TimeSpan? retryAfter,
            bool authenticationFailure,
            CancellationToken ct) =>
            Task.FromException<CredentialFailure>(new NpgsqlException("database is down"));

        public Task Defer(long credentialId, DateTimeOffset until, CancellationToken ct) => Task.CompletedTask;

        public Task Reset(long credentialId, CancellationToken ct) => Task.CompletedTask;
    }
}
