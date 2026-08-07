using Playdex.Api.Shared.Common;

namespace Playdex.Api.Modules.Manual;

public sealed class ManualSessionHandler(ManualService manualService) : IEventHandler<StartSessionRequested>
{
    public async Task Handle(StartSessionRequested @event, CancellationToken ct)
    {
        var request = new ManualSessionStartRequest(@event.Title ?? "");
        await manualService.StartSession(@event.UserId, request, ct);
    }
}

public sealed class ManualStopHandler(ManualService manualService) : IEventHandler<StopSessionRequested>
{
    public async Task Handle(StopSessionRequested @event, CancellationToken ct)
    {
        await manualService.StopSession(@event.UserId, ct);
    }
}
