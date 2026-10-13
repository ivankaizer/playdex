using System.ComponentModel;
using System.Diagnostics;

namespace Playdex.Core.Tests;

/// <summary>Games added to Playdex are found running however they were started, by their monitor settings.</summary>
public sealed class RunningGamesTests : IDisposable
{
    private static readonly string _cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    // A copy of cmd.exe under a unique name stands in for the game, so no other process on the machine matches it.
    private readonly string _game = Path.Combine(Directory.CreateTempSubdirectory("playdex-running-").FullName, $"game-{Guid.NewGuid():N}.exe");

    // Only the Playdex game: Steam's running app (read from this PC's registry) has no library entry to map to.
    private readonly Dictionary<string, LibraryGame> _library = new()
    {
        ["playdex-test"] = new LibraryGame("playdex-test", "Test Game", "playdex", null, null, null, ConfigGameId: "test"),
    };

    public RunningGamesTests() => File.Copy(_cmd, _game);

    [Fact]
    public void LaunchedProcessGameIsFoundByItsLaunchExecutable()
    {
        using var running = Start(_game);

        var found = RunningGames.Detect(Config(new MonitorConfig { Mode = MonitorMode.LaunchedProcess }), _library);

        Assert.NotNull(found);
        Assert.Equal("playdex-test", found.GameId);
        Assert.Equal(running.Id, found.ProcessId);
        Assert.NotNull(found.Since);
        Assert.True(DateTimeOffset.Now - found.Since.Value < TimeSpan.FromSeconds(10), "since is the process start");
    }

    [Fact]
    public void ExecutablePathGameIsFoundByTheMonitoredExecutable()
    {
        using var running = Start(_game);
        var monitor = new MonitorConfig { Mode = MonitorMode.ExecutablePath, ExecutablePath = _game };

        var found = RunningGames.Detect(Config(monitor, launch: _cmd), _library);

        Assert.Equal(running.Id, found?.ProcessId);
    }

    [Fact]
    public void ExecutableNameGameIsFoundByName()
    {
        using var running = Start(_game);
        var monitor = new MonitorConfig { Mode = MonitorMode.ExecutableName, ExecutableName = Path.GetFileName(_game) };

        var found = RunningGames.Detect(Config(monitor, launch: _cmd), _library);

        Assert.Equal(running.Id, found?.ProcessId);
    }

    [Fact]
    public void TheSameFileNameElsewhereIsNotTheGame()
    {
        var elsewhere = Path.Combine(Directory.CreateTempSubdirectory("playdex-running-").FullName, Path.GetFileName(_game));
        File.Copy(_cmd, elsewhere);
        try
        {
            using var running = Start(elsewhere);

            Assert.Null(RunningGames.Detect(Config(new MonitorConfig { Mode = MonitorMode.LaunchedProcess }), _library));
        }
        finally
        {
            Kill(elsewhere);
            DeleteDirectory(Path.GetDirectoryName(elsewhere)!);
        }
    }

    [Fact]
    public void CloseEndsTheGameWithItsChildren()
    {
        using var running = Start(_game);

        var result = GameCloser.Close(Config(new MonitorConfig { Mode = MonitorMode.LaunchedProcess }), _library["playdex-test"], processId: null);

        Assert.Equal(new CloseResult(1, 0), result);
        Assert.True(running.WaitForExit(5000), "the game exited");
        Assert.Null(RunningGames.Detect(Config(new MonitorConfig { Mode = MonitorMode.LaunchedProcess }), _library));
    }

    [Fact]
    public void CloseWithNothingRunningEndsNothing() =>
        Assert.Equal(new CloseResult(0, 0), GameCloser.Close(Config(new MonitorConfig { Mode = MonitorMode.LaunchedProcess }), _library["playdex-test"], processId: null));

    [Fact]
    public void NothingRunningIsNull() =>
        Assert.Null(RunningGames.Detect(Config(new MonitorConfig { Mode = MonitorMode.LaunchedProcess }), _library));

    public void Dispose()
    {
        Kill(_game);
        DeleteDirectory(Path.GetDirectoryName(_game)!);
    }

    private AppConfig Config(MonitorConfig monitor, string? launch = null)
    {
        var config = new AppConfig();
        var game = config.GetOrCreateGame("test");
        game.Launch.ExecutablePath = launch ?? _game;
        game.Monitor = monitor;
        return config;
    }

    /// <summary>Runs for about 30 s unless killed.</summary>
    private static Process Start(string file) =>
        Process.Start(new ProcessStartInfo(file, "/c ping -n 30 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true })!;

    private static void Kill(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Already exiting (a test closed it): its modules can't be read any more, or it is gone.
                }
            }
        }
    }

    /// <summary>
    /// Windows can keep a just-exited executable locked for a moment (the image unmapping, or Defender scanning the
    /// fresh copy), so retry before giving up. A leftover temp folder must not fail a test.
    /// </summary>
    private static void DeleteDirectory(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 20)
                {
                    return;
                }
                Thread.Sleep(100);
            }
        }
    }
}
