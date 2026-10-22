using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SkiaSharp;

namespace Api.Modules.Notifications;

public sealed record NotificationCard(
    string Title,
    string Platform,
    bool Ended,
    string? Duration);

public sealed class NotificationRenderer(HttpClient httpClient, CoverUrlPolicy coverUrls)
{
    public const int Width = 1280;
    public const int Height = 1024;

    /// <summary>Largest cover download accepted; real capsules and posters are a few hundred kilobytes.</summary>
    public const int MaxCoverBytes = 5 * 1024 * 1024;

    /// <summary>Largest cover decoded (a 6000 x 4000 image); a small file can still declare a huge bitmap.</summary>
    public const long MaxCoverPixels = 24_000_000;

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
        if (!coverUrls.IsAllowed(capsuleUrl))
            throw new InvalidOperationException("The game cover is not on an allowed host.");
        var bytes = await DownloadCover(capsuleUrl, ct);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidOperationException("The game cover is not a supported image.");
        if ((long)codec.Info.Width * codec.Info.Height > MaxCoverPixels)
            throw new InvalidOperationException("The game cover is too large to render.");
        using var cover = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("The game cover could not be decoded.");
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

    private async Task<byte[]> DownloadCover(string capsuleUrl, CancellationToken ct)
    {
        // The client does not follow redirects on its own (NotificationsModule), so each hop is checked here.
        var url = new Uri(capsuleUrl);
        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        for (var redirects = 0; IsRedirect(response) && redirects < 3; redirects++)
        {
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidOperationException("The game cover redirect has no location.");
            url = new Uri(url, location);
            if (!coverUrls.IsAllowed(url.AbsoluteUri))
                throw new InvalidOperationException("The game cover redirects to a host that is not allowed.");
            response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        using var _ = response;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxCoverBytes)
            throw new InvalidOperationException("The game cover is too large to download.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxCoverBytes)
                throw new InvalidOperationException("The game cover is too large to download.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsRedirect(HttpResponseMessage response) => (int)response.StatusCode is >= 300 and < 400;

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
