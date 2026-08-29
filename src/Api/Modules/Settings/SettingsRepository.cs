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

    public async Task<UserSettingsResponseDto> UpsertSettings(
        long userId,
        int ignoreShortMinutes,
        string? telegramChatId,
        CancellationToken ct)
    {
        var existing = await db.UserSettings
            .Where(s => s.UserId == userId)
            .FirstOrDefaultAsync(ct);
        var telegramChannel = await db.NotificationChannels.SingleOrDefaultAsync(
            channel =>
                channel.UserId == userId &&
                channel.Transport == NotificationChannel.Telegram,
            ct);

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            db.UserSettings.Add(new UserSettings
            {
                UserId = userId,
                IgnoreShortSessionsMinutes = ignoreShortMinutes,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.IgnoreShortSessionsMinutes = ignoreShortMinutes;
            existing.UpdatedAt = now;
        }
        if (telegramChatId is null && telegramChannel is not null)
        {
            db.NotificationChannels.Remove(telegramChannel);
        }
        else if (telegramChatId is not null && telegramChannel is null)
        {
            db.NotificationChannels.Add(new NotificationChannel
            {
                UserId = userId,
                Transport = NotificationChannel.Telegram,
                Destination = telegramChatId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else if (telegramChatId is not null && telegramChannel is not null)
        {
            telegramChannel.Destination = telegramChatId;
            telegramChannel.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return new UserSettingsResponseDto(ignoreShortMinutes, telegramChatId);
    }
}
