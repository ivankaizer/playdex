using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SkiaSharp;

namespace Api.Modules.Notifications;

public sealed record NotificationCard(
    string Title,
    string Platform,
    bool Ended,
    string? Duration);

public sealed class NotificationRenderer(HttpClient httpClient)
{
    public const int Width = 1280;
    public const int Height = 1024;

    private static readonly Lazy<SKTypeface> RegularTypeface = new(() =>
        LoadEmbeddedFont("Resources.Fonts.SpaceGrotesk-Regular.ttf"));
    private static readonly Lazy<SKTypeface> BoldTypeface = new(() =>
        LoadEmbeddedFont("Resources.Fonts.SpaceGrotesk-Bold.ttf"));

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "SKTypeface managed by Lazy<T>; disposed on process exit")]
    private static SKTypeface LoadEmbeddedFont(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"Api.{resourceName}");
        return stream is null ? SKTypeface.Default : SKTypeface.FromStream(stream);
    }

    public static SKTypeface GetRegularTypeface() => RegularTypeface.Value;
    public static SKTypeface GetBoldTypeface() => BoldTypeface.Value;

    public async Task<byte[]> RenderAsync(
        NotificationCard card,
        string capsuleUrl,
        CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(capsuleUrl, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        using var cover = SKBitmap.Decode(bytes);
        if (cover is null)
            throw new InvalidOperationException("The game cover is not a supported image.");
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(10, 13, 18));
        DrawCover(canvas, cover);
        using (var overlay = new SKPaint { Color = new SKColor(10, 13, 18, 184) })
            canvas.DrawRect(0, 0, Width, Height, overlay);

        var panel = new SKRoundRect(new SKRect(64, 600, 1216, 960), 24, 24);
        using (var panelPaint = new SKPaint { Color = new SKColor(9, 12, 18, 220) })
            canvas.DrawRoundRect(panel, panelPaint);
        using (var border = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 20),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
        })
            canvas.DrawRoundRect(panel, border);

        var regular = RegularTypeface.Value;
        var bold = BoldTypeface.Value;
        DrawText(canvas, card.Ended ? "SESSION ENDED" : "SESSION STARTED",
            96, 666, 30, new SKColor(249, 216, 74), bold);
        DrawWrappedTitle(canvas, card.Title, bold);
        DrawText(canvas, $"Platform: {card.Platform}",
            96, 892, 30, new SKColor(247, 247, 242, 210), regular);
        if (card.Ended && !string.IsNullOrWhiteSpace(card.Duration))
        {
            DrawText(canvas, $"Duration: {card.Duration}",
                700, 892, 30, new SKColor(247, 247, 242, 210), regular);
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 92);
        return encoded.ToArray();
    }

    private static void DrawCover(SKCanvas canvas, SKBitmap cover)
    {
        var scale = Math.Max((float)Width / cover.Width, (float)Height / cover.Height);
        var sourceWidth = Width / scale;
        var sourceHeight = Height / scale;
        var source = new SKRect(
            (cover.Width - sourceWidth) / 2,
            (cover.Height - sourceHeight) / 2,
            (cover.Width + sourceWidth) / 2,
            (cover.Height + sourceHeight) / 2);
        canvas.DrawBitmap(
            cover,
            source,
            new SKRect(0, 0, Width, Height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            null);
    }

    private static void DrawWrappedTitle(SKCanvas canvas, string title, SKTypeface bold)
    {
        var size = title.Length > 35 ? 48 : 60;
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        using var measureFont = new SKFont(bold, size);
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (measureFont.MeasureText(candidate) > 1050 && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        if (current.Length > 0) lines.Add(current);
        foreach (var (line, index) in lines.Take(2).Select((line, index) => (line, index)))
        {
            DrawText(canvas, line, 96, 758 + index * 62, size,
                new SKColor(247, 247, 242), bold);
        }
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        float size,
        SKColor color,
        SKTypeface typeface)
    {
        using var font = new SKFont(typeface, size) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }
}
