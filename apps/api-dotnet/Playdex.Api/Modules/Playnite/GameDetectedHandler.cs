using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Common;

namespace Playdex.Api.Modules.Playnite;

public sealed class GameDetectedHandler(
    PlayniteRepository playniteRepo) : IEventHandler<GameDetected>
{
    public async Task Handle(GameDetected @event, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var gameId = await playniteRepo.UpsertGame(
            @event.Title, @event.ExternalGameId, @event.Platform,
            $"{@event.Platform}:{@event.ExternalGameId}", null, ct);

        var active = await playniteRepo.FetchActiveSessionForCredential(@event.CredentialId, ct);

        if (active is not null)
        {
            if (active.GameId == gameId)
            {
                await playniteRepo.TouchActiveSession(active.Id, now, ct);
            }
            else
            {
                await playniteRepo.EndActiveSessionsForCredential(@event.CredentialId, now, ct);
                await playniteRepo.InsertActivePlaySession(@event.CredentialId, gameId, now, ct);
            }
        }
        else
        {
            await playniteRepo.InsertActivePlaySession(@event.CredentialId, gameId, now, ct);
        }
    }
}
