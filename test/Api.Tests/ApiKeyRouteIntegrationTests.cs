using System.Net;
using System.Text.Json;
using Xunit;

namespace Api.Tests;

[Collection(AdminApiCollectionDefinition.Name)]
public sealed class ApiKeyRouteIntegrationTests(AdminApiFixture fixture)
{
    [Fact]
    public async Task PlaydexAppKeyIsCreatedAtApiKeyAndReturnedAgainOnRepeat()
    {
        if (fixture.Factory is null) return;
        using var client = await fixture.LoginAsync(AdminApiFixture.UserEmail);

        var first = await AdminApiFixture.ReadDataAsync<JsonElement>(await client.PostAsync("/api/key", null));
        var second = await AdminApiFixture.ReadDataAsync<JsonElement>(await client.PostAsync("/api/key", null));

        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("apiKey").GetString()));
        Assert.Equal(first.GetProperty("apiKey").GetString(), second.GetProperty("apiKey").GetString());
    }

    [Fact]
    public async Task DoubledPrefixRouteIsGone()
    {
        if (fixture.Factory is null) return;
        using var client = await fixture.LoginAsync(AdminApiFixture.UserEmail);

        using var response = await client.PostAsync("/api/api/key", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
