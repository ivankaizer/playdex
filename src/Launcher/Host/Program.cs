using System.Diagnostics;
using System.Runtime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Playdex.Launcher.Host;

/// <summary>
/// The launcher: the Full Screen Experience home app. One full-screen <see cref="ShellWindow"/> for the whole life of the
/// process; every relaunch (FSE, Game Bar "Home", a second start) is redirected to it.
/// </summary>
/// <remarks>
/// Flags beyond <see cref="LaunchOptions"/>:
/// --selftest-job                 run the Job Object exit-detection self-test and exit
/// --selftest-quick-settings[=toggle]
///                                log the quick settings state (sound outputs, HDR, refresh rates, FPS counter); with
///                                =toggle, switch the sound output, HDR and refresh rate of each display, check, switch
///                                back, then toggle the FPS counter twice
/// </remarks>
public static class Program
{
    /// <summary>Stopwatch timestamp at Main(); startup metrics are measured from here and from process start.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Startup.MainEntered = Stopwatch.GetTimestamp();
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (args.Contains("--selftest-job"))
        {
            return SelfTest.JobExit();
        }
        if (args.FirstOrDefault(a => a.StartsWith("--selftest-quick-settings", StringComparison.Ordinal)) is { } quickSettings)
        {
            return SelfTest.QuickSettingsState(toggle: quickSettings.EndsWith("=toggle", StringComparison.Ordinal));
        }

        var instance = AppInstance.FindOrRegisterForKey("playdex-launcher");
        if (!instance.IsCurrent)
        {
            Redirect(instance);
            return 0;
        }

        var options = LaunchOptions.Parse(args);
        Paths.MoveLegacyLauncherData();
        var config = ConfigStore.Default().LoadOrCreate();
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options, config, instance);
        });
        return 0;
    }

    /// <summary>
    /// Hand this activation to the running launcher, which comes to the front and goes home. Bounded, so a hung
    /// primary instance cannot hang every later launch.
    /// </summary>
    private static void Redirect(AppInstance primary)
    {
        Native.AllowSetForegroundWindow(primary.ProcessId);
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        // The redirect must not run on this STA thread.
        var redirect = Task.Run(() => primary.RedirectActivationToAsync(args).AsTask().Wait(TimeSpan.FromSeconds(3)));
        if (!redirect.Wait(TimeSpan.FromSeconds(4)))
        {
            Log.Info("redirect to the running launcher timed out");
        }
    }
}

internal sealed partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly AppConfig _config;
    private readonly AppInstance _instance;
    private ShellWindow? _window;

    public App(LaunchOptions options, AppConfig config, AppInstance instance)
    {
        _options = options;
        _config = config;
        _instance = instance;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Start WebView2 (browser process spawn) first; window creation runs while it starts.
        var environment = CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            Path.Combine(Paths.DataDirectory, "WebView2"),
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection" }).AsTask();

        var services = new ShellServices(_config, DispatcherQueue.GetForCurrentThread());
        _window = new ShellWindow(environment, _options, services, ShellMode.FullScreen);
        _window.Closed += (_, _) => services.Dispose();
        _instance.Activated += OnRedirected;
        _window.Show();
        services.Start();
    }

    private void OnRedirected(object? sender, AppActivationArguments args)
    {
        string? gameId = null;
        if (args.Kind == ExtendedActivationKind.Protocol && args.Data is IProtocolActivatedEventArgs protocol)
        {
            gameId = LaunchOptions.ProtocolGameId(protocol.Uri.AbsoluteUri);
        }
        else if (args.Kind == ExtendedActivationKind.Launch && args.Data is ILaunchActivatedEventArgs launch)
        {
            gameId = LaunchOptions.ProtocolGameId(launch.Arguments?.Split(' ').FirstOrDefault(a => a.StartsWith("playdex-launcher:", StringComparison.OrdinalIgnoreCase)));
        }
        _window?.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () => _window.OnRedirectedActivation(gameId));
    }
}
