using Playdex.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Notifications;

/// <summary>
/// Resolves notification routing data from user-owned channels. Sessions of a game the user ignores are not notified.
/// </summary>
public sealed class NotificationsRepository(PlaydexDbContext db)
{
    public Task<NotificationTarget?> GetSessionStartedTarget(
        long sessionId,
        CancellationToken ct)
    {
        return (
            from session in db.PlaySessions
            join channel in db.NotificationChannels on session.UserId equals channel.UserId
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on session.PlatformId equals platform.Id
            where session.Id == sessionId && channel.Transport == NotificationChannel.Telegram
            where !db.IgnoredGames.Any(ignored => ignored.UserId == session.UserId && ignored.GameId == session.GameId)
            where !db.PlaySessions.Any(other =>
                other.Id != session.Id &&
                other.UserId == session.UserId &&
                other.GameId == session.GameId &&
                other.PlatformId == session.PlatformId &&
                (other.StartedAt < session.StartedAt ||
                 other.StartedAt == session.StartedAt && other.Id < session.Id) &&
                (other.EndedAt == null || other.EndedAt > session.StartedAt))
            select new NotificationTarget(
                channel.Destination,
                game.Title,
                platform.Name,
                game.CapsuleUrl))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public Task<NotificationTarget?> GetSessionEndedTarget(
        long sessionId,
        CancellationToken ct)
    {
        return (
            from session in db.PlaySessions
            join channel in db.NotificationChannels on session.UserId equals channel.UserId
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on session.PlatformId equals platform.Id
            where session.Id == sessionId && session.EndedAt != null
                && channel.Transport == NotificationChannel.Telegram
            where !db.IgnoredGames.Any(ignored => ignored.UserId == session.UserId && ignored.GameId == session.GameId)
            where !db.PlaySessions.Any(other =>
                other.Id != session.Id &&
                other.UserId == session.UserId &&
                other.GameId == session.GameId &&
                other.PlatformId == session.PlatformId &&
                other.StartedAt < session.EndedAt &&
                (other.EndedAt == null ||
                 other.EndedAt > session.EndedAt ||
                 other.EndedAt == session.EndedAt && other.Id > session.Id))
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
            from integration in db.Integrations
            join channel in db.NotificationChannels on integration.UserId equals channel.UserId
            join platform in db.Platforms on integration.PlatformId equals platform.Id
            where integration.Id == credentialId && channel.Transport == NotificationChannel.Telegram
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
