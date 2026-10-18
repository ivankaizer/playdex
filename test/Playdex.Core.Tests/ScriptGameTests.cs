using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class ScriptGameTests
{
    [Theory]
    [InlineData("Emulator.bat")]
    [InlineData("Emulator.cmd")]
    public async Task ABatchScriptCanBeAddedAsAGame(string fileName)
    {
        var script = Path.Combine(NewTempDir(), fileName);
        File.WriteAllText(script, "@echo off\r\n");

        var game = await GameCatalog.AddFromExecutableAsync(new AppConfig(), script);

        Assert.Equal("Emulator", game.GameId);
        Assert.Equal("Emulator", game.DisplayName);
        Assert.Equal(script, game.Launch.ExecutablePath);
    }

    [Fact]
    public async Task OtherFilesAreNotGames()
    {
        var file = Path.Combine(NewTempDir(), "notes.txt");
        File.WriteAllText(file, "");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => GameCatalog.AddFromExecutableAsync(new AppConfig(), file));
        Assert.Contains(".bat", error.Message);
    }

    [Fact]
    public async Task AScriptThatStartsTheGameAndReturnsIsFollowedUntilTheGameExits()
    {
        var script = Path.Combine(NewTempDir(), "Launch.bat");
        // Like a launcher script: start the game (here a ~2 s ping) and return at once.
        File.WriteAllText(script, "@echo off\r\nstart \"\" /b ping -n 3 127.0.0.1 >nul\r\nexit\r\n");
        var game = new GameConfig { GameId = $"script-{Guid.NewGuid():N}", DisplayName = "Launch", Launch = new LaunchConfig { ExecutablePath = script } };
        var startedAt = DateTimeOffset.Now;

        using var started = GameProcess.Start(game);

        Assert.NotNull(started.Tree);
        Assert.NotEmpty(ProcessTree.ProcessesOf(game.GameId));
        await new ProcessMonitor(game.Monitor).WaitForExitAsync(started, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(DateTimeOffset.Now - startedAt > TimeSpan.FromSeconds(1.5), "the session ended with the script, before the game it started");
        Assert.Empty(ProcessTree.ProcessesOf(game.GameId));
    }

    [Fact]
    public void AnExecutableIsNotFollowedAsAScript()
    {
        Assert.True(GameProcess.IsScript(@"C:\Games\Run.BAT"));
        Assert.True(GameProcess.IsScript(@"C:\Games\run.cmd"));
        Assert.False(GameProcess.IsScript(@"C:\Games\Game.exe"));
    }
}
