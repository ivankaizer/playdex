using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.AppLifecycle;

namespace Playdex.Desktop;

/// <summary>
/// Playdex for the desktop: a normal window and the tray. One instance; starting it again opens the window.
/// </summary>
/// <remarks>
/// Flags beyond <see cref="LaunchOptions"/>:
/// --background                   start in the tray without a window (how it starts with Windows)
/// --update-health=PATH           started by the updater: confirm a healthy start by writing PATH
/// </remarks>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Startup.MainEntered = Stopwatch.GetTimestamp();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Log.UseFile(Path.Combine(Paths.DataDirectory, "desktop.log"));

        var instance = AppInstance.FindOrRegisterForKey("playdex-desktop");
        if (!instance.IsCurrent)
        {
            // Bring the running instance's window up instead.
            var redirect = Task.Run(() => instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait(TimeSpan.FromSeconds(3)));
            redirect.Wait(TimeSpan.FromSeconds(4));
            return 0;
        }

        var options = LaunchOptions.Parse(args);
        var background = args.Contains("--background");
        var healthSignal = args.FirstOrDefault(a => a.StartsWith("--update-health=", StringComparison.Ordinal))?["--update-health=".Length..];
        var store = ConfigStore.Default();
        var config = store.LoadOrCreate();
        Log.Info($"desktop: {DesktopHost.Version} starting{(background ? " in the tray" : "")}");
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options, store, config, instance, background, healthSignal);
        });
        return 0;
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
    private readonly string? _healthSignal;
    private Task<CoreWebView2Environment>? _environment;
    private ShellServices? _services;
    private DesktopHost? _host;
    private TrayIcon? _tray;
    private ShellWindow? _window;

    public App(LaunchOptions options, ConfigStore store, AppConfig config, AppInstance instance, bool background, string? healthSignal)
    {
        _options = options;
        _store = store;
        _config = config;
        _instance = instance;
        _background = background;
        _healthSignal = healthSignal;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
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
        // Not the launcher's profile: both apps can run at once, and WebView2 profiles are per process here.
        _environment = CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            Path.Combine(Paths.DataDirectory, "WebView2-desktop"),
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection" }).AsTask();

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _services = new ShellServices(_config, dispatcher);
        _host = new DesktopHost(_store, _config, _services, () => _window, Quit);
        _tray = new TrayIcon("Playdex", ShowWindow, Quit);
        _host.Tray = _tray;
        _instance.Activated += (_, _) => dispatcher.TryEnqueue(ShowWindow);

        if (!_background)
        {
            ShowWindow();
        }
        _services.Start();
        _host.Start();
        if (_healthSignal is not null)
        {
            ConfirmHealthy(_healthSignal);
        }
    }

    private void ShowWindow()
    {
        if (_window is { } open)
        {
            open.BringToFront();
            return;
        }
        _window = new ShellWindow(_environment!, _options, _services!, ShellMode.Desktop, _host!.HandleMessage);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    private void Quit()
    {
        Log.Info("desktop: quitting");
        _window?.Close();
        _tray?.Dispose();
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
