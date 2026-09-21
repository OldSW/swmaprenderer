using System.Collections.Concurrent;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SwMapRenderer.Map;
using SwMapRenderer.Rendering;

namespace SwMapRenderer.Tiling;

public readonly record struct PyramidPlan(
    SliceGrid Grid,
    int MinZoom,
    int MaxZoom,
    int NativeSliceCount,
    int TotalSliceCount,
    (int X0, int Y0, int X1, int Y1) NativeSlices,
    (int X0, int Y0, int X1, int Y1) Region,
    bool MaxZoomWasCapped)
{
    /// <summary>
    /// Single-threaded seconds one slice takes, fitted to measurements on this renderer. Two
    /// terms matter and they move independently:
    ///
    ///   fill     -- the slice's pixel area, which --zoom scales
    ///   geometry -- the tiles it covers, which --max-zoom scales
    ///
    /// A slice above the deepest level is the same number of pixels over four times the ground
    /// per step, so counting only pixels understates it badly: at level 6 of a level-9 grid a
    /// slice covers 16,384 tiles rather than 256, and takes about twice as long as a native one.
    /// </summary>
    public double SerialSecondsPerSlice =>
        0.009
        + 3.3e-7 * (double)Grid.SliceSize * Grid.SliceSize
        + 8.8e-6 * Grid.TilesCoveredPerSlice(MaxZoom);

    /// <summary>Megabytes one slice takes on disk, from the same measurements.</summary>
    public double MegabytesPerSlice => 1.0e-6 * (double)Grid.SliceSize * Grid.SliceSize;

    /// <summary>
    /// Wall-clock estimate. Scaling is not linear in threads -- the measured speedup was about
    /// 7x on twelve -- so the thread count is discounted rather than taken at face value.
    /// </summary>
    public TimeSpan Estimate(int threads) =>
        TimeSpan.FromSeconds(NativeSliceCount * SerialSecondsPerSlice / Math.Max(1, threads * 0.65));
}

public readonly record struct PyramidResult(
    int Rendered,
    int Downsampled,
    int SkippedEmpty,
    int SkippedExisting,
    TimeSpan Elapsed);

/// <summary>
/// Writes a slippy-map tile pyramid for a facet: the deepest level is rendered slice by slice,
/// and each shallower level is built by averaging the four slices below it.
///
/// Shallower levels are downsampled rather than re-rendered at a smaller zoom. It is far
/// cheaper -- the whole pyramid above the deepest level costs a third as many pixels again --
/// and it looks better, because averaging four pixels antialiases the point-sampled art that
/// rendering at a small zoom would simply alias.
/// </summary>
public sealed class PyramidGenerator
{
    private readonly MapScene _scene;
    private readonly SliceGrid _grid;
    private readonly RenderOptions _options;
    private readonly string _outputDirectory;
    private readonly ImageFormat _format;

    public PyramidGenerator(MapScene scene, SliceGrid grid, string outputDirectory)
    {
        _scene = scene;
        _grid = grid;
        _options = scene.Options;
        _outputDirectory = outputDirectory;
        _format = _options.ResolvedFormat;
    }

    /// <summary>
    /// Slices already present in the output directory under a different extension, which a
    /// resume would silently ignore -- it only looks for the format it is writing now, so the
    /// run would render everything again and leave two pyramids interleaved on disk.
    /// </summary>
    public ImageFormat? DetectForeignFormat(PyramidPlan plan)
    {
        foreach (var candidate in ImageFormat.All)
        {
            if (candidate.Extension == _format.Extension)
                continue;

            for (int y = plan.NativeSlices.Y0; y <= Math.Min(plan.NativeSlices.Y1, plan.NativeSlices.Y0 + 24); y++)
            {
                for (int x = plan.NativeSlices.X0; x <= Math.Min(plan.NativeSlices.X1, plan.NativeSlices.X0 + 24); x++)
                {
                    if (File.Exists(SlicePath(plan.MaxZoom, x, y, candidate)))
                        return candidate;
                }
            }
        }

        return null;
    }

