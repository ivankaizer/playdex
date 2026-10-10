using System.Diagnostics;

namespace Playdex.Core;

/// <summary>
/// Launches a game, follows it to exit, records the session to Playdex (<see cref="SessionRecorder"/>) and reports each
/// state change (gameId, "starting" | "running" | "ended", seconds played) through <paramref name="report"/>, from any
/// thread.
/// </summary>
public sealed class SessionManager(
    AppConfig config, CaptureClient api, SessionQueue queue, Action<string, string, int> report, Action<long> gameExited)
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

    /// <summary>The game launched here, once it runs; null before that and after it exits.</summary>
    public RunningGame? Current
    {
        get
        {
            lock (_gate)
            {
                return _active is { RunningSince: { } since } session
                    ? new RunningGame(session.GameId, DateTimeOffset.Now - Stopwatch.GetElapsedTime(since), null)
                    : null;
            }
        }
    }

    /// <summary>UI thread. <paramref name="game"/> is the host's own library entry (null: not a game on this PC).</summary>
    public void Launch(string gameId, LibraryGame? game)
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

        IGameWatcher? watcher = null;
        CaptureGame? capture = null;
        if (game?.ConfigGameId is { } configId && config.Games.TryGetValue(configId, out var configured))
        {
            // A game added to Playdex: started directly with its launch settings and monitor, recorded here.
            watcher = new ConfiguredGameWatcher(config, configured);
            capture = CaptureGame.From(configured, config.Api);
        }
        else if (game?.SteamAppId is { } appId)
        {
            watcher = SteamGameWatcher.Create(appId);
            capture = new CaptureGame(game.Title, appId.ToString(System.Globalization.CultureInfo.InvariantCulture), "steam", "pc");
        }
        if (watcher is null)
        {
            Log.Info($"session: no way to launch {gameId}");
            Post(gameId, "ended", 0);
            return;
        }

        var session = new ActiveSession(gameId, watcher, capture);
        lock (_gate)
        {
            _active = session;
        }
        watcher.Running += () => OnRunning(session);
        watcher.Exited += (detected, processExit) => OnExited(session, detected, processExit);
        Post(gameId, "starting", 0);
        try
        {
            watcher.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error($"session: could not launch {gameId}", ex);
            lock (_gate)
            {
                if (_active == session)
                {
                    _active = null;
                }
            }
            watcher.Dispose();
            Post(gameId, "ended", 0);
        }
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
        Post(session.GameId, "running", 0);
        if (session.Capture is { } capture)
        {
            session.Recorder = new SessionRecorder(api, queue, capture, _pingInterval, seconds => Post(session.GameId, "running", seconds));
            session.Recorder.Start();
        }
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

        if (processExit != 0)
        {
            // Both clocks to FILETIME: detection time = now - (time since detection).
            var detectedFileTime = Native.PreciseNow() - (long)(Stopwatch.GetElapsedTime(detected).TotalMilliseconds * 10_000);
            Metrics.Record("exit.detection", (detectedFileTime - processExit) / 10_000.0, $"{session.GameId}: process exit -> exit event");
        }

        Post(session.GameId, "ended", session.Seconds);
        gameExited(detected);
        session.Watcher.Dispose();

        if (session.Recorder is not { } recorder)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            using (recorder)
            {
                // The game's own exit time where the watcher knows it (the monitor's debounce is not play).
                var outcome = await recorder.FinishAsync(processExit != 0 ? DateTimeOffset.FromFileTime(processExit) : null);
                Metrics.Record(
                    outcome == SessionOutcome.Queued ? "exit.session_end_queued" : "exit.session_end_acked",
                    Metrics.MsSince(detected),
                    $"{session.GameId}: exit event -> {outcome}");
            }
        });
    }

    private void Post(string gameId, string state, int seconds) => report(gameId, state, seconds);

    private sealed class ActiveSession(string gameId, IGameWatcher watcher, CaptureGame? capture)
    {
        public string GameId { get; } = gameId;
        public IGameWatcher Watcher { get; } = watcher;
        public CaptureGame? Capture { get; } = capture;
        public SessionRecorder? Recorder { get; set; }
        public long? RunningSince { get; set; }

        public int Seconds => RunningSince is { } since ? (int)Stopwatch.GetElapsedTime(since).TotalSeconds : 0;
    }
}
