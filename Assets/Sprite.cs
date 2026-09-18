namespace SwMapRenderer.Assets;

/// <summary>
/// A decoded image from art.mul / texmaps.mul, in straight RGBA.
///
/// CentrED packs these into GPU texture atlases and addresses them through SpriteInfo.UV;
/// here each sprite is its own surface, so UVs always span the full [0,1] range. The vertex
/// generation code still goes through UVs rather than raw pixels, to keep it a faithful port.
/// </summary>
public sealed class Sprite
{
    public readonly int Width;
    public readonly int Height;
    public readonly uint[] Pixels;

    public Sprite(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public static readonly Sprite Empty = new(0, 0);

    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>
    /// Equivalent of a tex2D fetch through SamplerState.PointClamp: nearest texel, clamped
    /// addressing. UVs outside [0,1] are pinned to the edge rather than wrapped.
    /// </summary>
    public uint Sample(float u, float v)
    {
        int x = (int)(u * Width);
        int y = (int)(v * Height);

        if (x < 0) x = 0; else if (x >= Width) x = Width - 1;
        if (y < 0) y = 0; else if (y >= Height) y = Height - 1;

        return Pixels[y * Width + x];
    }
}
