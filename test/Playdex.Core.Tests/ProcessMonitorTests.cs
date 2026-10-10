using System.Diagnostics;

namespace Playdex.Core.Tests;

public sealed class ProcessMonitorTests : IDisposable
{
    private static readonly string _cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    // A copy of cmd.exe under a unique name stands in for the game, so no other process on the machine matches it.
    private readonly string _game = Path.Combine(Directory.CreateTempSubdirectory("playdex-monitor-").FullName, $"game-{Guid.NewGuid():N}.exe");

    public ProcessMonitorTests() => File.Copy(_cmd, _game);

    [Fact]
    public async Task LaunchedProcessEndsWhenThatProcessExits()
    {
        using var launched = Start(_cmd, "/c ping -n 2 127.0.0.1 >nul");
        var monitor = new ProcessMonitor(new MonitorConfig { Mode = MonitorMode.LaunchedProcess, ExitDebounceSeconds = 10 });

        var exitedAt = await monitor.WaitForExitAsync(launched, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(launched.HasExited);
        Assert.True(DateTimeOffset.Now - exitedAt < TimeSpan.FromSeconds(1), "no debounce for the launched process");
    }

    [Fact]
    public async Task ExecutablePathFollowsAGameStartedByALauncherAndReturnsItsExitTime()
    {
        // The "launcher" starts the game and exits at once; the game runs for about a second.
        using var launcher = Start(_cmd, $"/c start \"\" \"{_game}\" /c ping -n 2 127.0.0.1 >nul");
        var monitor = new ProcessMonitor(new MonitorConfig { Mode = MonitorMode.ExecutablePath, ExecutablePath = _game, ExitDebounceSeconds = 2 });
        var started = DateTimeOffset.Now;

        var exitedAt = await monitor.WaitForExitAsync(launcher, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(exitedAt - started > TimeSpan.FromSeconds(0.8), "the game's own exit, not the launcher's");
        Assert.True(DateTimeOffset.Now - exitedAt >= TimeSpan.FromSeconds(1.9), "returned only after the debounce");
    }

    [Fact]
    public async Task ExecutableNameSurvivesARestartWithinTheDebounce()
    {
        // The game runs, is gone for about a second, then runs again.
        var name = Path.GetFileName(_game);
        using var launcher = Start(_cmd, $"/c \"\"{_game}\" /c ping -n 2 127.0.0.1 >nul & ping -n 2 127.0.0.1 >nul & \"{_game}\" /c ping -n 2 127.0.0.1 >nul\"");
        var monitor = new ProcessMonitor(new MonitorConfig { Mode = MonitorMode.ExecutableName, ExecutableName = name, ExitDebounceSeconds = 3 });
        var started = DateTimeOffset.Now;

        var exitedAt = await monitor.WaitForExitAsync(launcher, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(exitedAt - started > TimeSpan.FromSeconds(2.5), $"ended at the first exit ({(exitedAt - started).TotalSeconds:N1} s)");
    }

    [Fact]
    public async Task AGameThatNeverAppearsEndsAfterTheDebounce()
    {
        using var launcher = Start(_cmd, "/c exit");
        var monitor = new ProcessMonitor(new MonitorConfig { Mode = MonitorMode.ExecutablePath, ExecutablePath = _game, ExitDebounceSeconds = 1 });
        var started = Stopwatch.StartNew();

        await monitor.WaitForExitAsync(launcher, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.InRange(started.Elapsed.TotalSeconds, 0.9, 3);
    }

    public void Dispose()
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_game)))
        {
            using (process)
            {
                process.Kill();
            }
        }
        Directory.Delete(Path.GetDirectoryName(_game)!, recursive: true);
    }

    private static Process Start(string file, string arguments) =>
        Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true })!;
}
