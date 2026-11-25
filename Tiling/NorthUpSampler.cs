using SwMapRenderer.Rendering;

namespace SwMapRenderer.Tiling;

/// <summary>
/// Draws one north-up slice by resampling the isometric slices it corresponds to.
///
/// Turning the isometric plane by 45 degrees makes the tile grid axis-aligned, but the art stays
/// where it was drawn: a tree is still an upright sprite, now standing at 45 degrees. Rotating
/// the rendered pixels is therefore the one honest way to get a north-up pyramid without a second
/// projection, and it accepts that tilt.
///
/// Each north-up pixel is mapped back to the isometric plane and bilinearly sampled from a window
/// assembled out of isometric slices at the same level. Those slices are the very pixels the
/// isometric pyramid is cut from, kept in memory rather than read back from a file, so no lossy
/// encoding comes in between. A window is a whole-pixel offset onto the shared isometric canvas,
/// so the sampled pixels do not depend on which north-up slice asked for them. That is what keeps
/// neighbouring north-up slices agreeing along their shared edges.
///
/// A level above the deepest is handled the same way the isometric pyramid handles it: the slices
/// are rendered at that level, not averaged. Turning and scaling commute, so the isometric canvas
/// at a level is the deepest one scaled down about the same origin, and the sampling only has to
/// scale its positions to match.
///
/// Interpolation happens on premultiplied colour, for the same reason averaging does: straight
/// colour from a transparent pixel would fringe every sprite edge.
/// </summary>
public sealed class NorthUpSampler
{
    private readonly SliceGrid _north;
    private readonly SliceGrid _iso;
    private readonly int _size;
    private readonly byte[] _window;

    /// <param name="north">The north-up grid whose slices are drawn.</param>
    /// <param name="iso">The isometric grid whose slices are sampled. Its slice size must match.</param>
    public NorthUpSampler(SliceGrid north, SliceGrid iso)
    {
        if (north.SliceSize != iso.SliceSize)
            throw new ArgumentException("North-up and isometric slices must be the same size.");

        _north = north;
        _iso = iso;
        _size = north.SourceSize;
        _window = new byte[_size * _size * 4];
    }

    /// <summary>
    /// The isometric slices, at <paramref name="level"/>, that a north-up slice's window touches,
    /// inclusive. The caller must have them in hand before calling <see cref="Render"/>.
    /// </summary>
    public static (int X0, int Y0, int X1, int Y1) IsoSlicesFor(SliceGrid north, SliceGrid iso, int level,
        int sliceX, int sliceY)
    {
        var (cx0, cy0) = WindowOrigin(north, iso, level, sliceX, sliceY);
        int size = north.SourceSize, s = iso.SliceSize;

        return (FloorDiv(cx0, s), FloorDiv(cy0, s), FloorDiv(cx0 + size - 1, s), FloorDiv(cy0 + size - 1, s));
    }

    /// <summary>
    /// Fills <paramref name="output"/> with north-up slice (<paramref name="sliceX"/>,
    /// <paramref name="sliceY"/>) at <paramref name="level"/>, as straight-alpha RGBA. The
    /// <paramref name="isoSlice"/> callback gives the straight-alpha pixels of an isometric slice at
    /// that level, or null where there is nothing; it must answer for every slice
    /// <see cref="IsoSlicesFor"/> names. Returns false when every pixel came out transparent.
    /// </summary>
    public bool Render(int level, int sliceX, int sliceY, byte[] output, Func<int, int, byte[]?> isoSlice)
    {
        int slice = _north.SliceSize;
        int size = _size;
        var (cx0, cy0) = WindowOrigin(_north, _iso, level, sliceX, sliceY);

        Array.Clear(_window);
        CopyWindow(cx0, cy0, isoSlice);

        // A pixel at this level is this many native pixels, and its centre is where it is sampled.
        double f = 1.0 / _north.ScaleAtLevel(level);
        double sIso = _iso.ScaleAtLevel(level);
        double x0 = sliceX * (double)_north.PixelsPerSlice(level);
        double y0 = sliceY * (double)_north.PixelsPerSlice(level);
        double ox = _iso.OriginX, oy = _iso.OriginY;
        bool any = false;

        for (int j = 0; j < slice; j++)
        {
            double ny = y0 + (j + 0.5) * f;
            for (int i = 0; i < slice; i++)
            {
                var (px, py) = _north.IsoAt(x0 + (i + 0.5) * f, ny);

                // Into the isometric canvas at this level, then into the window's pixels. Pixel
                // (a, b) of the window has its centre at (a + 0.5, b + 0.5), so the taps are the two
                // pixels either side of the sample point.
                double u = sIso * (px + ox) - cx0 - 0.5, v = sIso * (py + oy) - cy0 - 0.5;
                int a = (int)Math.Floor(u), b = (int)Math.Floor(v);
                float fx = (float)(u - a), fy = (float)(v - b);

                float r = 0, g = 0, bl = 0, al = 0;
                Accumulate(_window, size, a, b, (1 - fx) * (1 - fy), ref r, ref g, ref bl, ref al);
                Accumulate(_window, size, a + 1, b, fx * (1 - fy), ref r, ref g, ref bl, ref al);
                Accumulate(_window, size, a, b + 1, (1 - fx) * fy, ref r, ref g, ref bl, ref al);
                Accumulate(_window, size, a + 1, b + 1, fx * fy, ref r, ref g, ref bl, ref al);

                int o = (j * slice + i) * 4;
                if (al > 0f)
                {
                    float inv = 1f / al;
                    output[o] = ToByte(r * inv);
                    output[o + 1] = ToByte(g * inv);
                    output[o + 2] = ToByte(bl * inv);
                }
                else
                {
                    output[o] = output[o + 1] = output[o + 2] = 0;
                }

                output[o + 3] = ToByte(al);
                any |= output[o + 3] != 0;
            }
        }

        return any;
    }

