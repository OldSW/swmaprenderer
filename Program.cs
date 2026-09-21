using Microsoft.Extensions.Configuration;
using SwMapRenderer;
using SwMapRenderer.Assets;
using SwMapRenderer.Map;
using SwMapRenderer.Rendering;
using SwMapRenderer.Tiling;

if (CommandLine.WantsHelp(args))
{
    Console.WriteLine(CommandLine.HelpText);
    return 0;
}

try
{
    var options = LoadOptions(args);
    options.Validate();

    using var files = UoFiles.Open(options, Warn);

    if (options.Verbose)
    {
        Console.WriteLine($"Data folder : {Path.GetFullPath(options.DataPath)}");
        Console.WriteLine($"Facet       : map{options.Map} ({files.Dimensions.Width}x{files.Dimensions.Height} tiles)");
        Console.WriteLine($"Tile data   : {files.TileData.Format} format, " +
                          $"{files.TileData.StaticData.Length} static entries");
        Console.WriteLine($"Hues        : {files.Hues.HueCount}");

        if (files.Items is { } items)
        {
            var s = items.Stats;
            string expanded = string.Join(", ", new[]
            {
                s.Multis > 0 ? $"{s.Multis:N0} multis" : null,
                s.Designs > 0 ? $"{s.Designs:N0} house designs" : null,
            }.Where(part => part != null));

            Console.WriteLine($"Items       : {s.Items:N0} from {Path.GetFullPath(options.Items!)} " +
                              $"-> {s.Tiles:N0} statics" +
                              (expanded.Length > 0 ? $" ({expanded} expanded)" : string.Empty) +
                              (s.OffMap + s.Unresolved > 0 ? $", {s.OffMap + s.Unresolved:N0} skipped" : string.Empty));
        }

        Console.WriteLine($"Centre      : {options.X},{options.Y}  zoom {options.Zoom:0.##}");
    }

    var scene = new MapScene(files, options);

    return options.IsTiling
        ? RunTiling(scene, options)
        : RunSingleImage(scene, options, files);
}
catch (OptionsException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Run with --help to see the available options.");
    return 2;
}
catch (FileNotFoundException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 3;
}
catch (Exception e) when (e is InvalidDataException or DirectoryNotFoundException or IOException
                              or UnauthorizedAccessException or NotSupportedException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 4;
}

static int RunSingleImage(MapScene scene, RenderOptions options, UoFiles files)
{
    if (!files.Map.IsValidX(options.X) || !files.Map.IsValidY(options.Y))
    {
        Warn($"Centre tile {options.X},{options.Y} lies outside map{options.Map} " +
             $"({files.Dimensions.Width}x{files.Dimensions.Height}); the view will be mostly empty.");
    }

    var renderer = new SceneRenderer(scene);
    var stats = renderer.Render(renderer.CentredProjection);

    Save(renderer.Output, options);

    if (options.Verbose)
    {
        Console.WriteLine($"View range  : {stats.Range.ApproximateCount} tiles " +
                          $"(A {stats.Range.AMin}..{stats.Range.AMax}, B {stats.Range.BMin}..{stats.Range.BMax})");
        Console.WriteLine($"Drawn       : {stats.LandDrawn} land, {stats.StaticsDrawn} statics");
        Console.WriteLine($"Render time : {stats.Elapsed.TotalMilliseconds:0} ms");
    }

    var format = options.ResolvedFormat;
    WarnIfAlphaWillBeLost(options, format);

    // An explicit --format wins over the extension, which is what you want when writing to a
    // pipe-ish name, and confusing when it silently disagrees with the name on disk.
    var byExtension = ImageOutput.FromPath(options.Output);
    if (byExtension != null && byExtension.Extension != format.Extension)
    {
        Warn($"writing {format.Name} into '{Path.GetFileName(options.Output)}', whose extension " +
             $"says {byExtension.Name}. --format wins; rename the output to match.");
    }

    Console.WriteLine($"Wrote {options.Width}x{options.Height} {format.Name} " +
                      $"to {Path.GetFullPath(options.Output)}");
    return 0;
}

/// <summary>
/// A transparent background asked of a format that cannot carry one is a silent surprise
/// otherwise: the image comes back opaque and the caller only finds out by looking.
/// </summary>
static void WarnIfAlphaWillBeLost(RenderOptions options, ImageFormat format)
{
    if (format.SupportsAlpha || options.BackgroundColor.W >= 1f)
        return;

    Console.Error.WriteLine($"warning: {format.Name} has no transparency, so the background was " +
                            $"flattened to opaque. Pass --background to choose the colour.");
}

