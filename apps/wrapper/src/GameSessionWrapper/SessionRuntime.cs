namespace GameSessionWrapper;

internal sealed partial class ProcessMonitor(MonitorConfig config, ILogger<ProcessMonitor> logger)
{
    public async Task WaitForExitAsync(Process launchedProcess, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!IsRunning(launchedProcess))
            {
                LogExitDebounceStarted(logger);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, config.ExitDebounceSeconds)), cancellationToken);
                if (!IsRunning(launchedProcess))
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private bool IsRunning(Process launchedProcess) =>
        config.Mode switch
        {
            MonitorMode.LaunchedProcess => !launchedProcess.HasExited,
            MonitorMode.ExecutablePath => AnyProcessMatches(p => PathsEqual(SafeFileName(p), config.ExecutablePath)),
            MonitorMode.ExecutableName => AnyProcessMatches(p =>
                string.Equals(p.ProcessName + ".exe", config.ExecutableName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.ProcessName, config.ExecutableName, StringComparison.OrdinalIgnoreCase)),
            _ => !launchedProcess.HasExited
        };

    private static bool AnyProcessMatches(Func<Process, bool> predicate)
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(predicate);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string? SafeFileName(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Monitor target stopped; starting exit debounce")]
    private static partial void LogExitDebounceStarted(ILogger logger);
}

internal sealed partial class SessionCoordinator : IDisposable
{
    private readonly WrapperConfig config;
    private readonly GameConfig game;
    private readonly AppLoggerFactory logging;
    private readonly ILogger<SessionCoordinator> logger;
    private readonly PingApiClient? api;
    private readonly GameLock gameLock;

    private SessionCoordinator(WrapperConfig config, GameConfig game, AppLoggerFactory logging)
    {
        this.config = config;
        this.game = game;
        this.logging = logging;
        logger = logging.CreateLogger<SessionCoordinator>();

        api = config.Api.IsEnabled
            ? new PingApiClient(config.Api, game, logging.CreateLogger<PingApiClient>())
            : null;
        gameLock = GameLock.Acquire(config.Storage.StateDirectory, game.GameId);
    }

    public static SessionCoordinator Create(WrapperConfig config, GameConfig game, AppLoggerFactory logging) =>
        new(config, game, logging);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(game.Launch.ExecutablePath))
        {
            LogMissingExecutable(logger, game.GameId);
            throw new InvalidOperationException("Game executable path is not configured. Open tray setup to configure it.");
        }

        using var process = LaunchGame(game);
        if (api is null)
        {
            LogApiTrackingDisabled(logger, game.GameId);
            await CreateProcessMonitor().WaitForExitAsync(process, cancellationToken);
            return;
        }

        using var pingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pingService = new PingService(api, TimeSpan.FromSeconds(Math.Max(1, config.Api.PingIntervalSeconds)));
        var pingTask = pingService.RunAsync(pingCancellation.Token);
        try
        {
            await CreateProcessMonitor().WaitForExitAsync(process, cancellationToken);
        }
        finally
        {
            await pingCancellation.CancelAsync();
            await pingTask;
        }
    }

    private Process LaunchGame(GameConfig selectedGame)
    {
        var start = new ProcessStartInfo(selectedGame.Launch.ExecutablePath!)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(selectedGame.Launch.ExecutablePath)
        };
        foreach (var arg in selectedGame.Launch.Arguments)
        {
            start.ArgumentList.Add(arg);
        }

        LogGameLaunching(logger, selectedGame.GameId, selectedGame.Launch.ExecutablePath);
        return Process.Start(start) ?? throw new InvalidOperationException("Game process did not start.");
    }

    private ProcessMonitor CreateProcessMonitor() =>
        new(game.Monitor, logging.CreateLogger<ProcessMonitor>());

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Game {GameId} has no executable configured")]
    private static partial void LogMissingExecutable(ILogger logger, string gameId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "API tracking disabled for {GameId}")]
    private static partial void LogApiTrackingDisabled(ILogger logger, string gameId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Launching {GameId}: {ExecutablePath}")]
    private static partial void LogGameLaunching(ILogger logger, string gameId, string? executablePath);

    public void Dispose()
    {
        api?.Dispose();
        gameLock.Dispose();
    }
}
