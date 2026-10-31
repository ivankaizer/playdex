using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Api.Tests;

[Collection(AdminApiCollectionDefinition.Name)]
public sealed class AdminAuthorizationIntegrationTests(AdminApiFixture fixture)
{
    private static readonly (string Method, string Path)[] _adminRoutes =
    [
        ("GET", "/api/admin/dashboard"),
        ("GET", "/api/admin/platforms"),
        ("PUT", "/api/admin/platforms/steam"),
        ("GET", "/api/admin/integrations"),
        ("PATCH", "/api/admin/integrations/1"),
        ("GET", "/api/admin/sessions"),
        ("POST", "/api/admin/sessions/transfer"),
        ("POST", "/api/admin/sessions/delete"),
        ("GET", "/api/admin/games"),
        ("PATCH", "/api/admin/games/1"),
        ("POST", "/api/admin/games/merge"),
        ("GET", "/api/admin/ai-generations"),
        ("GET", $"/api/admin/ai-generations/{Guid.Empty}"),
        ("GET", $"/api/admin/ai-generations/{Guid.Empty}/image"),
        ("DELETE", $"/api/admin/ai-generations/{Guid.Empty}"),
    ];

    [Fact]
    public async Task LoginAndMeExposeAdministratorFlag()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = fixture.CreateClient();
        foreach (var (email, expected) in new[] { (AdminApiFixture.AdminEmail, true), (AdminApiFixture.UserEmail, false) })
        {
            using var login = await client.PostAsJsonAsync("/api/login", new { email, password = AdminApiFixture.Password });
            var data = await AdminApiFixture.ReadDataAsync<JsonElement>(login);
            Assert.Equal(expected, data.GetProperty("isAdmin").GetBoolean());

            using var authenticated = await fixture.LoginAsync(email);
            var me = await AdminApiFixture.ReadDataAsync<JsonElement>(await authenticated.GetAsync("/api/me"));
            Assert.Equal(expected, me.GetProperty("isAdmin").GetBoolean());
        }
    }

    [Fact]
    public async Task AnonymousCallersAreUnauthorized()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = fixture.CreateClient();
        foreach (var (method, path) in _adminRoutes)
        {
            using var response = await client.SendAsync(Request(method, path));
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} → {response.StatusCode}");
        }
    }

    [Fact]
    public async Task NonAdministratorsAreForbiddenFromEveryAdminRoute()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = await fixture.LoginAsync(AdminApiFixture.UserEmail);
        foreach (var (method, path) in _adminRoutes)
        {
            using var response = await client.SendAsync(Request(method, path));
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} → {response.StatusCode}");
            Assert.Equal("Forbidden.", await AdminApiFixture.ReadErrorAsync(response));
        }
    }

    [Fact]
    public async Task DemotedAdministratorIsForbiddenWithTheSameTokenAndRestoredOnPromotion()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        using (var allowed = await client.GetAsync("/api/admin/platforms"))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        await fixture.SetAdminAsync(false);
        try
        {
            foreach (var (method, path) in _adminRoutes)
            {
                using var response = await client.SendAsync(Request(method, path));
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} → {response.StatusCode}");
            }
        }
        finally
        {
            await fixture.SetAdminAsync(true);
        }

        using var restored = await client.GetAsync("/api/admin/platforms");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PUT" or "PATCH" or "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        return request;
    }
}
