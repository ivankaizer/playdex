using System.Diagnostics;

namespace Playdex.Desktop;

/// <summary>Headless checks run with command-line flags; results go to desktop.log and metrics.jsonl.</summary>
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

    /// <summary>
    /// Logs what the quick settings panel would show. With <paramref name="toggle"/>: makes another sound output the
    /// default, changes the volume and mutes, flips HDR on every display that supports it, switches each display to another refresh rate, reads each
    /// change back, then restores all of them; then toggles the NVIDIA FPS counter twice, 3 s apart.
    /// </summary>
    public static int QuickSettingsState(bool toggle)
    {
        var outputs = AudioOutputs.List();
        var displays = Displays.List();
        foreach (var output in outputs)
        {
            Log.Info($"selftest-quick-settings: output {(output.IsDefault ? "*" : " ")} {output.Name} [{output.Id}]");
        }
        var volume = AudioOutputs.ReadVolume();
        Log.Info($"selftest-quick-settings: volume {(volume is null ? "unavailable" : $"{volume.Level}{(volume.Muted ? " muted" : "")}")}");
        foreach (var display in displays)
        {
            Log.Info($"selftest-quick-settings: display {display.Name} [{display.Id}] hdr supported={display.HdrSupported} on={display.HdrOn} refresh={display.RefreshRate} Hz of [{string.Join(", ", display.RefreshRates)}]");
        }
        var fpsCounter = NvidiaOverlay.Read();
        Log.Info($"selftest-quick-settings: fps counter {(fpsCounter is null ? "unavailable" : $"NVIDIA overlay, {fpsCounter.Hotkey}")}");
        if (!toggle)
        {
            Log.Info("selftest-quick-settings: PASS (read only)");
            return 0;
        }

        var failures = 0;
        if (outputs.FirstOrDefault(o => o.IsDefault) is { } original && outputs.FirstOrDefault(o => !o.IsDefault) is { } other)
        {
            AudioOutputs.SetDefault(other.Id);
            var switched = AudioOutputs.List().Single(o => o.IsDefault).Id == other.Id;
            AudioOutputs.SetDefault(original.Id);
            var restored = AudioOutputs.List().Single(o => o.IsDefault).Id == original.Id;
            failures += switched && restored ? 0 : 1;
            Log.Info($"selftest-quick-settings: output switched={switched} restored={restored}");
        }
        if (volume is not null)
        {
            var otherLevel = volume.Level >= 50 ? volume.Level - 10 : volume.Level + 10;
            AudioOutputs.SetVolume(otherLevel);
            var changed = AudioOutputs.ReadVolume() == new AudioVolume(otherLevel, false);
            AudioOutputs.SetMuted(true);
            var muted = AudioOutputs.ReadVolume()?.Muted == true;
            AudioOutputs.SetVolume(volume.Level);
            AudioOutputs.SetMuted(volume.Muted);
            var restored = AudioOutputs.ReadVolume() == volume;
            failures += changed && muted && restored ? 0 : 1;
            Log.Info($"selftest-quick-settings: volume {volume.Level} -> {otherLevel} changed={changed} muted={muted} restored={restored}");
        }
        foreach (var display in displays.Where(d => d.HdrSupported))
        {
            Displays.SetHdr(display.Id, !display.HdrOn);
            Thread.Sleep(1500);
            var flipped = Displays.List().Single(d => d.Id == display.Id).HdrOn == !display.HdrOn;
            Displays.SetHdr(display.Id, display.HdrOn);
            Thread.Sleep(1500);
            var restored = Displays.List().Single(d => d.Id == display.Id).HdrOn == display.HdrOn;
            failures += flipped && restored ? 0 : 1;
            Log.Info($"selftest-quick-settings: {display.Name} hdr flipped={flipped} restored={restored}");
        }
        foreach (var display in displays.Where(d => d.RefreshRates.Count > 1))
        {
            var otherRate = display.RefreshRates.Contains(60) && display.RefreshRate != 60 ? 60 : display.RefreshRates.First(r => r != display.RefreshRate);
            Displays.SetRefreshRate(display.Id, otherRate);
            Thread.Sleep(2000);
            var switched = Displays.List().Single(d => d.Id == display.Id).RefreshRate == otherRate;
            Displays.SetRefreshRate(display.Id, display.RefreshRate);
            Thread.Sleep(2000);
            var restored = Displays.List().Single(d => d.Id == display.Id).RefreshRate == display.RefreshRate;
            failures += switched && restored ? 0 : 1;
            Log.Info($"selftest-quick-settings: {display.Name} refresh {display.RefreshRate} -> {otherRate} Hz switched={switched} restored={restored}");
        }
        if (fpsCounter is not null)
        {
            // The overlay's state can't be read back: pressed twice, so it ends as it was; check the screen in between.
            NvidiaOverlay.ToggleStatistics();
            Thread.Sleep(3000);
            NvidiaOverlay.ToggleStatistics();
            Log.Info("selftest-quick-settings: fps counter toggled twice");
        }
        Log.Info($"selftest-quick-settings: {(failures == 0 ? "PASS" : $"FAIL ({failures})")}");
        return failures == 0 ? 0 : 1;
    }
}
