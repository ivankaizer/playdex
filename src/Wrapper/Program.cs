namespace Wrapper;

internal static partial class Program
{
    [STAThread]
    public static int Main()
    {
        using var logging = AppLoggerFactory.Create();

        try
        {
            var launcherManifest = LauncherRouting.Resolve(Environment.ProcessPath);
            var configStore = new ConfigStore(launcherManifest?.ConfigPath ?? ConfigStore.DefaultPath);
            var config = configStore.LoadOrCreateAsync().GetAwaiter().GetResult();
            logging.UseDirectory(config.Storage.LogDirectory);

            return launcherManifest is null
                ? (int)RunTrayApplication(configStore, config, logging)
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
        AppLoggerFactory logging)
    {
        ApplicationConfiguration.Initialize();
        using var trayApplication = new UI.TraySetupApplication(configStore, config, logging);
        Application.Run();

        if (trayApplication.GameIdToLaunch is not null)
        {
            RunConfiguredGame(config, trayApplication.GameIdToLaunch, logging);
        }

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
