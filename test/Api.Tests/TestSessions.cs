using Api.Infrastructure.Common;
using Api.Modules.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Playdex.Persistence;

namespace Api.Tests;

internal static class TestSessions
{
    public static SessionLifecycle Lifecycle(PlaydexDbContext db, SessionEventRecorder? recorder = null)
    {
        var services = new ServiceCollection();
        if (recorder is not null)
        {
            services.AddSingleton<IEventSubscriber<SessionStarted>>(recorder);
            services.AddSingleton<IEventSubscriber<SessionEnded>>(recorder);
        }

        var eventBus = new EventBus(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EventBus>.Instance);
        return new SessionLifecycle(db, eventBus, NullLogger<SessionLifecycle>.Instance);
    }
}

/// <summary>Records session events in order; safe to share between concurrently running services.</summary>
internal sealed class SessionEventRecorder : IEventSubscriber<SessionStarted>, IEventSubscriber<SessionEnded>
{
    private readonly Lock _gate = new();
    private readonly List<object> _events = [];

    public IReadOnlyList<object> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<SessionStarted> Started => Events.OfType<SessionStarted>().ToList();

    public IReadOnlyList<SessionEnded> Ended => Events.OfType<SessionEnded>().ToList();

    public Task Handle(SessionStarted domainEvent, CancellationToken ct) => Record(domainEvent);

    public Task Handle(SessionEnded domainEvent, CancellationToken ct) => Record(domainEvent);

    private Task Record(object domainEvent)
    {
        lock (_gate)
        {
            _events.Add(domainEvent);
        }

        return Task.CompletedTask;
    }
}
