using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Notifications;

/// <summary>
/// Resolves notification routing data from user-owned channels.
/// </summary>
public sealed class NotificationsRepository(PlaydexDbContext db)
{
    public async Task<NotificationTarget?> GetNotificationTarget(
        long credentialId,
        long gameId,
        CancellationToken ct)
    {
        return await (
            from credential in db.PlatformCredentials
            join channel in db.NotificationChannels on credential.UserId equals channel.UserId
            join game in db.Games on gameId equals game.Id
            join platform in db.Platforms on game.PlatformId equals platform.Id
            where credential.Id == credentialId && channel.Transport == NotificationChannel.Telegram
            select new NotificationTarget(
                channel.Destination,
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
            from credential in db.PlatformCredentials
            join channel in db.NotificationChannels on credential.UserId equals channel.UserId
            join platform in db.Platforms on credential.PlatformId equals platform.Id
            where credential.Id == credentialId && channel.Transport == NotificationChannel.Telegram
            select new CredentialNotificationTarget(channel.Destination, platform.Name))
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
