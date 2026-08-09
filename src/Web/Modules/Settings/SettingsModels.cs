namespace Web.Modules.Settings;

public sealed record UserSettingsResponse(int IgnoreShortSessionsMinutes);
public sealed record UpdateSettingsRequest(int? IgnoreShortSessionsMinutes);
