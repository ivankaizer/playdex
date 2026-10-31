using System.Diagnostics;

namespace Playdex.Core;

/// <summary>
/// A game added to Playdex (the config's games), started directly and followed to exit by <see cref="ProcessMonitor"/>
/// with the game's monitor settings. It holds the game's <see cref="GameLock"/> while it runs, so the same game started
/// at the same time through its Steam shortcut (Playdex.Run) is not recorded twice: the second start fails.
/// </summary>
public sealed class ConfiguredGameWatcher(AppConfig config, GameConfig game) : IGameWatcher
{
    private readonly CancellationTokenSource _stop = new();
    private GameLock? _lock;

    // 0 until started; -1 started, but its process (an Xbox app game's starter) was already gone.
    private int _processId;

    public event Action? Running;

    public event Action<long, long>? Exited;

    /// <summary>
    /// What Playdex started, what a script started, and what runs from the game's folder: a launcher's game (Unreal's
    /// Game.exe starts Binaries\Win64\Game-Win64-Shipping.exe), which has the window.
    /// </summary>
    public IReadOnlyCollection<int> ProcessIds
    {
        get
        {
            if (Volatile.Read(ref _processId) is var started && started == 0)
            {
                return [];
            }
            var folder = game.Monitor.Mode == MonitorMode.Folder ? game.Monitor.Folder : Path.GetDirectoryName(game.Launch.ExecutablePath);
            return [.. started > 0 ? [started] : Array.Empty<int>(), .. ProcessTree.ProcessesOf(game.GameId), .. folder is null ? [] : ProcessMonitor.ProcessesIn(folder)];
        }
    }

    public void Start()
    {
        _lock = GameLock.Acquire(config.Storage.StateDirectory, game.GameId);
        StartedGame started;
        try
        {
            started = GameProcess.Start(game);
        }
        catch
        {
            _lock.Dispose();
            throw;
        }
        Volatile.Write(ref _processId, started.Process?.Id ?? -1);
        Log.Info($"game: started {game.DisplayName} ({game.Launch.ExecutablePath})");
        Running?.Invoke();
        _ = Task.Run(async () =>
        {
            using (started)
            {
                try
                {
                    var exitedAt = await new ProcessMonitor(game.Monitor).WaitForExitAsync(started, _stop.Token);
                    Log.Info($"game: {game.DisplayName} exited");
                    Exited?.Invoke(Stopwatch.GetTimestamp(), exitedAt.ToFileTime());
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                }
                finally
                {
                    _lock.Dispose();
                }
            }
        });
    }

    /// <summary>Stops following the game (it keeps running); the monitoring task then releases the lock.</summary>
    public void Dispose() => _stop.Cancel();
}
