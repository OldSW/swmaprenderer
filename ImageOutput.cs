using System.Numerics;

namespace SwMapRenderer;

/// <summary>
/// One of the image formats a render can be written as.
/// </summary>
/// <param name="Name">Canonical name, as given to --format.</param>
/// <param name="Extension">File extension, including the dot.</param>
/// <param name="SupportsAlpha">
/// Whether transparency survives. JPEG has none at all, so anything transparent has to be
/// flattened onto an opaque colour before encoding.
/// </param>
/// <param name="IsLossy">Whether <c>--quality</c> applies.</param>
public sealed record ImageFormat(string Name, string Extension, bool SupportsAlpha, bool IsLossy)
{
    // Size and time factors are measured on a level-8 pyramid of 600x500 tiles of Britain at
    // twelve threads, relative to PNG, and only feed the up-front estimate. PNG's zlib is the
    // slowest encoder here, so the lossless WebP and JPEG come out faster than it.
    public static readonly ImageFormat Png =
        new("png", ".png", SupportsAlpha: true, IsLossy: false);

    public static readonly ImageFormat Jpeg =
        new("jpg", ".jpg", SupportsAlpha: false, IsLossy: true) { SizeFactor = 0.22, TimeFactor = 0.76 };

    public static readonly ImageFormat Webp =
        new("webp", ".webp", SupportsAlpha: true, IsLossy: true) { SizeFactor = 0.24, TimeFactor = 1.08 };

    public static readonly ImageFormat WebpLossless =
        new("webp-lossless", ".webp", SupportsAlpha: true, IsLossy: false) { SizeFactor = 0.58, TimeFactor = 0.86 };

    public static readonly IReadOnlyList<ImageFormat> All = [Png, Jpeg, Webp, WebpLossless];

    /// <summary>
    /// Size relative to PNG for this renderer's content, from measurements on real slices. Used
    /// only to keep the up-front disk estimate honest across formats.
    /// </summary>
    public double SizeFactor { get; init; } = 1.0;

    /// <summary>Encoding time relative to PNG, from the same measurements.</summary>
    public double TimeFactor { get; init; } = 1.0;

    public override string ToString() => Name;
}

public static class ImageOutput
{
    /// <summary>Accepted spellings, including the ones people actually type.</summary>
    private static readonly Dictionary<string, ImageFormat> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = ImageFormat.Png,
        ["jpg"] = ImageFormat.Jpeg,
        ["jpeg"] = ImageFormat.Jpeg,
        ["webp"] = ImageFormat.Webp,
        ["webp-lossless"] = ImageFormat.WebpLossless,
        ["weblossless"] = ImageFormat.WebpLossless,
    };

    private static readonly Dictionary<string, ImageFormat> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ImageFormat.Png,
        [".jpg"] = ImageFormat.Jpeg,
        [".jpeg"] = ImageFormat.Jpeg,
        [".webp"] = ImageFormat.Webp,
    };

    public static string Names => string.Join(", ", ImageFormat.All.Select(f => f.Name));

    public static ImageFormat? Resolve(string? name) =>
        name != null && ByName.TryGetValue(name.Trim(), out var format) ? format : null;

    /// <summary>The format a path's extension implies, or null if it is not one we write.</summary>
    public static ImageFormat? FromPath(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var format) ? format : null;

    /// <summary>
    /// Writes straight-RGBA pixels in the requested format.
    ///
    /// For a format without alpha the pixels are composited onto <paramref name="flattenTo"/>
    /// first. Skipping that would not merely lose the transparency: JPEG would encode whatever
    /// colour happened to sit under a fully transparent pixel, which for a discarded texel is
    /// black, and every sprite edge would gain a dark fringe.
    /// </summary>
    public static void Save(byte[] rgba, int width, int height, string path, ImageFormat format,
        int quality, Vector4 flattenTo)
    {
        if (!format.SupportsAlpha)
            Flatten(rgba, flattenTo);

        SkiaOutput.Save(rgba, width, height, path, format, quality);
    }

    /// <summary>As <see cref="Save"/>, but via a temporary file moved into place.</summary>
    public static void SaveAtomically(byte[] rgba, int width, int height, string path, ImageFormat format,
        int quality, Vector4 flattenTo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temp = path + ".part";

        // A run killed mid-write (a container restart, say) leaves its .part behind. Unlinking needs
        // only the directory's write permission, where re-creating the file in place needs the
        // file's own, which a leftover owned by another uid does not grant. No-op if absent.
        File.Delete(temp);

        Save(rgba, width, height, temp, format, quality, flattenTo);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Composites straight-alpha pixels onto an opaque colour, in place.
    /// The backdrop arrives premultiplied, which is how the rasterizer carries colours.
    /// </summary>
    private static void Flatten(byte[] rgba, Vector4 backdrop)
    {
        // Undo the premultiplication so the backdrop can be blended as a plain colour.
        float inv = backdrop.W > 0f ? 1f / backdrop.W : 0f;
        byte br = ToByte(backdrop.X * inv);
        byte bg = ToByte(backdrop.Y * inv);
        byte bb = ToByte(backdrop.Z * inv);

        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte a = rgba[i + 3];
            if (a == 255)
                continue;

            if (a == 0)
            {
                rgba[i] = br;
                rgba[i + 1] = bg;
                rgba[i + 2] = bb;
            }
            else
            {
                rgba[i] = Mix(rgba[i], br, a);
                rgba[i + 1] = Mix(rgba[i + 1], bg, a);
                rgba[i + 2] = Mix(rgba[i + 2], bb, a);
            }

            rgba[i + 3] = 255;
        }
    }

    private static byte Mix(byte source, byte backdrop, byte alpha) =>
        (byte)((source * alpha + backdrop * (255 - alpha) + 127) / 255);

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}
