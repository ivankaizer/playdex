using System.Net;
using System.Text.Json;
using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

/// <summary>
/// A session whose process ends before the game does: Playdex crashes, Windows shuts down, the app is quit while a game
/// runs. Simulated by disposing the recorder without finishing it, which lets go of its journal file as a process
/// that ends does.
/// </summary>
public sealed class SessionJournalTests
{
    private static readonly CaptureGame _game = new("Hades", "1145360", "steam", "pc");

    private readonly string _queueFile = Path.Combine(NewTempDir(), "queue.jsonl");

    [Fact]
    public async Task AnOfflineSessionLeftRunningIsQueuedWithItsRealStart()
    {
        var offline = new Handler(_ => HttpStatusCode.ServiceUnavailable);
        DateTimeOffset startedAt;
        using (var api = new CaptureClient("key", "https://playdex.test/api", offline))
        using (var queue = new SessionQueue(api, _queueFile))
        using (var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMilliseconds(50)))
        {
            recorder.Start();
            startedAt = recorder.StartedAt;
            await Task.Delay(1500);
        }

        // The next start of the app flushes the queue, now online.
        var online = new Handler(_ => HttpStatusCode.OK);
        using (var api = new CaptureClient("key", "https://playdex.test/api", online))
        using (var queue = new SessionQueue(api, _queueFile))
        {
            await queue.FlushAsync();
        }

        var (path, body) = Assert.Single(online.Requests);
        Assert.Equal("capture/sessions", path);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(startedAt, json.RootElement.GetProperty("startedAt").GetDateTimeOffset());
        Assert.InRange(json.RootElement.GetProperty("durationInSeconds").GetInt32(), 1, 2);
        Assert.Empty(Directory.GetFiles(Path.ChangeExtension(_queueFile, ".running")));
        Assert.False(File.Exists(_queueFile));
    }

    [Fact]
    public async Task AnOnlineSessionLeftRunningIsLeftToTheServer()
    {
        var handler = new Handler(_ => HttpStatusCode.OK);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _queueFile);
        using (var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMilliseconds(50)))
        {
            recorder.Start();
            await Task.Delay(1200);
        }
        await Task.Delay(100);

        await queue.FlushAsync();

        // The server closes it from its last ping; queuing it as well would count it twice.
        Assert.DoesNotContain(handler.Requests, request => request.Path == "capture/sessions");
        Assert.Empty(Directory.GetFiles(Path.ChangeExtension(_queueFile, ".running")));
    }

    [Fact]
    public async Task ASessionStillRunningIsNotRecoveredAndIsQueuedOnceWhenItEnds()
    {
        var offline = new Handler(_ => HttpStatusCode.ServiceUnavailable);
        using var api = new CaptureClient("key", "https://playdex.test/api", offline);
        using var queue = new SessionQueue(api, _queueFile);
        using var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMilliseconds(50));
        recorder.Start();
        await Task.Delay(1200);

        // Another process (a game's launcher) flushes the same queue while the game runs.
        using (var otherApi = new CaptureClient("key", "https://playdex.test/api", new Handler(_ => HttpStatusCode.OK)))
        using (var other = new SessionQueue(otherApi, _queueFile))
        {
            await other.FlushAsync();
        }
        Assert.False(File.Exists(_queueFile));
        Assert.Single(Directory.GetFiles(Path.ChangeExtension(_queueFile, ".running")));

        Assert.Equal(SessionOutcome.Queued, await recorder.FinishAsync());
        await queue.FlushAsync();

        Assert.Single(File.ReadAllLines(_queueFile));
        Assert.Empty(Directory.GetFiles(Path.ChangeExtension(_queueFile, ".running")));
    }

    private sealed class Handler(Func<string, HttpStatusCode> status) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<(string Path, string Body)> _requests = [];

        public (string Path, string Body)[] Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/')["api/".Length..];
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate)
            {
                _requests.Add((path, body));
            }
            return new HttpResponseMessage(status(path));
        }
    }
}
