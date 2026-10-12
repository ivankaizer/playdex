using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Playdex.Core;
using static Playdex.Setup.NativeSetup;

namespace Playdex.Setup;

internal enum SetupOutcome
{
    Installed,
    Cancelled,
    Failed,
}

/// <summary>
/// The installer's window: asks for the Playdex server (API URL) and API key, checks the key with the server, then
/// shows the install running. Skip installs without a key (it can be added later in Playdex в†’ Settings). Plain Win32,
/// because the setup is a small Native AOT exe; one window per process, on the main thread.
/// </summary>
internal static class SetupWindow
{
    private const int UrlEditId = 100, KeyEditId = 101, SkipId = 102;
    private const uint CheckDone = WmApp + 1, InstallDone = WmApp + 2;

    // Layout in 96-DPI pixels.
    private const int Width = 480, Height = 360, Margin = 24, Inner = Width - 2 * Margin;

    private static Func<string, string, Task<ApiKeyCheck>> _check = null!;
    private static Action<string?, string?> _save = null!;
    private static Func<Task> _install = null!;
    private static nint _window, _url, _key, _status, _skip, _ok, _font, _titleFont;
    private static uint _dpi = 96;
    private static bool _busy, _installing, _error;
    private static (string Url, string Key) _pending;
    private static (string Url, string Key)? _unverified;
    private static ApiKeyCheck? _checked;
    private static Exception? _failure;
    private static SetupOutcome _outcome = SetupOutcome.Cancelled;

