using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Playdex.Core;

/// <summary>
/// Starts a game from the config: its executable, launch options and working folder, through the shell. A batch script
/// (.bat, .cmd) runs in a minimized console and is followed as a <see cref="ProcessTree"/>, so a script that starts the
/// game and returns at once ("start game.exe") still records the whole session. An Xbox app game is started as its
/// packaged app (<see cref="LaunchConfig.AppUserModelId"/>), an Epic game by its launcher (<see cref="LaunchConfig.Uri"/>).
/// </summary>
public static class GameProcess
{
    /// <summary>What a game can be added as: an executable or a batch script.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".exe", ".bat", ".cmd"];

    public static bool IsScript(string? path) =>
        Path.GetExtension(path) is { } extension
        && (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase));

    public static StartedGame Start(GameConfig game)
    {
        if (string.IsNullOrWhiteSpace(game.Launch.ExecutablePath))
        {
            throw new InvalidOperationException($"Game '{game.GameId}' has no executable configured.");
        }
        if (!string.IsNullOrWhiteSpace(game.Launch.AppUserModelId))
        {
            return new StartedGame(Running(PackagedApp.Start(game.Launch.AppUserModelId, game.Launch.Arguments)), null);
        }
        if (!string.IsNullOrWhiteSpace(game.Launch.Uri))
        {
            // The launcher starts the game: what the shell returns, if anything, is the launcher.
            Process.Start(new ProcessStartInfo(game.Launch.Uri) { UseShellExecute = true })?.Dispose();
            return new StartedGame(null, null);
        }
        var script = IsScript(game.Launch.ExecutablePath);
        var start = new ProcessStartInfo(game.Launch.ExecutablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(game.Launch.WorkingDirectory)
                ? Path.GetDirectoryName(game.Launch.ExecutablePath)
                : game.Launch.WorkingDirectory,
            // The script's console stays out of the game's way, but can still be brought up if the script asks something.
            WindowStyle = script ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
        };
        foreach (var argument in game.Launch.Arguments)
        {
            start.ArgumentList.Add(argument);
        }
        var process = Process.Start(start) ?? throw new InvalidOperationException($"{game.DisplayName} did not start.");
        return new StartedGame(process, script ? ProcessTree.Follow(game.GameId, process) : null);
    }

    /// <summary>The process Windows started for a packaged app; null once it has handed over to the game and exited.</summary>
    private static Process? Running(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// A game Playdex started: its process and, for a script, everything the script starts. No process for a game its
/// launcher starts, or an Xbox app game whose starter already exited: it is followed by its executable
/// (<see cref="MonitorMode.ExecutablePath"/>).
/// </summary>
public sealed class StartedGame(Process? process, ProcessTree? tree) : IDisposable
{
    public Process? Process => process;

    /// <summary>Null for an executable, or when the script could not be followed (then only its own process is).</summary>
    public ProcessTree? Tree => tree;

    public void Dispose()
    {
        tree?.Dispose();
        process?.Dispose();
    }
}

/// <summary>
/// A started script and every process it starts, in a Job Object whose completion port reports when the last of them
/// has exited. The script joins the job a moment after it starts (it is started through the shell); a batch script
/// reads its first line later than that, so what it starts is followed. While it runs, its processes are known by game
/// (<see cref="ProcessesOf"/>), so Close game can end them.
/// </summary>
public sealed unsafe partial class ProcessTree : IDisposable
{
    private const nuint StopKey = 1;
    private const int JobObjectBasicAccountingInformation = 1;

    private static readonly ConcurrentDictionary<string, ProcessTree> _running = new(StringComparer.Ordinal);

    private readonly string _gameId;
    private readonly nint _job;
    private readonly nint _port;
    private readonly ConcurrentDictionary<int, byte> _alive = new();
    private readonly TaskCompletionSource<DateTimeOffset> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    private ProcessTree(string gameId, nint job, nint port)
    {
        _gameId = gameId;
        _job = job;
        _port = port;
    }

    /// <summary>Follow <paramref name="process"/> and what it starts; null when it can't be (it is then followed alone).</summary>
    public static ProcessTree? Follow(string gameId, Process process)
    {
        var job = Native.CreateJobObject(0, 0);
        var port = job == 0 ? 0 : Native.CreateIoCompletionPort(new nint(-1), 0, 0, 1);
        var association = new Native.JobAssociateCompletionPort { CompletionKey = job, CompletionPort = port };
        if (port == 0
            || !Native.SetInformationJobObject(job, Native.JobObjectAssociateCompletionPortInformation, &association, (uint)sizeof(Native.JobAssociateCompletionPort))
            || !Native.AssignProcessToJobObject(job, process.Handle))
        {
            Log.Info($"game: {gameId}'s script is followed alone (job object error {Marshal.GetLastPInvokeError()})");
            if (port != 0)
            {
                Native.CloseHandle(port);
            }

            if (job != 0)
            {
                Native.CloseHandle(job);
            }

            return null;
        }
        var tree = new ProcessTree(gameId, job, port);
        tree._alive[process.Id] = 0;
        _running[gameId] = tree;
        new Thread(tree.Pump) { IsBackground = true, Name = "script-watch" }.Start();
        // Everything may have exited before the port was listening.
        if (tree.ActiveProcesses() == 0)
        {
            tree.Finish();
        }
        return tree;
    }

    /// <summary>The processes of a game's script that are running now (none when the game isn't running from one).</summary>
    public static IReadOnlyCollection<int> ProcessesOf(string gameId) =>
        _running.TryGetValue(gameId, out var tree) ? [.. tree._alive.Keys] : [];

    /// <summary>Completes when the script and everything it started have exited, with when that was.</summary>
    public Task<DateTimeOffset> WaitForExitAsync(CancellationToken cancellationToken) => _exited.Task.WaitAsync(cancellationToken);

    private void Pump()
    {
        while (Native.GetQueuedCompletionStatus(_port, out var message, out var key, out var data, Native.Infinite) && key != StopKey)
        {
            switch (message)
            {
                case Native.JobObjectMsgNewProcess:
                    _alive[(int)data] = 0;
                    break;
                case Native.JobObjectMsgExitProcess:
                case Native.JobObjectMsgAbnormalExitProcess:
                    _alive.TryRemove((int)data, out _);
                    break;
                case Native.JobObjectMsgActiveProcessZero:
                    Finish();
                    return;
            }
        }
    }

    private void Finish()
    {
        _alive.Clear();
        _running.TryRemove(new KeyValuePair<string, ProcessTree>(_gameId, this));
        _exited.TrySetResult(DateTimeOffset.Now);
    }

    private uint ActiveProcesses()
    {
        BasicAccounting info;
        return QueryInformationJobObject(_job, JobObjectBasicAccountingInformation, &info, (uint)sizeof(BasicAccounting), 0)
            ? info.ActiveProcesses
            : 1;
    }

    /// <summary>Stops following (the game keeps running: the job doesn't end its processes when closed).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _running.TryRemove(new KeyValuePair<string, ProcessTree>(_gameId, this));
        Native.PostQueuedCompletionStatus(_port, 0, StopKey, 0);
        Native.CloseHandle(_job);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint job, int infoClass, void* info, uint length, nint returnLength);
}
