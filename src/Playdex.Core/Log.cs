namespace Playdex.Core;

/// <summary>Append-only text log at %LOCALAPPDATA%\PlaydexLauncher\launcher.log. Never called on the input thread.</summary>
public static class Log
{
    private static readonly Lock _gate = new();
    private static StreamWriter? _writer;

    public static void Info(string message)
    {
        lock (_gate)
        {
            try
            {
                _writer ??= new StreamWriter(
                    new FileStream(Paths.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
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

public static class Paths
{
    public static readonly string DataDirectory = CreateDataDirectory();
    public static readonly string LogFile = Path.Combine(DataDirectory, "launcher.log");
    public static readonly string QueueFile = Path.Combine(DataDirectory, "queue.jsonl");
    public static readonly string ConfigFile = Path.Combine(DataDirectory, "config.json");
    public static readonly string MetricsFile = Path.Combine(DataDirectory, "metrics.jsonl");

    private static string CreateDataDirectory()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlaydexLauncher");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
