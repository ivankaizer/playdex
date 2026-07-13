namespace Playdex.Web.Models;

public sealed record UserSettingsResponse(int IgnoreShortSessionsMinutes);
public sealed record UpdateSettingsRequest(int? IgnoreShortSessionsMinutes);
