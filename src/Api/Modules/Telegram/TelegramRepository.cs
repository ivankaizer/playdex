using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;

namespace Api.Modules.Telegram;

/// <summary>
/// Repository for Telegram-related database operations.
///
/// The <c>users</c> table stores a <c>chat_id</c> column (text) that links a
/// Playdex account to a Telegram chat. This repository queries that link.
/// </summary>
public sealed class TelegramRepository(PlaydexDbContext db)
{
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

    public async Task<List<string>> GetAdminChatIds(CancellationToken ct)
    {
        return await db.Users
            .Where(user => user.IsAdmin && user.ChatId != null && user.ChatId != "")
            .OrderBy(user => user.Id)
            .Select(user => user.ChatId!)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}

public sealed record TelegramNotificationTarget(
    string? ChatId,
    string GameTitle,
    string Platform,
    string? CapsuleUrl);