static int RunTiling(MapScene scene, RenderOptions options)
{
    var files = scene.Files;
    var grid = new SliceGrid(
        files.Dimensions, options.SliceTiles, options.Zoom,
        files.Art.MaxStaticWidth, files.Art.MaxStaticHeight);

    string directory = Path.GetFullPath(options.Tiles!);
    var generator = new PyramidGenerator(scene, grid, directory);
    var plan = generator.Plan();

    var format = options.ResolvedFormat;

    Console.WriteLine($"Facet       : map{options.Map} ({grid.Map.Width}x{grid.Map.Height} tiles)");
    Console.WriteLine($"Format      : {format.Name}" +
                      (format.IsLossy ? $" at quality {options.Quality}" : " (lossless)"));
    Console.WriteLine($"Canvas      : {grid.CanvasWidth}x{grid.CanvasHeight} px at {options.Zoom:0.##}x " +
                      $"({grid.SliceSize}px slices of {grid.TilesPerSlice}x{grid.TilesPerSlice} tiles)");
    Console.WriteLine($"Region      : tiles {plan.Region.X0},{plan.Region.Y0} .. {plan.Region.X1},{plan.Region.Y1}");
    Console.WriteLine($"Levels      : {plan.MinZoom}..{plan.MaxZoom} of 0..{grid.MaxZoom} " +
                      $"(level {plan.MaxZoom} is {grid.TilePixelsAtLevel(plan.MaxZoom):0.##}px per tile)");
    Console.WriteLine($"Slices      : {plan.NativeSliceCount:N0} to render, {plan.TotalSliceCount:N0} in total");

    // Order of magnitude, from measured constants. Cost follows the slice's pixel area rather
    // than the slice count: --max-zoom changes how many slices there are, while --zoom changes
    // how big each one is, and both need to move the number.
    int threads = options.Threads > 0 ? options.Threads : Environment.ProcessorCount;
    Console.WriteLine($"Rough cost  : ~{Format(plan.Estimate(threads) * format.TimeFactor)} on {threads} threads, " +
                      $"~{FormatSize(plan.TotalSliceCount * plan.MegabytesPerSlice * format.SizeFactor)} on disk");

    if (plan.MaxZoomWasCapped)
    {
        Warn($"--max-zoom {options.MaxZoom} is deeper than this grid goes; using {plan.MaxZoom}. " +
             $"The number of levels follows --slice-tiles, not --zoom. To render finer than " +
             $"{grid.TilePixelsAtLevel(plan.MaxZoom):0.##}px per tile, raise --zoom.");
    }

    WarnIfAlphaWillBeLost(options, format);

    if (generator.DetectForeignFormat(plan) is { } foreign)
    {
        Warn($"{directory} already holds {foreign.Name} slices. A resume only looks for " +
             $"{format.Name}, so this run would render everything again and leave both formats " +
             $"side by side. Delete the directory, or pass --format {foreign.Name}.");
    }

    if (options.DryRun)
    {
        Console.WriteLine();
        Console.WriteLine("Dry run; nothing written. Narrow it with --region, or cap --max-zoom.");
        return 0;
    }

    Directory.CreateDirectory(directory);

    var result = generator.Generate(options.Verbose ? Console.WriteLine : null);
    string page = LeafletViewer.Write(grid, plan, options.Map, directory, options.ResolvedFormat);

    Console.WriteLine();
    Console.WriteLine($"Rendered {result.Rendered:N0} slices, downsampled {result.Downsampled:N0}, " +
                      $"skipped {result.SkippedEmpty:N0} empty and {result.SkippedExisting:N0} already present " +
                      $"in {Format(result.Elapsed)}.");
    // A file:// URI rather than the bare path: terminals turn it into something clickable, and
    // it survives the spaces and non-ASCII that a path picks up from --tiles.
    Console.WriteLine($"Open {new Uri(page).AbsoluteUri}");
    return 0;
}

static string FormatSize(double megabytes) =>
    megabytes >= 1024 ? $"{megabytes / 1024:N1} GB" : $"{megabytes:N0} MB";

static string Format(TimeSpan span) =>
    span.TotalHours >= 1 ? $"{span.TotalHours:0.#}h" :
    span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.#}m" :
    $"{span.TotalSeconds:0.#}s";

static RenderOptions LoadOptions(string[] args)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables("SWMAP_")
        .AddCommandLine(CommandLine.Normalize(args), CommandLine.SwitchMappings)
        .Build();

    try
    {
        return configuration.GetSection("Render").Get<RenderOptions>() ?? new RenderOptions();
    }
    catch (InvalidOperationException e)
    {
        // The binder reports an unparseable value by throwing, and its message names the
        // configuration path rather than the switch the user actually typed. Numbers are
        // parsed invariantly, so "0,5" is the common way to land here.
        throw new OptionsException([CommandLine.DescribeBindingFailure(e, configuration)]);
    }
}

static void Save(Rasterizer rasterizer, RenderOptions options)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(options.Output));
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

    var pixels = new byte[rasterizer.Width * rasterizer.Height * 4];
    rasterizer.CopyTo(pixels);

    ImageOutput.Save(pixels, rasterizer.Width, rasterizer.Height, options.Output,
        options.ResolvedFormat, options.Quality, options.BackgroundColor);
}

static void Warn(string message) => Console.Error.WriteLine($"warning: {message}");
