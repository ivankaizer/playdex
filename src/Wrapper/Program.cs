namespace Wrapper;

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var logging = AppLoggerFactory.Create();
        var coreLogger = logging.CreateLogger(typeof(Log).Namespace!);
        Log.UseSink(message => LogCore(coreLogger, message));

        try
        {
            if (UpdateCommandLine.TryGetApply(args, out var updateManifestPath, out var parentProcessId))
            {
                return UpdateApplier.RunAsync(updateManifestPath, parentProcessId).GetAwaiter().GetResult()
                    ? (int)CommandCode.Success
                    : (int)CommandCode.Failure;
            }

            var hasHealthSignal = UpdateCommandLine.TryGetHealthSignal(args, out var healthSignalPath);
            if (args.Length > 0 && !hasHealthSignal)
            {
                throw new InvalidDataException("The wrapper command line is invalid.");
            }
            var launcherManifest = LauncherRouting.Resolve(Environment.ProcessPath);
            // A launcher generated before the merge names the Wrapper's old config; the install's config replaces it.
            var configStore = launcherManifest is null || ConfigStore.IsDefaultPath(launcherManifest.ConfigPath)
                ? ConfigStore.Default()
                : new ConfigStore(launcherManifest.ConfigPath);
            var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
            logging.UseDirectory(config.Storage.LogDirectory);

            return launcherManifest is null
                ? (int)RunTrayApplication(configStore, config, logging, hasHealthSignal ? healthSignalPath : null)
                : (int)RunGeneratedLauncher(launcherManifest, config, logging);
        }
        catch (Exception ex)
        {
            LogFatalError(logging.CreateLogger(typeof(Program).FullName!), ex);
            return (int)CommandCode.Failure;
        }
    }

    private static CommandCode RunTrayApplication(
        ConfigStore configStore,
        AppConfig config,
        AppLoggerFactory logging,
        string? healthSignalPath)
    {
        ApplicationConfiguration.Initialize();
        using var trayApplication = new UI.TraySetupApplication(configStore, config, logging);
        if (healthSignalPath is not null)
        {
            UpdateHealthSignal.Confirm(healthSignalPath);
        }
        Application.Run();

        return CommandCode.Success;
    }

    private static CommandCode RunGeneratedLauncher(
        LauncherManifest launcherManifest,
        AppConfig config,
        AppLoggerFactory logging)
    {
        RunConfiguredGame(config, launcherManifest.GameId, logging);
        return CommandCode.Success;
    }

    private static void RunConfiguredGame(
        AppConfig config,
        string gameId,
        AppLoggerFactory logging)
    {
        var game = config.GetGameForLaunch(gameId);

        using var coordinator = SessionCoordinator.Create(config, game, logging.CreateLogger<SessionCoordinator>());
        coordinator.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Fatal application error")]
    private static partial void LogFatalError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Message}")]
    private static partial void LogCore(ILogger logger, string message);

    private enum CommandCode
    {
        Success = 0,
        Failure = 1
    }
}
