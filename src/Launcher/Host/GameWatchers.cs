using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Playdex.Launcher.Host;

/// <summary>Running/exit events for one launched game. Callbacks arrive on thread-pool threads.</summary>
internal interface IGameWatcher : IDisposable
{
    event Action? Running;

    /// <summary>Launch the game. Call after subscribing to the events.</summary>
    void Start();

    /// <summary>Exit detected. Argument: Stopwatch timestamp of detection, and the last game process' exit
    /// time (FILETIME, 0 if unknown) so exit-detection latency can be measured.</summary>
    event Action<long, long>? Exited;
}

/// <summary>
/// Steam games: launch with steam://rungameid, learn "running" from HKCU\Software\Valve\Steam\Apps\&lt;appid&gt;\Running
/// (RegNotifyChangeKeyValue, no polling) and detect exit from the game's own process handles.
/// </summary>
/// <remarks>
/// Steam clears Running about 2 s after the game process exits (measured; see spike-results.md), which misses the
/// 250 ms budget. So once running, the game's processes under its install folder are found and their handles waited
/// on: when the last one exits, one snapshot confirms nothing else from the install folder is alive and the exit is
/// reported. During the first 30 s (launcher to game hand-offs such as REDprelauncher to witcher3.exe) the folder is
/// rescanned every 2 s and only the registry may end the session. The registry remains the fallback throughout.
/// </remarks>
internal sealed partial class SteamGameWatcher : IGameWatcher
{
    private static readonly TimeSpan _startTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _bootWindow = TimeSpan.FromSeconds(30);

    private readonly int _appId;
    private readonly RegistryKey _key;
    private readonly AutoResetEvent _changed = new(false);
    private readonly Lock _gate = new();
    private readonly List<RegisteredWaitHandle> _processWaits = [];
    private readonly HashSet<uint> _tracked = [];
    private RegisteredWaitHandle? _wait;
    private string? _installDir;
    private long _runningSince;
    private int _alive;
    private bool _running;
    private bool _exited;
    private bool _disposed;
    private long _lastProcessExit;

    public event Action? Running;
    public event Action<long, long>? Exited;

    private SteamGameWatcher(int appId, RegistryKey key)
    {
        _appId = appId;
        _key = key;
    }

    public static SteamGameWatcher? Create(int appId)
    {
        var key = Registry.CurrentUser.OpenSubKey($@"Software\Valve\Steam\Apps\{appId}", writable: false);
        if (key is null)
        {
            Log.Info($"steam: no registry key for app {appId}; is it installed?");
            return null;
        }
        return new SteamGameWatcher(appId, key);
    }

    public void Start()
    {
        lock (_gate)
        {
            Arm();
        }
        Process.Start(new ProcessStartInfo($"steam://rungameid/{_appId}") { UseShellExecute = true })?.Dispose();
        Log.Info($"steam: launch requested for {_appId}");
        // Already running (launched outside Playdex): report it straight away.
        Check();
    }

    private void Arm()
    {
        // Re-arm before reading so a change between read and arm is never missed. Thread-agnostic: the
        // registration survives the thread-pool thread that made it.
        var status = Native.RegNotifyChangeKeyValue(
            _key.Handle, false, Native.RegNotifyChangeLastSet | Native.RegNotifyThreadAgnostic, _changed.SafeWaitHandle, true);
        if (status != 0)
        {
            Log.Info($"steam: RegNotifyChangeKeyValue failed {status}");
        }
        _wait = ThreadPool.RegisterWaitForSingleObject(
            _changed, (_, timedOut) => OnSignal(timedOut), null, _running ? Timeout.InfiniteTimeSpan : _startTimeout, executeOnlyOnce: true);
    }

