using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;

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
        var telegramChatId = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.TelegramChatId)
            .SingleAsync(ct);

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
        var user = await db.Users.SingleAsync(row => row.Id == userId, ct);

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
        user.TelegramChatId = telegramChatId;
        user.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return new UserSettingsResponseDto(ignoreShortMinutes, telegramChatId);
    }
}
