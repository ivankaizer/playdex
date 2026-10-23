using System.Collections.Concurrent;
using System.Net;
using Api.Modules.Posters;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public sealed class PosterServiceTests
{
    private static readonly byte[] Webp = [0x52, 0x49, 0x46, 0x46];

    [Fact]
    public async Task DownloadsFromWeModOnceAndServesLaterRequestsFromTheStore()
    {
        var store = new MemoryPosterStore();
        var weMod = new FakeWeMod(_ => Image());
        var service = CreateService(store, weMod);

        var first = await service.GetSteamCapsule(570, CancellationToken.None);
        var second = await service.GetSteamCapsule(570, CancellationToken.None);

        Assert.Equal(Webp, first!.Content);
        Assert.Equal("image/webp", first.ContentType);
        Assert.Equal(Webp, second!.Content);
        Assert.Equal(["https://wemod.test/steam_apps/570/hero_capsule.webp"], weMod.Requests);
        Assert.Equal(Webp, store.Posters["steam/570/hero_capsule.webp"].Content);
    }

    [Fact]
    public async Task ConcurrentMissesShareOneDownload()
    {
        var release = new TaskCompletionSource();
        var weMod = new FakeWeMod(async _ =>
        {
            await release.Task;
            return Image();
        });
        var service = CreateService(new MemoryPosterStore(), weMod);

        var requests = Enumerable.Range(0, 5)
            .Select(_ => service.GetSteamCapsule(570, CancellationToken.None))
            .ToArray();
        release.SetResult();
        var posters = await Task.WhenAll(requests);

        Assert.All(posters, poster => Assert.Equal(Webp, poster!.Content));
        Assert.Single(weMod.Requests);
    }

    [Fact]
    public async Task ReturnsNullWithoutStoringWhenWeModHasNoPoster()
    {
        var store = new MemoryPosterStore();
        var service = CreateService(store, new FakeWeMod(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await service.GetSteamCapsule(1, CancellationToken.None));
        Assert.Empty(store.Posters);
    }

    [Fact]
    public async Task AMissingPosterIsNotRequestedFromWeModAgainRightAway()
    {
        var weMod = new FakeWeMod(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(new MemoryPosterStore(), weMod);

        Assert.Null(await service.GetSteamCapsule(404, CancellationToken.None));
        Assert.Null(await service.GetSteamCapsule(404, CancellationToken.None));

        Assert.Single(weMod.Requests);
    }

    [Fact]
    public async Task StillServesThePosterWhenStoringItFails()
    {
        var store = new MemoryPosterStore { FailSaves = true };
        var service = CreateService(store, new FakeWeMod(_ => Image()));

        var poster = await service.GetSteamCapsule(570, CancellationToken.None);

        Assert.Equal(Webp, poster!.Content);
    }

    [Fact]
    public void PosterUrlsPointAtThisApiOnlyWhenR2IsConfigured()
    {
        var disabled = new PosterUrls(Options.Create(new PostersOptions()));
        var enabled = new PosterUrls(Options.Create(EnabledOptions()));

        Assert.Equal("https://api-cdn.wemod.com/steam_apps/570/hero_capsule.webp", disabled.SteamCapsule("570"));
        Assert.Equal("https://dash.example.test/api/posters/steam/570", enabled.SteamCapsule("570"));
        Assert.Null(enabled.SteamCapsule("../570"));
        Assert.Null(enabled.SteamCapsule(""));
    }

    private static PostersOptions EnabledOptions() => new()
    {
        PublicBaseUrl = "https://dash.example.test/",
        WeModBaseUrl = "https://wemod.test",
        R2 = new R2Options { AccountId = "account", AccessKeyId = "key", SecretAccessKey = "secret", Bucket = "posters" },
    };

    private static PosterService CreateService(IPosterStore store, FakeWeMod weMod) =>
        new(store, new SingleClientFactory(new HttpClient(weMod)), Options.Create(EnabledOptions()),
            NullLogger<PosterService>.Instance);

    private static HttpResponseMessage Image()
    {
        var content = new ByteArrayContent(Webp);
        content.Headers.ContentType = new("image/webp");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class MemoryPosterStore : IPosterStore
    {
        public ConcurrentDictionary<string, StoredPoster> Posters { get; } = new();
        public bool FailSaves { get; init; }

        public Task<StoredPoster?> Find(string key, CancellationToken ct) =>
            Task.FromResult(Posters.GetValueOrDefault(key));

        public Task Save(string key, StoredPoster poster, CancellationToken ct)
        {
            if (FailSaves)
            {
                throw new InvalidOperationException("R2 is down.");
            }

            Posters[key] = poster;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWeMod(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public FakeWeMod(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(request => Task.FromResult(respond(request)))
        {
        }

        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue(request.RequestUri!.ToString());
            return respond(request);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
