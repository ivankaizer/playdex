using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Playdex.Api.Shared.Common;

public interface IEventHandler<in T>
{
    Task Handle(T @event, CancellationToken ct);
}

public sealed record StartSessionRequested(long UserId, long PlatformId, string? Title);
public sealed record StopSessionRequested(long UserId);
public sealed record GameDetected(long CredentialId, string ExternalGameId, string Title, string Platform);

public sealed class EventBus(IServiceScopeFactory scopeFactory, ILogger<EventBus> logger)
{
    public async Task Publish<T>(T @event, CancellationToken ct = default)
    {
        logger.LogDebug("Publish {EventType}", typeof(T).Name);

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<T>>();

        foreach (var handler in handlers)
        {
            logger.LogDebug("Handler {HandlerType} handling {EventType}",
                handler.GetType().Name, typeof(T).Name);
            await handler.Handle(@event, ct);
        }
    }
}
