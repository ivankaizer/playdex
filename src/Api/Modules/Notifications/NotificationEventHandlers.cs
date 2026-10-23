using Api.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace Api.Modules.Notifications;

/// <summary>
/// Queues notification work in the <see cref="NotificationOutbox"/> and returns at once: delivery (cover download, up
/// to three Apprise calls) used to run inline and held up the wrapper's HTTP request or the polling loop that raised
/// the event.
/// </summary>
public sealed class NotificationEventHandlers(AppriseClient client, NotificationOutbox outbox, NotificationSignal signal) :
    IEventSubscriber<SessionStarted>,
    IEventSubscriber<SessionEnded>,
    IEventSubscriber<PollFailed>,
    IEventSubscriber<IntegrationPaused>
{
    public Task Handle(SessionStarted domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    public Task Handle(SessionEnded domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    public Task Handle(PollFailed domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    public Task Handle(IntegrationPaused domainEvent, CancellationToken ct) => Enqueue(domainEvent);

    private async Task Enqueue(object domainEvent)
    {
        if (!client.IsEnabled)
        {
            return;
        }

        // Not the publisher's token: the event has happened (the session is saved), so the request or poll that raised
        // it going away must not lose its notification.
        if (await outbox.Enqueue(domainEvent, CancellationToken.None))
        {
            signal.Notify();
        }
    }
}

/// <summary>
/// Wakes this instance's <see cref="NotificationDispatcher"/> when a notification is queued here. It also looks every
/// <see cref="NotificationDispatcher.PollInterval"/> for ones queued by other instances or due for a retry.
/// </summary>
public sealed class NotificationSignal : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);
    private readonly Lock _gate = new();

    public void Notify()
    {
        lock (_gate)
        {
            if (_semaphore.CurrentCount == 0)
            {
                _semaphore.Release();
            }
        }
    }

    /// <returns>True when notified, false when <paramref name="timeout"/> passed first.</returns>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>
/// Sends notifications from the outbox, oldest first, each in its own scope. A failed delivery is retried with
/// backoff; one in flight at shutdown is handed back and sent after the restart.
/// </summary>
public sealed partial class NotificationDispatcher(
    NotificationSignal signal,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    /// <summary>How long a claimed notification is reserved: the slowest delivery (a 15 s cover download and two
    /// 60 s Apprise calls) with room to spare. A sender that dies holds it no longer than this.</summary>
    internal static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    /// <summary>How long a delivered notification is remembered, so the same event is not sent twice.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending the {Kind} notification {NotificationId} failed (attempt {Attempt}); will retry")]
    private partial void LogSendFailed(Exception exception, string kind, long notificationId, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up the {Kind} notification {NotificationId} after {Attempts} attempts")]
    private partial void LogGaveUp(Exception exception, string kind, long notificationId, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dropped notification {NotificationId} of unknown kind {Kind}")]
    private partial void LogUnknownKind(long notificationId, string kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification outbox is unavailable; will retry")]
    private partial void LogOutboxFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not hand back notification {NotificationId} at shutdown; it is sent once its claim expires")]
    private partial void LogReleaseFailed(Exception exception, long notificationId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverDue(stoppingToken);
                if (clock.GetUtcNow() >= nextCleanup)
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<NotificationOutbox>()
                        .DeleteFinished(clock.GetUtcNow() - Retention, stoppingToken);
                    nextCleanup = clock.GetUtcNow() + CleanupInterval;
                }

                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The database is down, say: an escaping exception would stop this service for good.
                LogOutboxFailed(exception);
                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <returns>How many notifications were sent, retried or given up.</returns>
    internal async Task<int> DeliverDue(CancellationToken ct)
    {
        var handled = 0;
        while (await DeliverNext(ct))
        {
            handled++;
        }

        return handled;
    }

    /// <returns>False when nothing was due.</returns>
    internal async Task<bool> DeliverNext(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<NotificationOutbox>();
        var claimed = await outbox.ClaimNext(ClaimDuration, ct);
        if (claimed is null)
        {
            return false;
        }

        var notification = NotificationOutbox.Deserialize(claimed);
        if (notification is null)
        {
            LogUnknownKind(claimed.Id, claimed.Kind);
            await outbox.MarkFailed(claimed, $"Unknown notification kind '{claimed.Kind}'.", permanent: true, ct);
            return true;
        }

        try
        {
            await scope.ServiceProvider.GetRequiredService<NotificationSender>().Send(notification, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await Release(outbox, claimed);
            throw;
        }
        catch (Exception exception)
        {
            // Not cancellable: the attempt happened and must be recorded even during shutdown.
            if (await outbox.MarkFailed(claimed, exception.Message, permanent: false, CancellationToken.None))
            {
                LogGaveUp(exception, claimed.Kind, claimed.Id, claimed.Attempts);
            }
            else
            {
                LogSendFailed(exception, claimed.Kind, claimed.Id, claimed.Attempts);
            }

            return true;
        }

        // Sent: recorded even during shutdown, or it would be sent again after the restart.
        await outbox.MarkDelivered(claimed.Id, CancellationToken.None);
        return true;
    }

    private async Task Release(NotificationOutbox outbox, OutboxNotification claimed)
    {
        try
        {
            await outbox.Release(claimed, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogReleaseFailed(exception, claimed.Id);
        }
    }
}
