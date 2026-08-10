namespace Api.Modules.Polling;

public class PollingOptions
{
    public int IntervalSeconds { get; set; } = 60;
    public bool UseWiremock { get; set; }
    public SteamOptions Steam { get; set; } = new();
    public PsnOptions Psn { get; set; } = new();
    public OpenXblOptions OpenXbl { get; set; } = new();
    public IgdbOptions Igdb { get; set; } = new();
}

public class SteamOptions
{
    public string BaseUrl { get; set; } = "https://api.steampowered.com";
}

public class PsnOptions
{
    public string OAuthAuthorizeUrl { get; set; } = "https://ca.account.sony.com/api/authz/v3/oauth/authorize";
    public string OAuthTokenUrl { get; set; } = "https://ca.account.sony.com/api/authz/v3/oauth/token";
    public string PresenceBaseUrl { get; set; } = "https://m.np.playstation.com";
}

public class OpenXblOptions
{
    public string BaseUrl { get; set; } = "https://api.xbl.io";
}

public class IgdbOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}