    private void OnSignal(bool timedOut)
    {
        var detected = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_disposed || _exited)
            {
                return;
            }
            if (timedOut && !_running)
            {
                Log.Info($"steam: app {_appId} did not start within {_startTimeout}");
                ReportExit(detected, "start timeout");
                return;
            }
            Arm();
        }
        Check(detected);
    }

    private void Check(long detected = 0)
    {
        var running = ReadRunning();
        lock (_gate)
        {
            if (_disposed || _exited || running == _running)
            {
                return;
            }
            _running = running;
            _runningSince = Stopwatch.GetTimestamp();
        }
        if (running)
        {
            Log.Info($"steam: app {_appId} running");
            Running?.Invoke();
            _ = Task.Run(TrackBootAsync);
        }
        else
        {
            ReportExit(detected == 0 ? Stopwatch.GetTimestamp() : detected, "registry Running=0");
        }
    }

    private bool ReadRunning() => _key.GetValue("Running") is int value && value != 0;

    private void ReportExit(long detected, string source)
    {
        lock (_gate)
        {
            if (_exited)
            {
                return;
            }
            _exited = true;
        }
        Log.Info($"steam: app {_appId} exited ({source})");
        Exited?.Invoke(detected, Interlocked.Read(ref _lastProcessExit));
    }

    /// <summary>Rescan the install folder while the game boots: launchers start the real exe seconds later.</summary>
    private async Task TrackBootAsync()
    {
        _installDir = SteamLibrary.FindInstallDirectory(_appId);
        if (_installDir is null)
        {
            Log.Info($"steam: install folder of {_appId} not found; exit comes from the registry only");
            return;
        }
        for (var scan = 0; scan < 15 && !_disposed && !_exited; scan++)
        {
            TrackNewProcesses();
            await Task.Delay(2000);
        }
    }

    /// <summary>Wait on every process running from the install folder that is not tracked yet. Returns how many.</summary>
    private int TrackNewProcesses()
    {
        var found = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var pid = (uint)process.Id;
                lock (_gate)
                {
                    if (_tracked.Contains(pid))
                    {
                        continue;
                    }
                }
                var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation | Native.Synchronize, false, pid);
                if (handle == 0)
                {
                    continue;
                }
                var path = Native.ProcessImagePath(handle);
                if (path is null || !path.StartsWith(_installDir!, StringComparison.OrdinalIgnoreCase))
                {
                    Native.CloseHandle(handle);
                    continue;
                }
                lock (_gate)
                {
                    _tracked.Add(pid);
                    _alive++;
                }
                found++;
                Log.Info($"steam: tracking game process {pid} {path}");
                WaitForProcessExit(handle);
            }
        }
        return found;
    }

    private void WaitForProcessExit(nint handle)
    {
        var waitHandle = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true) };
        var registered = ThreadPool.RegisterWaitForSingleObject(waitHandle, (_, _) =>
        {
            var detected = Stopwatch.GetTimestamp();
            var observed = Native.PreciseNow();
            if (Native.GetProcessTimes(handle, out _, out var exit, out _, out _))
            {
                Interlocked.Exchange(ref _lastProcessExit, Math.Max(Interlocked.Read(ref _lastProcessExit), exit));
                Metrics.Record("exit.process_handle_signal", (observed - exit) / 10_000.0, $"app {_appId}");
            }
            waitHandle.Dispose();
            OnProcessExited(detected);
        }, null, Timeout.Infinite, executeOnlyOnce: true);
        lock (_gate)
        {
            _processWaits.Add(registered);
        }
    }

    private void OnProcessExited(long detected)
    {
        lock (_gate)
        {
            if (--_alive > 0 || _exited || _disposed || Stopwatch.GetElapsedTime(_runningSince) < _bootWindow)
            {
                return;
            }
        }
        // Last known game process is gone. One snapshot catches a process that took over (a relaunch, a crash
        // handler); if there is none, the game has exited.
        if (TrackNewProcesses() == 0)
        {
            ReportExit(detected, "last game process exited");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _wait?.Unregister(null);
            foreach (var wait in _processWaits)
            {
                wait.Unregister(null);
            }
        }
        _key.Dispose();
        _changed.Dispose();
    }
}

internal static partial class SteamLibrary
{
    public static string? FindInstallDirectory(int appId)
    {
        var steamPath = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        if (steamPath is null)
        {
            return null;
        }
        var libraries = new List<string> { steamPath };
        var folders = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(folders))
        {
            foreach (Match match in PathEntry().Matches(File.ReadAllText(folders)))
            {
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }
        foreach (var library in libraries)
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest))
            {
                continue;
            }
            var installDir = InstallDirEntry().Match(File.ReadAllText(manifest));
            if (installDir.Success)
            {
                return Path.GetFullPath(Path.Combine(library, "steamapps", "common", installDir.Groups[1].Value)) + Path.DirectorySeparatorChar;
            }
        }
        return null;
    }

    [GeneratedRegex("\"path\"\\s+\"(.+?)\"")]
    private static partial Regex PathEntry();

    [GeneratedRegex("\"installdir\"\\s+\"(.+?)\"")]
    private static partial Regex InstallDirEntry();
}

