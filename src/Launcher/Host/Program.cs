using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Playdex.Launcher.Host;

/// <summary>
/// The Full Screen Experience entry: the packaged home app Windows starts in FSE (and again on every Game Bar "Home" or
/// second start). It has no window of its own. It starts the Playdex app (Playdex.exe, installed outside the package)
/// in Big Picture, outside the package's container, and stays running while Playdex runs, so FSE sees its home app
/// alive. Later activations just hand on: Playdex is single instance and goes home.
/// </summary>
/// <remarks>
/// playdex-launcher://launch/GAMEID (and --launch=GAMEID) are passed on as --launch=GAMEID.
/// </remarks>
public static class Program
{
    private const string AppExecutable = "Playdex.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Info($"ERROR fse entry: unhandled exception, terminating: {e.ExceptionObject}");
        var gameId = RequestedGameId(args);
        var app = FindApp();
        if (app is null)
        {
            Log.Info("fse entry: Playdex.exe not found");
            NativeEntry.MessageBox(0, "Playdex isn't installed on this PC.\n\nInstall Playdex for Windows (Playdex-Setup.exe), then open the Full Screen Experience again.", "Playdex", 0x30);
            return 1;
        }

        using var resident = new Mutex(initiallyOwned: false, @"Local\PlaydexFullScreenEntry", out var first);
        // Started by the shell, this process may set the foreground; hand that on to Playdex.
        NativeEntry.AllowSetForegroundWindow(NativeEntry.AnyProcess);
        var arguments = $"--big-picture{(gameId is null ? "" : $" --launch={gameId}")}";
        int processId;
        try
        {
            processId = NativeEntry.StartOutsidePackage(app, arguments);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error($"fse entry: could not start {app}", ex);
            NativeEntry.MessageBox(0, $"Playdex could not be started.\n\n{ex.Message}", "Playdex", 0x10);
            return 1;
        }
        Log.Info($"fse entry: started {app} {arguments} (pid {processId}){(first ? "" : ", handing on")}");
        if (!first)
        {
            return 0; // Another entry is already resident.
        }
        StayWhilePlaydexRuns(processId, Path.GetFileNameWithoutExtension(app));
        Log.Info("fse entry: Playdex exited");
        return 0;
    }

    /// <summary>
    /// Wait for the process started here, then for the running Playdex: the one started may have only handed its request
    /// to an instance that was already running.
    /// </summary>
    private static void StayWhilePlaydexRuns(int processId, string processName)
    {
        try
        {
            using var started = System.Diagnostics.Process.GetProcessById(processId);
            started.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        while (true)
        {
            var running = System.Diagnostics.Process.GetProcessesByName(processName);
            try
            {
                if (running.Length == 0)
                {
                    return;
                }
                running[0].WaitForExit();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Thread.Sleep(1000);
            }
            finally
            {
                foreach (var process in running)
                {
                    process.Dispose();
                }
            }
        }
    }

    /// <summary>The Playdex.exe that last ran (recorded in the install's config), else the per-user install.</summary>
    private static string? FindApp()
    {
        if (File.Exists(Paths.ConfigFile))
        {
            try
            {
                if (ConfigStore.Default().LoadOrCreate().Shell.ExecutablePath is { } recorded && File.Exists(recorded))
                {
                    return recorded;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Log.Error("fse entry: could not read the config", ex);
            }
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playdex", AppExecutable);
        return File.Exists(installed) ? installed : null;
    }

    private static string? RequestedGameId(string[] args)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var uri = activation.Kind == ExtendedActivationKind.Protocol && activation.Data is IProtocolActivatedEventArgs protocol
            ? protocol.Uri.AbsoluteUri
            : args.FirstOrDefault(a => a.StartsWith("playdex-launcher:", StringComparison.OrdinalIgnoreCase));
        var id = args.FirstOrDefault(a => a.StartsWith("--launch=", StringComparison.Ordinal))?["--launch=".Length..] ?? ProtocolGameId(uri);
        // Passed on in a command line: ids are steam-<appid> or playdex-<id>, never spaces or quotes.
        return id is not null && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? id : null;
    }

    /// <summary>playdex-launcher://launch/GAMEID → GAMEID.</summary>
    private static string? ProtocolGameId(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Host != "launch")
        {
            return null;
        }
        var id = parsed.AbsolutePath.Trim('/');
        return id.Length > 0 ? Uri.UnescapeDataString(id) : null;
    }
}
