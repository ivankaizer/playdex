using System.Net;
using System.Text.Json;

namespace Playdex.Core.Tests;

public sealed class SessionRecorderTests : IDisposable
{
    private static readonly CaptureGame _game = new("Hades", "1145360", "steam", "pc");

    private readonly string _queueFile = Path.Combine(Path.GetTempPath(), $"playdex-recorder-{Guid.NewGuid():N}.jsonl");

    [Fact]
    public async Task OnlineSessionStartsPingsAndEnds()
    {
        var handler = new PathHandler(_ => HttpStatusCode.OK);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _queueFile);
        var pings = 0;
        using var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMilliseconds(40), _ => Interlocked.Increment(ref pings));

        recorder.Start();
        await Task.Delay(300);
        var outcome = await recorder.FinishAsync();

        Assert.Equal(SessionOutcome.Ended, outcome);
        Assert.Equal("capture/sessions/start", handler.Paths[0]);
        Assert.Equal("capture/sessions/end", handler.Paths[^1]);
        Assert.True(pings >= 2, $"{pings} ping(s)");
        Assert.All(handler.Paths[1..^1], p => Assert.Equal("capture/sessions/ping", p));
        Assert.False(File.Exists(_queueFile));
    }

    [Fact]
    public async Task OfflineSessionIsQueuedWholeAndNeverPinged()
    {
        var handler = new PathHandler(_ => HttpStatusCode.ServiceUnavailable);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _queueFile);
        using var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMilliseconds(20));

        recorder.Start();
        await Task.Delay(1100);
        var outcome = await recorder.FinishAsync();

        Assert.Equal(SessionOutcome.Queued, outcome);
        Assert.Equal(["capture/sessions/start"], handler.Paths);
        var queued = File.ReadAllLines(_queueFile).Single();
        Assert.Contains("\"gameId\":\"1145360\"", queued, StringComparison.Ordinal);
        Assert.Contains("\"durationInSeconds\":1", queued, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQueuedSessionEndsWhenTheGameExitedNotAfterTheDebounce()
    {
        var handler = new PathHandler(_ => HttpStatusCode.ServiceUnavailable);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _queueFile);
        using var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMinutes(1));

        recorder.Start();
        await Task.Delay(2200);
        var outcome = await recorder.FinishAsync(recorder.StartedAt.AddSeconds(1.1));

        Assert.Equal(SessionOutcome.Queued, outcome);
        var queued = File.ReadAllLines(_queueFile).Single();
        Assert.Contains("\"durationInSeconds\":1,", queued, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedEndIsNotQueuedAsWell()
    {
        var handler = new PathHandler(path => path.EndsWith("/end", StringComparison.Ordinal) ? HttpStatusCode.BadGateway : HttpStatusCode.OK);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _queueFile);
        using var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMinutes(1));

        recorder.Start();
        await Task.Delay(1100);

        Assert.Equal(SessionOutcome.EndFailed, await recorder.FinishAsync());
        Assert.False(File.Exists(_queueFile));
    }

    [Fact]
    public async Task PlayWhilePingsFailLongerThanTheServerTimeoutIsSentWhenTheyGetThroughAgain()
    {
        var api = new Reachability();
        var handler = new PathHandler(_ => api.Up ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        using var client = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(client, _queueFile);
        using var recorder = new SessionRecorder(client, queue, _game, TimeSpan.FromMilliseconds(20))
        {
            ServerTimeout = TimeSpan.FromMilliseconds(100),
        };

        recorder.Start();
        await Task.Delay(150);
        api.Up = false;
        await Task.Delay(1700);
        var reconnected = DateTimeOffset.Now;
        api.Up = true;
        await Task.Delay(300);
        var outcome = await recorder.FinishAsync();

        Assert.Equal(SessionOutcome.Ended, outcome);
        // Only the play after the server closed the session, sent as a finished one while the game still ran.
        var sent = Assert.Single(handler.Requests, request => request.Path == "capture/sessions");
        using var body = JsonDocument.Parse(sent.Body);
        Assert.InRange(body.RootElement.GetProperty("durationInSeconds").GetInt32(), 1, 2);
        var startedAt = body.RootElement.GetProperty("startedAt").GetDateTimeOffset();
        Assert.InRange(startedAt, recorder.StartedAt.AddMilliseconds(100), reconnected.AddSeconds(-1));
        Assert.Equal("capture/sessions/end", handler.Paths[^1]);
        Assert.False(File.Exists(_queueFile));
    }

    [Fact]
    public async Task PlayAfterTheServerClosedTheSessionIsQueuedWhenTheGameExitsUnreachable()
    {
        var api = new Reachability();
        var handler = new PathHandler(_ => api.Up ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        using var client = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(client, _queueFile);
        using var recorder = new SessionRecorder(client, queue, _game, TimeSpan.FromMilliseconds(20))
        {
            ServerTimeout = TimeSpan.FromMilliseconds(100),
        };

        recorder.Start();
        await Task.Delay(150);
        api.Up = false;
        await Task.Delay(1600);
        var outcome = await recorder.FinishAsync();

        Assert.Equal(SessionOutcome.EndFailed, outcome);
        var queued = JsonSerializer.Deserialize(Assert.Single(File.ReadAllLines(_queueFile)), CaptureJson.Default.QueuedSession)!;
        Assert.InRange(queued.DurationInSeconds, 1, 2);
        Assert.True(queued.StartedAt > recorder.StartedAt.AddMilliseconds(100), $"{queued.StartedAt:O}");
    }

    [Fact]
    public async Task PingsFailingForLessThanTheServerTimeoutLoseNothing()
    {
        var api = new Reachability();
        var handler = new PathHandler(path => api.Up || path.EndsWith("/end", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        using var client = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(client, _queueFile);
        using var recorder = new SessionRecorder(client, queue, _game, TimeSpan.FromMilliseconds(20));

        recorder.Start();
        await Task.Delay(100);
        api.Up = false;
        await Task.Delay(1200);
        var outcome = await recorder.FinishAsync();

        Assert.Equal(SessionOutcome.Ended, outcome);
        Assert.DoesNotContain("capture/sessions", handler.Paths);
        Assert.False(File.Exists(_queueFile));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(180, null)]
    [InlineData(180.9, null)]
    [InlineData(181, 1)]
    [InlineData(600, 420)]
    public void OnlyPlayPastTheServersTimeoutIsLost(double secondsSinceTheLastAnswer, int? lost)
    {
        var answeredAt = new DateTimeOffset(2026, 10, 23, 18, 0, 0, TimeSpan.FromHours(2));

        var session = SessionRecorder.Lost(_game, answeredAt, answeredAt.AddSeconds(secondsSinceTheLastAnswer), SessionRecorder.DefaultServerTimeout);

        Assert.Equal(lost, session?.DurationInSeconds);
        if (session is not null)
        {
            Assert.Equal(answeredAt.AddMinutes(3), session.StartedAt);
        }
    }

    [Fact]
    public async Task AnOfflineSessionUnderASecondIsDropped()
    {
        var handler = new PathHandler(_ => HttpStatusCode.ServiceUnavailable);
        using var api = new CaptureClient("key", "https://playdex.test/api", handler);
        using var queue = new SessionQueue(api, _queueFile);
        using var recorder = new SessionRecorder(api, queue, _game, TimeSpan.FromMinutes(1));

        recorder.Start();

        Assert.Equal(SessionOutcome.Dropped, await recorder.FinishAsync());
        Assert.False(File.Exists(_queueFile));
    }

    [Fact]
    public async Task TwoQueuesOnOneFileKeepEverySession()
    {
        // The launcher and the Wrapper are separate processes with their own queue objects on one file.
        using var api = new CaptureClient(null, "https://playdex.test/api", new PathHandler(_ => HttpStatusCode.OK));
        using var first = new SessionQueue(api, _queueFile);
        using var second = new SessionQueue(api, _queueFile);

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => (i % 2 == 0 ? first : second).EnqueueAsync(
            new QueuedSession($"Game {i}", $"{i}", "steam", "pc", 60, DateTimeOffset.Now.AddMinutes(-1), DateTimeOffset.Now)))));

        Assert.Equal(40, File.ReadAllLines(_queueFile).Length);
    }

    public void Dispose()
    {
        File.Delete(_queueFile);
        File.Delete(_queueFile + ".lock");
        if (Directory.Exists(Path.ChangeExtension(_queueFile, ".running")))
        {
            Directory.Delete(Path.ChangeExtension(_queueFile, ".running"), recursive: true);
        }
    }

    private sealed class Reachability
    {
        private volatile bool _up = true;

        public bool Up
        {
            get => _up;
            set => _up = value;
        }
    }

    private sealed class PathHandler(Func<string, HttpStatusCode> status) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<(string Path, string Body)> _requests = [];

        public string[] Paths => [.. Requests.Select(request => request.Path)];

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
            return new HttpResponseMessage(status(request.RequestUri.AbsolutePath));
        }
    }
}
