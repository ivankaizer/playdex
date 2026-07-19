using Playdex.Api.Shared.Http;

namespace Playdex.Api.Modules.Settings;

public sealed class SettingsService(SettingsRepository repository)
{
    public Task<UserSettingsResponseDto> GetSettings(long userId, CancellationToken ct) =>
        repository.LoadUserSettings(userId, ct);

    public async Task<UserSettingsResponseDto> UpdateSettings(
        long userId, UpdateSettingsRequest request, CancellationToken ct)
    {
        var minutes = ValidateIgnoreShortSessionsMinutes(request.IgnoreShortSessionsMinutes);

        return await repository.UpsertSettings(userId, minutes, ct);
    }

    internal static int ValidateIgnoreShortSessionsMinutes(int? value) =>
        value is >= 0
            ? value.Value
            : throw AppProblem.Validation(
                "ignoreShortSessionsMinutes must be a non-negative integer.");
}
