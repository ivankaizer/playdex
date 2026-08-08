namespace Api.Shared.Auth;

public class AuthOptions
{
    public string JwtSecret { get; set; } = string.Empty;
    public string AccessTokenTtl { get; set; } = "15m";
}
