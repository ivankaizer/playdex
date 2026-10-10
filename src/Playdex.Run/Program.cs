using Microsoft.Extensions.Logging;
using Playdex.Core;

namespace Playdex.Run;

/// <summary>
/// The exe behind every generated per-game launcher, Playdex-&lt;game&gt;-&lt;hash&gt;.exe (the target of the game's
/// Steam shortcut). It reads the .playdex.json manifest next to itself, loads the install's config, starts the game,
/// follows it to exit and records the session (<see cref="SessionCoordinator"/>). No window.
/// </summary>
/// <remarks>
/// Exit codes: 0 the game ran, 1 it could not run (see run-&lt;date&gt;.log in the config's log folder), 2 this exe was
/// started on its own rather than as a generated launcher.
/// </remarks>
internal static class Program
{
    private static int Main()
    {
        Log.UseFile(Path.Combine(Paths.DataDirectory, "run.log"));
        LauncherManifest? manifest;
        try
        {
            manifest = LauncherRouting.Resolve(Environment.ProcessPath);
        }
        catch (InvalidDataException ex)
        {
            Log.Error("run: the launcher manifest is unusable", ex);
            return 1;
        }
        if (manifest is null)
        {
            Log.Info($"run: {Environment.ProcessPath} is not a generated launcher (no {Brand.LauncherManifestExtension} next to it)");
            return 2;
        }

        try
        {
            // A launcher made by an older Wrapper names the Wrapper's own config; the install's config replaces it.
            var store = ConfigStore.IsDefaultPath(manifest.ConfigPath) ? ConfigStore.Default() : new ConfigStore(manifest.ConfigPath);
            var config = store.LoadOrCreate();
            Log.UseFile(Path.Combine(config.Storage.LogDirectory, $"run-{DateTime.Now:yyyyMMdd}.log"));
            var game = config.GetGameForLaunch(manifest.GameId);
            Log.Info($"run: {game.DisplayName} ({manifest.GameId})");
            using var coordinator = SessionCoordinator.Create(config, game, LogLoggerFactory.Instance.CreateLogger<SessionCoordinator>());
            coordinator.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error($"run: {manifest.GameId} could not run", ex);
            return 1;
        }
    }
}
