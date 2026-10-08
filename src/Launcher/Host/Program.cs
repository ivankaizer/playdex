using System.Diagnostics;
using System.Runtime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Playdex.Launcher.Host;

internal sealed record LaunchOptions(
    bool Dev,
    string? UiUrl,
    bool ExitOnReady,
    int BenchInput,
    int BenchIntervalMs,
    bool ExitWhenDone,
    string? LaunchGameId,
    bool SuspendWhileGaming)
{
    /// <summary>
    /// --dev / --ui-url=URL           load the Vite dev server instead of the packaged ui/ folder
    /// --exit-on-ready                close once the UI is interactive (cold-start measurement)
    /// --bench-input=N [--bench-interval=MS] [--exit-when-done]
    ///                                inject N synthetic presses through the input pipeline and log latency
    /// --launch=GAMEID                launch a game once the UI is ready (also playdex-launcher://launch/GAMEID)
    /// --no-suspend                   keep the page running while a game is in front
    /// --selftest-job                 run the Job Object exit-detection self-test and exit
    /// </summary>
    public static LaunchOptions Parse(string[] args)
    {
        string? Value(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];
        bool Flag(string name) => args.Contains($"--{name}");
        var uiUrl = Value("ui-url") ?? Environment.GetEnvironmentVariable("PLAYDEX_LAUNCHER_UI_URL");
#if DEBUG
        var dev = !Flag("dist");
#else
        var dev = Flag("dev") || uiUrl is not null;
#endif
        return new LaunchOptions(
            dev,
            uiUrl,
            Flag("exit-on-ready"),
            int.TryParse(Value("bench-input"), out var n) ? n : 0,
            int.TryParse(Value("bench-interval"), out var ms) ? ms : 80,
            Flag("exit-when-done"),
            Value("launch") ?? ProtocolGameId(args.FirstOrDefault(a => a.StartsWith("playdex-launcher:", StringComparison.OrdinalIgnoreCase))),
            !Flag("no-suspend"));
    }

    /// <summary>playdex-launcher://launch/GAMEID → GAMEID.</summary>
    public static string? ProtocolGameId(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Host != "launch")
        {
            return null;
        }
        var id = parsed.AbsolutePath.Trim('/');
        return id.Length > 0 ? Uri.UnescapeDataString(id) : null;
    }
}

public static class Program
{
    /// <summary>Stopwatch timestamp at Main(); startup metrics are measured from here and from process start.</summary>
    internal static long MainEntered { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        MainEntered = Stopwatch.GetTimestamp();
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (args.Contains("--selftest-job"))
        {
            return SelfTest.JobExit();
        }

        var instance = AppInstance.FindOrRegisterForKey("playdex-launcher");
        if (!instance.IsCurrent)
        {
            Redirect(instance);
            return 0;
        }

        var options = LaunchOptions.Parse(args);
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(options, instance);
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
    private readonly AppInstance _instance;
    private MainWindow? _window;

    public App(LaunchOptions options, AppInstance instance)
    {
        _options = options;
        _instance = instance;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Start WebView2 (browser process spawn) first; window creation runs while it starts.
        var environment = CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            Paths.WebViewData,
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection" }).AsTask();

        _window = new MainWindow(environment, _options);
        _instance.Activated += OnRedirected;
        _window.Show();
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
