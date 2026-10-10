using System.Net;

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
    }

    private sealed class PathHandler(Func<string, HttpStatusCode> status) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<string> _paths = [];

        public string[] Paths
        {
            get
            {
                lock (_gate)
                {
                    return [.. _paths];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/')["api/".Length..];
            lock (_gate)
            {
                _paths.Add(path);
            }
            return Task.FromResult(new HttpResponseMessage(status(request.RequestUri.AbsolutePath)));
        }
    }
}
