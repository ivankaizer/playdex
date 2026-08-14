namespace Web.Modules.Settings;

public sealed record UserSettingsResponse(
    int IgnoreShortSessionsMinutes,
    string? TelegramChatId);

public sealed record UpdateSettingsRequest(
    int? IgnoreShortSessionsMinutes,
    string? TelegramChatId);

public sealed record TestNotificationResponse(bool Sent);