    public PyramidPlan Plan()
    {
        var region = ResolveRegion();
        var art = _scene.Files.Art;

        int maxZoom = _options.MaxZoom < 0 ? _grid.MaxZoom : Math.Min(_options.MaxZoom, _grid.MaxZoom);
        int minZoom = Math.Clamp(_options.MinZoom, 0, maxZoom);

        var native = _grid.SlicesForRegion(
            maxZoom, region.X0, region.Y0, region.X1, region.Y1, art.MaxStaticWidth, art.MaxStaticHeight);

        // Count the slices that can actually hold something rather than the whole bounding box.
        var art2 = _scene.Files.Art;
        int nativeCount = 0;
        for (int y = native.Y0; y <= native.Y1; y++)
        {
            for (int x = native.X0; x <= native.X1; x++)
            {
                if (_grid.SliceTouchesFacet(maxZoom, x, y, _options.MinZ, _options.MaxZ,
                        art2.MaxStaticWidth, art2.MaxStaticHeight))
                {
                    nativeCount++;
                }
            }
        }

        // Each shallower level covers the same ground with a quarter as many slices.
        int total = nativeCount;
        int levelCount = nativeCount;
        for (int z = maxZoom - 1; z >= minZoom; z--)
        {
            levelCount = (int)Math.Ceiling(levelCount / 4.0);
            total += levelCount;
        }

        return new PyramidPlan(_grid, minZoom, maxZoom, nativeCount, total, native, region,
            _options.MaxZoom > _grid.MaxZoom);
    }

    private (int X0, int Y0, int X1, int Y1) ResolveRegion()
    {
        var map = _grid.Map;
        if (_options.ParsedRegion is { } r)
        {
            return (Math.Clamp(r.X0, 0, map.Width - 1), Math.Clamp(r.Y0, 0, map.Height - 1),
                    Math.Clamp(r.X1, 0, map.Width - 1), Math.Clamp(r.Y1, 0, map.Height - 1));
        }

        return (0, 0, map.Width - 1, map.Height - 1);
    }

    public PyramidResult Generate(Action<string>? log = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var plan = Plan();

        int rendered = 0, skippedEmpty = 0, skippedExisting = 0, downsampled = 0;

        // The deepest level is the only one that touches the client files.
        int threads = _options.Threads > 0 ? _options.Threads : Environment.ProcessorCount;
        var renderers = new ConcurrentBag<SceneRenderer>();

        var slices = new List<(int X, int Y)>();
        for (int y = plan.NativeSlices.Y0; y <= plan.NativeSlices.Y1; y++)
            for (int x = plan.NativeSlices.X0; x <= plan.NativeSlices.X1; x++)
                slices.Add((x, y));

        int done = 0;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = threads };

        Parallel.ForEach(slices, parallelOptions, slice =>
        {
            string path = SlicePath(plan.MaxZoom, slice.X, slice.Y);

            if (!_options.Overwrite && File.Exists(path))
            {
                Interlocked.Increment(ref skippedExisting);
                ReportProgress(ref done, slices.Count, log);
                return;
            }

            // Cheap geometric rejection, before a canvas is cleared for nothing.
            if (!_grid.SliceTouchesFacet(plan.MaxZoom, slice.X, slice.Y, _options.MinZ, _options.MaxZ,
                    _scene.Files.Art.MaxStaticWidth, _scene.Files.Art.MaxStaticHeight))
            {
                Interlocked.Increment(ref skippedEmpty);
                ReportProgress(ref done, slices.Count, log);
                return;
            }

            if (!renderers.TryTake(out var renderer))
                renderer = new SceneRenderer(_scene, _grid.SliceSize, _grid.SliceSize);

            try
            {
                var projection = _grid.ProjectionFor(plan.MaxZoom, slice.X, slice.Y);
                var stats = renderer.Render(projection);

                if (stats.LandDrawn == 0 && stats.StaticsDrawn == 0)
                {
                    // Off the edge of the facet. Leaving the file absent is what makes the
                    // viewer show nothing there rather than a black square.
                    Interlocked.Increment(ref skippedEmpty);
                }
                else
                {
                    Save(renderer.Output, path);
                    Interlocked.Increment(ref rendered);
                }
            }
            finally
            {
                renderers.Add(renderer);
            }

            ReportProgress(ref done, slices.Count, log);
        });

        log?.Invoke($"level {plan.MaxZoom}: {rendered} rendered, {skippedEmpty} empty, {skippedExisting} already present");

        for (int zoom = plan.MaxZoom - 1; zoom >= plan.MinZoom; zoom--)
        {
            var (written, existing) = BuildLevel(zoom, parallelOptions);
            downsampled += written;
            skippedExisting += existing;
            log?.Invoke($"level {zoom}: {written} averaged from the level below, {existing} already present");
        }

