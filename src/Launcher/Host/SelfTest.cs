using System.Diagnostics;

namespace Playdex.Launcher.Host;

/// <summary>Headless checks run with command-line flags; results go to launcher.log and metrics.jsonl.</summary>
internal static class SelfTest
{
    /// <summary>
    /// Job Object exit detection with a launcher-style parent: cmd starts ping in the background and exits at once.
    /// Exit must be reported only when ping (the "real game") ends, about 2 s later.
    /// </summary>
    public static int JobExit()
    {
        var failures = 0;
        for (var run = 0; run < 10; run++)
        {
            using var exited = new ManualResetEventSlim();
            var started = Stopwatch.GetTimestamp();
            double detectionMs = -1, totalMs = 0;
            var watcher = new JobGameWatcher(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c start \"\" /b ping -n 3 127.0.0.1 >nul");
            watcher.Exited += (detected, processExit) =>
            {
                totalMs = Stopwatch.GetElapsedTime(started, detected).TotalMilliseconds;
                if (processExit != 0)
                {
                    var detectedFileTime = Native.PreciseNow() - (long)(Stopwatch.GetElapsedTime(detected).TotalMilliseconds * 10_000);
                    detectionMs = (detectedFileTime - processExit) / 10_000.0;
                }
                exited.Set();
            };
            watcher.Start();
            if (!exited.Wait(TimeSpan.FromSeconds(15)))
            {
                Log.Info("selftest-job: no exit event within 15 s");
                failures++;
                continue;
            }
            watcher.Dispose();
            // ping -n 3 runs ~2 s; an exit reported much earlier means the child was missed.
            var ok = totalMs > 1500;
            failures += ok ? 0 : 1;
            Metrics.Record("exit.job_detection", detectionMs, $"selftest run {run}: last process exit -> ACTIVE_PROCESS_ZERO; tree lived {totalMs:F0} ms; child tracked={ok}");
        }
        Log.Info($"selftest-job: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
        return failures == 0 ? 0 : 1;
    }
}
