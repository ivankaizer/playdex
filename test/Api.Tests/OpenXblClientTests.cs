using System.Net;
using System.Net.Http.Headers;
using Shared.Persistence;
using Api.Modules.Polling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public sealed class OpenXblClientTests
{
    [Fact]
    public async Task GetsAuthenticatedUsersActiveFullTitle()
    {
        var handler = new RecordingHandler(Json("""
            {
              "content": {
                "state": "Online",
                "devices": [{
                  "type": "XboxSeriesX",
                  "titles": [
                    { "id": "shell", "name": "Xbox Shell", "placement": "Background", "state": "Active" },
                    { "id": "1810924247", "name": "Halo Infinite", "placement": "Full", "state": "Active" }
                  ]
                }]
              },
              "code": 200
            }
            """));
        var client = CreateClient(handler);

        var result = await client.GetCurrentlyPlaying(Credential(), CancellationToken.None);

        Assert.Equal("Halo Infinite", result?.Game?.Title);
        Assert.Equal("1810924247", result?.Game?.ExternalId);
        Assert.Equal("xbox", result?.Game?.Platform);
        Assert.Null(result?.Game?.CapsuleUrl);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://openxbl.test/v2/presence", request.Uri.ToString());
        Assert.Equal("test-openxbl-key", request.ApiKey);
        Assert.Equal("application/json", request.Accept);
    }

    [Fact]
    public async Task UsesXuidEndpointWhenAccountIdIsConfigured()
    {
        var handler = new RecordingHandler(Json("""
            { "content": { "state": "Offline", "devices": [] }, "code": 200 }
            """));
        var credential = Credential(accountId: "2533274817903789");

        var result = await CreateClient(handler)
            .GetCurrentlyPlaying(credential, CancellationToken.None);

        Assert.Null(result?.Game);
        Assert.Equal(
            "https://openxbl.test/v2/presence/2533274817903789",
            Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task OnlineWithoutAnActiveTitleIsNotPlaying()
    {
        var handler = new RecordingHandler(Json("""
            {
              "content": {
                "state": "Online",
                "devices": [{ "titles": [{ "id": "1", "name": "Xbox Shell", "placement": "Background", "state": "Active" }] }]
              },
              "code": 200
            }
            """));

        var result = await CreateClient(handler)
            .GetCurrentlyPlaying(Credential(), CancellationToken.None);

        Assert.Null(result?.Game);
        Assert.NotNull(result?.RawPayload);
    }

    [Fact]
    public async Task SystemOnlineTitleIsNotPlaying()
    {
        var handler = new RecordingHandler(Json("""
            {
              "content": {
                "xuid": "2535400000000001",
                "state": "Online",
                "devices": [{
                  "type": "WindowsOneCore",
                  "titles": [{
                    "id": "704208617",
                    "name": "Online",
                    "placement": "Full",
                    "state": "Active"
                  }]
                }],
                "cloaked": true
              },
              "code": 200
            }
            """));

        var result = await CreateClient(handler)
            .GetCurrentlyPlaying(Credential(), CancellationToken.None);

        Assert.Null(result?.Game);
        Assert.NotNull(result?.RawPayload);
    }

    [Fact]
    public async Task RateLimitProducesProviderBackoff()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
        var client = CreateClient(new RecordingHandler(response));

        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            client.GetCurrentlyPlaying(Credential(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(45), failure.RetryAfter);
        Assert.Contains("rate limited", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedApiKeyHasActionableError(HttpStatusCode status)
    {
        var client = CreateClient(new RecordingHandler(new HttpResponseMessage(status)));

        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            client.GetCurrentlyPlaying(Credential(), CancellationToken.None));

        Assert.Equal("OpenXBL rejected the API key.", failure.Message);
    }

    [Fact]
    public async Task MissingApiKeyDoesNotCallOpenXbl()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            client.GetCurrentlyPlaying(Credential(apiKey: null), CancellationToken.None));

        Assert.Contains("API key", failure.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetsPosterFromMarketplaceForPersistedGame()
    {
        var handler = new RecordingHandler(Json("""
                {
                  "content": {
                    "Products": [{ "LocalizedProperties": [{ "Images": [
                      { "ImagePurpose": "Screenshot", "Uri": "//images.test/screenshot" },
                      { "ImagePurpose": "Poster", "Uri": "//images.test/thymesia-poster" }
                    ] }] }]
                  },
                  "code": 200
                }
                """));

        var poster = await CreateClient(handler)
            .GetCapsuleUrl(Credential(), "1963849763", CancellationToken.None);

        Assert.Equal("https://images.test/thymesia-poster", poster);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://openxbl.test/v2/marketplace/title/1963849763",
            request.Uri.ToString());
        Assert.Equal("test-openxbl-key", request.ApiKey);
        Assert.Equal("application/json", request.Accept);
    }

    private static OpenXblClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new OpenXblOptions { BaseUrl = "https://openxbl.test" }));

    private static IntegrationCredentials Credential(
        string? apiKey = "test-openxbl-key",
        string? accountId = null) => new()
        {
            ApiKey = apiKey,
            AccountId = accountId,
        };

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Headers.TryGetValues("X-Authorization", out var keys) ? Assert.Single(keys) : null,
                request.Headers.Accept.SingleOrDefault()?.MediaType));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed record RecordedRequest(Uri Uri, string? ApiKey, string? Accept);
}
