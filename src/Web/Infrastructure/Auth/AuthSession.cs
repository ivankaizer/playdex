using Web.Infrastructure.Browser;
using Web.Infrastructure.Auth;

namespace Web.Infrastructure.Auth;

public sealed class AuthSession(IBrowserStorage storage)
{
    private const string AccessTokenKey = "playdex.token";
    private const string RefreshTokenKey = "playdex.refresh";
    private const string EmailKey = "playdex.email";

    public async ValueTask<bool> IsLoggedInAsync() =>
        !string.IsNullOrEmpty(await GetAccessTokenAsync());

    public ValueTask<string?> GetAccessTokenAsync() => storage.GetAsync(AccessTokenKey);

    public ValueTask<string?> GetRefreshTokenAsync() => storage.GetAsync(RefreshTokenKey);

    public ValueTask<string?> GetEmailAsync() => storage.GetAsync(EmailKey);

    public async ValueTask StoreLoginAsync(LoginResponse response)
    {
        await storage.SetAsync(AccessTokenKey, response.AccessToken);
        await storage.SetAsync(RefreshTokenKey, response.RefreshToken);
        await storage.SetAsync(EmailKey, response.Email);
    }

    public async ValueTask StoreProfileAsync(MeResponse response)
    {
        await storage.SetAsync(EmailKey, response.Email);
    }

    public async ValueTask StoreTokensAsync(string accessToken, string refreshToken)
    {
        await storage.SetAsync(AccessTokenKey, accessToken);
        await storage.SetAsync(RefreshTokenKey, refreshToken);
    }

    public async ValueTask LogoutAsync()
    {
        await storage.RemoveAsync(AccessTokenKey);
        await storage.RemoveAsync(RefreshTokenKey);
        await storage.RemoveAsync(EmailKey);
    }
}
