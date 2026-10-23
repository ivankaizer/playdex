using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Settings;

public sealed class SettingsRepository(PlaydexDbContext db)
{
    public async Task<UserSettingsResponseDto> LoadUserSettings(long userId, CancellationToken ct)
    {
        var ignoreShortMinutes = await db.UserSettings
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.IgnoreShortSessionsMinutes)
            .FirstOrDefaultAsync(ct);
        var telegramChatId = await db.NotificationChannels
            .AsNoTracking()
            .Where(channel =>
                channel.UserId == userId &&
                channel.Transport == NotificationChannel.Telegram)
            .Select(channel => channel.Destination)
            .SingleOrDefaultAsync(ct);

        return new UserSettingsResponseDto(ignoreShortMinutes, telegramChatId);
    }

    /// <summary>
    /// Insert-or-update in one statement per table: a read-then-insert lets two first saves race on the primary key.
    /// </summary>
    public async Task<UserSettingsResponseDto> UpsertSettings(
        long userId,
        int ignoreShortMinutes,
        string? telegramChatId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into user_settings (user_id, ignore_short_sessions_minutes, created_at, updated_at)
            values ({userId}, {ignoreShortMinutes}, {now}, {now})
            on conflict (user_id) do update set
                ignore_short_sessions_minutes = excluded.ignore_short_sessions_minutes,
                updated_at = excluded.updated_at
            """, ct);
        if (telegramChatId is null)
        {
            await db.NotificationChannels
                .Where(channel => channel.UserId == userId && channel.Transport == NotificationChannel.Telegram)
                .ExecuteDeleteAsync(ct);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into notification_channels (user_id, transport, destination, created_at, updated_at)
                values ({userId}, {NotificationChannel.Telegram}, {telegramChatId}, {now}, {now})
                on conflict (user_id, transport) do update set
                    destination = excluded.destination,
                    updated_at = excluded.updated_at
                """, ct);
        }

        await transaction.CommitAsync(ct);
        return new UserSettingsResponseDto(ignoreShortMinutes, telegramChatId);
    }
}
