using Api.Infrastructure.Common;
using Api.Modules.Metrics;
using Api.Modules.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    private static ServiceProvider CreateServices(IProviderClient client, RecordingPollFailedHandler recorder) =>
        new ServiceCollection()
            .AddLogging()
            .AddScoped<EventBus>()
            .AddSingleton(client)
            .AddSingleton<IEventSubscriber<PollFailed>>(recorder)
            .BuildServiceProvider();

    private static PollingBackgroundService CreatePolling(IServiceProvider services, MetricsCollector metrics) => new(
        services,
        Options.Create(new PollingOptions()),
        metrics,
        NullLogger<PollingBackgroundService>.Instance);

    private sealed class ThrowingClient(Exception exception) : IProviderClient
    {
        public string PlatformKey => "steam";

        public Task<ProviderPollResult?> GetCurrentlyPlaying(IntegrationCredentials credential, CancellationToken ct) =>
            Task.FromException<ProviderPollResult?>(exception);
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
