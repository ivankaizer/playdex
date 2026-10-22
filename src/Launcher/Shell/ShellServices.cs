using Playdex.Core;
using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Playdex.Shell;

/// <summary>
/// What outlives a window: the capture client and offline queue, the session manager (a game keeps being tracked while
/// no window is open), controller input and the library. The launcher has one window for its whole life; the desktop
/// app opens and closes its window from the tray, and each window subscribes here while it is open.
/// </summary>
internal sealed class ShellServices : IDisposable
{
    public ShellServices(AppConfig config, DispatcherQueue dispatcher)
    {
        Config = config;
        Api = new CaptureClient(config.Api);
        Queue = new SessionQueue(Api);
        Summary = new PlaydexSummary(Api);
        Sessions = new SessionManager(
            config,
            Api,
            Queue,
            (gameId, state, seconds) => SessionChanged?.Invoke(gameId, state, seconds),
            detected => GameExited?.Invoke(detected),
            () => Summary.SteamPolling);
        Input = new ControllerInput(new InputQueue(dispatcher, (code, timestamp) => InputReceived?.Invoke(code, timestamp)));
        _libraryScannedAt = Stopwatch.GetTimestamp();
        LibraryScan = Task.Run(() => Library.Scan(Config));
    }

    private long _libraryScannedAt;

    public AppConfig Config { get; }

    public CaptureClient Api { get; }

    public SessionQueue Queue { get; }

    /// <summary>The user's Playdex data the page shows (stats, play time per game); cached between runs.</summary>
    public PlaydexSummary Summary { get; }

    public SessionManager Sessions { get; }

    public ControllerInput Input { get; }

    /// <summary>The games on this PC, scanned off the UI thread. <see cref="RescanLibrary"/> replaces it.</summary>
    public Task<IReadOnlyDictionary<string, LibraryGame>> LibraryScan { get; private set; }

    /// <summary>A game's session changed: (gameId, "starting" | "steam" | "running" | "ended", seconds). Any thread.</summary>
    public event Action<string, string, int>? SessionChanged;

    /// <summary>
    /// A launched game exited and no game launched after it still runs (Stopwatch timestamp of detection). Any thread.
    /// </summary>
    public event Action<long>? GameExited;

    /// <summary>Controller input, on the UI thread.</summary>
    public event Action<InputCode, long>? InputReceived;

    public void Start()
    {
        Input.Start();
        _ = Task.Run(Queue.FlushAsync);
    }

    public LibraryGame? FindGame(string id) =>
        LibraryScan.IsCompletedSuccessfully ? LibraryScan.Result.GetValueOrDefault(id) : null;

    /// <summary>Scan again after games were added, removed or changed.</summary>
    public Task<IReadOnlyDictionary<string, LibraryGame>> RescanLibrary()
    {
        _libraryScannedAt = Stopwatch.GetTimestamp();
        return LibraryScan = Task.Run(() => Library.Scan(Config));
    }

    /// <summary>
    /// The library, scanned again when the last scan is older than <paramref name="maxAge"/>: Steam installs and
    /// uninstalls games while the app waits in the tray or behind a game.
    /// </summary>
    public Task<IReadOnlyDictionary<string, LibraryGame>> FreshLibrary(TimeSpan maxAge) =>
        Stopwatch.GetElapsedTime(_libraryScannedAt) < maxAge ? LibraryScan : RescanLibrary();

    public void Dispose()
    {
        Summary.Dispose();
        Queue.Dispose();
        Api.Dispose();
    }
}
