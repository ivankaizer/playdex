using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WinRT.Interop;

namespace Playdex.Launcher.Host;

[JsonSerializable(typeof(string))]
internal sealed partial class HostJson : JsonSerializerContext;

/// <summary>
/// The one full-screen window: a native splash under a WebView2 that hosts the React UI. Code-only (no XAML) to
/// keep startup short.
/// </summary>
#pragma warning disable CA1001 // Owned disposables are released in Closed; the window lives as long as the process.
internal sealed partial class MainWindow : Window
#pragma warning restore CA1001
{
    private const string DevUrl = "http://localhost:5190/";
    private const string VirtualHost = "app.playdex";

    private readonly Task<CoreWebView2Environment> _environment;
    private readonly WebView2 _webView;
    private readonly nint _hwnd;
    private readonly InputBridge _bridge = new();
    private readonly ControllerInput _input;
    private readonly SessionManager _sessions;
    private readonly SessionQueue _queue;
    private readonly CaptureClient _api = new();
    private readonly LaunchOptions _options;
    private readonly Task<IReadOnlyDictionary<string, LibraryGame>> _library = Task.Run(Library.Scan);
    private long _returnStart;
    private long _activateStart;
    private bool _ready;
    private bool _suspended;

    public MainWindow(Task<CoreWebView2Environment> environment, LaunchOptions options)
    {
        _environment = environment;
        _options = options;
        Title = "Playdex";
        _hwnd = WindowNative.GetWindowHandle(this);

        // Native splash: what shows until the page paints over it (WebView2's own background is transparent).
        var splash = new TextBlock
        {
            Text = "PLAYDEX",
            FontSize = 28,
            CharacterSpacing = 400,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xA7, 0xF3, 0xD0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _webView = new WebView2 { DefaultBackgroundColor = Colors.Transparent };
        var root = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x0E, 0x14, 0x13)) };
        root.Children.Add(splash);
        root.Children.Add(_webView);
        Content = root;

        _queue = new SessionQueue(_api);
        _sessions = new SessionManager(_api, _queue, PostFromAnyThread, OnGameExited);
        _input = new ControllerInput(new InputQueue(DispatcherQueue, OnInput));

        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _bridge.Flush();
            _queue.Dispose();
            _api.Dispose();
        };

        // Newer builds ignore a presenter set before the window is shown, so set it again once it is.
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    public void Show()
    {
        Activate();
        DispatcherQueue.TryEnqueue(() => AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen));
        Metrics.Record("startup.window_shown", Metrics.MsSince(Program.MainEntered));
        _ = InitializeWebViewAsync();
        _input.Start();
        _ = Task.Run(_queue.FlushAsync);
    }

    /// <summary>Another activation (second launch, FSE, Game Bar "Home", protocol link): come to the front, go home.</summary>
    public void OnRedirectedActivation(string? launchGameId)
    {
        _activateStart = Stopwatch.GetTimestamp();
        BringToFront();
        Post("""{"type":"home"}""");
        if (launchGameId is not null)
        {
            Post($$"""{"type":"launch","gameId":{{JsonSerializer.Serialize(launchGameId, HostJson.Default.String)}}}""");
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            var environment = await _environment;
            Metrics.Record("startup.webview_environment_wait", Metrics.MsSince(started));
            await _webView.EnsureCoreWebView2Async(environment);
            Metrics.Record("startup.webview_ready", Metrics.MsSince(Program.MainEntered));

            var web = _webView.CoreWebView2;
            var settings = web.Settings;
            settings.AreDefaultContextMenusEnabled = _options.Dev;
            settings.AreDevToolsEnabled = _options.Dev;
            settings.AreBrowserAcceleratorKeysEnabled = _options.Dev;
            settings.IsZoomControlEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            web.WebMessageReceived += OnWebMessage;
            web.ProcessFailed += (_, e) => Log.Info($"webview process failed: {e.ProcessFailedKind}");
            _bridge.Attach(web);
            // The library is in place before the page's own scripts run, so the UI renders real games on first paint.
            // Not awaited: calls reach the browser process in order, so the script is registered before Navigate
            // below; awaiting the round trip cost ~90 ms of cold start.
            var injected = web.AddScriptToExecuteOnDocumentCreatedAsync($"window.__PLAYDEX_LIBRARY__ = {Library.ToJson(await _library)};");

            var dist = Path.Combine(AppContext.BaseDirectory, "ui");
            string url;
            if (!_options.Dev && File.Exists(Path.Combine(dist, "index.html")))
            {
                web.SetVirtualHostNameToFolderMapping(VirtualHost, dist, CoreWebView2HostResourceAccessKind.Allow);
                url = $"https://{VirtualHost}/index.html";
            }
            else
            {
                url = _options.UiUrl ?? DevUrl;
            }
            Log.Info($"navigating to {url}");
            web.Navigate(url);
            await injected;
        }
        catch (Exception ex)
        {
            Log.Error("WebView2 initialisation failed", ex);
        }
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var doc = JsonDocument.Parse(args.WebMessageAsJson);
        var message = doc.RootElement;
        switch (message.GetProperty("type").GetString())
        {
            case "ack":
                _bridge.Ack(message.GetProperty("id").GetInt32(), message.TryGetProperty("presented", out var presented) && presented.GetBoolean());
                if (_options.BenchInput > 0 && _bridge.Completed == _options.BenchInput)
                {
                    FinishBench();
                }
                break;
            case "ready":
                OnUiReady();
                break;
            case "home-ready":
                var reason = message.GetProperty("reason").GetString();
                if (reason == "game-exit" && _returnStart != 0)
                {
                    Metrics.Record("return_home.after_game_exit", Metrics.MsSince(_returnStart), "exit event -> home frame");
                    _returnStart = 0;
                }
                else if (reason == "activate" && _activateStart != 0)
                {
                    Metrics.Record("return_home.activation", Metrics.MsSince(_activateStart), "activation -> home frame");
                    _activateStart = 0;
                }
                break;
            case "launch":
                // Launch only what the host found on disk; the page never supplies a path or an app id.
                var gameId = message.GetProperty("gameId").GetString()!;
                _sessions.Launch(gameId, _library.IsCompletedSuccessfully ? _library.Result.GetValueOrDefault(gameId) : null);
                break;
            case "cancel":
                _sessions.Cancel(message.GetProperty("gameId").GetString()!);
                break;
        }
    }

    private void OnUiReady()
    {
        if (_ready)
        {
            return;
        }
        _ready = true;
        var processStart = Process.GetCurrentProcess().StartTime;
        Metrics.Record("startup.cold_to_interactive", (DateTime.Now - processStart).TotalMilliseconds, "process start -> first interactive frame");
        Metrics.Record("startup.main_to_interactive", Metrics.MsSince(Program.MainEntered), "Main() -> first interactive frame");
        Log.Info($"controller: {_input.ConnectedPads} pad(s), guide {(_input.GuideSupported ? "supported" : "unsupported")}");

        if (_options.ExitOnReady)
        {
            Close();
            return;
        }
        if (_options.BenchInput > 0)
        {
            _input.StartBench(_options.BenchInput, _options.BenchIntervalMs);
        }
        if (_options.LaunchGameId is { } gameId)
        {
            Post($$"""{"type":"launch","gameId":{{JsonSerializer.Serialize(gameId, HostJson.Default.String)}}}""");
        }
    }

    private void FinishBench()
    {
        if (_options.ExitWhenDone)
        {
            Close(); // Closed flushes the stats.
            return;
        }
        _bridge.Flush();
    }

    private void OnInput(InputCode code, long timestamp)
    {
        if (code == InputCode.Guide && !IsForeground())
        {
            // A game is in front: the Guide button brings the launcher back.
            OnRedirectedActivation(null);
            return;
        }
        _bridge.Deliver(code, timestamp);
    }

    private void OnGameExited(long detected)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
        {
            _returnStart = detected;
            ResumeWebView();
            BringToFront();
            Metrics.Record("return_home.window_activated", Metrics.MsSince(detected), "exit event -> launcher in front");
        });
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        var active = args.WindowActivationState != WindowActivationState.Deactivated;
        _input.Foreground = active;
        if (active)
        {
            ResumeWebView();
        }
        else if (_sessions.IsGameRunning && _options.SuspendWhileGaming)
        {
            _ = SuspendWebViewAsync();
        }
    }

    /// <summary>While a game is in front the page is hidden and suspended, so it costs no CPU.</summary>
    private async Task SuspendWebViewAsync()
    {
        if (_suspended || _webView.CoreWebView2 is null)
        {
            return;
        }
        _webView.Visibility = Visibility.Collapsed;
        _suspended = await _webView.CoreWebView2.TrySuspendAsync();
        Log.Info($"webview suspended: {_suspended}");
    }

    private void ResumeWebView()
    {
        if (_webView.Visibility == Visibility.Visible && !_suspended)
        {
            return;
        }
        _webView.Visibility = Visibility.Visible;
        if (_suspended)
        {
            _webView.CoreWebView2?.Resume();
            _suspended = false;
        }
    }

    private bool IsForeground() => Native.GetForegroundWindow() == _hwnd;

    private void BringToFront()
    {
        ResumeWebView();
        if (Native.IsIconic(_hwnd))
        {
            Native.ShowWindow(_hwnd, Native.SwRestore);
        }
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        if (Native.SetForegroundWindow(_hwnd) && IsForeground())
        {
            return;
        }
        // Foreground lock: borrow the foreground thread's input state for the call.
        var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var thisThread = Native.GetCurrentThreadId();
        Native.AttachThreadInput(thisThread, foregroundThread, true);
        Native.BringWindowToTop(_hwnd);
        Native.SetForegroundWindow(_hwnd);
        Native.AttachThreadInput(thisThread, foregroundThread, false);
        Log.Info($"bring to front via AttachThreadInput: {(IsForeground() ? "ok" : "failed")}");
    }

    private void PostFromAnyThread(string json) => DispatcherQueue.TryEnqueue(() => Post(json));

    private void Post(string json)
    {
        if (_webView.CoreWebView2 is { } web)
        {
            web.PostWebMessageAsJson(json);
        }
    }
}
