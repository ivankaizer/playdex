namespace Wrapper.UI;

internal sealed partial class TraySetupApplication : IDisposable
{
    private readonly ConfigStore store;
    private readonly WrapperConfig config;
    private readonly AppLoggerFactory logging;
    private readonly ILogger<TraySetupApplication> logger;
    private readonly LauncherManager launcherManager;
    private readonly string sourceExecutable;
    private readonly Icon applicationIcon;
    private readonly NotifyIcon icon;
    private readonly TrayApplicationWindow window;
    private readonly LudusaviRunner ludusaviRunner;
    private readonly BackupCoordinator backupCoordinator;

    public TraySetupApplication(ConfigStore store, WrapperConfig config, AppLoggerFactory logging)
    {
        this.store = store;
        this.config = config;
        this.logging = logging;
        logger = logging.CreateLogger<TraySetupApplication>();
        launcherManager = new LauncherManager(logging.CreateLogger<LauncherManager>());
        sourceExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the wrapper executable path.");
        ludusaviRunner = new LudusaviRunner(new LudusaviProcess());
        var backupStateStore = new BackupStateStore(Path.Combine(config.Storage.StateDirectory, "ludusavi-backups.json"));
        var backupState = backupStateStore.LoadAsync().GetAwaiter().GetResult();

        applicationIcon = AppBrand.LoadApplicationIcon();
        icon = new NotifyIcon
        {
            Icon = applicationIcon,
            Text = AppBrand.ProductName,
            Visible = true
        };
        backupCoordinator = new BackupCoordinator(
            config,
            store,
            backupState,
            backupStateStore,
            ludusaviRunner,
            logging.CreateLogger<BackupCoordinator>(),
            RefreshGameMenuOnUiThread);
        window = new TrayApplicationWindow(
            () => config.Games.Values,
            gameId => BackupStatusPresentation.From(
                config.Games[gameId],
                backupCoordinator.GetState(gameId),
                DateTimeOffset.Now),
            new TrayWindowActions(
                gameId => RunTrayActionAsync("Game launch", () => LaunchGameAsync(gameId)),
                gameId => RunTrayActionAsync("Save backup", () => BackupGameAsync(gameId)),
                gameId => RunTrayActionAsync("Game settings", () => EditGameSettingsAsync(gameId)),
                gameId => RunTrayActionAsync("Game removal", () => RemoveGameAsync(gameId)),
                () => RunTrayActionAsync("Add a game", AddGameAsync),
                () => RunTrayActionAsync("Settings", OpenSettingsAsync),
                () => RunTrayActionAsync("Open launcher folder", () =>
                {
                    OpenLauncherFolder();
                    return Task.CompletedTask;
                }),
                () => RunTrayActionAsync("Launcher regeneration", () => SynchronizeLaunchersAsync(showConfirmation: true)),
                () =>
                {
                    Application.ExitThread();
                    return Task.CompletedTask;
                }));
        icon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button is MouseButtons.Left or MouseButtons.Right)
            {
                window.ShowWindow(Cursor.Position);
            }
        };
        window.ShowWindow(Cursor.Position);
        _ = RunTrayActionAsync("Launcher synchronization", () => SynchronizeLaunchersAsync(showConfirmation: false));
        _ = backupCoordinator.RunSchedulerAsync();
    }

    private void RefreshGameMenu()
    {
        if (window.Visible)
        {
            window.RefreshContent();
        }
    }

    private void RefreshGameMenuOnUiThread()
    {
        if (window.IsDisposed || !window.IsHandleCreated) return;
        try
        {
            window.BeginInvoke(new Action(RefreshGameMenu));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string FormatGameName(GameConfig game) =>
        string.Equals(game.DisplayName, game.GameId, StringComparison.OrdinalIgnoreCase)
            ? game.GameId
            : $"{game.DisplayName} ({game.GameId})";

    private async Task RunTrayActionAsync(string actionName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LogTrayActionFailed(logger, actionName, ex);
            icon.ShowBalloonTip(5000, "Setup failed", $"{actionName} failed. See the wrapper log for details.", ToolTipIcon.Error);
        }
    }

    private async Task AddGameAsync()
    {
        using var picker = new OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Add a game"
        };

        if (picker.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var game = await GameCatalog.AddFromExecutableAsync(config, picker.FileName);
        await store.SaveAsync(config);
        RefreshGameMenu();
        try
        {
            launcherManager.SynchronizeGame(config.Storage, game, store.PathToConfig, sourceExecutable);
        }
        catch (Exception ex)
        {
            LogLauncherGenerationFailed(logger, game.GameId, ex);
            icon.ShowBalloonTip(5000, "Launcher not generated", "The game was saved, but its launcher could not be generated. Retry from the tray.", ToolTipIcon.Warning);
        }

        LogGameAdded(logger, game.GameId, picker.FileName);
        icon.ShowBalloonTip(2500, "Game added", $"{game.DisplayName} was added.", ToolTipIcon.Info);
    }

    private Task LaunchGameAsync(string gameId)
    {
        var game = config.GetGameForLaunch(gameId);
        LogGameLaunching(logger, game.GameId);
        _ = RunGameSessionAsync(game);
        return Task.CompletedTask;
    }

    private async Task RunGameSessionAsync(GameConfig game)
    {
        try
        {
            using var coordinator = SessionCoordinator.Create(config, game, logging);
            await coordinator.RunAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogTrayActionFailed(logger, "Game session", ex);
            icon.ShowBalloonTip(5000, "Game session failed", $"Could not monitor {game.DisplayName}. See the wrapper log for details.", ToolTipIcon.Error);
        }
    }

    private async Task BackupGameAsync(string gameId)
    {
        if (!config.Games.TryGetValue(gameId, out var game))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        await backupCoordinator.RequestManualAsync(game);
        var state = backupCoordinator.GetState(gameId);
        if (state?.Result == BackupResultKind.Success)
        {
            icon.ShowBalloonTip(3000, "Save backup complete", $"{game.DisplayName} was backed up.", ToolTipIcon.Info);
        }
        else
        {
            var detail = state?.LastError ?? "The backup did not complete.";
            icon.ShowBalloonTip(5000, "Save backup failed", detail, ToolTipIcon.Warning);
        }
    }

    private async Task RemoveGameAsync(string gameId)
    {
        if (!config.Games.TryGetValue(gameId, out var game))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        var result = MessageBox.Show(
            $"Remove '{FormatGameName(game)}' from {AppBrand.ProductName}?{Environment.NewLine}{Environment.NewLine}The installed game files will not be deleted. Steam shortcuts must be removed separately.",
            "Remove game",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }

        var removed = GameCatalog.Remove(config, gameId);
        await store.SaveAsync(config);
        await backupCoordinator.RemoveStateAsync(gameId);
        launcherManager.RemoveLauncherArtifacts(config.Storage, removed);
        RefreshGameMenu();
        LogGameRemoved(logger, removed.GameId);
        icon.ShowBalloonTip(2500, "Game removed", $"{removed.DisplayName} was removed from the wrapper.", ToolTipIcon.Info);
    }

    private async Task EditGameSettingsAsync(string gameId)
    {
        if (!config.Games.TryGetValue(gameId, out var game))
        {
            throw new InvalidOperationException($"Game '{gameId}' is not configured.");
        }

        var currentId = string.IsNullOrWhiteSpace(game.ApiGameId) ? game.GameId : game.ApiGameId;
        using var editor = new GameSettingsDialog(game.DisplayName, currentId, game.Backup.DailyEnabled, TrayTheme.Current(), window.DeviceDpi);
        if (editor.ShowDialog(window) != DialogResult.OK)
        {
            return;
        }

        GameCatalog.ChangeDisplayName(config, gameId, editor.GameName);
        GameCatalog.ChangeApiGameId(config, gameId, editor.GameId);
        game.Backup.DailyEnabled = editor.DailyBackupEnabled;
        await store.SaveAsync(config);
        RefreshGameMenu();
        LogGameSettingsChanged(logger, game.GameId, game.DisplayName, game.ApiGameId);
    }

    private async Task OpenSettingsAsync()
    {
        using var editor = new ApiSettingsDialog(
            config.Api.BaseUrl,
            config.Api.ApiKey,
            config.Ludusavi.ExecutablePath,
            TrayTheme.Current(),
            window.DeviceDpi);
        if (editor.ShowDialog(window) != DialogResult.OK)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(editor.LudusaviExecutablePath))
        {
            try
            {
                await ludusaviRunner.ValidateAsync(editor.LudusaviExecutablePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(window, ex.Message, "Invalid Ludusavi executable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        config.Api.BaseUrl = editor.BaseUrl;
        config.Api.ApiKey = editor.ApiKey;
        config.Ludusavi.ExecutablePath = editor.LudusaviExecutablePath;
        await store.SaveAsync(config);
        LogApiSettingsSaved(
            logger,
            config.Api.BaseUrl,
            config.Api.ApiKey);
        icon.ShowBalloonTip(2500, "Settings saved", "API settings were updated.", ToolTipIcon.Info);
        RefreshGameMenu();
    }

    private Task SynchronizeLaunchersAsync(bool showConfirmation)
    {
        var paths = launcherManager.SynchronizeAll(config, store.PathToConfig, sourceExecutable);
        if (showConfirmation)
        {
            icon.ShowBalloonTip(3000, "Launchers ready", $"Generated {paths.Count} game launcher(s).", ToolTipIcon.Info);
        }

        return Task.CompletedTask;
    }

    private void OpenLauncherFolder()
    {
        var directory = LauncherManager.GetLauncherDirectory(config.Storage);
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    public void Dispose()
    {
        backupCoordinator.Dispose();
        window.Dispose();
        icon.Dispose();
        applicationIcon.Dispose();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Tray action failed: {ActionName}")]
    private static partial void LogTrayActionFailed(ILogger logger, string actionName, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Launcher generation failed for {GameId}")]
    private static partial void LogLauncherGenerationFailed(ILogger logger, string gameId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Tray added game {GameId}: {ExecutablePath}")]
    private static partial void LogGameAdded(ILogger logger, string gameId, string executablePath);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Tray launching game: {GameId}")]
    private static partial void LogGameLaunching(ILogger logger, string gameId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Tray removed game: {GameId}")]
    private static partial void LogGameRemoved(ILogger logger, string gameId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Tray changed game settings for {GameId}: displayName={DisplayName}, apiGameId={ApiGameId}")]
    private static partial void LogGameSettingsChanged(ILogger logger, string gameId, string displayName, string? apiGameId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Tray saved API settings: baseUrl={BaseUrl}, apiKey={apiKey}")]
    private static partial void LogApiSettingsSaved(ILogger logger, string baseUrl, string? apiKey);
}
