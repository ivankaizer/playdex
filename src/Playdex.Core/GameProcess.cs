using System.Diagnostics;

namespace Playdex.Core;

/// <summary>Starts a game from the config: its executable, launch options and working folder, through the shell.</summary>
public static class GameProcess
{
    public static Process Start(GameConfig game)
    {
        if (string.IsNullOrWhiteSpace(game.Launch.ExecutablePath))
        {
            throw new InvalidOperationException($"Game '{game.GameId}' has no executable configured.");
        }
        var start = new ProcessStartInfo(game.Launch.ExecutablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(game.Launch.ExecutablePath),
        };
        foreach (var argument in game.Launch.Arguments)
        {
            start.ArgumentList.Add(argument);
        }
        return Process.Start(start) ?? throw new InvalidOperationException($"{game.DisplayName} did not start.");
    }
}
