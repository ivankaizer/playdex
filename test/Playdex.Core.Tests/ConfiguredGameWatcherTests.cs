using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class ConfiguredGameWatcherTests
{
    [Fact]
    public async Task StartsTheGameHoldsItsLockAndReportsItsExitTime()
    {
        var directory = NewTempDir();
        var config = new AppConfig { Storage = new StorageConfig { StateDirectory = directory, LogDirectory = directory } };
        var game = config.GetOrCreateGame($"watched-{Guid.NewGuid():N}");
        game.Launch.ExecutablePath = TestProcess.ExecutablePath;
        game.Launch.Arguments = [.. TestProcess.Arguments];
        using var watcher = new ConfiguredGameWatcher(config, game);
        var running = false;
        var exited = new TaskCompletionSource<long>();
        watcher.Running += () => running = true;
        watcher.Exited += (_, processExit) => exited.TrySetResult(processExit);

        watcher.Start();

        Assert.True(running);
        Assert.True(GameLock.IsHeld(directory, game.GameId), "the game is locked while it runs");
        var exitTime = DateTimeOffset.FromFileTime(await exited.Task.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.True(DateTimeOffset.Now - exitTime < TimeSpan.FromSeconds(2), "the reported time is the game's exit");
        await Task.Delay(200);
        Assert.False(GameLock.IsHeld(directory, game.GameId), "the lock is released after the game exits");
    }

    [Fact]
    public void ASecondStartOfTheSameGameFailsWhileItRuns()
    {
        var directory = NewTempDir();
        var config = new AppConfig { Storage = new StorageConfig { StateDirectory = directory, LogDirectory = directory } };
        var game = config.GetOrCreateGame($"locked-{Guid.NewGuid():N}");
        game.Launch.ExecutablePath = TestProcess.ExecutablePath;
        game.Launch.Arguments = [.. TestProcess.Arguments];
        using var held = GameLock.Acquire(directory, game.GameId);
        using var watcher = new ConfiguredGameWatcher(config, game);

        Assert.Throws<InvalidOperationException>(watcher.Start);
    }
}
