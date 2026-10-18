using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;

namespace Playdex.Core;

/// <summary>A library game running now.</summary>
/// <param name="Since">When it started; null if unknown (its process can't be read).</param>
/// <param name="ProcessId">Its process, when known (games added to Playdex).</param>
public sealed record RunningGame(string GameId, DateTimeOffset? Since, int? ProcessId);

/// <summary>
/// Finds a library game running now, however it was started (the launcher's own sessions are known without this):
/// <list type="bullet">
/// <item>Games added to Playdex: a process matching the game's monitor settings (the monitored executable's path or
/// name, or for <see cref="MonitorMode.LaunchedProcess"/> the launch executable). Checked first, as in
/// <see cref="Library.Scan"/>, where they win over the same Steam app.</item>
/// <item>Steam games: Steam's running app, HKCU\Software\Valve\Steam\RunningAppID (0 when none), while the Steam
/// client runs: a client that quits or crashes mid-game leaves the value behind. Steam doesn't say when it started, so
/// that is the first process running from its install folder, as <see cref="SteamGameWatcher"/> tracks it.</item>
/// </list>
/// One process list snapshot per call; meant for polling every few seconds while a window shows the result.
/// </summary>
public static class RunningGames
{
    // The Steam game's start, found once: reading every process's image path on each poll would cost far more.
    private static (int AppId, DateTimeOffset Since)? _steamStart;

    public static RunningGame? Detect(AppConfig config, IReadOnlyDictionary<string, LibraryGame> library) =>
        DetectPlaydex(config, library) ?? DetectSteam(library);

    private static RunningGame? DetectPlaydex(AppConfig config, IReadOnlyDictionary<string, LibraryGame> library)
    {
        var candidates = library.Values
            .Where(g => g.ConfigGameId is { } id && config.Games.ContainsKey(id))
            .Select(g => (Game: g, Config: config.Games[g.ConfigGameId!]))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        var processes = Process.GetProcesses();
        try
        {
            foreach (var (game, settings) in candidates)
            {
                foreach (var process in processes)
                {
                    if (IsGameProcess(settings, process))
                    {
                        return new RunningGame(game.Id, StartTime(process), process.Id);
                    }
                }
            }
            return null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    internal static bool IsGameProcess(GameConfig game, Process process)
    {
        if (game.Monitor.Mode != MonitorMode.LaunchedProcess)
        {
            return ProcessMonitor.Matches(game.Monitor, process);
        }
        if (string.IsNullOrWhiteSpace(game.Launch.ExecutablePath))
        {
            return false;
        }
        try
        {
            // By name first: opening every process to read its image path would cost far more.
            return string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(game.Launch.ExecutablePath), StringComparison.OrdinalIgnoreCase)
                && ProcessMonitor.IsImage(process, game.Launch.ExecutablePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static RunningGame? DetectSteam(IReadOnlyDictionary<string, LibraryGame> library)
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steam?.GetValue("RunningAppID") is not int appId || appId == 0 || !SteamClient.IsRunning())
        {
            _steamStart = null;
            return null;
        }
        // A game added to Playdex with this Steam app id replaced the Steam entry in the library.
        var game = library.GetValueOrDefault($"steam-{appId}") ?? library.Values.FirstOrDefault(g => g.SteamAppId == appId);
        if (game is null)
        {
            return null;
        }
        if (_steamStart is not { } start || start.AppId != appId)
        {
            // Not found yet while Steam starts the game (its exe comes seconds later), or when no process can be read.
            _steamStart = SteamLibrary.FindInstallDirectory(appId) is { } installDir && StartedFrom(installDir) is { } since
                ? (appId, since)
                : null;
        }
        return new RunningGame(game.Id, _steamStart?.Since, null);
    }

    /// <summary>When the first process still running from <paramref name="folder"/> started; null if none.</summary>
    internal static DateTimeOffset? StartedFrom(string folder)
    {
        DateTimeOffset? first = null;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (ProcessMonitor.ImagePath(process) is { } path
                    && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                    && StartTime(process) is { } since
                    && (first is null || since < first))
                {
                    first = since;
                }
            }
        }
        return first;
    }

    private static DateTimeOffset? StartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
