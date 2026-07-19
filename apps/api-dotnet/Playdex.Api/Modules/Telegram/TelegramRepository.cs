using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Modules.Telegram;

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
}
