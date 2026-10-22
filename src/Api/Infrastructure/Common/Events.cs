using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Common;

public interface IEventSubscriber<in T>
{
    Task Handle(T domainEvent, CancellationToken ct);
}

public sealed record SessionStarted(
    long SessionId,
    long CredentialId,
    long GameId,
    DateTimeOffset StartedAt);
public sealed record SessionEnded(
    long SessionId,
    long CredentialId,
    long GameId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);
public sealed record PollFailed(
    long CredentialId,
    string Platform,
    string Message,
    TimeSpan RetryDelay);

/// <summary>Polling paused itself because the provider kept rejecting the credential.</summary>
public sealed record IntegrationPaused(
    long CredentialId,
    string Platform,
    string Reason,
    int AuthFailures,
    DateTimeOffset PausedAt);

public sealed partial class EventBus(IServiceScopeFactory scopeFactory, ILogger<EventBus> logger)
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Publish {EventType}")]
    private partial void LogPublish(string eventType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handler {HandlerType} handling {EventType}")]
    private partial void LogHandlerHandling(string handlerType, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler {HandlerType} failed for {EventType}")]
    private partial void LogHandlerFailed(Exception exception, string handlerType, string eventType);

    /// <summary>
    /// Runs every subscriber, each isolated: one that throws is logged and does not stop the others. Only
    /// cancellation of <paramref name="ct"/> propagates.
    /// </summary>
    public async Task Publish<T>(T domainEvent, CancellationToken ct = default)
    {
        LogPublish(typeof(T).Name);

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventSubscriber<T>>();

        foreach (var handler in handlers)
        {
            LogHandlerHandling(handler.GetType().Name, typeof(T).Name);
            try
            {
                await handler.Handle(domainEvent, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogHandlerFailed(exception, handler.GetType().Name, typeof(T).Name);
            }
        }
    }
}
