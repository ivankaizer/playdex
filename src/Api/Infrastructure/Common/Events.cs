using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Api.Infrastructure.Common;

public interface IEventSubscriber<in T>
{
    Task Handle(T domainEvent, CancellationToken ct);
}

public sealed record StartSessionRequested(long UserId, long PlatformId, string? Title);
public sealed record StopSessionRequested(long UserId);
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

public sealed partial class EventBus(IServiceScopeFactory scopeFactory, ILogger<EventBus> logger)
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Publish {EventType}")]
    private partial void LogPublish(string eventType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handler {HandlerType} handling {EventType}")]
    private partial void LogHandlerHandling(string handlerType, string eventType);

    public async Task Publish<T>(T domainEvent, CancellationToken ct = default)
    {
        LogPublish(typeof(T).Name);

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventSubscriber<T>>();

        foreach (var handler in handlers)
        {
            LogHandlerHandling(handler.GetType().Name, typeof(T).Name);
            await handler.Handle(domainEvent, ct);
        }
    }
}
