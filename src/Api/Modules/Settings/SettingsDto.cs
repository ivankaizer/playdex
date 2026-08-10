namespace Api.Modules.Settings;

public sealed record UserSettingsResponseDto(
    int IgnoreShortSessionsMinutes,
    string? TelegramChatId);

public sealed record UpdateSettingsRequest(
    int? IgnoreShortSessionsMinutes,
    string? TelegramChatId);

public sealed record TestNotificationResponseDto(bool Sent);
