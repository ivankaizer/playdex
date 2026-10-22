using System.Diagnostics;
using System.Runtime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Playdex.Desktop;

/// <summary>
/// Playdex for Windows: one app with two modes, Desktop (a normal window and the tray) and Big Picture (full screen,
/// controller first). The mode is a setting (<see cref="ShellConfig.Mode"/>); the window switches in place. One
/// instance; starting it again opens the window. The Full Screen Experience package starts it with --big-picture.
/// </summary>
/// <remarks>
/// Flags beyond <see cref="LaunchOptions"/>:
/// --background                   start in the tray without a window (how it starts with Windows)
/// --big-picture                  open in Big Picture whatever the saved mode (the Full Screen Experience entry)
/// --fse                          started from the Full Screen Experience: switching to desktop mode leaves it
/// --quit                         quit the running Playdex (the setup, before it updates or uninstalls it)
/// --update-health=PATH           started by the updater: confirm a healthy start by writing PATH
/// --selftest-job                 run the Job Object exit-detection self-test and exit
/// --selftest-quick-settings[=toggle]
///                                log the quick settings state (sound outputs, HDR, refresh rates, FPS counter); with
///                                =toggle, switch the sound output, HDR and refresh rate of each display, check, switch
///                                back, then toggle the FPS counter twice
/// </remarks>
public static class Program
{
    public const string BigPictureFlag = "--big-picture";
    public const string QuitFlag = "--quit";
    public const string FseFlag = "--fse";

    [STAThread]
    private static int Main(string[] args)
    {
        Startup.MainEntered = Stopwatch.GetTimestamp();
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Log.UseFile(Path.Combine(Paths.DataDirectory, "desktop.log"));
        // A crash off the UI thread (input, watchers) lands in the log too, not only in the Windows event log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Info($"ERROR unhandled exception, terminating: {e.ExceptionObject}");

        if (args.Contains("--selftest-job"))
        {
            return SelfTest.JobExit();
        }
        if (args.FirstOrDefault(a => a.StartsWith("--selftest-quick-settings", StringComparison.Ordinal)) is { } quickSettings)
        {
            return SelfTest.QuickSettingsState(toggle: quickSettings.EndsWith("=toggle", StringComparison.Ordinal));
        }

        var instance = AppInstance.FindOrRegisterForKey("playdex-desktop");
        if (!instance.IsCurrent)
        {
            // Bring the running instance's window up instead; it may take the foreground from us.
            Native.AllowSetForegroundWindow(instance.ProcessId);
            var redirect = Task.Run(() => instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait(TimeSpan.FromSeconds(3)));
            redirect.Wait(TimeSpan.FromSeconds(4));
            return 0;
        }

        if (args.Contains(QuitFlag))
        {
            return 0; // Nothing running to quit.
        }

        var options = LaunchOptions.Parse(args);
        var background = args.Contains("--background");
        var bigPicture = args.Contains(BigPictureFlag);
        var fromFse = args.Contains(FseFlag);
        var healthSignal = args.FirstOrDefault(a => a.StartsWith("--update-health=", StringComparison.Ordinal))?["--update-health=".Length..];
        Paths.MoveLegacyLauncherData();
        var store = ConfigStore.Default();
        // A config that can't be read is moved aside and replaced with a default one: the app still opens.
        var config = store.LoadOrRecover();
        Log.Info($"desktop: {DesktopHost.Version} starting{(background ? " in the tray" : "")}{(bigPicture ? " in Big Picture" : "")}, mode {config.Shell.Mode}{(HasPackageIdentity() ? ", WITH package identity" : "")}");
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options, store, config, instance, background, bigPicture, fromFse, healthSignal);
        });
        return 0;
    }

    /// <summary>
    /// Playdex runs unpackaged. Started by the Full Screen Experience package without breaking away, it would share the
    /// package's identity (and its single-instance keys), so this is logged.
    /// </summary>
    private static unsafe bool HasPackageIdentity()
    {
        uint length = 0;
        return NativeDesktop.GetCurrentPackageFullName(&length, null) != NativeDesktop.NoPackage;
    }
}

