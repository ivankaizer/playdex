using Playdex.Web.Browser;
using Playdex.Web.Models;

namespace Playdex.Web.Auth;

public sealed class AuthSession(IBrowserStorage storage)
{
    private const string AccessTokenKey = "playdex.token";
    private const string RefreshTokenKey = "playdex.refresh";
    private const string EmailKey = "playdex.email";
    private const string AdminKey = "playdex.isAdmin";

    public async ValueTask<bool> IsLoggedInAsync() =>
        !string.IsNullOrEmpty(await GetAccessTokenAsync());

    public async ValueTask<bool> IsAdminAsync() =>
        await storage.GetAsync(AdminKey) == "1";

    public ValueTask<string?> GetAccessTokenAsync() => storage.GetAsync(AccessTokenKey);

    public ValueTask<string?> GetRefreshTokenAsync() => storage.GetAsync(RefreshTokenKey);

    public ValueTask<string?> GetEmailAsync() => storage.GetAsync(EmailKey);

    public async ValueTask StoreLoginAsync(LoginResponse response)
    {
        await storage.SetAsync(AccessTokenKey, response.AccessToken);
        await storage.SetAsync(RefreshTokenKey, response.RefreshToken);
        await storage.SetAsync(EmailKey, response.Email);
        await storage.SetAsync(AdminKey, response.IsAdmin ? "1" : "0");
    }

    public async ValueTask StoreProfileAsync(MeResponse response)
    {
        await storage.SetAsync(EmailKey, response.Email);
        await storage.SetAsync(AdminKey, response.IsAdmin ? "1" : "0");
    }

    public async ValueTask StoreTokensAsync(string accessToken, string refreshToken)
    {
        await storage.SetAsync(AccessTokenKey, accessToken);
        await storage.SetAsync(RefreshTokenKey, refreshToken);
    }

    public ValueTask StoreAdminAsync(bool isAdmin) =>
        storage.SetAsync(AdminKey, isAdmin ? "1" : "0");

    public ValueTask ClearAdminAsync() => storage.RemoveAsync(AdminKey);

    public async ValueTask LogoutAsync()
    {
        await storage.RemoveAsync(AccessTokenKey);
        await storage.RemoveAsync(RefreshTokenKey);
        await storage.RemoveAsync(EmailKey);
        await storage.RemoveAsync(AdminKey);
    }
}
