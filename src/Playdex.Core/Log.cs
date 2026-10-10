namespace Playdex.Core;

/// <summary>
/// Core's log. By default an append-only text file, <see cref="Paths.LogFile"/> (the launcher's); another app picks its
/// own file with <see cref="UseFile"/>, or routes lines to its own logging with <see cref="UseSink"/>. Classes that take
/// an <c>ILogger</c> reach it through <see cref="LogLoggerFactory"/>. Never called on the input thread.
/// </summary>
public static class Log
{
    private static readonly Lock _gate = new();
    private static StreamWriter? _writer;
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
                if (_writer is null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                    _writer = new StreamWriter(new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                }
                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}");
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
        System.Diagnostics.Debug.WriteLine(message);
    }

    public static void Error(string message, Exception ex) => Info($"ERROR {message}: {ex}");
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
