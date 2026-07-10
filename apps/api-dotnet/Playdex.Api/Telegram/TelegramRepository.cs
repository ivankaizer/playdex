using Dapper;
using Npgsql;
using Playdex.Api.Data;

namespace Playdex.Api.Telegram;

public interface ITelegramRepository
{
    /// <summary>
    /// Look up a user by their linked Telegram chat ID.
    /// </summary>
    Task<User?> GetUserByChatId(string chatId, CancellationToken ct);
}

/// <summary>
/// Repository for Telegram-related database operations.
///
/// The <c>users</c> table stores a <c>chat_id</c> column (text) that links a
/// Playdex account to a Telegram chat. This repository queries that link.
/// </summary>
public sealed class TelegramRepository(NpgsqlDataSource dataSource) : ITelegramRepository
{
    public async Task<User?> GetUserByChatId(string chatId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<User>("""
            SELECT
                id AS Id,
                email AS Email,
                password AS Password,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt,
                chat_id AS ChatId,
                is_admin AS IsAdmin
            FROM users
            WHERE chat_id = @ChatId
            """, new { ChatId = chatId });
    }
}
