using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Playdex.Desktop.NativeDesktop;

namespace Playdex.Desktop;

/// <summary>
/// The notification area icon: a click opens Playdex, the context menu offers Open, switching the mode and Quit, and <see cref="Notify"/>
/// shows a Windows notification. Plain Shell_NotifyIcon on a hidden window owned by the UI thread (WinUI has no tray
/// API). The icon is added again when Explorer restarts (the "TaskbarCreated" broadcast).
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WmApp + 1;
    private const nuint OpenCommand = 1;
    private const nuint QuitCommand = 2;
    private const nuint SwitchModeCommand = 3;

    private static TrayIcon? _current;

    private readonly Action _open;
    private readonly Action _quit;
    private readonly Func<string> _switchModeLabel;
    private readonly Action _switchMode;
    private readonly uint _taskbarCreated;
    private readonly nint _window;
    private readonly nint _icon;
    private readonly string _tooltip;

    /// <summary>NOTIFYICON_VERSION_4 took: a click arrives as NIN_SELECT (and a WM_LBUTTONUP too, which is then ignored).</summary>
    private bool _version4;

    /// <summary>Call on the UI thread: the hidden window's messages arrive through its message loop.</summary>
    /// <param name="switchModeLabel">The menu item that switches between desktop mode and Big Picture, as it reads now.</param>
    public TrayIcon(string tooltip, Action open, Action quit, Func<string> switchModeLabel, Action switchMode)
    {
        _tooltip = tooltip;
        _open = open;
        _quit = quit;
        _switchModeLabel = switchModeLabel;
        _switchMode = switchMode;
        _current = this;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        var instance = GetModuleHandle(null);
        fixed (char* className = "PlaydexTray")
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc,
                Instance = instance,
                ClassName = className,
            };
            RegisterClassEx(&windowClass);
            // A real (never shown) top-level window, not a message-only one: only top-level windows get TaskbarCreated.
            _window = CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        }

        nint small;
        _icon = ExtractIconEx(Environment.ProcessPath ?? "", 0, null, &small, 1) > 0 ? small : 0;
        Add();
    }

    /// <summary>A Windows notification from the tray icon, for news while no window is open.</summary>
    public void Notify(string title, string text, bool warning = false)
    {
        var data = Data(NifInfo);
        Copy(title, data.InfoTitle, 64);
        Copy(text, data.Info, 256);
        data.InfoFlags = warning ? NiifWarning : NiifInfo;
        ShellNotifyIcon(NimModify, &data);
    }

    private void Add()
    {
        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        if (!ShellNotifyIcon(NimAdd, &data))
        {
            Log.Info("tray: could not add the notification icon");
            return;
        }
        data.VersionOrTimeout = NotifyIconVersion4;
        _version4 = ShellNotifyIcon(NimSetVersion, &data);
    }

    private NotifyIconData Data(uint flags)
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = _window,
            Id = 1,
            Flags = flags,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
        };
        Copy(_tooltip, data.Tip, 128);
        return data;
    }

    private static void Copy(string value, char* destination, int capacity)
    {
        var length = Math.Min(value.Length, capacity - 1);
        value.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, OpenCommand, "Open Playdex");
        AppendMenu(menu, MfString, SwitchModeCommand, _switchModeLabel());
        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString, QuitCommand, "Quit Playdex");
        GetCursorPos(out var point);
        // The menu closes when it loses focus only if its owner is in front.
        SetForegroundWindow(_window);
        var command = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton | TpmBottomAlign, point.X, point.Y, 0, _window, 0);
        PostMessage(_window, WmNull, 0, 0);
        DestroyMenu(menu);
        switch ((nuint)command)
        {
            case OpenCommand:
                _open();
                break;
            case SwitchModeCommand:
                _switchMode();
                break;
            case QuitCommand:
                _quit();
                break;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        var tray = _current;
        try
        {
            if (tray is not null && message == CallbackMessage)
            {
                // NOTIFYICON_VERSION_4: the event is the low word of lParam. One click sends both NIN_SELECT and
                // WM_LBUTTONUP; opening on both made two windows.
                switch ((uint)(lParam & 0xFFFF))
                {
                    case NinSelect or NinKeySelect:
                    case WmLButtonUp when !tray._version4:
                        tray._open();
                        break;
                    case WmContextMenu:
                        tray.ShowMenu();
                        break;
                }
                return 0;
            }
            if (tray is not null && message == tray._taskbarCreated)
            {
                tray.Add();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error("tray: action failed", ex);
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        var data = Data(0);
        ShellNotifyIcon(NimDelete, &data);
        if (_icon != 0)
        {
            DestroyIcon(_icon);
        }
        DestroyWindow(_window);
        _current = null;
    }
}
