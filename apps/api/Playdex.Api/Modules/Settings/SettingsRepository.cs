using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Modules.Settings;

public sealed class SettingsRepository(PlaydexDbContext db)
{
    public async Task<UserSettingsResponseDto> LoadUserSettings(long userId, CancellationToken ct)
    {
        var settings = await db.UserSettings
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.IgnoreShortSessionsMinutes)
            .FirstOrDefaultAsync(ct);

        return new UserSettingsResponseDto(settings);
    }

    public async Task<UserSettingsResponseDto> UpsertSettings(long userId, int ignoreShortMinutes, CancellationToken ct)
    {
        var existing = await db.UserSettings
            .Where(s => s.UserId == userId)
            .FirstOrDefaultAsync(ct);

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

        await db.SaveChangesAsync(ct);
        return new UserSettingsResponseDto(ignoreShortMinutes);
    }
}
