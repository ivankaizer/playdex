using System.Net;
using System.Text;
using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class CustomArtTests
{
    private static SteamGridDbClient Client(ArtHandler handler) => new(new HttpClient(handler));

    [Fact]
    public async Task APickedImageIsTheGamesArtOfThatKind()
    {
        var root = NewTempDir();

        await CustomArt.SetAsync(root, "playdex-MyGame", "portrait", 101, "https://cdn2.steamgriddb.com/grid/abc.png", Client(new ArtHandler()));

        var art = CustomArt.Find(root, "playdex-MyGame");
        Assert.Equal("playdex-MyGame/portrait.101.png", art?.Portrait);
        Assert.Null(art?.Hero);
        Assert.Equal(new Dictionary<string, long> { ["portrait"] = 101 }, CustomArt.Picked(root, "playdex-MyGame"));
    }

    [Fact]
    public async Task AnotherPickReplacesTheOneBefore()
    {
        var root = NewTempDir();
        var client = Client(new ArtHandler());

        await CustomArt.SetAsync(root, "steam-620", "hero", 1, "https://cdn2.steamgriddb.com/hero/one.png", client);
        await CustomArt.SetAsync(root, "steam-620", "hero", 2, "https://cdn2.steamgriddb.com/hero/two.jpg", client);

        Assert.Equal("steam-620/hero.2.jpg", CustomArt.Find(root, "steam-620")?.Hero);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "steam-620")));
    }

    [Fact]
    public async Task ResetGoesBackToNoPickedArt()
    {
        var root = NewTempDir();
        await CustomArt.SetAsync(root, "steam-620", "logo", 7, "https://cdn2.steamgriddb.com/logo/l.png", Client(new ArtHandler()));

        CustomArt.Reset(root, "steam-620", "logo");

        Assert.Null(CustomArt.Find(root, "steam-620"));
        Assert.Empty(CustomArt.Picked(root, "steam-620"));
    }

    [Fact]
    public async Task OnlySteamGridDbImagesArePicked()
    {
        var root = NewTempDir();
        var handler = new ArtHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CustomArt.SetAsync(root, "steam-620", "portrait", 1, "https://example.com/p.png", Client(handler)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CustomArt.SetAsync(root, "steam-620", "portrait", 1, "http://cdn2.steamgriddb.com/p.png", Client(handler)));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/../../b")]
    [InlineData("a\\b")]
    public void ALibraryIdThatLeavesTheArtFolderIsRefused(string libraryId) =>
        Assert.Throws<InvalidOperationException>(() => CustomArt.Find(NewTempDir(), libraryId));

    [Fact]
    public void AnUnknownKindIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => CustomArt.Reset(NewTempDir(), "steam-620", "icon"));

    [Fact]
    public async Task ANonImageResponseLeavesNothingBehind()
    {
        var root = NewTempDir();
        var handler = new ArtHandler { ContentType = "text/html" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CustomArt.SetAsync(root, "steam-620", "portrait", 1, "https://cdn2.steamgriddb.com/grid/p.png", Client(handler)));

        Assert.Empty(Directory.GetFiles(Path.Combine(root, "steam-620")));
    }

    [Fact]
    public async Task ImagesOfAKindArePagedFromSteamGridDb()
    {
        var handler = new ArtHandler
        {
            Json = """
                {"success":true,"page":0,"total":51,"limit":50,"data":[
                  {"id":5,"url":"https://cdn2.steamgriddb.com/grid/a.png","thumb":"https://cdn2.steamgriddb.com/thumb/a.jpg","width":600,"height":900,"style":"alternate"},
                  {"id":6,"url":"https://elsewhere.test/b.png","thumb":"https://elsewhere.test/b.png","width":600,"height":900}
                ]}
                """
        };

        var page = await Client(handler).ListImagesAsync("key", 42, "portrait");

        var image = Assert.Single(page.Images);
        Assert.Equal(new SteamGridDbImage(5, "https://cdn2.steamgriddb.com/grid/a.png", "https://cdn2.steamgriddb.com/thumb/a.jpg", 600, 900, "alternate"), image);
        Assert.True(page.More);
        Assert.Contains(handler.Requests, uri => uri.Contains("/grids/game/42?dimensions=600x900", StringComparison.Ordinal) && uri.EndsWith("&page=0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchReadsGamesAndTheirYear()
    {
        var handler = new ArtHandler
        {
            Json = """{"success":true,"data":[{"id":42,"name":"Portal 2","release_date":1303171200},{"id":43,"name":"Portal 2 Sixense"}]}"""
        };

        var games = await Client(handler).SearchGamesAsync("key", "Portal 2");

        Assert.Equal([new SteamGridDbGame(42, "Portal 2", 2011), new SteamGridDbGame(43, "Portal 2 Sixense", null)], games);
    }

    [Fact]
    public async Task BrowsingWithoutAKeyAsksForOne()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(new ArtHandler()).SearchGamesAsync("", "Portal 2"));
        Assert.Contains("SteamGridDB API key", error.Message);
    }

    private sealed class ArtHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public string ContentType { get; init; } = "image/png";

        public string Json { get; init; } = """{"success":true,"data":[]}""";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            Requests.Add(uri);
            HttpContent content = uri.Contains("/api/v2/", StringComparison.Ordinal)
                ? new StringContent(Json, Encoding.UTF8, "application/json")
                : new ByteArrayContent([1, 2, 3, 4]) { Headers = { ContentType = new(ContentType) } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
