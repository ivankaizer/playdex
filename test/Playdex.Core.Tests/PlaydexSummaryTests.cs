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
    public async Task TheKeyIsNotSentOverPlainHttpToAnotherHost()
    {
        var handler = new SummaryHandler(Envelope("""{"generatedAt":"2026-10-10T16:00:00Z"}"""));
        using var api = new CaptureClient("plx_key", "http://playdex.test/api", handler);

        Assert.Null(await api.GetLauncherSummaryAsync());
        var check = await api.CheckKeyAsync();

        Assert.Empty(handler.Requests);
        Assert.Equal(ApiKeyStatus.Unreachable, check.Status);
        Assert.Contains("HTTPS", check.Detail, StringComparison.Ordinal);

        // A server on this PC is fine over HTTP.
        using var local = new CaptureClient("plx_key", "http://localhost:5000/api", handler);
        Assert.NotNull(await local.GetLauncherSummaryAsync());
        Assert.Equal("Bearer plx_key", Assert.Single(handler.Requests).Authorization);
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

    [Fact]
    public async Task PolledProvidersComeFromTheSummaryAndAreOffWithoutOne()
    {
        var handler = new SummaryHandler(Envelope("""{"generatedAt":"2026-10-10T16:00:00Z","steamPolling":true,"polledProviders":["steam","xbox"]}"""));
        using var api = new CaptureClient("plx_key", "https://playdex.test/api", handler);
        using var summary = new PlaydexSummary(api, CacheFile);
        Assert.False(summary.IsPolled("xbox"));

        Assert.True(await summary.RefreshAsync());
        Assert.True(summary.IsPolled("steam"));
        Assert.True(summary.IsPolled("xbox"));
        Assert.False(summary.IsPolled("psn"));

        handler.Body = Envelope("""{"generatedAt":"2026-10-10T16:05:00Z","steamPolling":false,"polledProviders":[]}""");
        Assert.True(await summary.RefreshAsync());
        Assert.False(summary.IsPolled("steam"));
        Assert.False(summary.IsPolled("xbox"));
    }

    [Fact]
    public async Task AnOlderServerOnlySaysWhetherItPollsSteam()
    {
        var handler = new SummaryHandler(Envelope("""{"generatedAt":"2026-10-10T16:00:00Z","steamPolling":true}"""));
        using var api = new CaptureClient("plx_key", "https://playdex.test/api", handler);
        using var summary = new PlaydexSummary(api, CacheFile);

        Assert.True(await summary.RefreshAsync());
        Assert.True(summary.IsPolled("steam"));
        Assert.False(summary.IsPolled("xbox"));
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

    [Fact]
    public async Task FetchesAYearsRecapWithTheKeyInThePagesTimeZone()
    {
        var handler = new SummaryHandler(Envelope("""{"totalSessions":12}"""));
        using var api = new CaptureClient("plx_key", "https://playdex.test/api", handler);

        Assert.Equal("""{"totalSessions":12}""", await api.GetLauncherRecapAsync(2025, "Europe/Warsaw"));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://playdex.test/api/launcher/recap?year=2025&tz=Europe/Warsaw", Uri.UnescapeDataString(request.Uri));
        Assert.Equal("Bearer plx_key", request.Authorization);

        // A page that sends no zone gets this PC's.
        await api.GetLauncherRecapAsync(2025);
        Assert.Equal(
            $"https://playdex.test/api/launcher/recap?year=2025&tz={TimeZoneInfo.Local.Id}",
            Uri.UnescapeDataString(handler.Requests[1].Uri));

        handler.Body = "";
        using var offline = new CaptureClient("plx_key", "https://playdex.test/api", new SummaryHandler("") { Throw = true });
        Assert.Null(await offline.GetLauncherRecapAsync(2025));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, ApiKeyStatus.Accepted, null)]
    [InlineData(HttpStatusCode.Unauthorized, ApiKeyStatus.Rejected, null)]
    [InlineData(HttpStatusCode.Forbidden, ApiKeyStatus.Rejected, null)]
    [InlineData(HttpStatusCode.NotFound, ApiKeyStatus.Unreachable, "HTTP 404")]
    [InlineData(HttpStatusCode.BadGateway, ApiKeyStatus.Unreachable, "HTTP 502")]
    public async Task ChecksTheKeyWithTheSummaryRequest(HttpStatusCode status, ApiKeyStatus expected, string? detail)
    {
        var handler = new SummaryHandler(Envelope("{}")) { Status = status };
        using var api = new CaptureClient(" plx_key ", "https://playdex.test/api/", handler);

        Assert.Equal(new ApiKeyCheck(expected, detail), await api.CheckKeyAsync());
        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://playdex.test/api/launcher/summary?", request.Uri, StringComparison.Ordinal);
        Assert.Equal("Bearer plx_key", request.Authorization);
    }

    [Fact]
    public async Task AKeyCheckWithoutAnAnswerIsUnreachableAndWithoutAKeyIsRejected()
    {
        using var offline = new CaptureClient("plx_key", "https://playdex.test/api", new SummaryHandler("") { Throw = true });
        Assert.Equal(new ApiKeyCheck(ApiKeyStatus.Unreachable, "No route to host."), await offline.CheckKeyAsync());

        var handler = new SummaryHandler("");
        using var keyless = new CaptureClient(" ", "https://playdex.test/api", handler);
        Assert.Equal(ApiKeyStatus.Rejected, (await keyless.CheckKeyAsync()).Status);
        Assert.Empty(handler.Requests);
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
