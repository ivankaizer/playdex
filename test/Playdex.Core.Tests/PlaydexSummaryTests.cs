using System.Net;
using System.Text;

namespace Playdex.Core.Tests;

public sealed class PlaydexSummaryTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir();

    private string CacheFile => Path.Combine(_dir, "playdex-summary.json");

    [Fact]
    public async Task FetchesTheSummaryWithTheKeyAndThisPcsTimeZoneAndCachesIt()
    {
        var handler = new SummaryHandler(Envelope("""{"generatedAt":"2026-10-10T16:00:00Z","today":{"totalSeconds":60}}"""));
        using var api = new CaptureClient("plx_key", "https://playdex.test/api", handler);
        using var summary = new PlaydexSummary(api, CacheFile);

        Assert.True(await summary.RefreshAsync());

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://playdex.test/api/launcher/summary?tz=", request.Uri, StringComparison.Ordinal);
        // Uri.ToString() shows the query unescaped.
        Assert.Contains($"tz={TimeZoneInfo.Local.Id}&", request.Uri, StringComparison.Ordinal);
        Assert.Contains("&utcOffsetMinutes=", request.Uri, StringComparison.Ordinal);
        Assert.Equal("Bearer plx_key", request.Authorization);
        Assert.Equal("""{"generatedAt":"2026-10-10T16:00:00Z","today":{"totalSeconds":60}}""", summary.Current);

        // A later run starts from the cache, before any request.
        using var nextRun = new PlaydexSummary(api, CacheFile);
        Assert.Equal(summary.Current, nextRun.Current);
    }

    [Fact]
    public async Task ASummaryThatOnlyDiffersInGeneratedAtIsNoChange()
    {
        var handler = new SummaryHandler(Envelope("""{"generatedAt":"2026-10-10T16:00:00Z","today":{"totalSeconds":60}}"""));
        using var api = new CaptureClient("plx_key", "https://playdex.test/api", handler);
        using var summary = new PlaydexSummary(api, CacheFile);
        Assert.True(await summary.RefreshAsync());

        handler.Body = Envelope("""{"generatedAt":"2026-10-10T16:05:00Z","today":{"totalSeconds":60}}""");
        Assert.False(await summary.RefreshAsync());

        handler.Body = Envelope("""{"generatedAt":"2026-10-10T16:10:00Z","today":{"totalSeconds":120}}""");
        Assert.True(await summary.RefreshAsync());
        Assert.Contains("120", summary.Current, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedRequestKeepsTheLastSummary(HttpStatusCode status)
    {
        File.WriteAllText(CacheFile, """{"today":{"totalSeconds":60}}""");
        var handler = new SummaryHandler("""{"data":null,"error":{"error":"Invalid api key."}}""") { Status = status };
        using var api = new CaptureClient("plx_key", "https://playdex.test/api", handler);
        using var summary = new PlaydexSummary(api, CacheFile);

        Assert.False(await summary.RefreshAsync());
        Assert.Equal("""{"today":{"totalSeconds":60}}""", summary.Current);
    }

    [Fact]
    public async Task OfflineKeepsTheLastSummary()
    {
        File.WriteAllText(CacheFile, """{"today":{"totalSeconds":60}}""");
        using var api = new CaptureClient("plx_key", "https://playdex.test/api", new SummaryHandler("") { Throw = true });
        using var summary = new PlaydexSummary(api, CacheFile);

        Assert.False(await summary.RefreshAsync());
        Assert.Equal("""{"today":{"totalSeconds":60}}""", summary.Current);
    }

    [Fact]
    public async Task WithoutAnApiKeyNothingIsFetchedAndTheCacheIsCleared()
    {
        File.WriteAllText(CacheFile, """{"today":{"totalSeconds":60}}""");
        var handler = new SummaryHandler(Envelope("{}"));
        using var api = new CaptureClient(null, "https://playdex.test/api", handler);
        using var summary = new PlaydexSummary(api, CacheFile);

        Assert.Null(summary.Current);
        Assert.False(await summary.RefreshAsync());
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(CacheFile));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Envelope(string data) => $$"""{"data":{{data}},"error":null}""";

    private sealed class SummaryHandler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public bool Throw { get; init; }

        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new HttpRequestException("No route to host.");
            }
            Requests.Add(new RecordedRequest(
                request.Method.Method, request.RequestUri?.ToString() ?? "", request.Headers.Authorization?.ToString() ?? "", ""));
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
}
