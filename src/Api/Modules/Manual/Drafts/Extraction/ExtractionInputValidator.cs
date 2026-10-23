using Api.Infrastructure.Http;
using SkiaSharp;

namespace Api.Modules.Manual.Drafts.Extraction;

public static class ExtractionInputValidator
{
    public static void Validate(ExtractionInput input, AiDraftOptions options)
    {
        ValidateRequest(input, options);
        ValidateImage(input, options);
    }

    /// <summary>Cheap checks on text length, screenshot size and declared type; nothing is decoded.</summary>
    public static void ValidateRequest(ExtractionInput input, AiDraftOptions options)
    {
        if (string.IsNullOrWhiteSpace(input.Text) && input.Image is null)
            throw new AppProblemException(400, "Enter text or attach one screenshot.");
        if (input.Text?.Length > options.MaxTextCharacters)
            throw new AppProblemException(400, $"Text cannot exceed {options.MaxTextCharacters} characters.");
        if (input.Image is null) return;
        if (input.Image.Length == 0 || input.Image.Length > options.MaxImageBytes)
            throw new AppProblemException(400, $"Screenshot must contain between 1 and {options.MaxImageBytes} bytes.");
        ExpectedFormat(input.ImageContentType);
    }

    /// <summary>
    /// Decodes the screenshot to prove it is one complete still image within the pixel limit. A full decode
    /// allocates the whole bitmap, so callers run it once, after the per-user lock and the quota check.
    /// </summary>
    public static void ValidateImage(ExtractionInput input, AiDraftOptions options)
    {
        if (input.Image is null) return;
        var format = ExpectedFormat(input.ImageContentType);
        using var data = SKData.CreateCopy(input.Image);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != format)
            throw new AppProblemException(400, "Screenshot content does not match a valid supported image.");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > options.MaxImagePixels)
            throw new AppProblemException(400, $"Screenshot cannot exceed {options.MaxImagePixels} pixels.");
        if (codec.FrameCount > 1)
            throw new AppProblemException(400, "Use a single still screenshot, not an animated image.");
        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new AppProblemException(400, "Screenshot could not be fully decoded.");
    }

    private static SKEncodedImageFormat ExpectedFormat(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => SKEncodedImageFormat.Png,
        "image/jpeg" => SKEncodedImageFormat.Jpeg,
        "image/webp" => SKEncodedImageFormat.Webp,
        _ => throw new AppProblemException(400, "Use a PNG, JPEG, or WebP screenshot."),
    };
}
