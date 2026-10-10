using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Playdex.Shell;

/// <summary>The NVIDIA App's statistics overlay (FPS, GPU, CPU, latency); posted with the quick settings state when available.</summary>
/// <param name="Hotkey">Its hotkey as NVIDIA shows it, e.g. "Alt+R".</param>
internal sealed record FpsCounter(string Hotkey);

/// <summary>
/// Shows and hides the NVIDIA App's statistics overlay by pressing its hotkey, which the overlay registers system-wide
/// (NVIDIA has no public API for it). The hotkey is read from the overlay's settings
/// (HKCU\Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS, PMOCOverlayHKey*: a count and one virtual-key code per
/// key, modifiers first), so a rebound hotkey still works. The overlay keeps no readable on/off state, so this toggles.
/// </summary>
internal static unsafe class NvidiaOverlay
{
    private const string SettingsKey = @"Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS";
    private const string HotkeyName = "PMOCOverlayHKey";
    private const string OverlayProcess = "NVIDIA Overlay";

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLeftWin = 0x5B;

    /// <summary>
    /// An unassigned key, pressed while Alt is still down: a released Alt with nothing else seen by the foreground
    /// window would open its window menu (the hotkey's own key never reaches the window).
    /// </summary>
    private const ushort VkMask = 0xE8;

    /// <summary>The statistics overlay, or null when the NVIDIA overlay isn't running or has no statistics hotkey.</summary>
    public static FpsCounter? Read()
    {
        try
        {
            if (Hotkey() is not { } keys || !Running())
            {
                return null;
            }
            return new FpsCounter(string.Join('+', keys.Select(KeyName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Press the statistics hotkey: the overlay shows its statistics if they're hidden and hides them if shown.</summary>
    public static void ToggleStatistics()
    {
        var keys = Hotkey() ?? throw new InvalidOperationException("The NVIDIA overlay has no statistics hotkey.");
        if (!Running())
        {
            throw new InvalidOperationException("The NVIDIA overlay isn't running.");
        }
        var inputs = new List<Native.KeyInput>();
        foreach (var key in keys)
        {
            inputs.Add(Native.KeyInput.Create(key, up: false));
        }
        inputs.Add(Native.KeyInput.Create(keys[^1], up: true));
        if (keys.Contains(VkMenu))
        {
            inputs.Add(Native.KeyInput.Create(VkMask, up: false));
            inputs.Add(Native.KeyInput.Create(VkMask, up: true));
        }
        for (var i = keys.Length - 2; i >= 0; i--)
        {
            inputs.Add(Native.KeyInput.Create(keys[i], up: true));
        }
        var array = inputs.ToArray();
        fixed (Native.KeyInput* buffer = array)
        {
            var sent = Native.SendInput((uint)array.Length, buffer, sizeof(Native.KeyInput));
            if (sent != array.Length)
            {
                throw new InvalidOperationException($"SendInput sent {sent} of {array.Length} key events: {Marshal.GetLastPInvokeError()}");
            }
        }
    }

    private static bool Running()
    {
        var processes = Process.GetProcessesByName(OverlayProcess);
        foreach (var process in processes)
        {
            process.Dispose();
        }
        return processes.Length > 0;
    }

    /// <summary>The hotkey's virtual-key codes, modifiers first; null if there is none.</summary>
    private static ushort[]? Hotkey()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (settings is null || Dword(settings, HotkeyName + "Count") is not int count || count is < 1 or > 4)
        {
            return null;
        }
        var keys = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            if (Dword(settings, HotkeyName + i) is not int key || key is < 1 or > 0xFE)
            {
                return null;
            }
            keys[i] = (ushort)key;
        }
        return keys;
    }

    /// <summary>NVIDIA stores these values as 4-byte binaries, little-endian.</summary>
    private static int? Dword(RegistryKey key, string name) =>
        key.GetValue(name) is byte[] { Length: 4 } bytes ? BitConverter.ToInt32(bytes) : null;

    private static string KeyName(ushort key) => key switch
    {
        VkShift => "Shift",
        VkControl => "Ctrl",
        VkMenu => "Alt",
        VkLeftWin => "Win",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        _ => $"key {key:X2}",
    };
}
