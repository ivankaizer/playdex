using System.Diagnostics;
using System.Text.Json;

namespace Playdex.Launcher.Host;

/// <summary>
/// Launches a game for the UI, follows it to exit, records the session to Playdex and reports
/// {type:"session", gameId, state:"starting|running|ended", seconds} back to the UI.
/// </summary>
internal sealed class SessionManager(CaptureClient api, SessionQueue queue, Action<string> postToUi, Action<long> gameExited)
{
    private static readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private ActiveSession? _active;

    public bool IsGameRunning
    {
        get
        {
            lock (_gate)
            {
                return _active is { RunningSince: not null };
            }
        }
    }

    /// <summary>UI thread.</summary>
    public void Launch(string gameId, string title, int? steamAppId)
    {
        lock (_gate)
        {
            if (_active is not null)
            {
                Log.Info($"session: {_active.GameId} is still active; ignoring launch of {gameId}");
                Post(gameId, _active.GameId == gameId ? (_active.RunningSince is null ? "starting" : "running") : "ended", 0);
                return;
            }
        }

        var watcher = steamAppId is { } appId ? SteamGameWatcher.Create(appId) : null;
        if (watcher is null)
        {
            Log.Info($"session: no way to launch {gameId}");
            Post(gameId, "ended", 0);
            return;
        }

        var capture = steamAppId is { } id ? new CaptureGame(title, id.ToString(System.Globalization.CultureInfo.InvariantCulture), "steam", "pc") : null;
        var session = new ActiveSession(gameId, watcher, capture);
        lock (_gate)
        {
            _active = session;
        }
        watcher.Running += () => OnRunning(session);
        watcher.Exited += (detected, processExit) => OnExited(session, detected, processExit);
        Post(gameId, "starting", 0);
        watcher.Start();
    }

    /// <summary>UI thread. Cancels a launch that has not started running; a running game keeps being tracked.</summary>
    public void Cancel(string gameId)
    {
        lock (_gate)
        {
            if (_active is not { RunningSince: null } session || session.GameId != gameId)
            {
                return;
            }
            _active = null;
            session.Watcher.Dispose();
        }
        Log.Info($"session: launch of {gameId} cancelled");
    }

    /// <summary>Directly started executables (Job Object tracking), for the self-test and future non-Steam games.</summary>
    public static IGameWatcher LaunchExecutable(string executable, string arguments) => new JobGameWatcher(executable, arguments);

    private void OnRunning(ActiveSession session)
    {
        session.RunningSince = Stopwatch.GetTimestamp();
        session.StartedAt = DateTimeOffset.Now;
        Post(session.GameId, "running", 0);
        if (session.Capture is null)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            session.Online = await api.StartAsync(session.Capture);
            if (!session.Online)
            {
                Log.Info($"session: {session.GameId} offline; it will be queued at exit");
                return;
            }
            using var timer = new PeriodicTimer(_pingInterval);
            while (await timer.WaitForNextTickAsync(session.Stop.Token).ConfigureAwait(false) is true)
            {
                await api.PingAsync(session.Capture);
                Post(session.GameId, "running", session.Seconds);
            }
        }).ContinueWith(t => { }, TaskScheduler.Default);
    }

    private void OnExited(ActiveSession session, long detected, long processExit)
    {
        lock (_gate)
        {
            if (_active != session)
            {
                return;
            }
            _active = null;
        }
        session.Stop.Cancel();

        if (processExit != 0)
        {
            // Both clocks to FILETIME: detection time = now - (time since detection).
            var detectedFileTime = Native.PreciseNow() - (long)(Stopwatch.GetElapsedTime(detected).TotalMilliseconds * 10_000);
            Metrics.Record("exit.detection", (detectedFileTime - processExit) / 10_000.0, $"{session.GameId}: process exit -> exit event");
        }

        var seconds = session.Seconds;
        Post(session.GameId, "ended", seconds);
        gameExited(detected);
        session.Watcher.Dispose();

        if (session.Capture is null || session.RunningSince is null)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            var recorded = false;
            if (session.Online)
            {
                var handedOff = Metrics.MsSince(detected);
                recorded = await api.EndAsync(session.Capture);
                Metrics.Record("exit.session_end_sent", handedOff, $"{session.GameId}: exit event -> end request");
                Metrics.Record("exit.session_end_acked", Metrics.MsSince(detected), $"{session.GameId}: exit event -> API 2xx={recorded}");
                if (!recorded)
                {
                    // The server already holds this session and closes it 3 minutes after the last ping, so it is not
                    // queued again (that would double count). At most one ping interval of play is lost.
                    Log.Info($"session: end for {session.GameId} failed; the server will close it from the last ping");
                }
            }
            else if (seconds >= 1)
            {
                var c = session.Capture;
                await queue.EnqueueAsync(new QueuedSession(
                    c.GameTitle, c.GameId, c.GameIdProvider, c.Platform, seconds, session.StartedAt, DateTimeOffset.Now));
                Metrics.Record("exit.session_end_queued", Metrics.MsSince(detected), $"{session.GameId}: exit event -> queued");
                return;
            }
            if (recorded)
            {
                await queue.FlushAsync();
            }
        });
    }

    private void Post(string gameId, string state, int seconds) =>
        postToUi($$"""{"type":"session","gameId":{{JsonSerializer.Serialize(gameId, HostJson.Default.String)}},"state":"{{state}}","seconds":{{seconds}}}""");

    private sealed class ActiveSession(string gameId, IGameWatcher watcher, CaptureGame? capture)
    {
        public string GameId { get; } = gameId;
        public IGameWatcher Watcher { get; } = watcher;
        public CaptureGame? Capture { get; } = capture;
        public CancellationTokenSource Stop { get; } = new();
        public long? RunningSince { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public bool Online { get; set; }

        public int Seconds => RunningSince is { } since ? (int)Stopwatch.GetElapsedTime(since).TotalSeconds : 0;
    }
}
