using System.Runtime.InteropServices;

namespace Playdex.Desktop;

/// <summary>
/// Leaving the Windows Full Screen Experience (Xbox mode) for the desktop. Windows has no API for it; its shortcut,
/// Win+F11, toggles it, so it is pressed only while the Full Screen Experience is on, which shows as the taskbar being
/// hidden (pressing it on the desktop would enter it instead).
/// </summary>
internal static unsafe partial class FullScreenExperience
{
    private const uint KeyboardInput = 1;
    private const uint KeyUp = 0x0002;
    private const ushort LeftWindows = 0x5B;
    private const ushort F11 = 0x7A;

    /// <summary>Press Win+F11 when the Full Screen Experience is on; true when it was.</summary>
    public static bool Leave()
    {
        if (!IsOn())
        {
            Log.Info("desktop: the Full Screen Experience is not on; nothing to leave");
            return false;
        }
        var keys = stackalloc Input[4];
        keys[0] = Key(LeftWindows, 0);
        keys[1] = Key(F11, 0);
        keys[2] = Key(F11, KeyUp);
        keys[3] = Key(LeftWindows, KeyUp);
        var sent = SendInput(4, keys, sizeof(Input));
        Log.Info($"desktop: leaving the Full Screen Experience (Win+F11, {sent} of 4 key events sent)");
        return sent == 4;
    }

    /// <summary>The Full Screen Experience hides the taskbar; on the desktop it is a visible window.</summary>
    private static bool IsOn()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        return taskbar != 0 && !IsWindowVisible(taskbar);
    }

    private static Input Key(ushort key, uint flags) =>
        new() { Type = KeyboardInput, Keyboard = new KeyboardInputData { VirtualKey = key, Flags = flags } };

    // INPUT: a 4-byte type, then the union at pointer alignment (8 on 64-bit); the mouse member makes it 40 bytes.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);
}
