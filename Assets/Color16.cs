namespace SwMapRenderer.Assets;

/// <summary>
/// UO stores every image as 16-bit A1R5G5B5. This expands to straight (non-premultiplied)
/// 0xAABBGGRR, matching the byte order ImageSharp's Rgba32 expects.
/// </summary>
public static class Color16
{
    /// <summary>Expands a 5-bit channel to 8 bits by replicating the high bits.</summary>
    private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));

    public static uint ToRgba(ushort c, byte alpha)
    {
        uint r = Expand5((c >> 10) & 0x1F);
        uint g = Expand5((c >> 5) & 0x1F);
        uint b = Expand5(c & 0x1F);
        return r | (g << 8) | (b << 16) | ((uint)alpha << 24);
    }

    /// <summary>
    /// Art and texmaps use colour 0x0000 as the transparency key. Fully black opaque pixels
    /// are simply not present in the original assets, so keying on zero is lossless.
    /// </summary>
    public static uint ToRgbaKeyed(ushort c) => c == 0 ? 0u : ToRgba(c, 255);

    public static uint ToRgbaOpaque(ushort c) => ToRgba(c, 255);
}
