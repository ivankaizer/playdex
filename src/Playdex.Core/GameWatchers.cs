using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Playdex.Core;

/// <summary>Running/exit events for one launched game. Callbacks arrive on thread-pool threads.</summary>
public interface IGameWatcher : IDisposable
{
    event Action? Running;

    /// <summary>Launch the game. Call after subscribing to the events.</summary>
    void Start();

    /// <summary>Exit detected. Argument: Stopwatch timestamp of detection, and the last game process' exit
    /// time (FILETIME, 0 if unknown) so exit-detection latency can be measured.</summary>
    event Action<long, long>? Exited;
}

/// <summary>
/// Steam games: launch with steam://rungameid (starting Steam and waiting for login first if it isn't running), learn "running" from HKCU\Software\Valve\Steam\Apps\&lt;appid&gt;\Running
/// (RegNotifyChangeKeyValue, no polling) and detect exit from the game's own process handles.
/// </summary>
/// <remarks>
/// Steam clears Running about 2 s after the game process exits (measured; see spike-results.md), which misses the
/// 250 ms budget. So once running, the game's processes under its install folder are found and their handles waited
/// on: when the last one exits, one snapshot confirms nothing else from the install folder is alive and the exit is
/// reported. During the first 30 s (launcher to game hand-offs such as REDprelauncher to witcher3.exe) the folder is
/// rescanned every 2 s and only the registry may end the session. The registry remains the fallback throughout.
/// Until the game runs it is also looked for every 2 s (<see cref="FollowStart"/>): a game Steam started without the
/// registry saying so was otherwise never seen running, and a cold start kept sending its launch URL.
/// </remarks>
public sealed partial class SteamGameWatcher : IGameWatcher
{
    private static readonly TimeSpan _startTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _bootWindow = TimeSpan.FromSeconds(30);

