using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Notifications;

namespace Api.Modules.Settings;

public sealed class SettingsService(
    SettingsRepository repository,
    AppriseClient appriseClient)
{
    public Task<UserSettingsResponseDto> GetSettings(long userId, CancellationToken ct) =>
        repository.LoadUserSettings(userId, ct);

    public async Task<UserSettingsResponseDto> UpdateSettings(
        long userId, UpdateSettingsRequest request, CancellationToken ct)
    {
        var minutes = ValidateIgnoreShortSessionsMinutes(request.IgnoreShortSessionsMinutes);
        var telegramChatId = ValidateTelegramChatId(request.TelegramChatId);
        if (telegramChatId is not null)
            await appriseClient.ConfigureTelegramAsync(telegramChatId, ct);

        return await repository.UpsertSettings(userId, minutes, telegramChatId, ct);
    }

    public async Task<TestNotificationResponseDto> SendTestNotification(
        long userId,
        CancellationToken ct)
    {
        var settings = await repository.LoadUserSettings(userId, ct);
        if (string.IsNullOrWhiteSpace(settings.TelegramChatId))
            throw AppProblemException.Validation(
                "Configure a Telegram chat ID before sending a test notification.");

        await appriseClient.SendMessageAsync(
            settings.TelegramChatId,
            "Playdex test notification. Notifications are configured correctly.",
            ct);
        return new TestNotificationResponseDto(true);
    }

    /// <summary>
    /// Clamped to a day: no session is longer (<see cref="SessionLimits.MaxDurationMinutes"/>), and a huge value
    /// overflowed the minutes-to-seconds arithmetic in the stats queries.
    /// </summary>
    internal static int ValidateIgnoreShortSessionsMinutes(int? value) =>
        value is >= 0
            ? Math.Min(value.Value, SessionLimits.MaxDurationMinutes)
            : throw AppProblemException.Validation(
                "ignoreShortSessionsMinutes must be a non-negative integer.");

    internal static string? ValidateTelegramChatId(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (!long.TryParse(
                trimmed,
                System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture,
                out var chatId) || chatId == 0)
            throw AppProblemException.Validation(
                "telegramChatId must be a non-zero integer.");
        return chatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
