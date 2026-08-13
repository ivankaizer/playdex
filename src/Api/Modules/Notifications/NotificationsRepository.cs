using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Notifications;

/// <summary>
/// Resolves notification routing data from the user's Telegram chat ID.
/// </summary>
public sealed class NotificationsRepository(PlaydexDbContext db)
{
    public async Task<NotificationTarget?> GetNotificationTarget(
        long credentialId,
        long gameId,
        CancellationToken ct)
    {
        return await (
            from integration in db.Integrations
            join user in db.Users on integration.UserId equals user.Id
            join game in db.Games on gameId equals game.Id
            from session in db.PlaySessions
                .Where(row => row.IntegrationId == integration.Id && row.GameId == gameId)
                .OrderByDescending(row => row.StartedAt)
                .Take(1)
            join platform in db.Platforms on session.PlatformId equals platform.Id
            where integration.Id == credentialId
            select new NotificationTarget(
                user.TelegramChatId,
                game.Title,
                platform.Name,
                game.CapsuleUrl))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public async Task<CredentialNotificationTarget?> GetCredentialTarget(
        long credentialId,
        CancellationToken ct)
    {
        return await (
            from integration in db.Integrations
            join user in db.Users on integration.UserId equals user.Id
            join platform in db.Platforms on integration.PlatformId equals platform.Id
            where integration.Id == credentialId
            select new CredentialNotificationTarget(user.TelegramChatId, platform.Name))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }
}

public sealed record NotificationTarget(
    string? TelegramChatId,
    string GameTitle,
    string Platform,
    string? CapsuleUrl);

public sealed record CredentialNotificationTarget(
    string? TelegramChatId,
    string Platform);