    /// <summary>
    /// Copies the isometric slices into the window. Each slice is the same canvas shifted by a
    /// whole number of slices, so a row of its pixels lands in the window unchanged.
    /// </summary>
    private void CopyWindow(int cx0, int cy0, Func<int, int, byte[]?> isoSlice)
    {
        int s = _iso.SliceSize, size = _size;
        int ix0 = FloorDiv(cx0, s), iy0 = FloorDiv(cy0, s);
        int ix1 = FloorDiv(cx0 + size - 1, s), iy1 = FloorDiv(cy0 + size - 1, s);

        for (int iy = iy0; iy <= iy1; iy++)
        {
            for (int ix = ix0; ix <= ix1; ix++)
            {
                var source = isoSlice(ix, iy);
                if (source == null)
                    continue;

                // The part of this isometric slice that falls inside the window, in canvas pixels.
                int left = Math.Max(cx0, ix * s), right = Math.Min(cx0 + size, ix * s + s);
                int top = Math.Max(cy0, iy * s), bottom = Math.Min(cy0 + size, iy * s + s);
                if (left >= right || top >= bottom)
                    continue;

                int run = (right - left) * 4;
                for (int y = top; y < bottom; y++)
                {
                    int from = ((y - iy * s) * s + (left - ix * s)) * 4;
                    int to = ((y - cy0) * size + (left - cx0)) * 4;
                    Buffer.BlockCopy(source, from, _window, to, run);
                }
            }
        }
    }

    /// <summary>
    /// The window's origin in isometric canvas pixels at <paramref name="level"/>: the whole-pixel
    /// corner under the north-up slice's bounding square, with a margin for the bilinear taps.
    /// </summary>
    private static (int X, int Y) WindowOrigin(SliceGrid north, SliceGrid iso, int level, int sliceX, int sliceY)
    {
        double s = iso.ScaleAtLevel(level);
        double per = north.PixelsPerSlice(level);
        double x0 = sliceX * per, y0 = sliceY * per;

        // The slice's four corners give the extremes, since the map from one to the other is a rotation.
        double minX = double.MaxValue, minY = double.MaxValue;
        foreach (var (cx, cy) in new[] { (x0, y0), (x0 + per, y0), (x0, y0 + per), (x0 + per, y0 + per) })
        {
            var (px, py) = north.IsoAt(cx, cy);
            minX = Math.Min(minX, s * (px + iso.OriginX));
            minY = Math.Min(minY, s * (py + iso.OriginY));
        }

        return ((int)Math.Floor(minX) - SliceGrid.SourceMargin, (int)Math.Floor(minY) - SliceGrid.SourceMargin);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>
    /// Adds one tap to the premultiplied sums. Straight-alpha source bytes are premultiplied
    /// here, so the sums are of colour times alpha and of alpha alone.
    /// </summary>
    private static void Accumulate(byte[] src, int size, int x, int y, float weight,
        ref float r, ref float g, ref float b, ref float a)
    {
        if (weight == 0f)
            return;

        int p = (y * size + x) * 4;
        float alpha = src[p + 3] / 255f;
        float w = weight * alpha;

        r += src[p] / 255f * w;
        g += src[p + 1] / 255f * w;
        b += src[p + 2] / 255f * w;
        a += w;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}
