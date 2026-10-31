using Playdex.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Notifications;

/// <summary>
/// Resolves notification routing data from user-owned channels, with what keeps a session from being announced: no
/// Telegram channel, a game the user ignores, or another session already announced for the same play.
/// </summary>
public sealed class NotificationsRepository(PlaydexDbContext db)
{
    public Task<NotificationTarget?> GetSessionStartedTarget(
        long sessionId,
        CancellationToken ct)
    {
        return (
            from session in db.PlaySessions
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on session.PlatformId equals platform.Id
            join channel in db.NotificationChannels.Where(channel => channel.Transport == NotificationChannel.Telegram)
                on session.UserId equals channel.UserId into channels
            from channel in channels.DefaultIfEmpty()
            where session.Id == sessionId
            select new NotificationTarget(
                session.UserId,
                channel == null ? null : channel.Destination,
                game.Title,
                platform.Name,
                game.CapsuleUrl,
                db.IgnoredGames.Any(ignored => ignored.UserId == session.UserId && ignored.GameId == session.GameId),
                db.PlaySessions.Any(other =>
                    other.Id != session.Id &&
                    other.UserId == session.UserId &&
                    other.GameId == session.GameId &&
                    other.PlatformId == session.PlatformId &&
                    (other.StartedAt < session.StartedAt ||
                     other.StartedAt == session.StartedAt && other.Id < session.Id) &&
                    (other.EndedAt == null || other.EndedAt > session.StartedAt))))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public Task<NotificationTarget?> GetSessionEndedTarget(
        long sessionId,
        CancellationToken ct)
    {
        return (
            from session in db.PlaySessions
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on session.PlatformId equals platform.Id
            join channel in db.NotificationChannels.Where(channel => channel.Transport == NotificationChannel.Telegram)
                on session.UserId equals channel.UserId into channels
            from channel in channels.DefaultIfEmpty()
            where session.Id == sessionId && session.EndedAt != null
            select new NotificationTarget(
                session.UserId,
                channel == null ? null : channel.Destination,
                game.Title,
                platform.Name,
                game.CapsuleUrl,
                db.IgnoredGames.Any(ignored => ignored.UserId == session.UserId && ignored.GameId == session.GameId),
                db.PlaySessions.Any(other =>
                    other.Id != session.Id &&
                    other.UserId == session.UserId &&
                    other.GameId == session.GameId &&
                    other.PlatformId == session.PlatformId &&
                    other.StartedAt < session.EndedAt &&
                    (other.EndedAt == null ||
                     other.EndedAt > session.EndedAt ||
                     other.EndedAt == session.EndedAt && other.Id > session.Id))))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public async Task<CredentialNotificationTarget?> GetCredentialTarget(
        long credentialId,
        CancellationToken ct)
    {
        return await (
            from integration in db.Integrations
            join platform in db.Platforms on integration.PlatformId equals platform.Id
            join channel in db.NotificationChannels.Where(channel => channel.Transport == NotificationChannel.Telegram)
                on integration.UserId equals channel.UserId into channels
            from channel in channels.DefaultIfEmpty()
            where integration.Id == credentialId
            select new CredentialNotificationTarget(
                integration.UserId,
                channel == null ? null : channel.Destination,
                platform.Name))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }
}

/// <param name="Ignored">The user ignores the game: the session is not announced.</param>
/// <param name="Overlapping">Another session of the same play (a second integration, say) is announced instead.</param>
public sealed record NotificationTarget(
    long UserId,
    string? TelegramChatId,
    string GameTitle,
    string Platform,
    string? CapsuleUrl,
    bool Ignored,
    bool Overlapping);

public sealed record CredentialNotificationTarget(
    long UserId,
    string? TelegramChatId,
    string Platform);
