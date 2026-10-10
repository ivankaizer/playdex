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

    public event Action? Running;

    public event Action<long, long>? Exited;

    public void Start()
    {
        _lock = GameLock.Acquire(config.Storage.StateDirectory, game.GameId);
        Process process;
        try
        {
            process = GameProcess.Start(game);
        }
        catch
        {
            _lock.Dispose();
            throw;
        }
        Log.Info($"game: started {game.DisplayName} ({game.Launch.ExecutablePath})");
        Running?.Invoke();
        _ = Task.Run(async () =>
        {
            using (process)
            {
                try
                {
                    var exitedAt = await new ProcessMonitor(game.Monitor).WaitForExitAsync(process, _stop.Token);
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
