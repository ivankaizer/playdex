namespace Wrapper;

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var logging = AppLoggerFactory.Create();

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
            var configStore = new ConfigStore(launcherManifest?.ConfigPath ?? ConfigStore.DefaultPath);
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
        WrapperConfig config,
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
        WrapperConfig config,
        AppLoggerFactory logging)
    {
        RunConfiguredGame(config, launcherManifest.GameId, logging);
        return CommandCode.Success;
    }

    private static void RunConfiguredGame(
        WrapperConfig config,
        string gameId,
        AppLoggerFactory logging)
    {
        var game = config.GetGameForLaunch(gameId);

        using var coordinator = SessionCoordinator.Create(config, game, logging);
        coordinator.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Fatal application error")]
    private static partial void LogFatalError(ILogger logger, Exception exception);

    private enum CommandCode
    {
        Success = 0,
        Failure = 1
    }
}
