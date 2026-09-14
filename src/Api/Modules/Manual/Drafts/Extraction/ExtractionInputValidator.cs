using Api.Infrastructure.Http;
using SkiaSharp;

namespace Api.Modules.Manual.Drafts.Extraction;

public static class ExtractionInputValidator
{
    public static void Validate(ExtractionInput input, AiDraftOptions options)
    {
        if (string.IsNullOrWhiteSpace(input.Text) && input.Image is null)
            throw new AppProblemException(400, "Enter text or attach one screenshot.");
        if (input.Text?.Length > options.MaxTextCharacters)
            throw new AppProblemException(400, $"Text cannot exceed {options.MaxTextCharacters} characters.");
        if (input.Image is null) return;
        if (input.Image.Length == 0 || input.Image.Length > options.MaxImageBytes)
            throw new AppProblemException(400, $"Screenshot must contain between 1 and {options.MaxImageBytes} bytes.");
        var format = input.ImageContentType?.ToLowerInvariant() switch
        {
            "image/png" => SKEncodedImageFormat.Png,
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/webp" => SKEncodedImageFormat.Webp,
            _ => throw new AppProblemException(400, "Use a PNG, JPEG, or WebP screenshot."),
        };
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
}
