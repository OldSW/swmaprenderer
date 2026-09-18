using System.Numerics;
using SwMapRenderer.Assets;

namespace SwMapRenderer.Rendering;

/// <summary>A vertex after projection and the viewport transform. X/Y are pixels, Z is [0,1] depth.</summary>
public struct ScreenVertex
{
    public float X;
    public float Y;
    public float Z;
    public Vector3 Texture;
    public Vector4 Hue;
    public Vector3 Normal;
}

/// <summary>
/// Software replacement for the fixed-function half of the GPU pipeline: triangle setup,
/// rasterization, depth testing and blending.
///
/// It only implements the states the ported renderer actually uses -- CullNone, PointClamp
/// sampling, a depth buffer compared with Less and written on pass, and premultiplied alpha
/// blending (which is what XNA/FNA's BlendState.AlphaBlend is).
///
/// Because the projection is orthographic, w is 1 everywhere and attribute interpolation can
/// be plain affine barycentric, with no perspective correction to get wrong.
/// </summary>
public sealed class Rasterizer
{
    public int Width { get; }
    public int Height { get; }

    private readonly Vector4[] _color;
    private readonly float[] _depth;

    public Rasterizer(int width, int height)
    {
        Width = width;
        Height = height;
        _color = new Vector4[width * height];
        _depth = new float[width * height];
    }

    public void Clear(Vector4 color)
    {
        Array.Fill(_color, color);
        Array.Fill(_depth, 1.0f);
    }

    public void DrawTriangle(in ScreenVertex v0, in ScreenVertex v1, in ScreenVertex v2, Sprite texture,
        MapEffect effect)
    {
        if (texture.IsEmpty)
            return;

        double area = Edge(v0.X, v0.Y, v1.X, v1.Y, v2.X, v2.Y);
        if (Math.Abs(area) < 1e-9)
            return;

        double invArea = 1.0 / area;

        int minX = Math.Max(0, (int)MathF.Floor(Min3(v0.X, v1.X, v2.X)));
        int maxX = Math.Min(Width - 1, (int)MathF.Ceiling(Max3(v0.X, v1.X, v2.X)));
        int minY = Math.Max(0, (int)MathF.Floor(Min3(v0.Y, v1.Y, v2.Y)));
        int maxY = Math.Min(Height - 1, (int)MathF.Ceiling(Max3(v0.Y, v1.Y, v2.Y)));

        if (minX > maxX || minY > maxY)
            return;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = y * Width;

            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;

                // The inside test is inclusive: a pixel centre lying exactly on an edge is
                // covered by both adjacent triangles rather than by neither. Drawing it twice
                // is harmless, because the interpolated depth along a shared edge is identical
                // and the second triangle then fails the Less test.
                double b0 = Edge(v1.X, v1.Y, v2.X, v2.Y, px, py) * invArea;
                if (b0 < 0) continue;

                double b1 = Edge(v2.X, v2.Y, v0.X, v0.Y, px, py) * invArea;
                if (b1 < 0) continue;

                double b2 = 1.0 - b0 - b1;
                if (b2 < 0) continue;

                float w0 = (float)b0;
                float w1 = (float)b1;
                float w2 = (float)b2;

                float depth = w0 * v0.Z + w1 * v1.Z + w2 * v2.Z;

                // Equivalent of near/far clipping, which the GPU would do before rasterizing.
                if (depth < 0f || depth > 1f)
                    continue;

                int index = row + x;
                if (depth >= _depth[index])
                    continue;

                PixelInput pin;
                pin.Texture = w0 * v0.Texture + w1 * v1.Texture + w2 * v2.Texture;
                pin.Hue = w0 * v0.Hue + w1 * v1.Hue + w2 * v2.Hue;
                pin.Normal = w0 * v0.Normal + w1 * v1.Normal + w2 * v2.Normal;

                if (!effect.Shade(texture, pin, out var source))
                    continue;

                // BlendState.AlphaBlend: dst = src + dst * (1 - src.a)
                var dest = _color[index];
                _color[index] = source + dest * (1f - source.W);

                _depth[index] = depth;
            }
        }
    }

    /// <summary>
    /// Signed area of the triangle (a, b, c), doubled. Positive and negative results are both
    /// accepted by the inside test, which is how CullNone is honoured.
    ///
    /// Evaluated in double precision deliberately. Screen coordinates run into the hundreds, so
    /// in single precision the subtraction of the two products loses almost all significance
    /// near an edge -- the result comes out around 1e-6 of the triangle's area instead of zero,
    /// with an arbitrary sign. When that sign went negative for both triangles sharing an edge,
    /// the pixel was dropped by both, and because the error varies with position the dropouts
    /// lined up into visible one-pixel seams across the terrain. Double precision evaluates the
    /// products exactly for these magnitudes, so the sign is trustworthy.
    /// </summary>
    private static double Edge(double ax, double ay, double bx, double by, double cx, double cy) =>
        (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    private static float Min3(float a, float b, float c) => MathF.Min(a, MathF.Min(b, c));

    private static float Max3(float a, float b, float c) => MathF.Max(a, MathF.Max(b, c));

    /// <summary>Copies the framebuffer out as 8-bit straight RGBA.</summary>
    public void CopyTo(Span<byte> rgba)
    {
        if (rgba.Length < _color.Length * 4)
            throw new ArgumentException("Destination buffer is too small.", nameof(rgba));

        for (int i = 0; i < _color.Length; i++)
        {
            var c = _color[i];
            int o = i * 4;
            rgba[o] = ToByte(c.X);
            rgba[o + 1] = ToByte(c.Y);
            rgba[o + 2] = ToByte(c.Z);
            rgba[o + 3] = ToByte(c.W);
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}
