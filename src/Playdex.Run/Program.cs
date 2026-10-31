using Microsoft.Extensions.Logging;
using Playdex.Core;

namespace Playdex.Run;

/// <summary>
/// The exe behind every generated per-game launcher, Playdex-&lt;game&gt;-&lt;hash&gt;.exe (the target of the game's
/// Steam shortcut). It reads the .playdex.json manifest next to itself, loads the install's config, starts the game,
/// follows it to exit and records the session (<see cref="SessionCoordinator"/>). No window.
/// </summary>
/// <remarks>
/// Exit codes: 0 the game ran, 1 it could not run (see run-&lt;date&gt;.log in the config's log folder, kept for two
/// weeks), 2 this exe was
/// started on its own rather than as a generated launcher.
/// A copy of it is also the desktop app's update helper: <c>--apply-update &lt;apply.json&gt; &lt;pid&gt;</c> replaces
/// the app's folder once that process exits (<see cref="AppUpdate.ApplyAsync"/>); 0 installed, 1 rolled back, 3 not
/// installed and the previous version could not be put back.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--apply-update", var applyPath, var parent] && int.TryParse(parent, out var parentProcessId))
        {
            // In the update folder of the update being applied: apply.json sits one folder below it.
            Log.UseFile(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(applyPath)))!, "update.log"));
            // Held on this thread for the whole swap, so the Full Screen Experience entry waits instead of starting Playdex.
            using var applying = AppUpdate.HoldApplyingLock();
            var outcome = AppUpdate.ApplyAsync(Path.GetFullPath(applyPath), parentProcessId).GetAwaiter().GetResult();
            applying.ReleaseMutex();
            return outcome switch
            {
                UpdateOutcome.Installed => 0,
                UpdateOutcome.RolledBack => 1,
                _ => 3,
            };
        }

        LauncherManifest? manifest;
        try
        {
            manifest = LauncherRouting.Resolve(Environment.ProcessPath);
        }
        catch (InvalidDataException ex)
        {
            Log.UseFile(Path.Combine(Paths.DataDirectory, "run.log"));
            Log.Error("run: the launcher manifest is unusable", ex);
            return 1;
        }
        if (manifest is null)
        {
            // Started on its own, not as a generated launcher: nothing to do, and nothing worth a log line.
            return 2;
        }

        try
        {
            // A launcher made by an older Wrapper names the Wrapper's own config; the install's config replaces it.
            var store = ConfigStore.IsDefaultPath(manifest.ConfigPath) ? ConfigStore.Default() : new ConfigStore(manifest.ConfigPath);
            var config = store.LoadOrCreate();
            Log.UseFile(Path.Combine(config.Storage.LogDirectory, $"run-{DateTime.Now:yyyyMMdd}.log"));
            Log.DeleteOlderThan(config.Storage.LogDirectory, "run-*.log", TimeSpan.FromDays(14));
            var game = config.GetGameForLaunch(manifest.GameId);
            Log.Info($"run: {game.DisplayName} ({manifest.GameId})");
            // The launcher's last summary says which accounts Playdex polls: a game it records by polling isn't recorded here.
            using var summaryApi = new CaptureClient(config.Api);
            using var summary = new PlaydexSummary(summaryApi);
            using var coordinator = SessionCoordinator.Create(
                config, game, LogLoggerFactory.Instance.CreateLogger<SessionCoordinator>(), polled: summary.IsPolled);
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