#pragma warning disable CA1001 // Everything it owns is released in Quit, the only way the app ends.
internal sealed partial class App : Application
#pragma warning restore CA1001
{
    private readonly LaunchOptions _options;
    private readonly ConfigStore _store;
    private readonly AppConfig _config;
    private readonly AppInstance _instance;
    private readonly bool _background;
    private readonly bool _bigPicture;
    private readonly string? _healthSignal;
    private Task<CoreWebView2Environment>? _environment;
    private ShellServices? _services;
    private DesktopHost? _host;
    private TrayIcon? _tray;
    private UpdateFeature? _updates;
    private FseFeature? _fse;
    private ShellWindow? _window;
    private bool _windowOpened;

    /// <summary>A window is being created: creating it pumps messages, so another Open can run before it is _window.</summary>
    private bool _creatingWindow;

    /// <summary>Opened from the Full Screen Experience entry: switching to desktop mode leaves the FSE first.</summary>
    private bool _fromFse;

    public App(LaunchOptions options, ConfigStore store, AppConfig config, AppInstance instance, bool background, bool bigPicture, bool fromFse, string? healthSignal)
    {
        _fromFse = fromFse;
        _options = options;
        _store = store;
        _config = config;
        _instance = instance;
        _background = background;
        _bigPicture = bigPicture;
        _healthSignal = healthSignal;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        UnhandledException += (_, e) =>
        {
            Log.Error("desktop: unhandled exception", e.Exception);
            e.Handled = true;
        };
        try
        {
            Launch();
        }
        catch (Exception ex)
        {
            Log.Error("desktop: startup failed", ex);
            throw;
        }
    }

    private void Launch()
    {
        // The app lives in the tray: closing the window releases it (and WebView2) but keeps running.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        RecordExecutablePath(_store, _config);
        // Autoplay without a gesture: navigation sounds play on controller input, which reaches the page as host messages.
        _environment = CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            Path.Combine(Paths.DataDirectory, "WebView2-desktop"),
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection --autoplay-policy=no-user-gesture-required" }).AsTask();

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _services = new ShellServices(_config, dispatcher);
        _host = new DesktopHost(_store, _config, _services, () => _window, Quit, () => Open());
        _tray = new TrayIcon("Playdex", () => Open(), Quit, () => IsBigPicture ? "Switch to desktop mode" : "Switch to Big Picture", ToggleMode);
        _host.Tray = _tray;
        if (UpdateFeature.InstallDirectory is not null)
        {
            // News goes to a Windows notification only while no window is open; the window shows it on its Settings tab.
            _updates = new UpdateFeature(_host.PushState, () => _services.Sessions.IsGameRunning, (title, text) =>
            {
                if (_window is null)
                {
                    _tray.Notify(title, text);
                }
            });
            _host.Updates = _updates;
        }
        _fse = new FseFeature(_host.PushState);
        _host.Fse = _fse;
        _instance.Activated += (_, e) =>
        {
            var (bigPicture, gameId, quit, fromFse) = Requested(e);
            dispatcher.TryEnqueue(() =>
            {
                if (quit)
                {
                    Quit();
                    return;
                }
                _fromFse |= fromFse;
                Open(bigPicture ? ShellMode.FullScreen : null, gameId);
            });
        };
        if (UpdateFeature.InstallDirectory is { } installed)
        {
            // Settings → Apps shows the version that runs, also after a self-update.
            _ = Task.Run(() =>
            {
                try
                {
                    InstallRegistration.Refresh(installed, DesktopHost.Version);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Log.Error("desktop: could not update the Installed apps entry", ex);
                }
            });
        }

        if (_bigPicture)
        {
            Open(ShellMode.FullScreen);
        }
        else if (!_background)
        {
            Open();
        }
        _services.Start();
        _host.Start();
        _updates?.Start();
        _fse.Start();
        if (_healthSignal is not null)
        {
            ConfirmHealthy(_healthSignal);
        }
    }

    private bool IsBigPicture => _config.Shell.Mode == ShellConfig.BigPictureMode;

    /// <summary>
    /// Bring Playdex up: in <paramref name="requested"/> mode, else the saved one. A window in the other mode is
    /// replaced; Big Picture (and a game to launch) also goes home, as the launcher always did on a relaunch.
    /// </summary>
    private void Open(ShellMode? requested = null, string? launchGameId = null)
    {
        if (_creatingWindow)
        {
            return; // the window being created comes up anyway
        }
        var mode = requested ?? (IsBigPicture ? ShellMode.FullScreen : ShellMode.Desktop);
        if (mode == ShellMode.Desktop && _fromFse)
        {
            // Desktop mode belongs on the desktop: leave the Full Screen Experience, then show the window once the
            // shell has switched (it animates).
            _fromFse = false;
            if (FullScreenExperience.Leave())
            {
                var fullScreen = _window;
                _window = null;
                fullScreen?.Close();
                _ = OpenAfterAsync(TimeSpan.FromMilliseconds(900), mode, launchGameId);
                return;
            }
        }
        if (_window is { } open)
        {
            if (open.Mode == mode)
            {
                if (mode == ShellMode.FullScreen || launchGameId is not null)
                {
                    open.OnRedirectedActivation(launchGameId);
                }
                else
                {
                    open.BringToFront();
                }
                return;
            }
            Log.Info($"desktop: switching to {(mode == ShellMode.FullScreen ? "Big Picture" : "desktop mode")}");
            _window = null;
            open.Close();
        }

        // Command-line launches and measurements apply to the first window only.
        var options = _windowOpened
            ? _options with { LaunchGameId = launchGameId, ExitOnReady = false, BenchInput = 0, ExitWhenDone = false }
            : launchGameId is null ? _options : _options with { LaunchGameId = launchGameId };
        _windowOpened = true;
        ShellWindow window;
        _creatingWindow = true;
        try
        {
            window = new ShellWindow(_environment!, options, _services!, mode, _host!.HandleMessage);
        }
        finally
        {
            _creatingWindow = false;
        }
        window.Closed += (_, _) =>
        {
            if (_window != window)
            {
                return; // replaced by a window in the other mode
            }
            _window = null;
            if (options.ExitOnReady || options.ExitWhenDone)
            {
                Quit(); // a measurement run
            }
        };
        _window = window;
        window.Show();
    }

    private async Task OpenAfterAsync(TimeSpan delay, ShellMode mode, string? launchGameId)
    {
        await Task.Delay(delay);
        Open(mode, launchGameId);
    }

    /// <summary>The tray's "Switch to …": save the other mode and show it.</summary>
    private void ToggleMode()
    {
        _host!.SetMode(IsBigPicture ? ShellConfig.DesktopMode : ShellConfig.BigPictureMode);
        Open();
    }

    /// <summary>
    /// What a second start asked for: Big Picture (the Full Screen Experience entry, which also says --fse), a game to
    /// launch, or to quit (the setup).
    /// </summary>
    private static (bool BigPicture, string? GameId, bool Quit, bool FromFse) Requested(AppActivationArguments args)
    {
        var line = args.Kind == ExtendedActivationKind.Launch && args.Data is ILaunchActivatedEventArgs launch ? launch.Arguments ?? "" : "";
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.Trim('"')).ToArray();
        return (parts.Contains(Program.BigPictureFlag), LaunchOptions.Parse(parts).LaunchGameId, parts.Contains(Program.QuitFlag),
            parts.Contains(Program.FseFlag));
    }

    /// <summary>The Full Screen Experience package lives elsewhere and starts this exe; tell it where it is.</summary>
    private static void RecordExecutablePath(ConfigStore store, AppConfig config)
    {
#if !DEBUG
        var path = Environment.ProcessPath;
        if (path is null || string.Equals(config.Shell.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        config.Shell.ExecutablePath = path;
        try
        {
            store.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("desktop: could not record the app's path", ex);
        }
#endif
    }

    private void Quit()
    {
        Log.Info("desktop: quitting");
        var window = _window;
        _window = null;
        window?.Close();
        _tray?.Dispose();
        _updates?.Dispose();
        _fse?.Dispose();
        _host?.Dispose();
        _services?.Dispose();
        Exit();
    }

    /// <summary>The updater waits for this file before it deletes the previous version.</summary>
    private static void ConfirmHealthy(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "healthy");
            Log.Info("desktop: confirmed a healthy start after the update");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("desktop: could not confirm the update", ex);
        }
    }
}
