using System.Threading.Channels;
using Api.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace Api.Modules.Notifications;

/// <summary>
/// Queues notification work and returns at once: delivery (cover download, up to three Apprise calls) used to run
/// inline and held up the wrapper's HTTP request or the polling loop that raised the event.
/// </summary>
public sealed class NotificationEventHandlers(AppriseClient client, PendingNotifications pending) :
    IEventSubscriber<SessionStarted>,
    IEventSubscriber<SessionEnded>,
    IEventSubscriber<PollFailed>,
    IEventSubscriber<IntegrationPaused>
{
    public Task Handle(SessionStarted domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    public Task Handle(SessionEnded domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    public Task Handle(PollFailed domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    public Task Handle(IntegrationPaused domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    private Task Enqueue(object domainEvent)
    {
        if (client.IsEnabled)
        {
            pending.Enqueue(domainEvent);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Bounded, in-memory: notifications are best effort and a full queue drops new ones rather than block.</summary>
public sealed partial class PendingNotifications(ILogger<PendingNotifications> logger)
{
    public const int Capacity = 256;

    private readonly Channel<object> _channel = Channel.CreateBounded<object>(
        new BoundedChannelOptions(Capacity) { SingleReader = true });

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification queue is full; dropped {EventType}")]
    private partial void LogDropped(string eventType);

    public ChannelReader<object> Reader => _channel.Reader;

    public void Enqueue(object domainEvent)
    {
        if (!_channel.Writer.TryWrite(domainEvent))
        {
            LogDropped(domainEvent.GetType().Name);
        }
    }
}

/// <summary>Sends queued notifications one at a time, in order, each in its own scope.</summary>
public sealed partial class NotificationDispatcher(
    PendingNotifications pending,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the {EventType} notification failed")]
    private partial void LogSendFailed(Exception exception, string eventType);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var domainEvent in pending.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NotificationSender>().Send(domainEvent, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogSendFailed(exception, domainEvent.GetType().Name);
            }
        }
    }
}
