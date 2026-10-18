using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Playdex.Desktop;

/// <param name="LastSuccessAt">The last backup that went through, epoch ms.</param>
internal sealed record BackupView(string Kind, string Text, long? LastSuccessAt = null);

internal sealed record DesktopGame(
    string GameId,
    string LibraryId,
    string DisplayName,
    string ApiGameId,
    string GameProvider,
    string? Platform,
    string LaunchOptions,
    string ExecutablePath,
    bool ExecutableExists,
    bool DailyBackup,
    BackupView Backup);

/// <param name="Path">What starts it on this PC (Discord's updater, the default browser); empty when it isn't here.</param>
/// <param name="Available">It is on this PC, so Big Picture lists it.</param>
internal sealed record DesktopApp(string Id, string Name, string Kind, string Path, string LaunchOptions, bool Available);

internal sealed record DesktopSettings(
    string BaseUrl, bool ApiKeySet, string Platform, bool SteamGridDbKeySet, string LudusaviPath, bool StartWithWindows);

internal sealed record UpdateView(string Kind, string Message, string? Version);

internal sealed record FseView(string State, string Message);

/// <summary>Everything the desktop screens show, sent with every reply that changes it and pushed when it changes.</summary>
internal sealed record DesktopState(
    string Version,
    IReadOnlyList<DesktopGame> Games,
    IReadOnlyList<DesktopApp> Apps,
    IReadOnlyList<DesktopApp> SuggestedApps,
    DesktopSettings Settings,
    UpdateView Update,
    FseView Fse,
    string Mode,
    string LauncherDirectory,
    bool RunAvailable);

internal sealed record PlatformsView(IReadOnlyList<PlatformOption> Platforms, string? Error);

internal sealed record MessageView(string Message, bool Warning = false);

internal sealed record PathView(string? Path);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DesktopState))]
[JsonSerializable(typeof(PlatformsView))]
[JsonSerializable(typeof(MessageView))]
[JsonSerializable(typeof(PathView))]
[JsonSerializable(typeof(string))]
internal sealed partial class DesktopJson : JsonSerializerContext;

