using System.Diagnostics;
using Microsoft.Win32;

namespace Playdex.Launcher.Host;

/// <summary>
/// The Steam client's state, from HKCU\Software\Valve\Steam\ActiveProcess: <c>pid</c> of the running client and
/// <c>ActiveUser</c> once someone is logged in. Steam drops a steam:// launch URL that arrives while it is starting
/// (spike-results.md, blocker 3), so a launch first makes sure the client is up and logged in.
/// </summary>
internal static class SteamClient
{
    private const string ActiveProcessKey = @"Software\Valve\Steam\ActiveProcess";

    /// <summary>A client process is alive and a user is logged in.</summary>
    public static bool IsLoggedIn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ActiveProcessKey, writable: false);
        return key is not null && IsLoggedIn(key);
    }

    /// <summary>
    /// Start Steam if it isn't running, in the tray (-silent) so no Steam window covers the launcher. Not tested
    /// without a saved login. False if Steam is not installed or could not be started.
    /// </summary>
    public static bool StartIfNeeded()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(ActiveProcessKey, writable: false))
        {
            if (key is not null && IsClientAlive(key))
            {
                return true;
            }
        }
        if (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamExe") is not string exe || !File.Exists(exe))
        {
            Log.Info("steam: Steam is not installed");
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe, "-silent") { UseShellExecute = false })?.Dispose();
            Log.Info("steam: client not running; started it in the tray");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("steam: could not start the client", ex);
            return false;
        }
    }

    /// <summary>
    /// Wait until a user is logged in (ActiveProcess changes are watched with RegNotifyChangeKeyValue, no polling).
    /// False on timeout or cancellation.
    /// </summary>
    public static bool WaitForLogin(TimeSpan timeout, CancellationToken cancel)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ActiveProcessKey, writable: false);
        using var changed = new AutoResetEvent(false);
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!cancel.IsCancellationRequested)
        {
            // Arm before reading so a change between the read and the wait is never missed.
            var status = Native.RegNotifyChangeKeyValue(key.Handle, false, Native.RegNotifyChangeLastSet, changed.SafeWaitHandle, true);
            if (IsLoggedIn(key))
            {
                return true;
            }
            var remaining = (deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
            if (remaining <= 0)
            {
                return false;
            }
            // If the notification could not be armed, re-read once a second instead.
            var wait = status == 0 ? (int)remaining : (int)Math.Min(remaining, 1000);
            WaitHandle.WaitAny([changed, cancel.WaitHandle], wait);
        }
        return false;
    }

    private static bool IsLoggedIn(RegistryKey key) => key.GetValue("ActiveUser") is int user && user != 0 && IsClientAlive(key);

    /// <summary>The pid value survives a Steam crash, so check that it is still a Steam process.</summary>
    private static bool IsClientAlive(RegistryKey key)
    {
        if (key.GetValue("pid") is not int pid || pid == 0)
        {
            return false;
        }
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
