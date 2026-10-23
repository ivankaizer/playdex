using System.Net;
using Api.Modules.Polling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public sealed class IgdbClientTests
{
    [Fact]
    public async Task LookupUsesTheSelectedIdAndReturnsAuthoritativeMetadata()
    {
        var handler = new RecordingHandler(
            Json("""{"access_token":"token","expires_in":3600}"""),
            Json("""[{"id":114283,"name":"Authoritative Hades II","cover":{"image_id":"cover-id"},"platforms":[{"id":6,"name":"PC"}]}]"""));
        var client = CreateClient(handler);

        var game = await client.GetGameById("114283", CancellationToken.None);

        Assert.NotNull(game);
        Assert.Equal("114283", game.Id);
        Assert.Equal("Authoritative Hades II", game.Title);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/cover-id.jpg", game.CoverUrl);
        Assert.Contains("where id = (114283)", handler.RequestBodies[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("0")]
    public async Task LookupRejectsInvalidIdsWithoutCallingIgdb(string id)
    {
        var handler = new RecordingHandler();

        var game = await CreateClient(handler).GetGameById(id, CancellationToken.None);

        Assert.Null(game);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task TheTwitchTokenIsFetchedOnceAndSentInTheBody()
    {
        var handler = new RecordingHandler(
            Json("""{"access_token":"token","expires_in":3600}"""),
            Json("[]"),
            Json("[]"));
        var tokenCache = new IgdbTokenCache();

        await CreateClient(handler, tokenCache).SearchGames("Hades", CancellationToken.None);
        await CreateClient(handler, tokenCache).SearchGames("Hades II", CancellationToken.None);

        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.Equal("https://id.twitch.tv/oauth2/token", handler.RequestUris[0]);
        Assert.Contains("client_secret=secret", handler.RequestBodies[0]);
        Assert.All(handler.RequestUris, uri => Assert.DoesNotContain("secret", uri));
    }

    [Fact]
    public async Task ARejectedTokenIsFetchedAgain()
    {
        var handler = new RecordingHandler(
            Json("""{"access_token":"revoked","expires_in":3600}"""),
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            Json("""{"access_token":"fresh","expires_in":3600}"""),
            Json("[]"));
        var client = CreateClient(handler, new IgdbTokenCache());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchGames("Hades", CancellationToken.None));
        await client.SearchGames("Hades", CancellationToken.None);

        Assert.Equal("Bearer fresh", handler.Authorizations[3]);
    }

    private static IgdbClient CreateClient(HttpMessageHandler handler, IgdbTokenCache? tokenCache = null) => new(
        new HttpClient(handler),
        Options.Create(new IgdbOptions { ClientId = "client", ClientSecret = "secret" }),
        tokenCache ?? new IgdbTokenCache());

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<string> RequestBodies { get; } = [];
        public List<string> RequestUris { get; } = [];
        public List<string?> Authorizations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.ToString());
            Authorizations.Add(request.Headers.Authorization?.ToString());
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }
}
