namespace Playdex.Api.Modules.Settings;

public sealed record UserSettingsResponseDto(int IgnoreShortSessionsMinutes);

public sealed record UpdateSettingsRequest(int? IgnoreShortSessionsMinutes);
