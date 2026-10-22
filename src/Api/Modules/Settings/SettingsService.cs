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

    /// <summary>
    /// Saves first, then registers the chat with Apprise. If Apprise fails, the saved chat ID is still the source of
    /// truth: sending re-registers a chat Apprise does not know (see <see cref="AppriseClient.SendMessageAsync"/>), so
    /// the two can only diverge in the direction that heals itself.
    /// </summary>
    public async Task<UserSettingsResponseDto> UpdateSettings(
        long userId, UpdateSettingsRequest request, CancellationToken ct)
    {
        var minutes = ValidateIgnoreShortSessionsMinutes(request.IgnoreShortSessionsMinutes);
        var telegramChatId = ValidateTelegramChatId(request.TelegramChatId);
        var saved = await repository.UpsertSettings(userId, minutes, telegramChatId, ct);
        if (telegramChatId is not null)
        {
            try
            {
                await appriseClient.ConfigureTelegramAsync(telegramChatId, ct);
            }
            catch (AppriseApiException)
            {
                throw new AppProblemException(
                    StatusCodes.Status502BadGateway,
                    "Settings were saved, but Telegram notifications could not be set up. " +
                    "Check the Telegram chat ID and send a test notification.");
            }
        }

        return saved;
    }

    public async Task<TestNotificationResponseDto> SendTestNotification(
        long userId,
        CancellationToken ct)
    {
        var settings = await repository.LoadUserSettings(userId, ct);
        if (string.IsNullOrWhiteSpace(settings.TelegramChatId))
            throw AppProblemException.Validation(
                "Configure a Telegram chat ID before sending a test notification.");

        try
        {
            await appriseClient.SendMessageAsync(
                settings.TelegramChatId,
                "Playdex test notification. Notifications are configured correctly.",
                ct);
        }
        catch (AppriseApiException)
        {
            throw new AppProblemException(
                StatusCodes.Status502BadGateway,
                "Notification delivery failed. Check the Telegram chat ID and try again.");
        }

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
