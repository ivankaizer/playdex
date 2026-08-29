using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shared.Persistence;
using Api.Modules.Polling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public sealed class PsnClientTests
{
    [Fact]
    public async Task GetCurrentlyPlayingFollowsThePsnApiAuthenticationContract()
    {
        var handler = new RecordingHandler(
            Response(HttpStatusCode.Found, response =>
                response.Headers.Location = new Uri("https://redirect.test/callback?code=access-code")),
            Json("""{"access_token":"access-token","expires_in":3600,"refresh_token":"refresh-token"}"""),
            Json("""{"basicPresence":{"gameTitleInfoList":[{"titleName":"Astro Bot","titleId":"PPSA01325_00","conceptIconUrl":"https://images.test/astro-bot.png"}]}}"""));
        var client = CreateClient(handler);

        var result = await client.GetCurrentlyPlaying(Credential(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Astro Bot", result.Game?.Title);
        Assert.Equal("PPSA01325_00", result.Game?.ExternalId);
        Assert.Equal("https://images.test/astro-bot.png", result.Game?.CapsuleUrl);
        Assert.NotNull(result.RawPayload);
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://auth.test/authorize", request.Uri.GetLeftPart(UriPartial.Path));
                Assert.Contains("client_id=09515159-7237-4370-9b40-3806e67c0891", request.Uri.Query);
                Assert.Contains(
                    "redirect_uri=com.scee.psxandroid.scecompcall%3A%2F%2Fredirect",
                    request.Uri.Query);
                Assert.Contains("scope=psn%3Amobile.v2.core%20psn%3Aclientapp", request.Uri.Query);
                Assert.Equal("npsso=test-npsso", request.Cookie);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://auth.test/token", request.Uri.ToString());
                Assert.Equal(
                    "Basic MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A=",
                    request.Authorization);
                Assert.Contains("code=access-code", request.Body);
                Assert.Contains(
                    "redirect_uri=com.scee.psxandroid.scecompcall%3A%2F%2Fredirect",
                    request.Body);
                Assert.Contains("grant_type=authorization_code", request.Body);
                Assert.Contains("token_format=jwt", request.Body);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal(
                    "https://presence.test/api/userProfile/v1/internal/users/account-2/basicPresences?type=primary",
                    request.Uri.ToString());
                Assert.Equal("Bearer access-token", request.Authorization);
            });
    }

    [Fact]
    public async Task GetCurrentlyPlayingPreservesAuthorizeFailures()
    {
        var client = CreateClient(new RecordingHandler(Response(HttpStatusCode.Unauthorized)));

        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            client.GetCurrentlyPlaying(Credential(), CancellationToken.None));

        Assert.Contains("authorize", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("401", failure.Message);
    }

    [Fact]
    public async Task GetCurrentlyPlayingPreservesPresenceApiErrors()
    {
        var handler = new RecordingHandler(
            Response(HttpStatusCode.Found, response =>
                response.Headers.Location = new Uri("https://redirect.test/callback?code=access-code")),
            Json("""{"access_token":"access-token","expires_in":3600}"""),
            Json("""{"error":{"code":2105356,"message":"User not found"}}"""));
        var client = CreateClient(handler);

        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            client.GetCurrentlyPlaying(Credential(), CancellationToken.None));

        Assert.Equal("PSN presence request failed: User not found", failure.Message);
    }

    [Fact]
    public async Task GetCurrentlyPlayingDoesNotReplaceConfiguredTargetAfterNotFound()
    {
        var accessToken = JwtWithAccountId("1234567890123456789");
        var handler = new RecordingHandler(
            Response(HttpStatusCode.Found, response =>
                response.Headers.Location = new Uri("https://redirect.test/callback?code=access-code")),
            Json($$"""{"access_token":"{{accessToken}}","expires_in":3600}"""),
            Response(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var failure = await Assert.ThrowsAsync<ProviderPollException>(() =>
            client.GetCurrentlyPlaying(Credential(), CancellationToken.None));

        Assert.Contains("404", failure.Message);
        Assert.Equal(
            "https://presence.test/api/userProfile/v1/internal/users/account-2/basicPresences?type=primary",
            handler.Requests[2].Uri.ToString());
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task AccessTokenCacheIsSharedAcrossClientInstances()
    {
        var handler = new RecordingHandler(
            Response(HttpStatusCode.Found, response =>
                response.Headers.Location = new Uri("https://redirect.test/callback?code=access-code")),
            Json("""{"access_token":"access-token","expires_in":3600}"""),
            Json("""{"basicPresence":{"gameTitleInfoList":[]}}"""),
            Json("""{"basicPresence":{"gameTitleInfoList":[]}}"""));
        var tokenCache = new PsnTokenCache();

        await CreateClient(handler, tokenCache)
            .GetCurrentlyPlaying(Credential(), CancellationToken.None);
        await CreateClient(handler, tokenCache)
            .GetCurrentlyPlaying(Credential(), CancellationToken.None);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Single(handler.Requests, request =>
            request.Uri.GetLeftPart(UriPartial.Path) == "https://auth.test/authorize");
        Assert.Single(handler.Requests, request =>
            request.Uri.GetLeftPart(UriPartial.Path) == "https://auth.test/token");
        Assert.Equal(2, handler.Requests.Count(request =>
            request.Uri.AbsolutePath.EndsWith("/basicPresences", StringComparison.Ordinal)));
    }

    private static PsnClient CreateClient(
        HttpMessageHandler handler,
        PsnTokenCache? tokenCache = null) => new(
        new HttpClient(handler),
        Options.Create(new PsnOptions
        {
            OAuthAuthorizeUrl = "https://auth.test/authorize",
            OAuthTokenUrl = "https://auth.test/token",
            PresenceBaseUrl = "https://presence.test",
        }),
        tokenCache ?? new PsnTokenCache());

    private static IntegrationCredentials Credential() => new()
    {
        ApiKey = "test-npsso",
        AccountId = "account-2",
    };

    private static string JwtWithAccountId(string accountId)
    {
        var payload = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($$"""{"account_id":"{{accountId}}"}"""))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"header.{payload}.signature";
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(System.Text.Json.JsonDocument.Parse(json).RootElement),
    };

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        Action<HttpResponseMessage>? configure = null)
    {
        var response = new HttpResponseMessage(status);
        configure?.Invoke(response);
        return response;
    }

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("Cookie", out var cookies) ? Assert.Single(cookies) : null,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Cookie,
        string? Authorization,
        string Body);
}