        stopwatch.Stop();
        return new PyramidResult(rendered, downsampled, skippedEmpty, skippedExisting, stopwatch.Elapsed);
    }

    private static void ReportProgress(ref int done, int total, Action<string>? log)
    {
        int n = Interlocked.Increment(ref done);
        if (log != null && (n % 500 == 0 || n == total))
            log($"  {n}/{total} slices");
    }

    /// <summary>
    /// Builds one level by averaging each group of four slices from the level below. Groups with
    /// no children at all are left absent, so empty ocean does not grow files as it rises
    /// through the pyramid.
    /// </summary>
    private (int Written, int Existing) BuildLevel(int zoom, ParallelOptions parallelOptions)
    {
        int across = _grid.SlicesAcross(zoom);
        int down = _grid.SlicesDown(zoom);
        int size = _grid.SliceSize;

        var coords = new List<(int X, int Y)>();
        for (int y = 0; y < down; y++)
            for (int x = 0; x < across; x++)
                coords.Add((x, y));

        int written = 0;
        int existing = 0;

        Parallel.ForEach(coords, parallelOptions, slice =>
        {
            string path = SlicePath(zoom, slice.X, slice.Y);
            if (!_options.Overwrite && File.Exists(path))
            {
                Interlocked.Increment(ref existing);
                return;
            }

            // Premultiply before averaging. Averaging straight alpha would drag the colour of
            // fully transparent pixels into their neighbours, fringing every sprite edge.
            var accum = new float[size * size * 4];
            bool any = false;

            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    string childPath = SlicePath(zoom + 1, slice.X * 2 + dx, slice.Y * 2 + dy);
                    if (!File.Exists(childPath))
                        continue;

                    using var child = Image.Load<Rgba32>(childPath);
                    if (child.Width != size || child.Height != size)
                        continue;

                    any = true;
                    Accumulate(child, accum, size, dx * (size / 2), dy * (size / 2));
                }
            }

            if (!any)
                return;

            WriteAveraged(accum, size, path);
            Interlocked.Increment(ref written);
        });

        return (written, existing);
    }

    /// <summary>Averages each 2x2 block of the child into one pixel of the parent quadrant.</summary>
    private static void Accumulate(Image<Rgba32> child, float[] accum, int size, int offsetX, int offsetY)
    {
        int half = size / 2;

        child.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < half; y++)
            {
                var row0 = accessor.GetRowSpan(y * 2);
                var row1 = accessor.GetRowSpan(y * 2 + 1);
                int destY = offsetY + y;

                for (int x = 0; x < half; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;

                    for (int i = 0; i < 2; i++)
                    {
                        var p = i == 0 ? row0[x * 2] : row1[x * 2];
                        var q = i == 0 ? row0[x * 2 + 1] : row1[x * 2 + 1];

                        float pa = p.A / 255f, qa = q.A / 255f;
                        r += p.R / 255f * pa + q.R / 255f * qa;
                        g += p.G / 255f * pa + q.G / 255f * qa;
                        b += p.B / 255f * pa + q.B / 255f * qa;
                        a += pa + qa;
                    }

                    int o = ((destY * size) + offsetX + x) * 4;
                    accum[o] = r / 4f;
                    accum[o + 1] = g / 4f;
                    accum[o + 2] = b / 4f;
                    accum[o + 3] = a / 4f;
                }
            }
        });
    }

    private void WriteAveraged(float[] accum, int size, string path)
    {
        var pixels = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            int o = i * 4;
            float a = accum[o + 3];

            if (a > 0f)
            {
                float inv = 1f / a;
                pixels[o] = ToByte(accum[o] * inv);
                pixels[o + 1] = ToByte(accum[o + 1] * inv);
                pixels[o + 2] = ToByte(accum[o + 2] * inv);
            }

            pixels[o + 3] = ToByte(a);
        }

        ImageOutput.SaveAtomically(pixels, size, size, path, _format, _options.Quality, Backdrop);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

    private void Save(Rasterizer rasterizer, string path)
    {
        var pixels = new byte[rasterizer.Width * rasterizer.Height * 4];
        rasterizer.CopyTo(pixels);

        ImageOutput.SaveAtomically(pixels, rasterizer.Width, rasterizer.Height, path, _format,
            _options.Quality, Backdrop);
    }

    /// <summary>
    /// What a format without alpha flattens onto. The configured background if it is opaque,
    /// black otherwise -- a pyramid's background defaults to transparent, which JPEG cannot
    /// carry, and black is what the viewer's page shows behind absent slices anyway.
    /// </summary>
    private System.Numerics.Vector4 Backdrop =>
        _options.BackgroundColor.W >= 1f ? _options.BackgroundColor : new System.Numerics.Vector4(0, 0, 0, 1);

    private string SlicePath(int zoom, int x, int y) => SlicePath(zoom, x, y, _format);

    private string SlicePath(int zoom, int x, int y, ImageFormat format) =>
        Path.Combine(_outputDirectory, zoom.ToString(), x.ToString(), $"{y}{format.Extension}");
}
