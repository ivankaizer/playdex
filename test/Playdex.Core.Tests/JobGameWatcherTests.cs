namespace Playdex.Core.Tests;

public sealed class JobGameWatcherTests
{
    [Fact]
    public async Task ReportsRunningThenExitOnceTheProcessAndItsChildrenHaveExited()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        // The child outlives its parent by about a second, like a game launcher handing off to the game.
        using var watcher = new JobGameWatcher(cmd, $"/c start \"\" /b \"{cmd}\" /c ping -n 2 127.0.0.1 >nul");
        var running = new TaskCompletionSource();
        var exited = new TaskCompletionSource<long>();
        watcher.Running += () => running.TrySetResult();
        watcher.Exited += (_, processExit) => exited.TrySetResult(processExit);
        var started = DateTime.UtcNow;

        watcher.Start();

        await running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var lastExit = await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(DateTime.UtcNow - started > TimeSpan.FromSeconds(0.8), "exit was reported before the child process ended");
        Assert.True(lastExit > 0);
    }
}
