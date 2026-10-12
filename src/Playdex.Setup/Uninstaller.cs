using Microsoft.Win32;
using Playdex.Core;

namespace Playdex.Setup;

/// <summary>
/// Settings → Apps → Playdex → Uninstall (Playdex-Setup.exe --uninstall [--quiet]). It closes Playdex and removes the
/// app folder, and of the Full Screen Experience package, the start-with-Windows entry, the Start menu shortcut and the
/// Settings → Apps entry whichever belong to this install (another copy keeps its own), then asks whether to remove the
/// settings and data in %LOCALAPPDATA%\Playdex too (--quiet keeps them).
/// The play history is in the Playdex account and isn't touched. Started from inside the app folder, it continues from
/// a copy in %TEMP% first, which removes itself at the end.
/// </summary>
internal static class Uninstaller
{
    private const uint YesNo = 0x04, RetryCancel = 0x05, Question = 0x20, Warning = 0x30, Information = 0x40, Error = 0x10;
    private const int Yes = 6, Retry = 4;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static int Run(string[] args, string? installDirectory)
    {
        var quiet = args.Contains("--quiet");
        var install = installDirectory ?? InstallRegistration.InstalledLocation() ?? Program.DefaultInstallDirectory;
        if (Program.IsInside(Environment.ProcessPath, install))
        {
            return Program.RelaunchFromTemp(args, install);
        }
        try
        {
            if (!quiet && Program.MessageBox(0, "Remove Playdex from this PC?\n\nYour play history stays in your Playdex account.", "Uninstall Playdex", YesNo | Question) != Yes)
            {
                return 1;
            }
            Log.Info($"uninstall: removing Playdex from {install}");
            if (!ClosePlaydex(Path.Combine(install, AppUpdate.AppExecutable), quiet))
            {
                return 1;
            }
            if (FseStartsThisInstall(install))
            {
                RemoveFsePackage();
            }
            RemoveStartWithWindows(install);
            if (ShortcutTarget(Program.StartMenuShortcut) is { } target && Program.IsInside(target, install))
            {
                File.Delete(Program.StartMenuShortcut);
            }
            if (InstallRegistration.IsRegistered(install))
            {
                InstallRegistration.Unregister();
            }
            DeleteFolder(install);
            Log.Info("uninstall: Playdex removed");

            if (!quiet && Program.MessageBox(0,
                    "Also remove Playdex's settings and data from this PC?\n\nThat is the server address and API key, game sessions not uploaded yet, logs, and the game launchers that Steam shortcuts made by Playdex use. Choose No to keep them for a later install.",
                    "Uninstall Playdex", YesNo | Question) == Yes)
            {
                // The log lives in that folder.
                Log.UseFile(Path.Combine(Path.GetTempPath(), "playdex-uninstall.log"));
                DeleteFolder(Paths.DataDirectory);
                Log.Info("uninstall: settings and data removed");
            }
            if (!quiet)
            {
                Program.MessageBox(0, "Playdex was removed from this PC.", "Uninstall Playdex", Information);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("uninstall: failed", ex);
            if (!quiet)
            {
                Program.MessageBox(0, $"Playdex could not be removed completely.\n\n{ex.Message}", "Uninstall Playdex", Error);
            }
            return 1;
        }
        finally
        {
            Program.DeleteSelfLater(args);
        }
    }

    /// <summary>Ask Playdex to quit and wait; asks the user to quit it when it doesn't (false when they cancel).</summary>
    private static bool ClosePlaydex(string executable, bool quiet)
    {
        while (true)
        {
            if (Program.InstalledVersion(executable) is { } installed)
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                try
                {
                    // Only the automatic part: --quit and 20 s for it to take effect.
                    Task.Run(() => Program.ClosePlaydexAsync(executable, installed, _ => { }), stop.Token).Wait(TimeSpan.FromSeconds(22));
                }
                catch (AggregateException)
                {
                    // Reported below if it is still running.
                }
            }
            if (!Program.IsPlaydexRunning(executable))
            {
                return true;
            }
            if (quiet || Program.MessageBox(0,
                    "Playdex is still running. Quit it (right-click its icon in the notification area, then Quit Playdex), then choose Retry.",
                    "Uninstall Playdex", RetryCancel | Warning) != Retry)
            {
                Log.Info("uninstall: Playdex is still running; stopped");
                return false;
            }
        }
    }

    private static void RemoveFsePackage()
    {
        try
        {
            if (FsePackage.IsInstalledAsync().GetAwaiter().GetResult())
            {
                FsePackage.RemoveAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("uninstall: the Full Screen Experience package was not removed", ex);
        }
    }

    /// <summary>The Full Screen Experience package starts the Playdex.exe the config records, else the default install.</summary>
    private static bool FseStartsThisInstall(string install)
    {
        string? recorded = null;
        if (File.Exists(Paths.ConfigFile))
        {
            try
            {
                recorded = ConfigStore.Default().LoadOrCreate().Shell.ExecutablePath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Log.Error("uninstall: could not read the config", ex);
            }
        }
        return recorded is not null
            ? Program.IsInside(recorded, install)
            : InstallRegistration.SameDirectory(install, Program.DefaultInstallDirectory);
    }

    /// <summary>The value Playdex writes is "&lt;its exe&gt;" --background; another copy's is left alone.</summary>
    private static void RemoveStartWithWindows(string install)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue("Playdex") is string command && Program.IsInside(command.TrimStart('"').Split('"')[0], install))
        {
            key.DeleteValue("Playdex", throwOnMissingValue: false);
        }
    }

    /// <summary>A shortcut's target, through Windows Script Host's shortcut object (via PowerShell, no COM interop here).</summary>
    private static string? ShortcutTarget(string shortcut)
    {
        if (!File.Exists(shortcut))
        {
            return null;
        }
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
            $"(New-Object -ComObject WScript.Shell).CreateShortcut('{shortcut.Replace("'", "''", StringComparison.Ordinal)}').TargetPath" })
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using var process = System.Diagnostics.Process.Start(start);
            var target = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit(30_000);
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("uninstall: could not read the Start menu shortcut", ex);
            return null;
        }
    }

    /// <summary>WebView2 can hold files for a moment after Playdex exits.</summary>
    private static void DeleteFolder(string path)
    {
        for (var attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(500);
            }
        }
    }
}
