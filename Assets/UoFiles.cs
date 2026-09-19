using SwMapRenderer.Map;

namespace SwMapRenderer.Assets;

/// <summary>
/// Opens and owns every client file a render needs. Stands in for CentrED's UOFileManager,
/// minus the pieces that only matter to an interactive editor (gumps, animations, sounds).
/// </summary>
public sealed class UoFiles : IDisposable
{
    public TileDataFile TileData { get; }
    public ArtFile Art { get; }
    public TexmapFile Texmaps { get; }
    public HuesFile Hues { get; }
    public MapFile Map { get; }
    public StaticsFile Statics { get; }
    public MapDimensions Dimensions { get; }

    private UoFiles(TileDataFile tileData, ArtFile art, TexmapFile texmaps, HuesFile hues, MapFile map,
        StaticsFile statics, MapDimensions dimensions)
    {
        TileData = tileData;
        Art = art;
        Texmaps = texmaps;
        Hues = hues;
        Map = map;
        Statics = statics;
        Dimensions = dimensions;
    }

    public static UoFiles Open(RenderOptions options, Action<string>? warn = null)
    {
        var resolver = new DataFolder(options.DataPath);

        int index = options.Map;
        string mapPath = resolver.Require($"map{index}.mul");

        MapDimensions dimensions;
        if (options.MapWidth > 0 && options.MapHeight > 0)
        {
            dimensions = new MapDimensions(options.MapWidth, options.MapHeight);
            long expected = (long)dimensions.BlockCount * (4 + 64 * 3);
            long actual = new FileInfo(mapPath).Length;
            if (expected != actual)
            {
                warn?.Invoke(
                    $"Configured dimensions {dimensions.Width}x{dimensions.Height} imply a {expected} byte " +
                    $"map{index}.mul, but the file is {actual} bytes. Rendering anyway; tiles may be misplaced.");
            }
        }
        else
        {
            if (!MapDimensions.TryResolve(index, new FileInfo(mapPath).Length, out dimensions, out var warning))
                throw new InvalidDataException(warning);

            if (warning != null)
                warn?.Invoke(warning);
        }

        long spriteBudget = (long)options.SpriteCacheMegabytes * 1024 * 1024;

        var tileData = new TileDataFile(resolver.Require("tiledata.mul"), options.TileDataFormat);
        var art = new ArtFile(resolver.Require("artidx.mul"), resolver.Require("art.mul"), spriteBudget);
        var texmaps = new TexmapFile(resolver.Require("texidx.mul"), resolver.Require("texmaps.mul"), spriteBudget);
        var hues = new HuesFile(resolver.Require("hues.mul"));
        var map = new MapFile(mapPath, dimensions, options.CachedBlocks);
        var statics = new StaticsFile(
            resolver.Require($"staidx{index}.mul"),
            resolver.Require($"statics{index}.mul"),
            dimensions,
            tileData,
            options.CachedBlocks);

        return new UoFiles(tileData, art, texmaps, hues, map, statics, dimensions);
    }

    public void Dispose()
    {
        Statics.Dispose();
        Map.Dispose();
        Texmaps.Dispose();
        Art.Dispose();
    }
}

/// <summary>
/// Resolves file names inside the client folder case-insensitively. Shipped clients mix cases
/// freely (map0.mul next to ANIM2.DEF), which only works by luck on a case-sensitive volume.
/// </summary>
public sealed class DataFolder
{
    private readonly string _path;
    private readonly Dictionary<string, string> _files;

    public DataFolder(string path)
    {
        _path = path;
        _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(path))
            _files[Path.GetFileName(file)] = file;
    }

    public bool TryGet(string name, out string fullPath) => _files.TryGetValue(name, out fullPath!);

    public string Require(string name)
    {
        if (TryGet(name, out var fullPath))
            return fullPath;

        throw new FileNotFoundException($"'{name}' not found in data folder '{_path}'.", name);
    }
}