/// <summary>
/// Games the launcher starts directly: the process is created suspended inside a Job Object whose completion port
/// reports JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO once it and every child it started have exited.
/// </summary>
internal sealed unsafe class JobGameWatcher : IGameWatcher
{
    private const nuint StopKey = 1;

    private readonly nint _job;
    private readonly nint _port;
    private readonly string _executable;
    private readonly string _arguments;
    private readonly string? _workingDirectory;
    private readonly Dictionary<uint, nint> _processes = [];
    private long _lastProcessExit;

    public event Action? Running;
    public event Action<long, long>? Exited;

    public JobGameWatcher(string executable, string arguments, string? workingDirectory = null)
    {
        _executable = executable;
        _arguments = arguments;
        _workingDirectory = workingDirectory;
        _job = Native.CreateJobObject(0, 0);
        _port = Native.CreateIoCompletionPort(new nint(-1), 0, 0, 1);
        if (_job == 0 || _port == 0)
        {
            throw new InvalidOperationException("Could not create the job object.");
        }
        var association = new Native.JobAssociateCompletionPort { CompletionKey = _job, CompletionPort = _port };
        if (!Native.SetInformationJobObject(_job, Native.JobObjectAssociateCompletionPortInformation, &association, (uint)sizeof(Native.JobAssociateCompletionPort)))
        {
            throw new InvalidOperationException($"SetInformationJobObject failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        }
    }

    public void Start()
    {
        new Thread(Pump) { IsBackground = true, Name = "job-watch" }.Start();

        var commandLine = $"\"{_executable}\" {_arguments}";
        var directory = _workingDirectory ?? Path.GetDirectoryName(_executable) ?? Environment.CurrentDirectory;
        var startup = new Native.StartupInfo { Cb = (uint)sizeof(Native.StartupInfo) };
        Native.ProcessInformation info;
        fixed (char* app = _executable)
        fixed (char* cmd = commandLine)
        fixed (char* dir = directory)
        {
            if (!Native.CreateProcess(app, cmd, 0, 0, false, Native.CreateSuspended | Native.CreateUnicodeEnvironment, 0, dir, &startup, &info))
            {
                throw new InvalidOperationException($"CreateProcess failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
            }
        }
        // Assign before the first instruction runs, so even an immediate child process lands in the job.
        Native.AssignProcessToJobObject(_job, info.Process);
        _ = Native.ResumeThread(info.Thread);
        Native.CloseHandle(info.Thread);
        Native.CloseHandle(info.Process);
        Log.Info($"job: started {commandLine} as {info.ProcessId}");
        Running?.Invoke();
    }

    private void Pump()
    {
        while (Native.GetQueuedCompletionStatus(_port, out var message, out var key, out var data, Native.Infinite))
        {
            if (key == StopKey)
            {
                return;
            }
            var pid = (uint)data;
            switch (message)
            {
                case Native.JobObjectMsgNewProcess:
                    // Keep a handle to read the exit time later, for the exit-detection measurement.
                    var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, pid);
                    if (handle != 0)
                    {
                        _processes[pid] = handle;
                    }
                    break;
                case Native.JobObjectMsgExitProcess:
                case Native.JobObjectMsgAbnormalExitProcess:
                    if (_processes.Remove(pid, out var exited))
                    {
                        if (Native.GetProcessTimes(exited, out _, out var exit, out _, out _))
                        {
                            _lastProcessExit = Math.Max(_lastProcessExit, exit);
                        }
                        Native.CloseHandle(exited);
                    }
                    break;
                case Native.JobObjectMsgActiveProcessZero:
                    var detected = Stopwatch.GetTimestamp();
                    Exited?.Invoke(detected, _lastProcessExit);
                    return;
            }
        }
    }

    public void Dispose()
    {
        // Stops the pump; the game keeps running (no kill-on-close), only tracking ends.
        Native.PostQueuedCompletionStatus(_port, 0, StopKey, 0);
        Native.CloseHandle(_job);
    }
}
