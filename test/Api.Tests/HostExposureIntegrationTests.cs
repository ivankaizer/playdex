using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Api.Tests;

/// <summary>What the host publishes beyond the user API: metrics for administrators, Swagger only in Development.</summary>
[Collection(AdminApiCollectionDefinition.Name)]
public sealed class HostExposureIntegrationTests(AdminApiFixture fixture)
{
    [Fact]
    public async Task AdministratorsCanReadMetrics()
    {
        if (fixture.Factory is null) return;
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);

        using var response = await client.GetAsync("/api/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerIsServedInDevelopment()
    {
        if (fixture.Factory is null) return;
        using var client = fixture.CreateClient();

        using var ui = await client.GetAsync("/swagger");
        using var document = await client.GetAsync("/swagger/json");

        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task SwaggerIsNotServedOutsideDevelopment(string environment)
    {
        if (fixture.Factory is null) return;
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var ui = await client.GetAsync("/swagger");
        using var document = await client.GetAsync("/swagger/json");

        Assert.Equal(HttpStatusCode.NotFound, ui.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
    }
}
