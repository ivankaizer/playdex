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

/// <summary>What the window says and offers.</summary>
/// <param name="KeySaved">The config has an API key: an empty key field keeps it, and there is no Skip.</param>
/// <param name="OfferFse">Show "Also use as Full Screen Experience home" (Developer Mode is on).</param>
/// <param name="FseChecked">Its first state: checked when the package is installed already (it is then updated).</param>
internal sealed record SetupPage(
    string Heading, string Body, string BaseUrl, bool KeySaved, bool OfferFse, bool FseChecked, string InstallLabel);

/// <summary>
/// The installer's window: the Playdex server (API URL) and API key, checked with the server before they are saved, the
/// Full Screen Experience option, then the install running with its progress. Skip installs without a key (Playdex →
/// Settings takes it later). Plain Win32, because the setup is a small Native AOT exe; one window per process, on the
/// main thread.
/// </summary>
internal static class SetupWindow
{
    private const int UrlEditId = 100, KeyEditId = 101, SkipId = 102, FseId = 103;
    private const uint CheckDone = WmApp + 1, InstallDone = WmApp + 2, Progress = WmApp + 3;

    // Layout in 96-DPI pixels.
    private const int Width = 480, Margin = 24, Inner = Width - 2 * Margin, ButtonWidth = 104;

    private static SetupPage _page = null!;
    private static Func<string, string, Task<ApiKeyCheck>> _check = null!;
    private static Action<string?, string?> _save = null!;
    private static Func<bool, Action<string>, Task> _install = null!;
    private static nint _window, _url, _key, _fse, _status, _skip, _ok, _font, _titleFont;
    private static uint _dpi = 96;
    private static int _height;
    private static bool _busy, _installing, _error;
    private static (string Url, string Key) _pending;
    private static (string Url, string Key)? _unverified;
    private static ApiKeyCheck? _checked;
    private static volatile string _progress = "";
    private static Exception? _failure;
    private static SetupOutcome _outcome = SetupOutcome.Cancelled;

    /// <param name="check">Asks the server whether it takes the key; runs off the UI thread.</param>
    /// <param name="save">Saves the server and key (null leaves one as it is) once they are accepted.</param>
    /// <param name="install">The install (whether to set up the FSE package, a progress report); off the UI thread.</param>
    public static unsafe (SetupOutcome Outcome, Exception? Failure) Run(
        SetupPage page, Func<string, string, Task<ApiKeyCheck>> check, Action<string?, string?> save, Func<bool, Action<string>, Task> install)
    {
        _page = page;
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
            CreateWindow(large, small);
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

    private static unsafe void CreateWindow(nint icon, nint smallIcon)
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

        // The window's height follows its rows.
        var statusTop = _page.OfferFse ? 286 : 250;
        _height = statusTop + 40 + 12 + 32 + Margin;
        const uint style = WsOverlapped | WsCaption | WsSysMenu | WsMinimizeBox;
        var frame = new Rect { Right = Scale(Width), Bottom = Scale(_height) };
        AdjustWindowRectExForDpi(&frame, style, false, WsExControlParent, _dpi);
        int width = frame.Right - frame.Left, height = frame.Bottom - frame.Top;
        _window = CreateWindowEx(
            WsExControlParent, "PlaydexSetup", "Playdex Setup", style,
            (GetSystemMetrics(SmCxScreen) - width) / 2, (GetSystemMetrics(SmCyScreen) - height) / 2, width, height,
            0, 0, instance, 0);

        Text(_page.Heading, 20, 30, _titleFont);
        Text(_page.Body, 56, 60);
        Text("Playdex server (API URL)", 126, 20);
        _url = Edit(UrlEditId, 148, password: false, _page.BaseUrl);
        Text("API key", 186, 20);
        _key = Edit(KeyEditId, 208, password: true, "");
        fixed (char* cue = _page.KeySaved ? "Saved: leave empty to keep it" : "Paste your API key")
        {
            SendMessage(_key, EmSetCueBanner, 1, (nint)cue);
        }
        if (_page.OfferFse)
        {
            _fse = CreateWindowEx(0, "BUTTON", "Also use Playdex as the Full Screen Experience home",
                WsChild | WsVisible | WsTabStop | BsAutoCheckBox, Scale(Margin), Scale(248), Scale(Inner), Scale(26), _window, FseId, 0, 0);
            SendMessage(_fse, WmSetFont, _font, 1);
            SendMessage(_fse, BmSetCheck, _page.FseChecked ? 1 : 0, 0);
        }
        _status = Text("", statusTop, 40);
        _skip = Button(SkipId, "Skip", Width - Margin - 2 * ButtonWidth - 8, isDefault: false);
        _ok = Button(IdOk, _page.InstallLabel, Width - Margin - ButtonWidth, isDefault: true);
        if (_page.KeySaved)
        {
            ShowWindow(_skip, SwHide); // An empty key keeps the saved one; there is nothing to skip.
        }

        ShowWindow(_window, SwShow);
        SetForegroundWindow(_window);
        SetFocus(_page.KeySaved ? _ok : _key);
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
            Scale(x), Scale(_height - Margin - 32), Scale(ButtonWidth), Scale(32), _window, id, 0, 0);
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
                case Progress:
                    Show(_progress, error: false);
                    return 0;
                case InstallDone:
                    _installing = false;
                    _outcome = _failure is null ? SetupOutcome.Installed : SetupOutcome.Failed;
                    DestroyWindow(_window);
                    return 0;
                case WmCtlColorStatic:
                    // Labels and the checkbox on the window's own background; problems in red.
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
            if (_page.KeySaved)
            {
                Accept(url, null);
                return;
            }
            Show("Paste your API key, or choose Skip to add it later in Playdex → Settings.", error: true);
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
        Show("Checking your key…", error: false);
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
                Show($"Couldn't check the key ({_checked?.Detail ?? "no answer"}). Check the server address, or press {_page.InstallLabel} again to save it anyway.", error: true);
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
        Accept(url, null);
    }

    private static void Accept(string? url, string? key)
    {
        _save(url, key);
        _installing = true;
        SetBusy(true);
        EnableWindow(_url, false);
        EnableWindow(_key, false);
        var fse = _fse != 0 && SendMessage(_fse, BmGetCheck, 0, 0) == 1;
        if (_fse != 0)
        {
            EnableWindow(_fse, false);
        }
        Show("Starting…", error: false);
        var install = _install;
        var window = _window;
        _ = Task.Run(async () =>
        {
            try
            {
                await install(fse, text =>
                {
                    _progress = text;
                    PostMessage(window, Progress, 0, 0);
                });
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
