using System.ComponentModel;
using System.Diagnostics;

namespace Playdex.Core;

/// <summary>
/// Follows a game the Wrapper started to its exit, per <see cref="MonitorConfig.Mode"/>:
/// <list type="bullet">
/// <item><see cref="MonitorMode.LaunchedProcess"/>: the started process itself.</item>
/// <item><see cref="MonitorMode.ExecutablePath"/> / <see cref="MonitorMode.ExecutableName"/>: any process running that
/// executable, for games started through a launcher or that restart themselves. The game has exited once none has run
/// for <see cref="MonitorConfig.ExitDebounceSeconds"/>.</item>
/// </list>
/// Waits on process handles. The process list is only scanned (once a second) while no matching process is known: while
/// the game boots and during the exit debounce.
/// </summary>
public sealed class ProcessMonitor(MonitorConfig config)
{
    private static readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(1);

    /// <summary>Waits until the game has exited and returns when its last process exited.</summary>
    public async Task<DateTimeOffset> WaitForExitAsync(Process launched, CancellationToken cancellationToken)
    {
        if (config.Mode == MonitorMode.LaunchedProcess)
        {
            await launched.WaitForExitAsync(cancellationToken);
            return ExitTime(launched) ?? DateTimeOffset.Now;
        }

        var debounce = TimeSpan.FromSeconds(Math.Max(1, config.ExitDebounceSeconds));
        var lastExit = DateTimeOffset.Now; // Before the game is seen, its boot counts against the debounce.
        while (true)
        {
            var running = FindMatching();
            if (running.Count > 0)
            {
                try
                {
                    await Task.WhenAll(running.Select(p => WaitAsync(p, cancellationToken)));
                    lastExit = running.Select(ExitTime).Max() ?? DateTimeOffset.Now;
                    Log.Info($"monitor: {Target} stopped; waiting {debounce.TotalSeconds:N0} s for it to come back");
                }
                finally
                {
                    running.ForEach(p => p.Dispose());
                }
                continue;
            }
            if (DateTimeOffset.Now - lastExit >= debounce)
            {
                return lastExit;
            }
            await Task.Delay(_scanInterval, cancellationToken);
        }
    }

    private string Target => config.Mode == MonitorMode.ExecutablePath ? config.ExecutablePath ?? "" : config.ExecutableName ?? "";

    private List<Process> FindMatching()
    {
        var found = new List<Process>();
        foreach (var process in Process.GetProcesses())
        {
            if (Matches(process))
            {
                found.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }
        return found;
    }

    private bool Matches(Process process) => Matches(config, process);

    /// <summary>
    /// Whether <paramref name="process"/> is the monitored executable (<see cref="MonitorMode.ExecutablePath"/> or
    /// <see cref="MonitorMode.ExecutableName"/>; false for <see cref="MonitorMode.LaunchedProcess"/>).
    /// </summary>
    public static bool Matches(MonitorConfig config, Process process)
    {
        try
        {
            return config.Mode switch
            {
                MonitorMode.ExecutablePath => !string.IsNullOrWhiteSpace(config.ExecutablePath) && IsImage(process, config.ExecutablePath),
                MonitorMode.ExecutableName => !string.IsNullOrWhiteSpace(config.ExecutableName)
                    && (string.Equals(process.ProcessName + ".exe", config.ExecutableName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(process.ProcessName, config.ExecutableName, StringComparison.OrdinalIgnoreCase)),
                _ => false,
            };
        }
        catch (InvalidOperationException)
        {
            return false; // Exited while being inspected.
        }
    }

    /// <summary>Whether <paramref name="process"/> runs <paramref name="executablePath"/>. False if it can't be opened.</summary>
    public static bool IsImage(Process process, string executablePath) =>
        ImagePath(process) is { } path && string.Equals(path, Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>The full path of the process's executable; null if it can't be opened.</summary>
    internal static string? ImagePath(Process process)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)process.Id);
        if (handle == 0)
        {
            return null;
        }
        try
        {
            return Native.ProcessImagePath(handle);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Waits on the process handle. A process this user can't open for waiting (some anti-cheat protected games) is
    /// looked up by id once a second instead.
    /// </summary>
    private static async Task WaitAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            while (IsAlive(process.Id))
            {
                await Task.Delay(_scanInterval, cancellationToken);
            }
        }
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static DateTimeOffset? ExitTime(Process process)
    {
        try
        {
            return process.ExitTime;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
