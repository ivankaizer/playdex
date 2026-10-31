using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI;

namespace Playdex.Shell;

/// <summary>
/// Desktop mode's title bar is the page's own top bar: the page extends into the title bar area and Windows draws only
/// the caption buttons (minimize, maximize, close) over its right end, at the tall height, in the page's ink on a clear
/// background. The page says with {type:"title-bar", ink:[r,g,b], drag:[{x,y,width,height}]} (CSS pixels) where its
/// top bar is empty, which is what drags the window (a double-click maximizes it), and its ink; it re-sends both when
/// its layout or theme changes. The host answers with {type:"caption", right, height}: the room the caption buttons
/// take, in CSS pixels, so the bar leaves it free and matches their height.
/// </summary>
internal sealed partial class ShellWindow
{
    /// <summary>Three caption buttons of 46 px and the tall caption height, until Windows reports its own.</summary>
    private const double CaptionWidth = 138;
    private const double CaptionHeight = 48;

    private string? _caption;

    private void ExtendIntoTitleBar()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // Without drag rectangles the whole title bar would drag, and the top bar's tabs and buttons couldn't be
        // pressed: nothing drags until the page has said where its bar is empty.
        titleBar.SetDragRectangles([new RectInt32(0, 0, 1, 1)]);
    }

    /// <summary>The caption buttons' room for the page script, so the first frame lays the bar out for them.</summary>
    private string CaptionScript() => _mode == ShellMode.Desktop ? $"window.__PLAYDEX_CAPTION__ = {Caption()}; " : "";

    private void SetTitleBar(JsonElement message)
    {
        if (_mode != ShellMode.Desktop)
        {
            return;
        }
        var titleBar = AppWindow.TitleBar;
        if (message.TryGetProperty("ink", out var inkValue) && inkValue.ValueKind == JsonValueKind.Array && inkValue.GetArrayLength() == 3)
        {
            var rgb = inkValue.EnumerateArray().Select(c => (byte)Math.Clamp(c.TryGetDouble(out var v) ? v : 0, 0, 255)).ToArray();
            Color Ink(byte alpha) => ColorHelper.FromArgb(alpha, rgb[0], rgb[1], rgb[2]);
            titleBar.ButtonForegroundColor = Ink(0xFF);
            titleBar.ButtonHoverForegroundColor = Ink(0xFF);
            titleBar.ButtonPressedForegroundColor = Ink(0xFF);
            titleBar.ButtonInactiveForegroundColor = Ink(0x80);
            titleBar.ButtonHoverBackgroundColor = Ink(0x1A);
            titleBar.ButtonPressedBackgroundColor = Ink(0x33);
        }

        var scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        var drag = new List<RectInt32>();
        if (message.TryGetProperty("drag", out var rects) && rects.ValueKind == JsonValueKind.Array)
        {
            foreach (var rect in rects.EnumerateArray())
            {
                double Number(string name) => rect.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) ? n : 0;
                var (x, y, width, height) = (Number("x"), Number("y"), Number("width"), Number("height"));
                if (width >= 1 && height >= 1)
                {
                    drag.Add(new RectInt32((int)Math.Round(x * scale), (int)Math.Round(y * scale), (int)Math.Round(width * scale), (int)Math.Round(height * scale)));
                }
            }
        }
        titleBar.SetDragRectangles(drag.Count > 0 ? [.. drag] : [new RectInt32(0, 0, 1, 1)]);

        // The caption buttons' room changes with the display's scale; tell the page when it does.
        var caption = Caption();
        if (caption != _caption)
        {
            _caption = caption;
            Post($$"""{"type":"caption",{{caption[1..]}}""");
        }
    }

    /// <summary>{"right":…,"height":…}: the caption buttons' width and height in CSS pixels.</summary>
    private string Caption()
    {
        var titleBar = AppWindow.TitleBar;
        var scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        var right = titleBar.RightInset > 0 ? titleBar.RightInset / scale : CaptionWidth;
        var height = titleBar.Height > 0 ? titleBar.Height / scale : CaptionHeight;
        return FormattableString.Invariant($$"""{"right":{{Math.Ceiling(right)}},"height":{{Math.Round(height)}}}""");
    }
}
