using Api.Infrastructure.Common;

namespace Api.Modules.Manual;

public sealed class ManualSessionHandler(ManualService manualService) : IEventSubscriber<StartSessionRequested>
{
    public async Task Handle(StartSessionRequested @event, CancellationToken ct)
    {
        var request = new ManualSessionStartRequest(@event.Title ?? "");
        await manualService.StartSession(@event.UserId, request, ct);
    }
}

public sealed class ManualStopHandler(ManualService manualService) : IEventSubscriber<StopSessionRequested>
{
    public async Task Handle(StopSessionRequested @event, CancellationToken ct)
    {
        await manualService.StopSession(@event.UserId, ct);
    }
}
