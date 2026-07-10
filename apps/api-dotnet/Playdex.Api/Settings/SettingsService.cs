using Playdex.Api.Http;

namespace Playdex.Api.Settings;

public sealed class SettingsService(ISettingsRepository repository)
{
    public Task<UserSettingsResponseDto> GetSettings(long userId, CancellationToken ct) =>
        repository.LoadUserSettings(userId, ct);

    public async Task<UserSettingsResponseDto> UpdateSettings(
        long userId, UpdateSettingsRequest request, CancellationToken ct)
    {
        var minutes = request.IgnoreShortSessionsMinutes ?? 0;
        if (minutes < 0 || minutes > 1440)
        {
            throw AppProblem.Validation("ignoreShortSessionsMinutes must be between 0 and 1440.");
        }

        return await repository.UpsertSettings(userId, minutes, ct);
    }
}
