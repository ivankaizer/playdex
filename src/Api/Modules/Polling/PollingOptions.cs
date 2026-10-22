namespace Api.Modules.Polling;

public class PollingOptions
{
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>How many credentials one poll cycle polls at the same time.</summary>
    public int MaxConcurrentPolls { get; set; } = 4;

    /// <summary>
    /// Days to keep poll_results (one row per successful poll; the raw provider response only when the detected game
    /// changed) before the hourly cleanup deletes them; 0 or less keeps them forever.
    /// </summary>
    public int PollResultRetentionDays { get; set; } = 30;
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