    /// <param name="check">Asks the server whether it takes the key; runs off the UI thread.</param>
    /// <param name="save">Saves the server and key (null leaves one as it is) once they are accepted.</param>
    /// <param name="install">The install itself; runs off the UI thread while the window shows it.</param>
    public static unsafe (SetupOutcome Outcome, Exception? Failure) Run(
        string baseUrl, Func<string, string, Task<ApiKeyCheck>> check, Action<string?, string?> save, Func<Task> install)
    {
        _check = check;
        _save = save;
        _install = install;
        _dpi = GetDpiForSystem();
        _font = CreateFont(-Scale(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _titleFont = CreateFont(-Scale(20), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        nint large = 0, small = 0;
        ExtractIconEx(Environment.ProcessPath ?? "", 0, &large, &small, 1);
        try
        {
            CreateWindow(baseUrl, large, small);
            Msg message;
            while (GetMessage(&message, 0, 0, 0) > 0)
            {
                // Tab between fields, Enter installs, Escape cancels.
                if (!IsDialogMessage(_window, &message))
                {
                    TranslateMessage(&message);
                    DispatchMessage(&message);
                }
            }
            return (_outcome, _failure);
        }
        finally
        {
            DeleteObject(_font);
            DeleteObject(_titleFont);
            if (large != 0)
            {
                DestroyIcon(large);
            }
            if (small != 0)
            {
                DestroyIcon(small);
            }
        }
    }

    private static unsafe void CreateWindow(string baseUrl, nint icon, nint smallIcon)
    {
        var instance = GetModuleHandle(null);
        fixed (char* className = "PlaydexSetup")
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc,
                Instance = instance,
                Icon = icon,
                SmallIcon = smallIcon,
                Cursor = LoadCursor(0, 32512), // IDC_ARROW
                Background = GetSysColorBrush(ColorWindow),
                ClassName = className,
            };
            RegisterClassEx(&windowClass);
        }

        const uint style = WsOverlapped | WsCaption | WsSysMenu | WsMinimizeBox;
        var frame = new Rect { Right = Scale(Width), Bottom = Scale(Height) };
        AdjustWindowRectExForDpi(&frame, style, false, WsExControlParent, _dpi);
        int width = frame.Right - frame.Left, height = frame.Bottom - frame.Top;
        _window = CreateWindowEx(
            WsExControlParent, "PlaydexSetup", "Install Playdex", style,
            (GetSystemMetrics(SmCxScreen) - width) / 2, (GetSystemMetrics(SmCyScreen) - height) / 2, width, height,
            0, 0, instance, 0);

        Text("Connect Playdex to your account", 20, 30, _titleFont);
        Text("Playdex records the games you play on this PC with your API key. Create one on the Playdex website under Integrations, then paste it here.", 56, 60);
        Text("Playdex server (API URL)", 126, 20);
        _url = Edit(UrlEditId, 148, password: false, baseUrl);
        Text("API key", 186, 20);
        _key = Edit(KeyEditId, 208, password: true, "");
        fixed (char* cue = "Paste your API key")
        {
            SendMessage(_key, EmSetCueBanner, 1, (nint)cue);
        }
        _status = Text("", 246, 40);
        _skip = Button(SkipId, "Skip", Width - Margin - 2 * 104 - 8, isDefault: false);
        _ok = Button(IdOk, "Install", Width - Margin - 104, isDefault: true);

        ShowWindow(_window, SwShow);
        SetForegroundWindow(_window);
        SetFocus(_key);
    }

    private static nint Text(string text, int y, int height, nint font = 0)
    {
        var control = CreateWindowEx(0, "STATIC", text, WsChild | WsVisible | SsLeft,
            Scale(Margin), Scale(y), Scale(Inner), Scale(height), _window, 0, 0, 0);
        SendMessage(control, WmSetFont, font == 0 ? _font : font, 1);
        return control;
    }

    private static nint Edit(int id, int y, bool password, string text)
    {
        var control = CreateWindowEx(WsExClientEdge, "EDIT", text,
            WsChild | WsVisible | WsTabStop | EsAutoHScroll | (password ? EsPassword : 0),
            Scale(Margin), Scale(y), Scale(Inner), Scale(28), _window, id, 0, 0);
        SendMessage(control, WmSetFont, _font, 1);
        return control;
    }

    private static nint Button(int id, string text, int x, bool isDefault)
    {
        var control = CreateWindowEx(0, "BUTTON", text, WsChild | WsVisible | WsTabStop | (isDefault ? BsDefPushButton : BsPushButton),
            Scale(x), Scale(Height - Margin - 32), Scale(104), Scale(32), _window, id, 0, 0);
        SendMessage(control, WmSetFont, _font, 1);
        return control;
    }

    private static int Scale(int pixels) => (int)(pixels * _dpi / 96);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case WmCommand:
                    switch ((int)(wParam & 0xFFFF))
                    {
                        case IdOk:
                            OnInstall();
                            return 0;
                        case SkipId:
                            OnSkip();
                            return 0;
                        case IdCancel:
                            Close();
                            return 0;
                    }
                    break;
                case WmClose:
                    Close();
                    return 0;
                case WmDestroy:
                    PostQuitMessage(0);
                    return 0;
                case CheckDone:
                    OnChecked();
                    return 0;
                case InstallDone:
                    _installing = false;
                    _outcome = _failure is null ? SetupOutcome.Installed : SetupOutcome.Failed;
                    DestroyWindow(_window);
                    return 0;
                case WmCtlColorStatic:
                    // Labels on the window's own background; problems in red.
                    _ = SetBkColor(wParam, GetSysColor(ColorWindow));
                    if (lParam == _status && _error)
                    {
                        _ = SetTextColor(wParam, 0x002020C0);
                    }
                    return GetSysColorBrush(ColorWindow);
            }
        }
        catch (Exception ex)
        {
            Log.Error("setup: window action failed", ex);
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void OnInstall()
    {
        if (_busy || _installing)
        {
            return;
        }
        string url;
        try
        {
            url = ApiConfig.NormalizeBaseUrl(Read(_url));
        }
        catch (InvalidOperationException ex)
        {
            Show(ex.Message.Replace("API base URL", "The server address", StringComparison.Ordinal), error: true);
            SetFocus(_url);
            return;
        }
        var key = Read(_key);
        if (url.Length == 0)
        {
            Show("Enter the Playdex server's API URL.", error: true);
            SetFocus(_url);
            return;
        }
        if (key.Length == 0)
        {
            Show("Paste your API key, or choose Skip to add it later in Playdex в†’ Settings.", error: true);
            SetFocus(_key);
            return;
        }
        if (_unverified == (url, key))
        {
            Accept(url, key); // the second press after the server couldn't be reached
            return;
        }

        _pending = (url, key);
        SetBusy(true);
        Show("Checking your keyвЂ¦", error: false);
        var check = _check;
        var window = _window;
        _ = Task.Run(async () =>
        {
            try
            {
                _checked = await check(url, key);
            }
            catch (Exception ex)
            {
                _checked = new ApiKeyCheck(ApiKeyStatus.Unreachable, ex.Message);
            }
            PostMessage(window, CheckDone, 0, 0);
        });
    }

    private static void OnChecked()
    {
        SetBusy(false);
        var (url, key) = _pending;
        switch (_checked?.Status)
        {
            case ApiKeyStatus.Accepted:
                Accept(url, key);
                break;
            case ApiKeyStatus.Rejected:
                Show("The server didn't accept this key. Check that you copied all of it, or create a new one in Playdex under Integrations.", error: true);
                SetFocus(_key);
                break;
            default:
                _unverified = (url, key);
                Show($"Couldn't check the key ({_checked?.Detail ?? "no answer"}). Check the server address, or press Install again to save it anyway.", error: true);
                break;
        }
    }

    private static void OnSkip()
    {
        if (_busy || _installing)
        {
            return;
        }
        string? url = null;
        try
        {
            url = ApiConfig.NormalizeBaseUrl(Read(_url)) is { Length: > 0 } normalized ? normalized : null;
        }
        catch (InvalidOperationException)
        {
            // An unusable address is left out; Settings asks again.
        }
        Log.Info("setup: API key skipped");
        _save(url, null);
        StartInstall();
    }

    private static void Accept(string url, string key)
    {
        _save(url, key);
        StartInstall();
    }

    private static void StartInstall()
    {
        _installing = true;
        SetBusy(true);
        EnableWindow(_url, false);
        EnableWindow(_key, false);
        Show("Downloading and installing Playdex. This takes a minuteвЂ¦", error: false);
        var install = _install;
        var window = _window;
        _ = Task.Run(async () =>
        {
            try
            {
                await install();
            }
            catch (Exception ex)
            {
                _failure = ex;
            }
            PostMessage(window, InstallDone, 0, 0);
        });
    }

    private static void Close()
    {
        if (_installing)
        {
            return; // The install finishes first; the window closes itself then.
        }
        Log.Info("setup: cancelled");
        _outcome = SetupOutcome.Cancelled;
        DestroyWindow(_window);
    }

    private static void SetBusy(bool busy)
    {
        _busy = busy;
        EnableWindow(_ok, !busy);
        EnableWindow(_skip, !busy);
    }

    private static void Show(string text, bool error)
    {
        _error = error;
        SetWindowText(_status, text);
    }

    private static unsafe string Read(nint control)
    {
        var length = GetWindowTextLength(control);
        if (length <= 0)
        {
            return "";
        }
        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            var read = GetWindowText(control, text, buffer.Length);
            return new string(text, 0, read).Trim();
        }
    }
}
