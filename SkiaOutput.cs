using SkiaSharp;

namespace SwMapRenderer;

/// <summary>
/// Reads and writes straight-RGBA pixels through SkiaSharp. Skia's WebP encoder is libwebp itself,
/// with no effort setting.
/// </summary>
internal static class SkiaOutput
{
    /// <summary>
    /// Decodes an image file to straight RGBA, whatever it holds (PNG, WebP, JPEG). Null,
    /// with a zero size, if Skia cannot read it.
    /// </summary>
    public static unsafe byte[]? Load(string path, out int width, out int height)
    {
        width = height = 0;

        using var codec = SKCodec.Create(path);
        if (codec == null)
            return null;

        // Asked for unpremultiplied, so a partly transparent pixel keeps its own colour rather
        // than being premultiplied by Skia and divided out again by the caller.
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new byte[info.Width * info.Height * 4];

        fixed (byte* p = pixels)
        {
            if (codec.GetPixels(info, (nint)p) != SKCodecResult.Success)
                return null;
        }

        width = info.Width;
        height = info.Height;
        return pixels;
    }

    public static unsafe void Save(byte[] rgba, int width, int height, string path, ImageFormat format, int quality)
    {
        // Straight alpha, which is what the renderer hands over; Skia is told so rather than
        // left to assume premultiplied.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        fixed (byte* pixels = rgba)
        {
            using var pixmap = new SKPixmap(info, (nint)pixels, width * 4);
            using SKData data = format.Name switch
            {
                "png" => pixmap.Encode(SKPngEncoderOptions.Default),
                "jpg" => pixmap.Encode(new SKJpegEncoderOptions(quality)),
                "webp" => pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality)),
                "webp-lossless" => pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100)),
                _ => throw new NotSupportedException($"SkiaSharp does not write '{format.Name}'."),
            } ?? throw new InvalidOperationException($"SkiaSharp could not encode the {format.Name} image.");

            using var file = File.Create(path);
            data.SaveTo(file);
        }
    }
}
