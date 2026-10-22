using System.Net;
using Api.Modules.Polling;
using Api.Modules.Posters;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public sealed class SteamClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedWebApiKeyIsAnAuthenticationFailure(HttpStatusCode status)
    {
        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            CreateClient(new HttpResponseMessage(status)).GetCurrentlyPlaying(Credential(), CancellationToken.None));

        Assert.True(failure.IsAuthenticationFailure);
        Assert.Contains("Web API key", failure.Message);
    }

    [Fact]
    public async Task ARateLimitIsNotAnAuthenticationFailure()
    {
        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            CreateClient(new HttpResponseMessage(HttpStatusCode.TooManyRequests))
                .GetCurrentlyPlaying(Credential(), CancellationToken.None));

        Assert.False(failure.IsAuthenticationFailure);
        Assert.Equal(TimeSpan.FromMinutes(1), failure.RetryAfter);
    }

    private static SteamClient CreateClient(HttpResponseMessage response) => new(
        new HttpClient(new StaticHandler(response)),
        Options.Create(new SteamOptions { BaseUrl = "https://steam.test" }),
        new PosterUrls(Options.Create(new PostersOptions())));

    private static IntegrationCredentials Credential() => new()
    {
        ApiKey = "steam-key",
        AccountId = "76561190000000001",
    };

    private sealed class StaticHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
