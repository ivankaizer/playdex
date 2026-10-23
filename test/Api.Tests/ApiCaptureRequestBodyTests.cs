using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Api.Tests;

/// <summary>A capture request's JSON body is read whether or not it comes with a Content-Length.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class ApiCaptureRequestBodyTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheBodyIsReadWithOrWithoutAContentLength(bool chunked)
    {
        if (!postgres.IsConfigured) return;

        ConfigureHost();
        await using var factory = new WebApplicationFactory<Program>();
        // The server's own handler: CreateClient's redirect handler buffers the body, which gives it a length.
        using var client = new HttpClient(factory.Server.CreateHandler()) { BaseAddress = new Uri("http://localhost") };
        // A title but no game id: the answer says the body was read (gameId is checked after gameTitle).
        var json = Encoding.UTF8.GetBytes("""{"gameTitle":"Hades","gameIdProvider":"steam","platform":"pc"}""");
        HttpContent content = chunked ? new StreamContent(new UnknownLengthStream(json)) : new ByteArrayContent(json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        Assert.Equal(chunked, content.Headers.ContentLength is null);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/capture/sessions/start") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "any-key");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("gameId is required.", body.RootElement.GetProperty("error").GetProperty("error").GetString());
    }

    private static void ConfigureHost()
    {
        Environment.SetEnvironmentVariable("DATABASE_URL", Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL"));
        Environment.SetEnvironmentVariable("Auth__JwtSecret", "capture-body-integration-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }

    /// <summary>A stream that can't tell its length, so the body goes without a Content-Length.</summary>
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
