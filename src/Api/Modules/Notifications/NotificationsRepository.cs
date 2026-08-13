using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Notifications;

/// <summary>
/// Resolves notification routing data from the user's Telegram chat ID.
/// </summary>
public sealed class NotificationsRepository(PlaydexDbContext db)
{
    public Task<NotificationTarget?> GetSessionStartedTarget(
        long sessionId,
        CancellationToken ct)
    {
        return (
            from session in db.PlaySessions
            join user in db.Users on session.UserId equals user.Id
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on session.PlatformId equals platform.Id
            where session.Id == sessionId
            where !db.PlaySessions.Any(other =>
                other.Id != session.Id &&
                other.UserId == session.UserId &&
                other.GameId == session.GameId &&
                other.PlatformId == session.PlatformId &&
                (other.StartedAt < session.StartedAt ||
                 other.StartedAt == session.StartedAt && other.Id < session.Id) &&
                (other.EndedAt == null || other.EndedAt > session.StartedAt))
            select new NotificationTarget(
                user.TelegramChatId,
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
            join user in db.Users on session.UserId equals user.Id
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on session.PlatformId equals platform.Id
            where session.Id == sessionId && session.EndedAt != null
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
