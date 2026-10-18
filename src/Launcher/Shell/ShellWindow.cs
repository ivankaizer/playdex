using Playdex.Core;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using WinRT.Interop;

namespace Playdex.Shell;

[JsonSerializable(typeof(string))]
internal sealed partial class HostJson : JsonSerializerContext;

/// <summary>How the shell window presents the UI.</summary>
internal enum ShellMode
{
    /// <summary>Big Picture: full screen, controller first (also the Full Screen Experience home); it comes back to the front after a game.</summary>
    FullScreen,

    /// <summary>Desktop mode: a normal window opened from the tray, mouse and keyboard first.</summary>
    Desktop,
}

/// <summary>When Main() started, for the startup metrics.</summary>
internal static class Startup
{
    public static long MainEntered { get; set; }
}

/// <summary>
/// A window hosting the React UI in WebView2, over a native splash: full screen in Big Picture, a normal window in
/// desktop mode. The app opens one from the tray and closes it (releasing WebView2) when hidden or when the mode changes. Sessions,
/// controller input and the library live in <see cref="ShellServices"/>, so they outlive the window. Code-only (no XAML)
/// to keep startup short.
/// </summary>
#pragma warning disable CA1001 // The bridge's stats are flushed in Closed; the window owns nothing else disposable.
internal sealed partial class ShellWindow : Window
#pragma warning restore CA1001
{
    private const string DevUrl = "http://localhost:5190/";
    private const string VirtualHost = "app.playdex";

    private readonly Task<CoreWebView2Environment> _environment;
    private readonly WebView2 _webView;
    private readonly nint _hwnd;
    private readonly InputBridge _bridge = new();
    private readonly ShellServices _services;
    private readonly LaunchOptions _options;
    private readonly ShellMode _mode;
    private readonly Func<string, JsonElement, bool>? _onMessage;
    private string? _libraryScriptId;

    // Library: the games the page was given. Checked against a fresh scan when the window comes back (activation), at
    // most every 30 s; the page reloads when games were installed, uninstalled or added.
    private static readonly TimeSpan _libraryMaxAge = TimeSpan.FromSeconds(30);
    private IReadOnlyDictionary<string, LibraryGame>? _pageLibrary;
    private bool _checkingLibrary;

    private long _returnStart;
    private bool _active;
    private long _activateStart;
    private bool _ready;
    private bool _suspended;

    // Now playing: polled while the window is active (games started elsewhere don't announce themselves).
    private static readonly TimeSpan _nowPlayingInterval = TimeSpan.FromSeconds(3);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _nowPlayingTimer;
    private RunningGame? _nowPlaying;
    private bool _nowPlayingPosted;
    private bool _detecting;

    /// <summary>The window in front when the shell was called up over a game: what "Resume" returns to.</summary>
    private nint _gameWindow;

    // Playdex summary: refreshed when the page loads, after sessions, when the window comes back, and every minute while
    // it shows (Playdex polls the gaming providers every minute). Desktop mode keeps refreshing behind other windows;
    // Big Picture stops while a game is in front.
    private static readonly TimeSpan _summaryInterval = TimeSpan.FromMinutes(1);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _summaryTimer;
    private long _summaryRefreshedAt;
    private bool _refreshingSummary;

    /// <param name="onMessage">Page messages the shell doesn't handle (the desktop app's own); true when handled.</param>
    public ShellWindow(
        Task<CoreWebView2Environment> environment,
        LaunchOptions options,
        ShellServices services,
        ShellMode mode,
        Func<string, JsonElement, bool>? onMessage = null)
    {
        _environment = environment;
        _options = options;
        _services = services;
        _mode = mode;
        _onMessage = onMessage;
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

        _nowPlayingTimer = DispatcherQueue.CreateTimer();
        _nowPlayingTimer.Interval = _nowPlayingInterval;
        _nowPlayingTimer.Tick += (_, _) => _ = RefreshNowPlayingAsync();

        // Ticks more often than the interval, so timer jitter never skips a refresh; RefreshSummary keeps the pace.
        _summaryTimer = DispatcherQueue.CreateTimer();
        _summaryTimer.Interval = TimeSpan.FromSeconds(15);
        _summaryTimer.Tick += (_, _) =>
        {
            if (_active || (_mode == ShellMode.Desktop && !Native.IsIconic(_hwnd)))
            {
                RefreshSummary(force: false);
            }
        };

        services.SessionChanged += PostSession;
        services.GameExited += OnGameExited;
        services.InputReceived += OnInput;
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _nowPlayingTimer.Stop();
            _summaryTimer.Stop();
            services.SessionChanged -= PostSession;
            services.GameExited -= OnGameExited;
            services.InputReceived -= OnInput;
            services.Input.Foreground = false;
            _bridge.Flush();
        };

        if (mode == ShellMode.FullScreen)
        {
            // Newer builds ignore a presenter set before the window is shown, so Show sets it again once it is.
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            // The size it restores to; it opens maximized (Show).
            var scale = Native.GetDpiForWindow(_hwnd) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(1360 * scale), (int)(860 * scale)));
        }
    }

    public nint Handle => _hwnd;

    public ShellMode Mode => _mode;

    public void Show()
    {
        Activate();
        if (_mode == ShellMode.FullScreen)
        {
            DispatcherQueue.TryEnqueue(() => AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen));
        }
        else
        {
            // Desktop mode opens using the whole screen, with its title bar and the taskbar still there. After Activate:
            // it shows the window at its normal size, which undid a Maximize made before it.
            Native.ShowWindow(_hwnd, Native.SwMaximize);
        }
        Metrics.Record("startup.window_shown", Metrics.MsSince(Startup.MainEntered));
        _summaryTimer.Start();
        _ = InitializeWebViewAsync();
    }

    /// <summary>Another activation (second launch, FSE, Game Bar "Home", protocol link): come to the front, go home.</summary>
    public void OnRedirectedActivation(string? launchGameId)
    {
        _activateStart = Stopwatch.GetTimestamp();
        if (Native.GetForegroundWindow() is var foreground && foreground != _hwnd)
        {
            _gameWindow = foreground; // GameWindows checks it is an app window before going back to it.
        }
        BringToFront();
        Post("""{"type":"home"}""");
        if (launchGameId is not null)
        {
            Post($$"""{"type":"launch","gameId":{{JsonSerializer.Serialize(launchGameId, HostJson.Default.String)}}}""");
        }
    }

    /// <summary>Show the library as it is now: rescan, replace the injected script and reload the page.</summary>
    public async Task ReloadLibraryAsync() => await ShowLibraryAsync(await _services.RescanLibrary());

    private async Task ShowLibraryAsync(IReadOnlyDictionary<string, LibraryGame> library)
    {
        if (_webView.CoreWebView2 is not { } web)
        {
            return;
        }
        if (_libraryScriptId is not null)
        {
            web.RemoveScriptToExecuteOnDocumentCreated(_libraryScriptId);
        }
        _pageLibrary = library;
        _libraryScriptId = await web.AddScriptToExecuteOnDocumentCreatedAsync(PageScript(library));
        web.Reload();
    }

    /// <summary>
    /// The window came back: show games installed or uninstalled since the page got its library. Not while a game
    /// launched here is starting or running, so its launch screen stays; the next activation catches up.
    /// </summary>
    private async Task CatchUpLibraryAsync()
    {
        if (_pageLibrary is null || _checkingLibrary || _services.Sessions.HasSession)
        {
            return;
        }
        _checkingLibrary = true;
        try
        {
            var library = await _services.FreshLibrary(_libraryMaxAge);
            if (ReferenceEquals(library, _pageLibrary) || Library.SameGames(library, _pageLibrary) || _services.Sessions.HasSession)
            {
                return;
            }
            Log.Info($"library: changed ({_pageLibrary.Count} -> {library.Count} game(s)), reloading the page");
            await ShowLibraryAsync(library);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("library: rescan failed", ex);
        }
        finally
        {
            _checkingLibrary = false;
        }
    }

    /// <summary>
    /// The library, the last Playdex summary (null before the first one), whether there is an API key, and the mode the
    /// page lays itself out for, in place before the page's own scripts run.
    /// </summary>
    private string PageScript(IReadOnlyDictionary<string, LibraryGame> library) =>
        $"window.__PLAYDEX_LIBRARY__ = {Library.ToJson(library)}; " +
        $"window.__PLAYDEX_SUMMARY__ = {_services.Summary.Current ?? "null"}; " +
        $"window.__PLAYDEX_API__ = {(_services.Api.IsConfigured ? "true" : "false")}; " +
        $"window.__PLAYDEX_MODE__ = \"{(_mode == ShellMode.Desktop ? "desktop" : "fse")}\";";

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var started = Stopwatch.GetTimestamp();
            var environment = await _environment;
            Metrics.Record("startup.webview_environment_wait", Metrics.MsSince(started));
            await _webView.EnsureCoreWebView2Async(environment);
            Metrics.Record("startup.webview_ready", Metrics.MsSince(Startup.MainEntered));

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
            // The desktop app opens a window from the tray long after the last scan: that window gets a fresh one.
            var library = await _services.FreshLibrary(_libraryMaxAge);
            _pageLibrary = library;
            var injected = web.AddScriptToExecuteOnDocumentCreatedAsync(PageScript(library));

            if (SteamLibrary.ArtCacheDirectory is { } artCache)
            {
                // Steam's own artwork cache, read in place (see Library.Scan).
                web.SetVirtualHostNameToFolderMapping(Library.ArtHost, artCache, CoreWebView2HostResourceAccessKind.Allow);
            }
            foreach (var (userId, grid) in SteamGrid.GridFolders)
            {
                // Steam grid art (custom and non-Steam shortcut artwork), read in place. Only the grid folders are
                // served, not the rest of userdata.
                web.SetVirtualHostNameToFolderMapping($"{userId}.{Library.GridHost}", grid, CoreWebView2HostResourceAccessKind.Allow);
            }

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
            _libraryScriptId = await injected;
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
        var type = message.GetProperty("type").GetString();
        switch (type)
        {
            case "ack":
                _bridge.Ack(message.GetProperty("id").GetInt32(), message.TryGetProperty("presented", out var presented) && presented.GetBoolean());
                if (_options.BenchInput > 0 && _bridge.Completed == _options.BenchInput)
                {
                    FinishBench();
                }
                break;
            case "ready":
                // Also after the desktop app reloads the page: it starts with nothing playing.
                _nowPlayingPosted = false;
                _ = RefreshNowPlayingAsync();
                // The script injected at startup may predate the summary we have now (a reloaded page): send it, then
                // check for a newer one.
                if (_services.Summary.Current is { } summary)
                {
                    Post($$"""{"type":"playdex-summary","summary":{{summary}}}""");
                }
                RefreshSummary(force: true);
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
                _services.Sessions.Launch(gameId, _services.FindGame(gameId));
                break;
            case "cancel":
                _services.Sessions.Cancel(message.GetProperty("gameId").GetString()!);
                break;
            case "quick-settings":
                PostQuickSettings(null, "read quick settings");
                break;
            case "audio-output":
                var outputId = message.GetProperty("id").GetString()!;
                PostQuickSettings(() => AudioOutputs.SetDefault(outputId), "switch the sound output");
                break;
            case "hdr":
                var displayId = message.GetProperty("display").GetString()!;
                var on = message.GetProperty("on").GetBoolean();
                PostQuickSettings(() => Displays.SetHdr(displayId, on), on ? "turn HDR on" : "turn HDR off");
                break;
            case "refresh-rate":
                var rateDisplay = message.GetProperty("display").GetString()!;
                var hz = message.GetProperty("hz").GetInt32();
                PostQuickSettings(() => Displays.SetRefreshRate(rateDisplay, hz), $"switch to {hz} Hz");
                break;
            case "playdex-recap":
                _ = PostRecapAsync(message.GetProperty("year").GetInt32());
                break;
            case "open-steam":
                OpenSteam();
                break;
            case "resume-game":
                if (!GameWindows.Activate(_hwnd, _gameWindow, _nowPlaying?.ProcessId))
                {
                    Post("""{"type":"resume-failed"}""");
                }
                break;
            case "close-game":
                _ = CloseGameAsync(message.GetProperty("gameId").GetString()!);
                break;
            case "fps-counter":
                PostQuickSettings(NvidiaOverlay.ToggleStatistics, "show or hide the FPS counter");
                break;
            case "power":
                switch (message.GetProperty("action").GetString())
                {
                    case "sleep":
                        PostQuickSettings(Power.Sleep, "put the PC to sleep");
                        break;
                    case "restart":
                        PostQuickSettings(Power.Restart, "restart the PC");
                        break;
                    case "shutdown":
                        PostQuickSettings(Power.ShutDown, "shut down the PC");
                        break;
                }
                break;
            default:
                if (type is null || _onMessage?.Invoke(type, message) != true)
                {
                    Log.Info($"page: unknown message {type}");
                }
                break;
        }
    }

    /// <summary>
    /// A year's Playdex recap for the Playdex tab, asked for when the page shows it; "recap" is null offline or without
    /// a key.
    /// </summary>
    private async Task PostRecapAsync(int year)
    {
        var recap = await Task.Run(() => _services.Api.GetLauncherRecapAsync(year));
        Post($$"""{"type":"playdex-recap","year":{{year.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"recap":{{recap ?? "null"}}}""");
    }

    /// <summary>
    /// Steam's Big Picture from Big Picture (controller first, like here), the Steam window from desktop mode. Through
    /// the steam:// protocol, so Steam handles an instance that is already running.
    /// </summary>
    private void OpenSteam()
    {
        var uri = _mode == ShellMode.FullScreen ? "steam://open/bigpicture" : "steam://open/main";
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
            Log.Info($"opened {uri}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // No handler for steam:// means Steam isn't installed.
            Log.Error($"could not open {uri}", ex);
            Post("""{"type":"open-steam-failed"}""");
        }
    }

    /// <summary>Quick settings run off the UI thread; the page gets the fresh state (and any error) back.</summary>
    private void PostQuickSettings(Action? change, string description) => _ = PostQuickSettingsAsync(change, description);

    private async Task PostQuickSettingsAsync(Action? change, string description) =>
        PostFromAnyThread(await QuickSettings.ApplyAsync(change, description));

    private void OnUiReady()
    {
        if (_ready)
        {
            return;
        }
        _ready = true;
        var processStart = Process.GetCurrentProcess().StartTime;
        Metrics.Record("startup.cold_to_interactive", (DateTime.Now - processStart).TotalMilliseconds, "process start -> first interactive frame");
        Metrics.Record("startup.main_to_interactive", Metrics.MsSince(Startup.MainEntered), "Main() -> first interactive frame");
        Log.Info($"controller: {_services.Input.ConnectedPads} pad(s), guide {(_services.Input.GuideSupported ? "supported" : "unsupported")}");

        if (_options.ExitOnReady)
        {
            Close();
            return;
        }
        if (_options.BenchInput > 0)
        {
            _services.Input.StartBench(_options.BenchInput, _options.BenchIntervalMs);
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
            // A game is in front: the Guide button brings Big Picture back. Desktop mode leaves it to Windows.
            if (_mode == ShellMode.FullScreen)
            {
                OnRedirectedActivation(null);
            }
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
            if (_mode == ShellMode.FullScreen)
            {
                BringToFront();
                Metrics.Record("return_home.window_activated", Metrics.MsSince(detected), "exit event -> launcher in front");
            }
        });
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        var active = args.WindowActivationState != WindowActivationState.Deactivated;
        _active = active;
        _services.Input.Foreground = active;
        if (active)
        {
            ResumeWebView();
            _ = RefreshNowPlayingAsync();
            _nowPlayingTimer.Start();
            if (_ready)
            {
                // Not before the page is up: its "ready" fetches the summary itself.
                RefreshSummary(force: false);
            }
            _ = CatchUpLibraryAsync();
        }
        else
        {
            _nowPlayingTimer.Stop();
            if (_mode == ShellMode.FullScreen && _services.Sessions.IsGameRunning && _options.SuspendWhileGaming)
            {
                _ = SuspendWebViewAsync();
            }
        }
    }

    /// <summary>
    /// Tell the page which game is running, if any: the one launched here, else one started elsewhere
    /// (<see cref="RunningGames"/>). Posted when it changes; "since" is epoch ms, or null when unknown.
    /// </summary>
    private async Task RefreshNowPlayingAsync()
    {
        if (_detecting)
        {
            return;
        }
        _detecting = true;
        try
        {
            var game = _services.Sessions.Current;
            if (game is null && _services.LibraryScan.IsCompletedSuccessfully)
            {
                var library = _services.LibraryScan.Result;
                game = await Task.Run(() => RunningGames.Detect(_services.Config, library));
            }
            var changed = !_nowPlayingPosted
                || game?.GameId != _nowPlaying?.GameId
                || game?.Since is null != _nowPlaying?.Since is null
                || (game?.Since is { } since && _nowPlaying?.Since is { } before && Math.Abs((since - before).TotalSeconds) > 2);
            _nowPlaying = game;
            if (game is null)
            {
                _gameWindow = 0;
            }
            if (!changed)
            {
                return;
            }
            _nowPlayingPosted = true;
            Post(game is null
                ? """{"type":"now-playing","gameId":null}"""
                : $$"""{"type":"now-playing","gameId":{{JsonSerializer.Serialize(game.GameId, HostJson.Default.String)}},"since":{{(game.Since is { } at ? at.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}}}""");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Log.Error("now playing: detection failed", ex);
        }
        finally
        {
            _detecting = false;
        }
    }

    /// <summary>
    /// Force-close the running game (<see cref="GameCloser"/>). Only the game reported as running, so the page can't end
    /// anything else. Its watcher reports the exit as usual; "close-failed" tells the page when nothing could be ended.
    /// </summary>
    private async Task CloseGameAsync(string gameId)
    {
        if (_nowPlaying is not { } running || running.GameId != gameId || _services.FindGame(gameId) is not { } game)
        {
            Log.Info($"close: {gameId} is not running");
            PostCloseFailed("The game isn't running anymore.");
            return;
        }
        var result = await Task.Run(() => GameCloser.Close(_services.Config, game, running.ProcessId));
        Log.Info($"close: {gameId}: {result.Closed} ended, {result.Denied} denied");
        if (result.Closed == 0)
        {
            PostCloseFailed(result.Denied > 0
                ? "Windows didn't let Playdex close the game. Close it from the game or Task Manager."
                : "Couldn't find the game's processes.");
            return;
        }
        _gameWindow = 0;
        // Watchers notice within moments; a game found running elsewhere is only seen by the next detection.
        await Task.Delay(1500);
        _ = RefreshNowPlayingAsync();
    }

    private void PostCloseFailed(string message) =>
        Post($$"""{"type":"close-failed","message":{{JsonSerializer.Serialize(message, HostJson.Default.String)}}}""");

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

    public void BringToFront()
    {
        ResumeWebView();
        if (Native.IsIconic(_hwnd))
        {
            Native.ShowWindow(_hwnd, Native.SwRestore);
        }
        if (_mode == ShellMode.FullScreen)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
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

    /// <summary>Post a message to the page from any thread.</summary>
    public void PostFromAnyThread(string json) => DispatcherQueue.TryEnqueue(() => Post(json));

    private void PostSession(string gameId, string state, int seconds)
    {
        PostFromAnyThread($$"""{"type":"session","gameId":{{JsonSerializer.Serialize(gameId, HostJson.Default.String)}},"state":"{{state}}","seconds":{{seconds}}}""");
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = RefreshNowPlayingAsync();
            if (state == "ended" || (state == "running" && seconds == 0))
            {
                // Once when the game starts running and when it ends (not on every 30 s ping): the session is in
                // Playdex once start (or end) went through, so give it a moment.
                _ = RefreshSummaryAfterAsync(TimeSpan.FromSeconds(5));
            }
        });
    }

    /// <summary>
    /// Fetch the Playdex summary and post it to the page when it changed: forced when the page loads and after a session
    /// starts or ends, otherwise at most every <see cref="_summaryInterval"/> while the window is active.
    /// </summary>
    private void RefreshSummary(bool force)
    {
        if (_refreshingSummary || (!force && Stopwatch.GetElapsedTime(_summaryRefreshedAt) < _summaryInterval && _summaryRefreshedAt != 0))
        {
            return;
        }
        _ = RefreshSummaryAsync();
    }

    private async Task RefreshSummaryAfterAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        RefreshSummary(force: true);
    }

    private async Task RefreshSummaryAsync()
    {
        _refreshingSummary = true;
        try
        {
            _summaryRefreshedAt = Stopwatch.GetTimestamp();
            if (await Task.Run(() => _services.Summary.RefreshAsync()))
            {
                Post($$"""{"type":"playdex-summary","summary":{{_services.Summary.Current ?? "null"}}}""");
            }
        }
        finally
        {
            _refreshingSummary = false;
        }
    }

    public void Post(string json)
    {
        if (_webView.CoreWebView2 is { } web)
        {
            web.PostWebMessageAsJson(json);
        }
    }
}
