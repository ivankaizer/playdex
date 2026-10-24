using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class GameLockTests
{
    [Fact]
    public void IsHeldOnlyWhileAProcessHoldsTheLock()
    {
        var directory = NewTempDir();
        Assert.False(GameLock.IsHeld(directory, "hades"), "no lock file yet");

        using (GameLock.Acquire(directory, "hades"))
        {
            Assert.True(GameLock.IsHeld(directory, "HADES"));
            Assert.False(GameLock.IsHeld(directory, "balatro"));
            Assert.Throws<InvalidOperationException>(() => GameLock.Acquire(directory, "hades"));
        }

        Assert.False(GameLock.IsHeld(directory, "hades"));
    }

    [Fact]
    public async Task CheckingTheLockNeverMakesAStartFail()
    {
        // The backup scheduler checks the lock while the game may be starting: the check must not take the lock.
        var directory = NewTempDir();
        using var stop = new CancellationTokenSource();
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = Task.Run(() =>
        {
            var count = 0;
            while (!stop.IsCancellationRequested)
            {
                _ = GameLock.IsHeld(directory, "hades");
                count++;
                checking.TrySetResult();
            }
            return count;
        });

        // On a busy runner the checks can start after the loop below has finished; make them overlap.
        await checking.Task;
        for (var i = 0; i < 500; i++)
        {
            using var held = GameLock.Acquire(directory, "hades");
        }
        await stop.CancelAsync();

        Assert.True(await checks > 0);
    }
}
