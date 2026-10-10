using Playdex.Core;
using System.Diagnostics;

namespace Playdex.Shell;

/// <summary>
/// Finds the running game's window and brings it to the front ("Resume"). In order:
/// <list type="number">
/// <item>the window that was in front when the shell was called up over the game (Guide button, Game Bar "Home");</item>
/// <item>a window of the game's process, when it is known (games added to Playdex);</item>
/// <item>the topmost app window below the shell (as Alt+Tab would pick), skipping Steam's own windows.</item>
/// </list>
/// Only top-level, visible, uncloaked, unowned app windows with a title count.
/// </summary>
internal static unsafe class GameWindows
{
    private static readonly string[] _launcherProcesses = ["steam", "steamwebhelper"];

    // The desktop and the taskbars: visible, titled, unowned, but not apps.
    private static readonly string[] _shellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static bool Activate(nint shell, nint remembered, int? processId)
    {
        var target = IsAppWindow(remembered, shell) ? remembered : processId is { } pid ? Find(shell, pid) : 0;
        if (target == 0)
        {
            target = Find(shell, null);
        }
        if (target == 0)
        {
            Log.Info("resume: no game window found");
            return false;
        }
        if (Native.IsIconic(target))
        {
            Native.ShowWindow(target, Native.SwRestore);
        }
        if (!Native.SetForegroundWindow(target) || Native.GetForegroundWindow() != target)
        {
            // Foreground lock (the shell isn't the foreground process): borrow the foreground thread's input state.
            var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
            var thisThread = Native.GetCurrentThreadId();
            Native.AttachThreadInput(thisThread, foregroundThread, true);
            Native.BringWindowToTop(target);
            Native.SetForegroundWindow(target);
            Native.AttachThreadInput(thisThread, foregroundThread, false);
        }
        var ok = Native.GetForegroundWindow() == target;
        Log.Info($"resume: window {target:x} {(ok ? "in front" : "could not be brought to front")}");
        return ok;
    }

    /// <summary>The topmost app window of <paramref name="processId"/>, or of any process but the shell's and Steam's.</summary>
    private static nint Find(nint shell, int? processId)
    {
        for (var window = Native.GetTopWindow(0); window != 0; window = Native.GetWindow(window, Native.GwHwndNext))
        {
            if (!IsAppWindow(window, shell))
            {
                continue;
            }
            Native.GetWindowThreadProcessId(window, out var owner);
            if (processId is { } pid ? owner == pid : !IsLauncher((int)owner))
            {
                return window;
            }
        }
        return 0;
    }

    private static bool IsAppWindow(nint window, nint shell)
    {
        if (window == 0 || window == shell || !Native.IsWindow(window) || !Native.IsWindowVisible(window))
        {
            return false;
        }
        Native.GetWindowThreadProcessId(window, out var owner);
        if (owner == Environment.ProcessId
            || Native.GetWindow(window, Native.GwOwner) != 0
            || (Native.GetWindowLongPtr(window, Native.GwlExStyle) & Native.WsExToolWindow) != 0
            || Native.GetWindowTextLength(window) == 0
            || _shellClasses.Contains(ClassName(window), StringComparer.Ordinal))
        {
            return false;
        }
        // Cloaked: suspended UWP windows, windows on other virtual desktops.
        return Native.DwmGetWindowAttribute(window, Native.DwmwaCloaked, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static string ClassName(nint window)
    {
        var name = stackalloc char[64];
        var length = Native.GetClassName(window, name, 64);
        return length > 0 ? new string(name, 0, length) : "";
    }

    private static bool IsLauncher(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return _launcherProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return true;
        }
    }
}
