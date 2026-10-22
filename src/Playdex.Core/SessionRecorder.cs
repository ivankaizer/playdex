using System.Diagnostics;

namespace Playdex.Core;

public enum SessionOutcome
{
    /// <summary>Started online and ended with POST /capture/sessions/end.</summary>
    Ended,

    /// <summary>
    /// Started online but the end request failed. The server closes the session 3 minutes after the last ping it got,
    /// so it is not queued as well, which would count it twice; only play after that close (pings had failed for
    /// longer than the server's timeout) is queued.
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
/// <remarks>
/// An online session whose pings stop reaching Playdex for longer than <see cref="ServerTimeout"/> (Wi-Fi gone, the
/// server down) is closed by the server <see cref="ServerTimeout"/> after the last ping it got. The play from there on
/// is not lost: when a ping gets through again (the server opens a new session with it) or the game exits, that play
/// (<see cref="Lost"/>) is queued as a finished session with its real start, and sent once Playdex answers. The server
/// records a session sent with its start only where it has nothing yet, so what it did record is not counted twice.
/// </remarks>
public sealed class SessionRecorder(
    CaptureClient api, SessionQueue queue, CaptureGame game, TimeSpan pingInterval, Action<int>? pinged = null) : IDisposable
{
    /// <summary>
    /// How long Playdex keeps an online session open without a ping (ApiCaptureService.PingTimeoutMinutes): it then
    /// closes the session that long after the last ping it got, and the next ping opens a new one.
    /// </summary>
    public static readonly TimeSpan DefaultServerTimeout = TimeSpan.FromMinutes(3);

    private readonly CancellationTokenSource _stop = new();
    private Task<bool>? _run;
    private long _runningSince;
    private RunningSession? _journal;

    // The last time Playdex answered for the online session (its start or a ping); null while offline.
    private DateTimeOffset? _answeredAt;

    // Lost play queued while the game runs, being sent; finished before the session is.
    private Task _sending = Task.CompletedTask;

    /// <summary><see cref="DefaultServerTimeout"/>; shorter in tests.</summary>
    internal TimeSpan ServerTimeout { get; init; } = DefaultServerTimeout;

    public CaptureGame Game => game;

    /// <summary>
    /// The play an online session lost on Playdex while its pings could not get through: Playdex closed the session
    /// <paramref name="serverTimeout"/> after <paramref name="answeredAt"/> (its last answer) and has nothing from there
    /// to <paramref name="until"/> (the exit, the ping that got through again, the game last seen running). Null when
    /// that is under a second.
    /// </summary>
    internal static QueuedSession? Lost(CaptureGame game, DateTimeOffset answeredAt, DateTimeOffset until, TimeSpan serverTimeout)
    {
        var closedAt = answeredAt + serverTimeout;
        var seconds = (int)(until - closedAt).TotalSeconds;
        return seconds < 1
            ? null
            : new QueuedSession(game.GameTitle, game.GameId, game.GameIdProvider, game.Platform, seconds, closedAt, until);
    }

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
        await _sending;

        if (online)
        {
            var ended = await api.EndAsync(game);
            // Pings had not got through for longer than the server's timeout: it closed the session, and the play
            // from there to the exit goes to the queue.
            if (_answeredAt is { } answered && Lost(game, answered, endedAt, ServerTimeout) is { } lost)
            {
                Log.Info($"session: Playdex last answered for {game.GameTitle} at {answered:O}; queuing the {lost.DurationInSeconds}s after it closed the session");
                await queue.EnqueueAsync(lost);
            }
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
            _answeredAt = DateTimeOffset.Now;
            _journal?.Seen(_answeredAt.Value, online: true, answeredAt: _answeredAt);
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
                var now = DateTimeOffset.Now;
                if (online && await api.PingAsync(game))
                {
                    await AnsweredAsync(now);
                }
                // Still running: how far a session is recovered if this process ends before the game.
                _journal?.Seen(now, answeredAt: _answeredAt);
                if (online)
                {
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
    /// Playdex answered the ping sent at <paramref name="sentAt"/>. After a silence longer than its timeout it had
    /// closed the session and this ping opened a new one: the play in between is queued, then sent alongside the pings.
    /// </summary>
    private async Task AnsweredAsync(DateTimeOffset sentAt)
    {
        if (_answeredAt is { } answered && Lost(game, answered, sentAt, ServerTimeout) is { } lost)
        {
            Log.Info($"session: Playdex answers for {game.GameTitle} again after {(sentAt - answered).TotalMinutes:F1} min; queuing the {lost.DurationInSeconds}s it lost");
            await queue.EnqueueAsync(lost);
            _sending = SendQueuedAsync(_sending);
        }
        _answeredAt = DateTimeOffset.Now;
    }

    private async Task SendQueuedAsync(Task previous)
    {
        await previous;
        try
        {
            await queue.FlushAsync();
        }
        catch (ObjectDisposedException)
        {
            // Let go of without finishing (the process is ending): the queue is sent at the next start.
        }
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
