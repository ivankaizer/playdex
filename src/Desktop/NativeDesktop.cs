using System.Runtime.InteropServices;

namespace Playdex.Desktop;

#pragma warning disable CA1069 // Win32 constants

/// <summary>Win32 imports for the tray icon and the file picker. Source-generated (LibraryImport) for Native AOT.</summary>
internal static unsafe partial class NativeDesktop
{
    // ---- Windows and messages ----

    public const uint WmNull = 0x0000;
    public const uint WmDestroy = 0x0002;
    public const uint WmCommand = 0x0111;
    public const uint WmContextMenu = 0x007B;
    public const uint WmLButtonUp = 0x0202;
    public const uint WmApp = 0x8000;
    public const uint NinSelect = 0x0400;
    public const uint NinKeySelect = 0x0401;

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(char* moduleName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(WndClassEx* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static partial nint CreateWindowEx(
        uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    // ---- Menus ----

    public const uint MfString = 0x0000;
    public const uint MfSeparator = 0x0800;
    public const uint TpmRightButton = 0x0002;
    public const uint TpmReturnCmd = 0x0100;
    public const uint TpmBottomAlign = 0x0020;

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    // ---- Notification area ----

    public const uint NimAdd = 0x0;
    public const uint NimModify = 0x1;
    public const uint NimDelete = 0x2;
    public const uint NimSetVersion = 0x4;
    public const uint NifMessage = 0x1;
    public const uint NifIcon = 0x2;
    public const uint NifTip = 0x4;
    public const uint NifInfo = 0x10;
    public const uint NifShowTip = 0x80;
    public const uint NiifInfo = 0x1;
    public const uint NiifWarning = 0x2;
    public const uint NotifyIconVersion4 = 4;

    /// <summary>NOTIFYICONDATAW (976 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint VersionOrTimeout;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellNotifyIcon(uint message, NotifyIconData* data);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ExtractIconEx(string file, int index, nint* largeIcons, nint* smallIcons, uint icons);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    // ---- File dialog ----

    public const uint OfnExplorer = 0x80000;
    public const uint OfnFileMustExist = 0x1000;
    public const uint OfnPathMustExist = 0x800;
    public const uint OfnNoChangeDir = 0x8;

    /// <summary>OPENFILENAMEW (152 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OpenFileName
    {
        public uint StructSize;
        public nint Owner;
        public nint Instance;
        public char* Filter;
        public char* CustomFilter;
        public uint MaxCustomFilter;
        public uint FilterIndex;
        public char* File;
        public uint MaxFile;
        public char* FileTitle;
        public uint MaxFileTitle;
        public char* InitialDirectory;
        public char* Title;
        public uint Flags;
        public ushort FileOffset;
        public ushort FileExtension;
        public char* DefaultExtension;
        public nint CustomData;
        public nint Hook;
        public char* TemplateName;
        public nint Reserved;
        public uint ReservedFlags;
        public uint FlagsEx;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetOpenFileName(OpenFileName* dialog);
}
