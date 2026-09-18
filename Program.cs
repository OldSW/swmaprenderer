using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SwMapRenderer;
using SwMapRenderer.Assets;
using SwMapRenderer.Map;
using SwMapRenderer.Rendering;

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
        Console.WriteLine($"Centre      : {options.X},{options.Y}  zoom {options.Zoom:0.##}");
    }

    if (!files.Map.IsValidX(options.X) || !files.Map.IsValidY(options.Y))
    {
        Warn($"Centre tile {options.X},{options.Y} lies outside map{options.Map} " +
             $"({files.Dimensions.Width}x{files.Dimensions.Height}); the view will be mostly empty.");
    }

    var scene = new MapScene(files, options);
    var renderer = new SceneRenderer(scene);
    var stats = renderer.Render();

    Save(renderer.Output, options.Output);

    if (options.Verbose)
    {
        Console.WriteLine($"View range  : {stats.MinTileX},{stats.MinTileY} .. {stats.MaxTileX},{stats.MaxTileY}");
        Console.WriteLine($"Drawn       : {stats.LandDrawn} land, {stats.StaticsDrawn} statics");
        Console.WriteLine($"Render time : {stats.Elapsed.TotalMilliseconds:0} ms");
    }

    Console.WriteLine($"Wrote {options.Width}x{options.Height} image to {Path.GetFullPath(options.Output)}");
    return 0;
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

static RenderOptions LoadOptions(string[] args)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables("SWMAP_")
        .AddCommandLine(CommandLine.Normalize(args), CommandLine.SwitchMappings)
        .Build();

    return configuration.GetSection("Render").Get<RenderOptions>() ?? new RenderOptions();
}

static void Save(Rasterizer rasterizer, string path)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

    var pixels = new byte[rasterizer.Width * rasterizer.Height * 4];
    rasterizer.CopyTo(pixels);

    using var image = Image.LoadPixelData<Rgba32>(pixels, rasterizer.Width, rasterizer.Height);

    // Format follows the extension; ImageSharp reports unsupported ones itself.
    image.Save(path);
}

static void Warn(string message) => Console.Error.WriteLine($"warning: {message}");
