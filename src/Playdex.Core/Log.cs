namespace Playdex.Core;

/// <summary>
/// Core's log. By default an append-only text file, <see cref="Paths.LogFile"/> (the launcher's); another app picks its
/// own file with <see cref="UseFile"/>, or routes lines to its own logging with <see cref="UseSink"/>. Classes that take
/// an <c>ILogger</c> reach it through <see cref="LogLoggerFactory"/>. Never called on the input thread. The file rolls
/// over by size (<see cref="RollingLogFile"/>), so an app that runs for weeks in the tray keeps a few megabytes of logs.
/// </summary>
public static class Log
{
    private static readonly Lock _gate = new();
    private static RollingLogFile? _writer;
    private static Action<string>? _sink;
    private static string _file = Paths.LogFile;

    /// <summary>Send every line to <paramref name="sink"/> instead of the log file. It may be called from any thread.</summary>
    public static void UseSink(Action<string> sink) => _sink = sink;

    /// <summary>Write to <paramref name="path"/> instead of the launcher's log. Call before the first line.</summary>
    public static void UseFile(string path)
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
            _file = path;
        }
    }

    public static void Info(string message)
    {
        if (_sink is { } sink)
        {
            sink(message);
            return;
        }
        lock (_gate)
        {
            try
            {
                _writer ??= new RollingLogFile(_file);
                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
        System.Diagnostics.Debug.WriteLine(message);
    }

    public static void Error(string message, Exception ex) => Info($"ERROR {message}: {ex}");

    /// <summary>
    /// Deletes the logs in <paramref name="directory"/> matching <paramref name="pattern"/> (such as "run-*.log", one
    /// file per day) last written more than <paramref name="maxAge"/> ago. Best effort: a file in use stays.
    /// </summary>
    public static void DeleteOlderThan(string directory, string pattern, TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles(pattern))
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// An append-only text file that rolls over by size: once it reaches <paramref name="maxBytes"/>, name.log becomes
/// name.1.log (name.1.log becomes name.2.log, and so on), and the oldest beyond <paramref name="keptFiles"/> is deleted.
/// Several processes may append to one file (each game's launcher does); while another one has it open it can't be
/// renamed, so the roll is tried again after another <paramref name="maxBytes"/>. Not thread-safe: <see cref="Log"/>
/// holds its lock around every call.
/// </summary>
internal sealed class RollingLogFile(string path, long maxBytes = RollingLogFile.DefaultMaxBytes, int keptFiles = RollingLogFile.DefaultKeptFiles) : IDisposable
{
    public const long DefaultMaxBytes = 5 * 1024 * 1024;
    public const int DefaultKeptFiles = 3;

    private StreamWriter? _writer;

    // How far past maxBytes the next roll waits: the size the log had when another process kept it from rolling.
    private long _rollDeferredBy;

    public void WriteLine(string line)
    {
        if (_writer is null)
        {
            Open();
            if (_writer!.BaseStream.Length >= maxBytes + _rollDeferredBy)
            {
                Roll();
            }
        }
        _writer!.WriteLine(line);
        if (_writer.BaseStream.Length >= maxBytes + _rollDeferredBy)
        {
            Roll();
        }
    }

    /// <summary>name.1.log for 1: the file the log rolled into <paramref name="generation"/> times ago.</summary>
    public static string Rolled(string path, int generation) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}.{generation}{Path.GetExtension(path)}");

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void Open()
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    }

    private void Roll()
    {
        var length = _writer!.BaseStream.Length;
        _writer.Dispose();
        _writer = null;
        var rolling = $"{path}.rolling";
        try
        {
            // First out of the way: this fails, and nothing else moves, while another process has the log open.
            File.Move(path, rolling, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _rollDeferredBy = length;
            Open();
            return;
        }
        try
        {
            File.Delete(Rolled(path, keptFiles));
            for (var generation = keptFiles - 1; generation >= 1; generation--)
            {
                if (File.Exists(Rolled(path, generation)))
                {
                    File.Move(Rolled(path, generation), Rolled(path, generation + 1), overwrite: true);
                }
            }
            File.Move(rolling, Rolled(path, 1), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An older log in use (open in an editor): keep going in a fresh file; the next roll tidies up.
        }
        _rollDeferredBy = 0;
        Open();
    }
}

/// <summary>
/// Where a Playdex install keeps its data: %LOCALAPPDATA%\Playdex. The Wrapper's state, logs and launchers stay where its
/// config says (an existing install keeps %LOCALAPPDATA%\PlaydexWrapper; Steam shortcuts point at those launchers).
/// </summary>
public static class Paths
{
    private static readonly string _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly string DataDirectory = CreateDataDirectory();
    public static readonly string ConfigFile = Path.Combine(DataDirectory, "config.json");
    public static readonly string QueueFile = Path.Combine(DataDirectory, "queue.jsonl");
    public static readonly string LogFile = Path.Combine(DataDirectory, "launcher.log");
    public static readonly string MetricsFile = Path.Combine(DataDirectory, "metrics.jsonl");

    public static readonly string DefaultStateDirectory = Path.Combine(DataDirectory, "state");
    public static readonly string DefaultLogDirectory = Path.Combine(DataDirectory, "logs");
    public static readonly string DefaultLauncherDirectory = Path.Combine(DataDirectory, "launchers");

    /// <summary>The Wrapper's folder before the merge. Its updater still stages updates here.</summary>
    public static readonly string LegacyWrapperDirectory = Path.Combine(_localAppData, "PlaydexWrapper");

    /// <summary>The launcher's folder before the merge.</summary>
    public static readonly string LegacyLauncherDirectory = Path.Combine(_localAppData, "PlaydexLauncher");

    /// <summary>
    /// Move the launcher's offline queue and WebView2 profile from <see cref="LegacyLauncherDirectory"/>, once. Call at
    /// startup, before the queue or WebView2 is used. The old config is migrated by <see cref="ConfigStore.Default"/>.
    /// </summary>
    public static void MoveLegacyLauncherData()
    {
        Move(Path.Combine(LegacyLauncherDirectory, "queue.jsonl"), QueueFile, File.Exists, File.Move);
        Move(Path.Combine(LegacyLauncherDirectory, "WebView2"), Path.Combine(DataDirectory, "WebView2"), Directory.Exists, Directory.Move);

        static void Move(string from, string to, Func<string, bool> exists, Action<string, string> move)
        {
            if (!exists(from) || exists(to))
            {
                return;
            }
            try
            {
                move(from, to);
                Log.Info($"paths: moved {from} to {to}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"paths: could not move {from}", ex);
            }
        }
    }

    private static string CreateDataDirectory()
    {
        var dir = Path.Combine(_localAppData, "Playdex");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
