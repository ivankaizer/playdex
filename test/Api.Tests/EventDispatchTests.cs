using Api.Infrastructure.Common;
using Api.Modules.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task NotificationsAreQueuedNotSentInline()
    {
        var apprise = new CountingHandler();
        var pending = new PendingNotifications(NullLogger<PendingNotifications>.Instance);
        var handlers = new NotificationEventHandlers(
            new AppriseClient(new HttpClient(apprise) { BaseAddress = new Uri("https://apprise.test/") }),
            pending);

        await handlers.Handle(Started, CancellationToken.None);

        Assert.Equal(0, apprise.Calls);
        Assert.True(pending.Reader.TryRead(out var queued));
        Assert.Equal(Started, queued);
    }

    [Fact]
    public async Task NothingIsQueuedWhenAppriseIsNotConfigured()
    {
        var pending = new PendingNotifications(NullLogger<PendingNotifications>.Instance);
        var handlers = new NotificationEventHandlers(new AppriseClient(new HttpClient()), pending);

        await handlers.Handle(Started, CancellationToken.None);

        Assert.False(pending.Reader.TryRead(out _));
    }

    [Fact]
    public void AFullQueueDropsInsteadOfBlocking()
    {
        var pending = new PendingNotifications(NullLogger<PendingNotifications>.Instance);

        for (var index = 0; index <= PendingNotifications.Capacity; index++)
        {
            pending.Enqueue(Started);
        }

        var count = 0;
        while (pending.Reader.TryRead(out _)) count++;
        Assert.Equal(PendingNotifications.Capacity, count);
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

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
