namespace Playdex.Api.Settings;

public sealed record UserSettingsResponseDto(int IgnoreShortSessionsMinutes);

public sealed record UpdateSettingsRequest(int? IgnoreShortSessionsMinutes);