/// <summary>
/// The desktop app's own work, what the WinForms Wrapper's tray did: game management (add, settings, remove, Steam
/// shortcut and SteamGridDB art, Ludusavi backups), settings, per-game launchers from Playdex.Run.exe, and the daily
/// backup scheduler. The page asks with {type:"desktop", id, method, params}; the reply is
/// {type:"desktop-result", id, ok, result | error}. {type:"desktop-state", state} is pushed when backups or updates change.
/// Requests run on the UI thread (the config is only changed there); slow work is awaited off it.
/// </summary>
internal sealed partial class DesktopHost : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Playdex";

    private readonly ConfigStore _store;
    private readonly AppConfig _config;
    private readonly ShellServices _services;
    private readonly Func<ShellWindow?> _window;
    private readonly Action _quit;
    private readonly Action _modeChanged;
    private readonly LauncherManager _launchers = new(LogLoggerFactory.Instance.CreateLogger<LauncherManager>());
    private readonly LudusaviRunner _ludusavi = new(new LudusaviProcess());
    private readonly BackupStateStore _backupStore;
    private readonly BackupCoordinator _backups;
    private readonly string? _runExecutable;

    /// <param name="modeChanged">Show Playdex in the saved mode (called once the reply has reached the page).</param>
    public DesktopHost(ConfigStore store, AppConfig config, ShellServices services, Func<ShellWindow?> window, Action quit, Action modeChanged)
    {
        _store = store;
        _config = config;
        _services = services;
        _window = window;
        _quit = quit;
        _modeChanged = modeChanged;
        var run = Path.Combine(AppContext.BaseDirectory, "Playdex.Run.exe");
        _runExecutable = File.Exists(run) ? run : null;
        // The Wrapper's backup state, in place: backups carry on where it left off.
        _backupStore = new BackupStateStore(Path.Combine(config.Storage.StateDirectory, "ludusavi-backups.json"));
        // Off the UI thread: waiting there on an await that resumes on the UI thread deadlocks.
        var backupState = Task.Run(() => _backupStore.LoadAsync()).GetAwaiter().GetResult();
        _backups = new BackupCoordinator(
            config, store, backupState, _backupStore, _ludusavi,
            LogLoggerFactory.Instance.CreateLogger<BackupCoordinator>(), PushState);
    }

    /// <summary>Shown in the tray menu's notifications while no window is open; set by the app.</summary>
    public TrayIcon? Tray { get; set; }

    public void Start()
    {
        _ = Task.Run(SynchronizeLaunchersQuietly);
        _ = _backups.RunSchedulerAsync();
    }

    /// <summary>
    /// Regenerate every game's launcher from Playdex.Run.exe. Launchers made by the WinForms Wrapper (copies of
    /// Wrapper.exe) are replaced in place, so their Steam shortcuts keep working.
    /// </summary>
    private void SynchronizeLaunchersQuietly()
    {
        if (_runExecutable is null)
        {
            Log.Info("desktop: Playdex.Run.exe is not next to Playdex.exe; per-game launchers are not regenerated");
            return;
        }
        try
        {
            var paths = _launchers.SynchronizeAll(_config, _store.PathToConfig, _runExecutable);
            Log.Info($"desktop: {paths.Count} game launcher(s) up to date");
        }
        catch (Exception ex)
        {
            Log.Error("desktop: launcher synchronization failed", ex);
        }
    }

    /// <summary>A page message for the desktop app; false for anything else.</summary>
    public bool HandleMessage(string type, JsonElement message)
    {
        if (type != "desktop")
        {
            return false;
        }
        var id = message.GetProperty("id").GetInt32();
        var method = message.GetProperty("method").GetString() ?? "";
        // The page's JSON document is disposed when this returns; the request runs later.
        var parameters = message.TryGetProperty("params", out var p) ? p.Clone() : default;
        _ = RunAsync(id, method, parameters);
        return true;
    }

    private async Task RunAsync(int id, string method, JsonElement parameters)
    {
        try
        {
            var result = await DispatchAsync(method, parameters);
            Reply(id, true, result);
        }
        catch (Exception ex)
        {
            Log.Error($"desktop: {method} failed", ex);
            Reply(id, false, JsonSerializer.Serialize(ex.Message, DesktopJson.Default.String));
        }
    }

    private async Task<string> DispatchAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "state":
                return StateJson();
            case "game.add":
                return await AddGameAsync();
            case "game.update":
                return await UpdateGameAsync(parameters);
            case "game.remove":
                return await RemoveGameAsync(Text(parameters, "gameId"));
            case "game.steam":
                return Json(await AddToSteamAsync(Text(parameters, "gameId")));
            case "game.backup":
                return Json(await BackupAsync(Text(parameters, "gameId")));
            case "game.dailyBackup":
                return await SetDailyBackupAsync(Text(parameters, "gameId"), parameters.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.True);
            case "app.add":
                return await AddAppAsync();
            case "app.addFound":
                return await ChangeAppsAsync("added", Text(parameters, "kind"), kind => AppShortcuts.AddDetected(_config, kind));
            case "app.update":
                return await ChangeAppsAsync("saved", Text(parameters, "id"), id =>
                    AppShortcuts.Change(_config, id, Text(parameters, "name"), OptionalText(parameters, "launchOptions")));
            case "app.remove":
                return await ChangeAppsAsync("removed", Text(parameters, "id"), id => AppShortcuts.Remove(_config, id));
            case "settings.save":
                return await SaveSettingsAsync(parameters);
            case "settings.startWithWindows":
                SetStartWithWindows(parameters.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True);
                return StateJson();
            case "settings.browseLudusavi":
                var ludusavi = FilePicker.PickFile(WindowHandle, "Select ludusavi.exe", ("Ludusavi (ludusavi.exe)", "ludusavi.exe"), ("Executables (*.exe)", "*.exe"));
                return JsonSerializer.Serialize(new PathView(ludusavi), DesktopJson.Default.PathView);
            case "platforms":
                return await PlatformsAsync();
            case "launchers.sync":
                return Json(SynchronizeLaunchers());
            case "launchers.open":
                Directory.CreateDirectory(_config.Storage.LauncherDirectory);
                Process.Start(new ProcessStartInfo(_config.Storage.LauncherDirectory) { UseShellExecute = true })?.Dispose();
                return Json(new MessageView("Opened the launchers folder."));
            case "mode.set":
                SetMode(Text(parameters, "mode"));
                _ = SwitchSoonAsync();
                return StateJson();
            case "app.quit":
                _quit();
                return Json(new MessageView("Quitting."));
            case "update.check":
                if (Updates is null)
                {
                    throw new InvalidOperationException("This build of Playdex doesn't update itself.");
                }
                await Updates.CheckAsync(explicitCheck: true);
                return StateJson();
            case "update.apply":
                if (Updates is null)
                {
                    throw new InvalidOperationException("This build of Playdex doesn't update itself.");
                }
                Updates.Apply();
                _quit();
                return Json(new MessageView("Restarting to update."));
            case "fse.install":
                return Json(new MessageView(await (Fse ?? throw new InvalidOperationException("Not available.")).InstallAsync()));
            default:
                throw new InvalidOperationException($"Unknown request '{method}'.");
        }
    }

    private async Task<string> AddGameAsync()
    {
        var path = FilePicker.PickFile(WindowHandle, "Add a game", ("Games (*.exe)", "*.exe"), ("All files (*.*)", "*.*"));
        if (path is null)
        {
            return StateJson();
        }
        var game = await GameCatalog.AddFromExecutableAsync(_config, path);
        await _store.SaveAsync(_config);
        Log.Info($"desktop: added {game.GameId} ({path})");
        if (_runExecutable is not null)
        {
            try
            {
                _launchers.SynchronizeGame(_config.Storage, game, _store.PathToConfig, _runExecutable);
            }
            catch (Exception ex)
            {
                Log.Error($"desktop: launcher for {game.GameId} not generated", ex);
            }
        }
        ReloadLibrarySoon();
        return StateJson();
    }

    /// <summary>Add an app for Big Picture's quick settings from its .exe.</summary>
    private async Task<string> AddAppAsync()
    {
        var path = FilePicker.PickFile(WindowHandle, "Add an app", ("Apps (*.exe)", "*.exe"));
        return path is null ? StateJson() : await ChangeAppsAsync("added", path, p => AppShortcuts.AddExecutable(_config, p));
    }

    /// <summary>Change the apps list and save it. Big Picture reads it each time quick settings opens.</summary>
    private async Task<string> ChangeAppsAsync(string verb, string subject, Action<string> change)
    {
        change(subject);
        await _store.SaveAsync(_config);
        Log.Info($"desktop: app {verb} ({subject})");
        return StateJson();
    }

    private async Task<string> UpdateGameAsync(JsonElement parameters)
    {
        var gameId = Text(parameters, "gameId");
        var game = Game(gameId);
        GameCatalog.ChangeDisplayName(_config, gameId, Text(parameters, "displayName"));
        GameCatalog.ChangeApiGameId(_config, gameId, Text(parameters, "apiGameId"));
        GameCatalog.ChangeLaunchOptions(_config, gameId, OptionalText(parameters, "launchOptions"));
        game.Platform = ApiConfig.NormalizePlatformOverride(OptionalText(parameters, "platform"));
        game.Backup.DailyEnabled = parameters.TryGetProperty("dailyBackup", out var daily) && daily.ValueKind == JsonValueKind.True;
        await _store.SaveAsync(_config);
        Log.Info($"desktop: saved settings of {gameId}");
        ReloadLibrarySoon();
        return StateJson();
    }

    private async Task<string> RemoveGameAsync(string gameId)
    {
        var removed = GameCatalog.Remove(_config, gameId);
        await _store.SaveAsync(_config);
        await _backups.RemoveStateAsync(gameId);
        _launchers.RemoveLauncherArtifacts(_config.Storage, removed);
        Log.Info($"desktop: removed {gameId}");
        ReloadLibrarySoon();
        return StateJson();
    }

    private async Task<MessageView> AddToSteamAsync(string gameId)
    {
        var game = Game(gameId);
        var run = _runExecutable ?? throw new InvalidOperationException("Playdex.Run.exe is missing next to Playdex.exe. Reinstall Playdex.");
        var launcher = _launchers.SynchronizeGame(_config.Storage, game, _store.PathToConfig, run);
        var shortcut = await Task.Run(() => SteamIntegration.AddShortcut(game, launcher));
        Log.Info($"desktop: Steam shortcut for {gameId}: app {shortcut.AppId}, added={shortcut.WasAdded}");
        if (!_config.SteamGridDb.IsConfigured)
        {
            return new MessageView(shortcut.WasAdded
                ? $"{game.DisplayName} was added to Steam. Add a SteamGridDB API key in Settings to get artwork."
                : $"{game.DisplayName} is already in Steam. Add a SteamGridDB API key in Settings to get artwork.");
        }
        try
        {
            var art = await new SteamGridDbClient().SynchronizeAsync(_config.SteamGridDb.ApiKey, game, shortcut.UserConfigDirectory, shortcut.AppId);
            return new MessageView(
                $"{game.DisplayName} {(shortcut.WasAdded ? "was added to Steam" : "is in Steam")} with {art.DownloadedCount} of {art.AvailableCount} artwork types. Open Steam to see it.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException)
        {
            Log.Error($"desktop: SteamGridDB artwork for {gameId} failed", ex);
            return new MessageView($"{game.DisplayName} is in Steam, but its artwork could not be downloaded: {ex.Message}", Warning: true);
        }
    }

    private async Task<MessageView> BackupAsync(string gameId)
    {
        var game = Game(gameId);
        await _backups.RequestManualAsync(game);
        var state = _backups.GetState(gameId);
        return state?.Result == BackupResultKind.Success
            ? new MessageView($"{game.DisplayName}'s saves were backed up.")
            : new MessageView(state?.LastError ?? "The backup did not complete.", Warning: true);
    }

    /// <summary>Daily backup on or off, from the game's page (Manage changes it with the rest of the game's settings).</summary>
    private async Task<string> SetDailyBackupAsync(string gameId, bool enabled)
    {
        Game(gameId).Backup.DailyEnabled = enabled;
        await _store.SaveAsync(_config);
        Log.Info($"desktop: daily backup of {gameId} {(enabled ? "on" : "off")}");
        return StateJson();
    }

    private async Task<string> SaveSettingsAsync(JsonElement parameters)
    {
        var baseUrl = ApiConfig.NormalizeBaseUrl(OptionalText(parameters, "baseUrl"));
        var platform = ApiConfig.NormalizePlatform(OptionalText(parameters, "platform"));
        var ludusavi = LudusaviConfig.NormalizeExecutablePath(OptionalText(parameters, "ludusaviPath"));
        if (ludusavi.Length > 0)
        {
            await _ludusavi.ValidateAsync(ludusavi);
        }
        _config.Api.BaseUrl = baseUrl;
        _config.Api.Platform = platform;
        // Keys are never sent to the page; a missing or null key leaves the saved one as it is.
        if (OptionalText(parameters, "apiKey") is { } apiKey)
        {
            _config.Api.ApiKey = apiKey.Trim();
        }
        if (OptionalText(parameters, "steamGridDbKey") is { } steamGridDbKey)
        {
            _config.SteamGridDb.ApiKey = SteamGridDbConfig.NormalizeApiKey(steamGridDbKey);
        }
        _config.Ludusavi.ExecutablePath = ludusavi;
        await _store.SaveAsync(_config);
        Log.Info($"desktop: settings saved (API {_config.Api.BaseUrl}, key {(string.IsNullOrWhiteSpace(_config.Api.ApiKey) ? "missing" : "set")})");
        return StateJson();
    }

    private async Task<string> PlatformsAsync()
    {
        if (!_config.Api.IsEnabled)
        {
            return JsonSerializer.Serialize(new PlatformsView([], "Set the API URL to load platforms."), DesktopJson.Default.PlatformsView);
        }
        using var client = new PlatformCatalogClient(_config.Api);
        var load = await client.LoadAsync(_config.Api.ApiKey);
        return JsonSerializer.Serialize(new PlatformsView(load.Platforms, load.Error), DesktopJson.Default.PlatformsView);
    }

    private MessageView SynchronizeLaunchers()
    {
        var run = _runExecutable ?? throw new InvalidOperationException("Playdex.Run.exe is missing next to Playdex.exe. Reinstall Playdex.");
        var paths = _launchers.SynchronizeAll(_config, _store.PathToConfig, run);
        return new MessageView($"{paths.Count} game launcher(s) regenerated.");
    }

    /// <summary>Save how Playdex opens: <see cref="ShellConfig.DesktopMode"/> or <see cref="ShellConfig.BigPictureMode"/>.</summary>
    public void SetMode(string mode)
    {
        _config.Shell.Mode = ShellConfig.NormalizeMode(mode);
        _store.Save(_config);
        Log.Info($"desktop: mode set to {_config.Shell.Mode}");
        PushState();
    }

    /// <summary>The window is replaced, so the reply goes out first.</summary>
    private async Task SwitchSoonAsync()
    {
        await Task.Delay(150);
        _modeChanged();
    }

    private GameConfig Game(string gameId) =>
        _config.Games.TryGetValue(gameId, out var game) ? game : throw new InvalidOperationException($"Game '{gameId}' is not in Playdex.");

    private nint WindowHandle => _window()?.Handle ?? 0;

    /// <summary>The library changed: the page reloads with the new one once this reply has reached it.</summary>
    private void ReloadLibrarySoon()
    {
        if (_window() is { } window)
        {
            window.DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(150);
                await window.ReloadLibraryAsync();
            });
        }
        else
        {
            _services.RescanLibrary();
        }
    }

    public string StateJson() => JsonSerializer.Serialize(BuildState(), DesktopJson.Default.DesktopState);

    private DesktopState BuildState()
    {
        var games = _config.Games.Values
            .OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new DesktopGame(
                g.GameId,
                Library.PlaydexId(g.GameId),
                g.DisplayName,
                string.IsNullOrWhiteSpace(g.ApiGameId) ? g.GameId : g.ApiGameId,
                g.GameProvider,
                g.Platform,
                LaunchOptionsCodec.Format(g.Launch.Arguments),
                g.Launch.ExecutablePath ?? "",
                File.Exists(g.Launch.ExecutablePath),
                g.Backup.DailyEnabled,
                Backup(g, _backups.GetState(g.GameId))))
            .ToList();
        var apps = AppShortcuts.List(_config).Select(App).ToList();
        var suggested = AppShortcuts.Suggestions(_config).Select(App).ToList();
        var settings = new DesktopSettings(
            _config.Api.BaseUrl,
            !string.IsNullOrWhiteSpace(_config.Api.ApiKey),
            _config.Api.Platform,
            _config.SteamGridDb.IsConfigured,
            _config.Ludusavi.ExecutablePath,
            StartsWithWindows());
        return new DesktopState(
            Version, games, apps, suggested, settings, Updates?.View ?? new UpdateView("unavailable", "This build of Playdex doesn't update itself (installed builds do).", null),
            Fse?.View ?? new FseView("unavailable", "Not available in this build."), _config.Shell.Mode, _config.Storage.LauncherDirectory, _runExecutable is not null);
    }

    private static DesktopApp App(AppShortcut app)
    {
        var resolved = AppShortcuts.Resolve(app);
        return new DesktopApp(
            app.Id, app.Name, app.Kind, resolved?.ExecutablePath ?? app.ExecutablePath ?? "", LaunchOptionsCodec.Format(app.Arguments), resolved is not null);
    }

    /// <summary>The app's self-updater (set by the app; null when this build can't update itself).</summary>
    public UpdateFeature? Updates { get; set; }

    /// <summary>"Use as Full Screen Experience home" (set by the app).</summary>
    public FseFeature? Fse { get; set; }

    public static string Version =>
        typeof(DesktopHost).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>The Wrapper's backup indicator, in words.</summary>
    private static BackupView Backup(GameConfig game, GameBackupState? state, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        static string When(DateTimeOffset? t) => t is { } v ? $" {v.ToLocalTime():g}" : "";
        BackupView view = state?.Result switch
        {
            BackupResultKind.Running => new("running", "Backing up…"),
            BackupResultKind.Failed => new("failed", $"Last backup failed{When(state.LastAttemptAt)}.{(state.LastError is { } e ? $" {e}" : "")}"),
            BackupResultKind.Unsupported => new("unsupported", $"Ludusavi doesn't support this game.{(state.LastError is { } e ? $" {e}" : "")}"),
            _ when game.Backup.DailyEnabled && BackupCoordinator.IsDue(state, at) => new("due", "Daily backup is due."),
            _ when state?.LastSuccessAt is not null => new("success", $"Backed up{When(state.LastSuccessAt)}."),
            _ => new(game.Backup.DailyEnabled ? "due" : "off", game.Backup.DailyEnabled ? "Daily backup is on." : "No backups yet."),
        };
        return view with { LastSuccessAt = state?.LastSuccessAt?.ToUnixTimeMilliseconds() };
    }

    private static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    /// <summary>Start in the tray when Windows starts (no window), so backups and launcher updates keep running.</summary>
    private static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --background");
        }
        else
        {
            key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>Tell the page that backups or updates changed. Any thread.</summary>
    public void PushState()
    {
        if (_window() is { } window)
        {
            window.PostFromAnyThread($$"""{"type":"desktop-state","state":{{StateJson()}}}""");
        }
    }

    private void Reply(int id, bool ok, string payload)
    {
        var json = ok
            ? $$"""{"type":"desktop-result","id":{{id}},"ok":true,"result":{{payload}}}"""
            : $$"""{"type":"desktop-result","id":{{id}},"ok":false,"error":{{payload}}}""";
        _window()?.Post(json);
    }

    private static string Json(MessageView view) => JsonSerializer.Serialize(view, DesktopJson.Default.MessageView);

    private static string Text(JsonElement parameters, string name) =>
        OptionalText(parameters, name) ?? throw new InvalidOperationException($"'{name}' is required.");

    private static string? OptionalText(JsonElement parameters, string name) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public void Dispose()
    {
        _backups.Dispose();
        _backupStore.Dispose();
    }
}
