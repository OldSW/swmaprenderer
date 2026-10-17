using System.Collections.Concurrent;
using System.Diagnostics;
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
    /// <summary>The slice size the cost constants below were measured at (16x16 tiles, 704x704).</summary>
    private const double ReferencePixels = 704.0 * 704.0;

    /// <summary>
    /// CPU-seconds one rendered slice takes, summed over every thread. Two terms matter and they
    /// move independently:
    ///
    ///   fill     -- the slice's pixels, which --zoom and --slice-tiles scale
    ///   geometry -- the tiles it covers, which --max-zoom scales
    ///
    /// A slice above the deepest level is the same number of pixels over four times the ground
    /// per step, so counting only pixels understates it badly: at level 5 of a level-9 grid a
    /// slice covers 65,536 tiles rather than 256, and takes over three times as long as a native
    /// one. Both constants were measured on 600x500 tiles of Britain (map 1), rendering only
    /// (--min-zoom equal to --max-zoom, so no averaging) at 2 threads, where the CPU time does
    /// not yet inflate with contention. The fill term is assumed to scale with the slice's pixel
    /// count; only 704x704 slices were measured.
    /// </summary>
    public double CpuSecondsPerRenderedSlice =>
        0.085 * Grid.SliceSize * Grid.SliceSize / ReferencePixels
        + 3.0e-6 * Grid.TilesCoveredPerSlice(MaxZoom);

    /// <summary>
    /// CPU-seconds one averaged slice takes: the 2x2 average plus encoding the result. The
    /// children are averaged from memory, so nothing is decoded.
    /// </summary>
    public double CpuSecondsPerAveragedSlice => 0.041 * Grid.SliceSize * Grid.SliceSize / ReferencePixels;

    /// <summary>Slices built by averaging rather than rendering.</summary>
    public int AveragedSliceCount => TotalSliceCount - NativeSliceCount;

    /// <summary>
    /// Megabytes one PNG slice takes on disk. Measured on land-dense terrain, so it overstates a
    /// facet that is mostly ocean; the other formats scale from it by <see cref="ImageFormat.SizeFactor"/>.
    /// </summary>
    public double MegabytesPerSlice => 1.5e-6 * (double)Grid.SliceSize * Grid.SliceSize;

    /// <summary>
    /// Wall-clock estimate, fitted to 30 runs at 2, 4 and 12 threads plus 9 more at 6, 8 and 10
    /// (levels 7 to 9, PNG) on an M2 Max, which has 8 performance cores and 4
    /// efficiency cores. The speedup has two regimes, and the knee is at the performance cores:
    ///
    ///   up to 8 threads  -- nearly linear, losing about 2% per extra thread (7.0x on eight)
    ///   past 8 threads   -- each thread lands on an efficiency core and adds about a fifth of
    ///                       a performance core (7.8x on twelve, 10% faster than eight for 17%
    ///                       more CPU and a third more memory)
    ///
    /// The knee is taken as two thirds of the logical cores, which is exactly right for the
    /// M-series Max and a guess anywhere else. The fixed second and a half is start-up: the
    /// client files, the shard overlays and the first slices before every thread is busy. Run
    /// back against the measurements it lands within -18% and +13% (mean -1%), with no trend in
    /// the thread count.
    /// </summary>
    public TimeSpan Estimate(int threads)
    {
        int fastCores = Math.Max(1, Environment.ProcessorCount * 2 / 3);
        double cpuSeconds = NativeSliceCount * CpuSecondsPerRenderedSlice
                            + AveragedSliceCount * CpuSecondsPerAveragedSlice;

        return TimeSpan.FromSeconds(1.5 + cpuSeconds / Speedup(Math.Max(1, threads), fastCores));
    }

    private static double Speedup(int threads, int fastCores)
    {
        static double Fast(int t) => t * Math.Max(0.5, 1 - 0.0217 * (t - 2));

        return threads <= fastCores
            ? (threads < 2 ? threads : Fast(threads))
            : Fast(fastCores) + 0.2 * (threads - fastCores);
    }
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

    public PyramidResult Generate(Action<string>? log = null, ProgressBar? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var plan = Plan();

        int rendered = 0, skippedEmpty = 0, skippedExisting = 0, downsampled = 0;

        // The deepest level is the only one that touches the client files.
        int threads = _options.Threads > 0 ? _options.Threads : Environment.ProcessorCount;
        var renderers = new ConcurrentBag<SceneRenderer>();

        // Z-order, so that the four slices under one parent are neighbours in the work list and
        // finish close together. Row order would leave a whole row of slices waiting in memory
        // for the row beneath it.
        var slices = new List<(int X, int Y)>();
        for (int y = plan.NativeSlices.Y0; y <= plan.NativeSlices.Y1; y++)
            for (int x = plan.NativeSlices.X0; x <= plan.NativeSlices.X1; x++)
                slices.Add((x, y));
        slices.Sort((a, b) => Morton(a.X, a.Y).CompareTo(Morton(b.X, b.Y)));

        int done = 0;

        // Every slice at every level is one tick, so the bar covers the averaged levels too
        // rather than sitting at 100% while they run.
        if (progress != null)
        {
            progress.SetTotal(slices.Count + Enumerable.Range(plan.MinZoom, Math.Max(plan.MaxZoom - plan.MinZoom, 0))
                .Sum(z => _grid.SlicesAcross(z) * _grid.SlicesDown(z)));
            progress.SetPhase($"levels {plan.MaxZoom}-{plan.MinZoom}");
        }

        var cascade = new Cascade(plan, progress);
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = threads };

        Parallel.ForEach(slices, parallelOptions, slice =>
        {
            string path = SlicePath(plan.MaxZoom, slice.X, slice.Y);
            var result = Child.Absent;

            if (!_options.Overwrite && File.Exists(path))
            {
                Interlocked.Increment(ref skippedExisting);
                result = Child.OnDisk;
            }
            else if (!_grid.SliceTouchesFacet(plan.MaxZoom, slice.X, slice.Y, _options.MinZ, _options.MaxZ,
                         _scene.Files.Art.MaxStaticWidth, _scene.Files.Art.MaxStaticHeight))
            {
                // Cheap geometric rejection, before a canvas is cleared for nothing.
                Interlocked.Increment(ref skippedEmpty);
            }
            else
            {
                if (!renderers.TryTake(out var renderer))
                    renderer = new SceneRenderer(_scene, _grid.SliceSize, _grid.SliceSize);

                try
                {
                    var projection = _grid.ProjectionFor(plan.MaxZoom, slice.X, slice.Y);
                    var stats = renderer.Render(projection);

                    if (stats.LandDrawn == 0 && stats.StaticsDrawn == 0 && stats.MobilesDrawn == 0)
                    {
                        // Off the edge of the facet. Leaving the file absent is what makes the
                        // viewer show nothing there rather than a black square.
                        Interlocked.Increment(ref skippedEmpty);
                    }
                    else
                    {
                        result = Child.InMemory(Save(renderer.Output, path));
                        Interlocked.Increment(ref rendered);
                    }
                }
                finally
                {
                    renderers.Add(renderer);
                }
            }

            // Reported on every path, empty or not: a parent waits for all four children.
            ReportProgress(ref done, slices.Count, log, progress);
            Report(cascade, plan.MaxZoom, slice.X, slice.Y, result);
        });

        log?.Invoke($"level {plan.MaxZoom}: {rendered} rendered, {skippedEmpty} empty, {skippedExisting} already present");

        for (int zoom = plan.MaxZoom - 1; zoom >= plan.MinZoom; zoom--)
        {
            int written = cascade.Written[zoom], existing = cascade.Existing[zoom];
            downsampled += written;
            skippedExisting += existing;
            log?.Invoke($"level {zoom}: {written} averaged from the level below, {existing} already present");
        }

        // Parents outside the region were never visited; the bar should still end full.
        progress?.Advance(Math.Max(0, progress.Total - progress.Done));

        stopwatch.Stop();
        return new PyramidResult(rendered, downsampled, skippedEmpty, skippedExisting, stopwatch.Elapsed);
    }

    private static long Morton(int x, int y)
    {
        long key = 0;
        for (int bit = 0; bit < 21; bit++)
            key |= ((long)(x >> bit & 1) << (2 * bit)) | ((long)(y >> bit & 1) << (2 * bit + 1));
        return key;
    }

    private static void ReportProgress(ref int done, int total, Action<string>? log, ProgressBar? progress)
    {
        progress?.Tick();
        int n = Interlocked.Increment(ref done);
        if (log != null && (n % 500 == 0 || n == total))
            log($"  {n}/{total} slices");
    }

    /// <summary>Where a slice's pixels are, as its parent will need them.</summary>
    private readonly record struct Child(byte[]? Pixels, bool IsOnDisk)
    {
        /// <summary>Not drawn and not written: nothing to average.</summary>
        public static readonly Child Absent = new(null, false);

        /// <summary>Written by an earlier run, or possibly so: read it back if it is there.</summary>
        public static readonly Child OnDisk = new(null, true);

        /// <summary>Just written; these are the pixels that were encoded, so nothing is decoded again.</summary>
        public static Child InMemory(byte[] pixels) => new(pixels, false);
    }

    /// <summary>A parent waiting for its four children, one per quadrant.</summary>
    private sealed class Node
    {
        public readonly object Gate = new();
        public readonly Child[] Slots = new Child[4];
        public int Reported;
    }

    /// <summary>
    /// The averaged levels, built bottom-up as the deepest level is rendered. A parent is
    /// averaged by whichever thread reports its fourth child, straight from the pixels that child
    /// was encoded from, and its own pixels go up the same way -- so no slice is decoded unless a
    /// previous run left it on disk, and none is kept longer than its siblings take to finish.
    /// </summary>
    private sealed class Cascade
    {
        public readonly ConcurrentDictionary<(int X, int Y), Node>[] Nodes;
        public readonly int[] Written, Existing;
        public readonly ProgressBar? Progress;
        public readonly (int X0, int Y0, int X1, int Y1)[] Active;
        public readonly int MinZoom;

        public Cascade(PyramidPlan plan, ProgressBar? progress)
        {
            Progress = progress;
            MinZoom = plan.MinZoom;
            Nodes = new ConcurrentDictionary<(int, int), Node>[plan.MaxZoom + 1];
            Written = new int[plan.MaxZoom + 1];
            Existing = new int[plan.MaxZoom + 1];
            Active = new (int, int, int, int)[plan.MaxZoom + 1];

            // The slices this run covers, at each level. Anything beside them may still be on
            // disk from an earlier run, and a parent at the edge has to average those in.
            for (int z = 0; z <= plan.MaxZoom; z++)
            {
                Nodes[z] = new();
                int d = plan.MaxZoom - z;
                var n = plan.NativeSlices;
                Active[z] = (n.X0 >> d, n.Y0 >> d, n.X1 >> d, n.Y1 >> d);
            }
        }
    }

    private void Report(Cascade c, int zoom, int x, int y, Child child)
    {
        while (zoom > c.MinZoom)
        {
            int parentZoom = zoom - 1, px = x >> 1, py = y >> 1;
            var node = c.Nodes[parentZoom].GetOrAdd((px, py), key => NewNode(c, parentZoom, key.X, key.Y));

            bool complete;
            lock (node.Gate)
            {
                node.Slots[(x & 1) | ((y & 1) << 1)] = child;
                complete = ++node.Reported == 4;
            }

            if (!complete)
                return;

            c.Nodes[parentZoom].TryRemove((px, py), out _);
            child = Average(c, node, parentZoom, px, py);
            (zoom, x, y) = (parentZoom, px, py);
        }
    }

    private static Node NewNode(Cascade c, int zoom, int px, int py)
    {
        var node = new Node();
        var (x0, y0, x1, y1) = c.Active[zoom + 1];

        for (int i = 0; i < 4; i++)
        {
            int cx = px * 2 + (i & 1), cy = py * 2 + (i >> 1);
            if (cx < x0 || cx > x1 || cy < y0 || cy > y1)
            {
                node.Slots[i] = Child.OnDisk;
                node.Reported++;
            }
        }

        return node;
    }

    /// <summary>
    /// Builds one parent from its four children. A parent with no children at all is left absent,
    /// so empty ocean does not grow files as it rises through the pyramid.
    /// </summary>
    private Child Average(Cascade c, Node node, int zoom, int px, int py)
    {
        c.Progress?.Tick();

        int size = _grid.SliceSize;
        string path = SlicePath(zoom, px, py);

        if (!_options.Overwrite && File.Exists(path))
        {
            Interlocked.Increment(ref c.Existing[zoom]);
            return Child.OnDisk;
        }

        var pixels = new byte[size * size * 4];
        bool any = false;

        for (int i = 0; i < 4; i++)
        {
            var slot = node.Slots[i];
            var data = slot.Pixels;

            if (data == null && slot.IsOnDisk)
            {
                string childPath = SlicePath(zoom + 1, px * 2 + (i & 1), py * 2 + (i >> 1));
                if (File.Exists(childPath))
                {
                    data = LoadChild(childPath, out int w, out int h);
                    if (w != size || h != size)
                        data = null;
                }
            }

            node.Slots[i] = default;
            if (data == null)
                continue;

            any = true;
            AverageInto(data, pixels, size, (i & 1) * (size / 2), (i >> 1) * (size / 2));
        }

        if (!any)
            return Child.Absent;

        ImageOutput.SaveAtomically(pixels, size, size, path, _format, _options.Quality, Backdrop);
        Interlocked.Increment(ref c.Written[zoom]);
        return Child.InMemory(pixels);
    }

    /// <summary>
    /// A child slice as straight RGBA bytes. Null, with a zero size, if the file cannot be decoded.
    /// </summary>
    private static byte[]? LoadChild(string path, out int width, out int height) =>
        SkiaOutput.Load(path, out width, out height);

    /// <summary>
    /// Averages each 2x2 block of the child into one pixel of the parent quadrant.
    ///
    /// Premultiplied before averaging: averaging straight alpha would drag the colour of fully
    /// transparent pixels into their neighbours, fringing every sprite edge.
    /// </summary>
    private static void AverageInto(byte[] child, byte[] parent, int size, int offsetX, int offsetY)
    {
        int half = size / 2;
        int stride = size * 4;

        for (int y = 0; y < half; y++)
        {
            int row0 = y * 2 * stride;
            int row1 = row0 + stride;
            int destY = offsetY + y;

            for (int x = 0; x < half; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;

                for (int i = 0; i < 2; i++)
                {
                    int p = (i == 0 ? row0 : row1) + x * 8;
                    int q = p + 4;

                    float pa = child[p + 3] / 255f, qa = child[q + 3] / 255f;
                    r += child[p] / 255f * pa + child[q] / 255f * qa;
                    g += child[p + 1] / 255f * pa + child[q + 1] / 255f * qa;
                    b += child[p + 2] / 255f * pa + child[q + 2] / 255f * qa;
                    a += pa + qa;
                }

                r /= 4f;
                g /= 4f;
                b /= 4f;
                a /= 4f;

                int o = ((destY * size) + offsetX + x) * 4;
                if (a > 0f)
                {
                    float inv = 1f / a;
                    parent[o] = ToByte(r * inv);
                    parent[o + 1] = ToByte(g * inv);
                    parent[o + 2] = ToByte(b * inv);
                }

                parent[o + 3] = ToByte(a);
            }
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

    /// <summary>Writes the slice and returns the pixels that were encoded.</summary>
    private byte[] Save(IRasterizer rasterizer, string path)
    {
        var pixels = new byte[rasterizer.Width * rasterizer.Height * 4];
        rasterizer.CopyTo(pixels);

        ImageOutput.SaveAtomically(pixels, rasterizer.Width, rasterizer.Height, path, _format,
            _options.Quality, Backdrop);
        return pixels;
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
