using Microsoft.EntityFrameworkCore;
using Playdex.Api.Data;

namespace Playdex.Api.Settings;

public interface ISettingsRepository
{
    Task<UserSettingsResponseDto> LoadUserSettings(long userId, CancellationToken ct);
    Task<UserSettingsResponseDto> UpsertSettings(long userId, int ignoreShortMinutes, CancellationToken ct);
}

public sealed class SettingsRepository(PlaydexDbContext db) : ISettingsRepository
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
            db.UserSettings.Add(new Data.UserSettings
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
