using System.ComponentModel;
using System.Diagnostics;

namespace Playdex.Core;

/// <param name="Closed">Processes ended, each with its child processes.</param>
/// <param name="Denied">Processes that could not be ended (some anti-cheat protected or elevated games).</param>
public sealed record CloseResult(int Closed, int Denied);

/// <summary>
/// Force-closes a running library game, as Task Manager's End task would: no chance to save. Its processes are
/// <list type="bullet">
/// <item>the one known to be the game (<c>processId</c>: a game added to Playdex found running);</item>
/// <item>for a game added to Playdex, those matching its monitor settings (<see cref="RunningGames"/>);</item>
/// <item>for a Steam game, every process running from its install folder, as <see cref="SteamGameWatcher"/> tracks it.</item>
/// </list>
/// Each is ended with its process tree. Steam itself and this app are never touched.
/// </summary>
public static class GameCloser
{
    public static CloseResult Close(AppConfig config, LibraryGame game, int? processId)
    {
        var settings = game.ConfigGameId is { } id ? config.Games.GetValueOrDefault(id) : null;
        var installDir = game.SteamAppId is { } appId ? SteamLibrary.FindInstallDirectory(appId) : null;
        var targets = new HashSet<int>();
        if (processId is { } known)
        {
            targets.Add(known);
        }
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                if ((settings is not null && RunningGames.IsGameProcess(settings, process))
                    || (installDir is not null && ProcessMonitor.ImagePath(process) is { } path && path.StartsWith(installDir, StringComparison.OrdinalIgnoreCase)))
                {
                    targets.Add(process.Id);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        targets.Remove(Environment.ProcessId);

        int closed = 0, denied = 0;
        foreach (var pid in targets)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                var name = process.ProcessName;
                process.Kill(entireProcessTree: true);
                closed++;
                Log.Info($"close: ended {game.Id} process {pid} ({name})");
            }
            catch (ArgumentException)
            {
                // Already gone (a child of one ended before it).
            }
            catch (Exception ex) when (ex is Win32Exception or AggregateException or InvalidOperationException or NotSupportedException)
            {
                denied++;
                Log.Info($"close: could not end {game.Id} process {pid}: {ex.Message}");
            }
        }
        return new CloseResult(closed, denied);
    }
}
