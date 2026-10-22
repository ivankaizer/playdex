using System.Diagnostics;

namespace Playdex.Core;

public enum SessionOutcome
{
    /// <summary>Started online and ended with POST /capture/sessions/end.</summary>
    Ended,

    /// <summary>
    /// Started online but the end request failed. The server closes the session 3 minutes after the last ping, so at
    /// most one ping interval of play is lost; it is not queued as well, which would count it twice.
    /// </summary>
    EndFailed,

    /// <summary>The start request failed, so the whole session went to the offline queue.</summary>
    Queued,

    /// <summary>Offline and under a second long: nothing worth recording.</summary>
    Dropped,
}

/// <summary>
/// One play session sent to Playdex: start when the game runs, a ping every interval while it does, end when it exits.
/// A session is either online (start succeeded) or offline (queued whole at exit), never both, so it can't be counted
/// twice. Used by the launcher's <see cref="SessionManager"/> and by the Wrapper. While it runs it is kept in the
/// queue's <see cref="SessionJournal"/>, updated every interval, so an offline session whose process ends before the
/// game does (a crash, a shutdown) is still queued, from its real start, by the next flush.
/// </summary>
public sealed class SessionRecorder(
    CaptureClient api, SessionQueue queue, CaptureGame game, TimeSpan pingInterval, Action<int>? pinged = null) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task<bool>? _run;
    private long _runningSince;
    private RunningSession? _journal;

    public CaptureGame Game => game;

    public DateTimeOffset StartedAt { get; private set; }

    public int Seconds => _runningSince == 0 ? 0 : (int)Stopwatch.GetElapsedTime(_runningSince).TotalSeconds;

    /// <summary>The game is running: send start, then ping until <see cref="FinishAsync"/>.</summary>
    public void Start()
    {
        if (_run is not null)
        {
            throw new InvalidOperationException("The session has already started.");
        }
        _runningSince = Stopwatch.GetTimestamp();
        StartedAt = DateTimeOffset.Now;
        _journal = queue.Journal.Begin(game, StartedAt);
        _run = Task.Run(RunAsync);
    }

    /// <summary>
    /// The game has exited: end the session online, or queue it if it never got online. <paramref name="exitedAt"/> is
    /// when the game's last process exited, if that was before now (the Wrapper's exit debounce); a queued session ends
    /// there. An online session ends when the end request arrives: the API takes no end time yet.
    /// </summary>
    public async Task<SessionOutcome> FinishAsync(DateTimeOffset? exitedAt = null)
    {
        if (_run is null)
        {
            throw new InvalidOperationException("The session has not started.");
        }
        var endedAt = exitedAt is { } exit && exit < DateTimeOffset.Now ? exit : DateTimeOffset.Now;
        var seconds = Math.Min(Seconds, Math.Max(0, (int)(endedAt - StartedAt).TotalSeconds));
        await _stop.CancelAsync();
        // A start request still in flight decides whether this session is online, so wait for it.
        var online = await _run;

        if (online)
        {
            var ended = await api.EndAsync(game);
            _journal?.Complete(); // the server has the session either way
            if (!ended)
            {
                Log.Info($"session: end for {game.GameTitle} failed; the server will close it from the last ping");
                return SessionOutcome.EndFailed;
            }
            await queue.FlushAsync();
            return SessionOutcome.Ended;
        }
        if (seconds < 1)
        {
            _journal?.Complete();
            return SessionOutcome.Dropped;
        }
        await queue.EnqueueAsync(new QueuedSession(
            game.GameTitle, game.GameId, game.GameIdProvider, game.Platform, seconds, StartedAt, endedAt));
        _journal?.Complete();
        return SessionOutcome.Queued;
    }

    private async Task<bool> RunAsync()
    {
        var online = await api.StartAsync(game);
        if (online)
        {
            _journal?.Seen(DateTimeOffset.Now, online: true);
        }
        else
        {
            Log.Info($"session: {game.GameTitle} offline; it will be queued at exit");
        }
        try
        {
            using var timer = new PeriodicTimer(pingInterval);
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                // Still running: how far an offline session is recovered if this process ends before the game.
                _journal?.Seen(DateTimeOffset.Now);
                if (online)
                {
                    await api.PingAsync(game);
                    pinged?.Invoke(Seconds);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        return online;
    }

    /// <summary>
    /// Stops pinging. A session that was not finished (<see cref="FinishAsync"/>) stays in the journal, to be
    /// recovered. The token source is not disposed: the ping loop may still be reading it.
    /// </summary>
    public void Dispose()
    {
        _stop.Cancel();
        _journal?.Dispose();
    }
}
