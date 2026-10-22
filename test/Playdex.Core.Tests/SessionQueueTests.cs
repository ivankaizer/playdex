using System.Net;
using System.Text.Json;

namespace Playdex.Core.Tests;

public sealed class SessionQueueTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"playdex-queue-{Guid.NewGuid():N}.jsonl");

    [Fact]
    public async Task FlushSendsEverySessionAndDeletesTheFile()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _file);
        await queue.EnqueueAsync(Session("Hades", 600));
        await queue.EnqueueAsync(Session("Balatro", 120));

        await queue.FlushAsync();

        Assert.False(File.Exists(_file));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("https://playdex.test/api/capture/sessions", r.Uri);
            Assert.Equal("Bearer key", r.Authorization);
        });
        Assert.Contains("\"gameTitle\":\"Hades\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"durationInSeconds\":600", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlushSendsWhenTheSessionStarted()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _file);
        var startedAt = new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.FromHours(2));
        await queue.EnqueueAsync(new QueuedSession("Hades", "1", "steam", "pc", 600, startedAt, startedAt.AddMinutes(10)));

        await queue.FlushAsync();

        var request = Assert.Single(handler.Requests);
        using var body = System.Text.Json.JsonDocument.Parse(request.Body);
        Assert.Equal(startedAt, body.RootElement.GetProperty("startedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task FlushKeepsEverySessionAfterTheFirstFailure()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _file);
        await queue.EnqueueAsync(Session("Hades", 600));
        await queue.EnqueueAsync(Session("Balatro", 120));

        await queue.FlushAsync();

        Assert.Single(handler.Requests);
        Assert.Equal(2, (await File.ReadAllLinesAsync(_file)).Length);
    }

    [Fact]
    public async Task ARejectedSessionIsSetAsideWithTheReasonAndTheRestStillGo()
    {
        // Playdex refuses the first session for good; the two behind it must not wait for it.
        var handler = new RecordingHandler(
            (HttpStatusCode.BadRequest, """{"data":null,"error":{"error":"Unknown platform."}}"""),
            (HttpStatusCode.OK, ""),
            (HttpStatusCode.OK, ""));
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _file);
        await queue.EnqueueAsync(Session("Hades", 600));
        await queue.EnqueueAsync(Session("Balatro", 120));
        await queue.EnqueueAsync(Session("Celeste", 60));

        await queue.FlushAsync();
        await queue.FlushAsync();

        Assert.Equal(3, handler.Requests.Count);
        Assert.False(File.Exists(_file));
        var rejected = JsonSerializer.Deserialize(
            Assert.Single(await File.ReadAllLinesAsync(queue.RejectedFile)), CaptureJson.Default.RejectedSession)!;
        Assert.Equal("Hades", rejected.Session!.GameTitle);
        Assert.Equal(400, rejected.Status);
        Assert.Equal("HTTP 400: Unknown platform.", rejected.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task AnAnswerThatIsNotAboutTheSessionKeepsTheWholeQueue(HttpStatusCode status)
    {
        var handler = new RecordingHandler(status);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _file);
        await queue.EnqueueAsync(Session("Hades", 600));
        await queue.EnqueueAsync(Session("Balatro", 120));

        await queue.FlushAsync();

        Assert.Single(handler.Requests);
        Assert.Equal(2, (await File.ReadAllLinesAsync(_file)).Length);
        Assert.False(File.Exists(queue.RejectedFile));
    }

    [Fact]
    public async Task AnUnreadableLineIsSetAsideNotDropped()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _file);
        await File.WriteAllTextAsync(_file, "{not json\n");
        await queue.EnqueueAsync(Session("Hades", 600));

        await queue.FlushAsync();

        Assert.Single(handler.Requests);
        Assert.False(File.Exists(_file));
        var rejected = JsonSerializer.Deserialize(
            Assert.Single(await File.ReadAllLinesAsync(queue.RejectedFile)), CaptureJson.Default.RejectedSession)!;
        Assert.Equal("{not json", rejected.Line);
        Assert.Null(rejected.Session);
    }

    [Theory]
    [InlineData(200, DeliveryStatus.Sent)]
    [InlineData(201, DeliveryStatus.Sent)]
    [InlineData(400, DeliveryStatus.Rejected)]
    [InlineData(409, DeliveryStatus.Rejected)]
    [InlineData(413, DeliveryStatus.Rejected)]
    [InlineData(422, DeliveryStatus.Rejected)]
    [InlineData(401, DeliveryStatus.Unavailable)]
    [InlineData(403, DeliveryStatus.Unavailable)]
    [InlineData(404, DeliveryStatus.Unavailable)]
    [InlineData(405, DeliveryStatus.Unavailable)]
    [InlineData(408, DeliveryStatus.Unavailable)]
    [InlineData(429, DeliveryStatus.Unavailable)]
    [InlineData(500, DeliveryStatus.Unavailable)]
    [InlineData(503, DeliveryStatus.Unavailable)]
    public void OnlyA4xxAboutTheSessionIsARefusal(int status, DeliveryStatus expected) =>
        Assert.Equal(expected, Delivery.FromResponse(status, null).Outcome);

    [Theory]
    [InlineData("""{"data":null,"error":{"error":"gameId is required."}}""", "gameId is required.")]
    [InlineData("""{"status":400,"detail":"durationInSeconds must be a positive number."}""", "durationInSeconds must be a positive number.")]
    [InlineData("Bad Request", "Bad Request")]
    [InlineData("", null)]
    public void TheReasonIsTheServersMessage(string body, string? expected) =>
        Assert.Equal(expected, Delivery.ErrorMessage(body));

    [Fact]
    public async Task WithoutAnApiKeyNothingIsSent()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var api = new CaptureClient(null, "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _file);
        await queue.EnqueueAsync(Session("Hades", 600));

        Assert.False(await api.StartAsync(new CaptureGame("Hades", "1145360", "steam", "pc")));
        await queue.FlushAsync();

        Assert.Empty(handler.Requests);
        Assert.Single(await File.ReadAllLinesAsync(_file));
    }

    public void Dispose()
    {
        File.Delete(_file);
        File.Delete(_file + ".lock");
        File.Delete(Path.ChangeExtension(_file, ".rejected.jsonl"));
        if (Directory.Exists(Path.ChangeExtension(_file, ".running")))
        {
            Directory.Delete(Path.ChangeExtension(_file, ".running"), recursive: true);
        }
    }

    private static QueuedSession Session(string title, int seconds) =>
        new(title, "1", "steam", "pc", seconds, DateTimeOffset.Now.AddSeconds(-seconds), DateTimeOffset.Now);

    /// <summary>Answers each request with the next of <paramref name="answers"/>, the last one from then on.</summary>
    private sealed class RecordingHandler(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        public RecordingHandler(HttpStatusCode status)
            : this((status, ""))
        {
        }

        public List<(string Uri, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            var (status, answer) = answers[Math.Min(Requests.Count, answers.Length) - 1];
            return new HttpResponseMessage(status) { Content = new StringContent(answer) };
        }
    }
}
