using Api.Infrastructure.Common;
using Api.Modules.Metrics;
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
        var metrics = new MetricsCollector();
        await using var services = CreateServices(new ThrowingClient(new ProviderPollException("token expired")), recorder);
        var polling = CreatePolling(services, metrics);
        var active = new ActiveCredential(new IntegrationCredentials { Id = 42 }, "steam");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await polling.PollCredential(active, CancellationToken.None);
        }

        var notification = Assert.Single(recorder.Events);
        Assert.Equal(42, notification.CredentialId);
        Assert.Equal("token expired", notification.Message);
        Assert.Equal(3, metrics.Snapshot().PollFailureTotal);
    }

    [Fact]
    public async Task ShutdownDuringAPollIsNotRecordedAsAFailure()
    {
        using var stopping = new CancellationTokenSource();
        var recorder = new RecordingPollFailedHandler();
        var metrics = new MetricsCollector();
        await using var services = CreateServices(new CancellingClient(stopping), recorder);
        var polling = CreatePolling(services, metrics);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => polling.PollCredential(
            new ActiveCredential(new IntegrationCredentials { Id = 7 }, "steam"), stopping.Token));

        Assert.Empty(recorder.Events);
        Assert.Equal(0, metrics.Snapshot().PollFailureTotal);
    }

    [Fact]
    public async Task CredentialsArePolledInParallelUpToTheLimit()
    {
        var client = new SlowFailingClient(TimeSpan.FromMilliseconds(150));
        await using var services = CreateServices(client, new RecordingPollFailedHandler());
        var polling = CreatePolling(services, new MetricsCollector(), maxConcurrentPolls: 3);
        var due = Enumerable.Range(1, 7)
            .Select(id => new ActiveCredential(new IntegrationCredentials { Id = id }, "steam"))
            .ToArray();

        await polling.PollAll(due, CancellationToken.None);

        Assert.Equal(7, client.Calls);
        Assert.Equal(3, client.MaxInFlight);
    }

    [Fact]
    public void BackoffTrackerIsSafeToUseFromParallelPolls()
    {
        var tracker = new CredentialBackoffTracker();
        var now = DateTimeOffset.UnixEpoch;

        Parallel.For(0, 2_000, index =>
        {
            var id = index % 50;
            tracker.RecordFailure(id, now);
            tracker.IsEligible(id, now, out _);
            if (index % 7 == 0) tracker.Reset(id);
        });

        tracker.Prune([]);
        Assert.True(tracker.IsEligible(1, now, out _));
    }

    private static ServiceProvider CreateServices(IProviderClient client, RecordingPollFailedHandler recorder) =>
        new ServiceCollection()
            .AddLogging()
            .AddScoped<EventBus>()
            .AddSingleton(client)
            .AddSingleton<IEventSubscriber<PollFailed>>(recorder)
            .BuildServiceProvider();

    private static PollingBackgroundService CreatePolling(
        IServiceProvider services,
        MetricsCollector metrics,
        int maxConcurrentPolls = 4) => new(
        services,
        Options.Create(new PollingOptions { MaxConcurrentPolls = maxConcurrentPolls }),
        metrics,
        new PollingLease(NpgsqlDataSource.Create("Host=unused")),
        new CapsuleLookupBackoff(),
        NullLogger<PollingBackgroundService>.Instance);

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
}
