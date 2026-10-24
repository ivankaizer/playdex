using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(AdminApiCollectionDefinition.Name)]
public sealed class ApiKeyRouteIntegrationTests(AdminApiFixture fixture)
{
    [Fact]
    public async Task EachPlaydexAppKeyIsShownOnceAndReplacesThePreviousOne()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = await fixture.LoginAsync(AdminApiFixture.UserEmail);

        var first = (await AdminApiFixture.ReadDataAsync<JsonElement>(await client.PostAsync("/api/key", null)))
            .GetProperty("apiKey").GetString()!;
        Assert.StartsWith("plx_", first);
        Assert.Equal(HttpStatusCode.OK, await LauncherSummaryStatus(first));

        using var providers = await client.GetAsync("/api/providers");
        var listing = await providers.Content.ReadAsStringAsync();
        Assert.DoesNotContain(first, listing);
        var app = (await AdminApiFixture.ReadDataAsync<JsonElement>(providers)).GetProperty("providers").EnumerateArray()
            .Single(provider => provider.GetProperty("platform").GetString() == "api");
        Assert.True(app.GetProperty("hasApiKey").GetBoolean());
        Assert.False(app.TryGetProperty("apiKey", out _));

        var second = (await AdminApiFixture.ReadDataAsync<JsonElement>(await client.PostAsync("/api/key", null)))
            .GetProperty("apiKey").GetString()!;
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.Unauthorized, await LauncherSummaryStatus(first));
        Assert.Equal(HttpStatusCode.OK, await LauncherSummaryStatus(second));

        await using var db = fixture.CreateDb();
        var stored = await (from integration in db.Integrations.AsNoTracking()
                            join user in db.Users.AsNoTracking() on integration.UserId equals user.Id
                            where integration.Type == IntegrationType.Api && user.Email == AdminApiFixture.UserEmail
                            select integration.ApiKey)
            .ToListAsync();
        Assert.Equal([PlaydexAppKeys.Hash(second)], stored);
    }

    private async Task<HttpStatusCode> LauncherSummaryStatus(string apiKey)
    {
        using var client = fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await client.GetAsync("/api/launcher/summary");
        return response.StatusCode;
    }

    [Fact]
    public async Task DoubledPrefixRouteIsGone()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = await fixture.LoginAsync(AdminApiFixture.UserEmail);

        using var response = await client.PostAsync("/api/api/key", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
