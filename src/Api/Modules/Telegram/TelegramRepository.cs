using Microsoft.EntityFrameworkCore;
using Api.Shared.Persistence;

namespace Api.Modules.Telegram;

/// <summary>
/// Repository for Telegram-related database operations.
///
/// The <c>users</c> table stores a <c>chat_id</c> column (text) that links a
/// Playdex account to a Telegram chat. This repository queries that link.
/// </summary>
public sealed class TelegramRepository(PlaydexDbContext db)
{
    public async Task<User?> GetUserByChatId(string chatId, CancellationToken ct)
    {
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
    }

    public async Task<TelegramNotificationTarget?> GetNotificationTarget(
        long credentialId,
        long gameId,
        CancellationToken ct)
    {
        return await (
            from credential in db.PlatformCredentials
            join user in db.Users on credential.UserId equals user.Id
            join game in db.Games on gameId equals game.Id
            join platform in db.Platforms on game.PlatformId equals platform.Id
            where credential.Id == credentialId
            select new TelegramNotificationTarget(
                user.ChatId,
                game.Title,
                platform.Name,
                game.CapsuleUrl))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public async Task<TelegramCredentialTarget?> GetCredentialTarget(
        long credentialId,
        CancellationToken ct)
    {
        return await (
            from credential in db.PlatformCredentials
            join user in db.Users on credential.UserId equals user.Id
            join platform in db.Platforms on credential.PlatformId equals platform.Id
            where credential.Id == credentialId
            select new TelegramCredentialTarget(user.ChatId, platform.Name))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public async Task<TelegramSessionSummary?> GetSessionSummary(
        long sessionId,
        CancellationToken ct)
    {
        return await (
            from session in db.PlaySessions
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on game.PlatformId equals platform.Id
            where session.Id == sessionId
            select new TelegramSessionSummary(game.Title, platform.Name))
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<TelegramSessionSummary>> EndActiveSessionsForUser(
        long userId,
        DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var active = await (
            from session in db.PlaySessions
            join credential in db.PlatformCredentials
                on session.PlatformCredentialId equals credential.Id
            join game in db.Games on session.GameId equals game.Id
            join platform in db.Platforms on game.PlatformId equals platform.Id
            where credential.UserId == userId && session.EndedAt == null
            select new { Session = session, game.Title, Platform = platform.Name })
            .ToListAsync(ct);
        foreach (var row in active)
        {
            row.Session.EndedAt = endedAt;
            row.Session.UpdatedAt = endedAt;
        }
        await db.SaveChangesAsync(ct);
        return active
            .Select(row => new TelegramSessionSummary(row.Title, row.Platform))
            .ToList();
    }
}

public sealed record TelegramNotificationTarget(
    string? ChatId,
    string GameTitle,
    string Platform,
    string? CapsuleUrl);

public sealed record TelegramCredentialTarget(string? ChatId, string Platform);
public sealed record TelegramSessionSummary(string Title, string Platform);
