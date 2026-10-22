using System.Net;

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
        if (Directory.Exists(Path.ChangeExtension(_file, ".running")))
        {
            Directory.Delete(Path.ChangeExtension(_file, ".running"), recursive: true);
        }
    }

    private static QueuedSession Session(string title, int seconds) =>
        new(title, "1", "steam", "pc", seconds, DateTimeOffset.Now.AddSeconds(-seconds), DateTimeOffset.Now);

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(string Uri, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(status);
        }
    }
}