    /// <summary>How often a launch that hasn't been seen running yet looks for the game.</summary>
    private static readonly TimeSpan _startPoll = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The launch URL sent right after login worked in testing (running 4.6 s later), but a client that is still busy
    /// (a client update, a slow login) drops it. A cold start sends it again after each of these delays, only while
    /// nothing of the game runs yet (<see cref="FindStarted"/>), so a game that is starting is never started twice.
    /// After the last one it waits for the start timeout.
    /// </summary>
    private static readonly TimeSpan[] _coldStartRetries = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15)];

    /// <summary>
    /// A client that has just logged in can still drop the URL while it settles (logged: sent 29 s after Steam started,
    /// the game never ran). A launch this soon after Steam started, such as Play again after leaving a cold start that
    /// was taking long, is treated as a cold start.
    /// </summary>
    private static readonly TimeSpan _clientSettleTime = TimeSpan.FromMinutes(2);

    private readonly int _appId;
    private readonly string _keyPath;
    private readonly AutoResetEvent _changed = new(false);
    private readonly Lock _gate = new();
    private readonly List<RegisteredWaitHandle> _processWaits = [];
    private readonly CancellationTokenSource _cancel = new();
    private readonly HashSet<uint> _tracked = [];
    private RegistryKey _key;
    private RegisteredWaitHandle? _wait;
    private string? _installDir;
    private long _runningSince;
    private int _alive;
    private bool _running;
    private bool _registryRunning;
    private bool _exited;
    private bool _disposed;
    private long _lastProcessExit;

    public event Action? Running;
    public event Action<long, long>? Exited;

    /// <summary>
    /// True while the launch waits for the Steam client to start and log in, false once it is in and the game is
    /// being started. Only raised when Steam was not logged in at launch.
    /// </summary>
    public event Action<bool>? WaitingForSteam;

    private SteamGameWatcher(int appId, string keyPath, RegistryKey key)
    {
        _appId = appId;
        _keyPath = keyPath;
        _key = key;
    }

    public static SteamGameWatcher? Create(int appId)
    {
        var keyPath = $@"Software\Valve\Steam\Apps\{appId}";
        var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        if (key is null)
        {
            Log.Info($"steam: no registry key for app {appId}; is it installed?");
            return null;
        }
        return new SteamGameWatcher(appId, keyPath, key);
    }

    public void Start()
    {
        lock (_gate)
        {
            Arm();
        }
        var settled = SteamClient.RunningFor() is not { } age || age >= _clientSettleTime;
        var coldStart = !settled || !SteamClient.IsLoggedIn();
        if (!coldStart)
        {
            RequestLaunch();
        }
        else if (!SteamClient.StartIfNeeded())
        {
            throw new InvalidOperationException("Steam is not installed.");
        }
        var waiting = coldStart && !SteamClient.IsLoggedIn();
        if (waiting)
        {
            WaitingForSteam?.Invoke(true);
        }
        _ = Task.Factory.StartNew(() => FollowStart(coldStart, waiting), _cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        // Already running (launched outside Playdex): report it straight away.
        Check();
    }

    private void RequestLaunch()
    {
        // Steam starts the game, not Playdex: let it take the foreground from Playdex's launch screen. A game left
        // behind Playdex would have the controller read by both.
        Native.AllowSetForegroundWindow(Native.AsfwAny);
        Process.Start(new ProcessStartInfo($"steam://rungameid/{_appId}") { UseShellExecute = true })?.Dispose();
        Log.Info($"steam: launch requested for {_appId}");
    }

    /// <summary>
    /// Until the game runs: on a cold start (or Steam started moments ago), wait for Steam's login, send the launch URL
    /// and send it again while nothing of the game shows up. Throughout, look for the game every
    /// <see cref="_startPoll"/>, in case the registry notification never comes.
    /// </summary>
    private void FollowStart(bool coldStart, bool waiting)
    {
        var retries = 0;
        var nextRetry = long.MaxValue;
        if (coldStart)
        {
            var started = Stopwatch.GetTimestamp();
            if (!SteamClient.WaitForLogin(_startTimeout, _cancel.Token))
            {
                return; // Disposed, or the start timeout ends the session.
            }
            if (waiting)
            {
                Log.Info($"steam: logged in after {Stopwatch.GetElapsedTime(started).TotalSeconds:N1} s");
                WaitingForSteam?.Invoke(false);
            }
            if (!IsStarting())
            {
                return;
            }
            RequestLaunch();
            nextRetry = Stopwatch.GetTimestamp() + Ticks(_coldStartRetries[0]);
        }
        while (!_cancel.Token.WaitHandle.WaitOne(_startPoll))
        {
            Check();
            if (!IsStarting())
            {
                return;
            }
            if (FindStarted() is { } found)
            {
                MarkRunning(found);
                return;
            }
            if (Stopwatch.GetTimestamp() < nextRetry)
            {
                continue;
            }
            retries++;
            Log.Info($"steam: app {_appId} not started yet; launch requested again ({retries}/{_coldStartRetries.Length})");
            RequestLaunch();
            nextRetry = retries < _coldStartRetries.Length ? Stopwatch.GetTimestamp() + Ticks(_coldStartRetries[retries]) : long.MaxValue;
        }
    }

    private bool IsStarting()
    {
        lock (_gate)
        {
            return !_running && !_exited && !_disposed;
        }
    }

    /// <summary>
    /// What shows that the game started when the registry didn't say so: Steam's running app, or a process running from
    /// its install folder. Null when there is neither.
    /// </summary>
    private string? FindStarted()
    {
        if (SteamRunningApp() == _appId)
        {
            return "RunningAppID";
        }
        _installDir ??= SteamLibrary.FindInstallDirectory(_appId);
        return _installDir is { } folder && AnyProcessIn(folder) ? "process in its install folder" : null;
    }

    private static int SteamRunningApp()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
        return steam?.GetValue("RunningAppID") is int appId ? appId : 0;
    }

    private static bool AnyProcessIn(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        var found = false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                found = found || (ProcessMonitor.ImagePath(process) is { } path && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
            }
        }
        return found;
    }

    private void Arm()
    {
        // Re-arm before reading so a change between read and arm is never missed. Thread-agnostic: the
        // registration survives the thread-pool thread that made it.
        var status = Notify();
        if (status != 0 && Registry.CurrentUser.OpenSubKey(_keyPath, writable: false) is { } current)
        {
            // The key the handle points at is gone (Steam rewrites its keys as it starts): watch the one there now.
            _key.Dispose();
            _key = current;
            status = Notify();
        }
        if (status != 0)
        {
            Log.Info($"steam: RegNotifyChangeKeyValue failed {status}");
        }
        _wait = ThreadPool.RegisterWaitForSingleObject(
            _changed, (_, timedOut) => OnSignal(timedOut), null, _running ? Timeout.InfiniteTimeSpan : _startTimeout, executeOnlyOnce: true);
    }

    private int Notify() => Native.RegNotifyChangeKeyValue(
        _key.Handle, false, Native.RegNotifyChangeLastSet | Native.RegNotifyThreadAgnostic, _changed.SafeWaitHandle, true);

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

    /// <summary>
    /// Follow the registry's Running. It ends the session only after it said the game runs: a game found running by
    /// <see cref="FindStarted"/> may never get Running=1.
    /// </summary>
    private void Check(long detected = 0)
    {
        var running = ReadRunning();
        bool started;
        lock (_gate)
        {
            if (_disposed || _exited || running == _registryRunning)
            {
                return;
            }
            _registryRunning = running;
            started = running && !_running;
            if (started)
            {
                _running = true;
                _runningSince = Stopwatch.GetTimestamp();
            }
        }
        if (started)
        {
            OnStarted("registry Running=1");
        }
        else if (!running)
        {
            ReportExit(detected == 0 ? Stopwatch.GetTimestamp() : detected, "registry Running=0");
        }
    }

    private void MarkRunning(string source)
    {
        lock (_gate)
        {
            if (_disposed || _exited || _running)
            {
                return;
            }
            _running = true;
            _runningSince = Stopwatch.GetTimestamp();
        }
        OnStarted(source);
    }

    private void OnStarted(string source)
    {
        Log.Info($"steam: app {_appId} running ({source})");
        Running?.Invoke();
        _ = Task.Run(TrackBootAsync);
    }

    /// <summary>Read through a fresh handle: the one the notification is armed on may be to a key Steam has replaced.</summary>
    private bool ReadRunning()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
        return key?.GetValue("Running") is int value && value != 0;
    }

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
        // Everything from the folder exited while the game booted (a crash at start), so no process exit ends the
        // session. Steam ends it when it still reports the game; otherwise nothing would.
        lock (_gate)
        {
            if (_alive > 0 || _exited || _disposed)
            {
                return;
            }
        }
        if (!ReadRunning() && SteamRunningApp() != _appId && TrackNewProcesses() == 0)
        {
            ReportExit(Stopwatch.GetTimestamp(), "no game process after boot");
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
        _cancel.Cancel();
        lock (_gate)
        {
            _disposed = true;
            _wait?.Unregister(null);
            foreach (var wait in _processWaits)
            {
                wait.Unregister(null);
            }
            _key.Dispose();
        }
        _changed.Dispose();
    }

    private static long Ticks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);
}

/// <summary>
/// Games the launcher starts directly: the process is created suspended inside a Job Object whose completion port
/// reports JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO once it and every child it started have exited.
/// </summary>
public sealed unsafe class JobGameWatcher : IGameWatcher
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
