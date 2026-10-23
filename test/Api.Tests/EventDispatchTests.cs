using Api.Infrastructure.Common;
using Api.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class EventDispatchTests
{
    private static readonly SessionStarted Started = new(1, 2, 3, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task AFailingHandlerDoesNotStopTheOthers()
    {
        var recorder = new RecordingHandler();
        await using var services = new ServiceCollection()
            .AddSingleton<IEventSubscriber<SessionStarted>>(new FailingHandler())
            .AddSingleton<IEventSubscriber<SessionStarted>>(recorder)
            .BuildServiceProvider();
        var bus = new EventBus(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<EventBus>.Instance);

        await bus.Publish(Started);

        Assert.Equal([Started], recorder.Events);
    }

    [Fact]
    public async Task CancellationStillPropagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await using var services = new ServiceCollection()
            .AddSingleton<IEventSubscriber<SessionStarted>>(new CancellationObservingHandler())
            .BuildServiceProvider();
        var bus = new EventBus(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<EventBus>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bus.Publish(Started, cancelled.Token));
    }

    [Fact]
    public async Task NothingIsQueuedWhenAppriseIsNotConfigured()
    {
        // The outbox would fail on first use: there is no database behind it.
        await using var db = new PlaydexDbContext(
            new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql("Host=unused.invalid").Options);
        using var signal = new NotificationSignal();
        var handlers = new NotificationEventHandlers(
            new AppriseClient(new HttpClient()),
            new NotificationOutbox(db, TimeProvider.System),
            signal);

        await handlers.Handle(Started, CancellationToken.None);

        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task ASignalWakesOneWaitAndRepeatedSignalsCollapse()
    {
        using var signal = new NotificationSignal();

        signal.Notify();
        signal.Notify();

        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    private sealed class FailingHandler : IEventSubscriber<SessionStarted>
    {
        public Task Handle(SessionStarted domainEvent, CancellationToken ct) =>
            throw new InvalidOperationException("handler bug");
    }

    private sealed class CancellationObservingHandler : IEventSubscriber<SessionStarted>
    {
        public Task Handle(SessionStarted domainEvent, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : IEventSubscriber<SessionStarted>
    {
        public List<SessionStarted> Events { get; } = [];

        public Task Handle(SessionStarted domainEvent, CancellationToken ct)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
