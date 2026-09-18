namespace SwMapRenderer.Map;

/// <summary>
/// Tile dimensions of a facet. Maps are stored as an array of 8x8 blocks with no header, so
/// the file itself only reveals the total block count -- the split into width and height has
/// to come from knowledge of the facet.
/// </summary>
public readonly record struct MapDimensions(int Width, int Height)
{
    public int BlockWidth => Width / 8;
    public int BlockHeight => Height / 8;
    public int BlockCount => BlockWidth * BlockHeight;

    /// <summary>Height in blocks is stable across client eras; width is what grew.</summary>
    private static readonly MapDimensions[] Defaults =
    [
        new(7168, 4096),   // 0 Felucca
        new(7168, 4096),   // 1 Trammel
        new(2304, 1600),   // 2 Ilshenar
        new(2560, 2048),   // 3 Malas
        new(1448, 1448),   // 4 Tokuno
        new(1280, 4096),   // 5 Ter Mur
    ];

    public static MapDimensions Default(int mapIndex) =>
        mapIndex >= 0 && mapIndex < Defaults.Length ? Defaults[mapIndex] : Defaults[0];

    /// <summary>
    /// Reconciles the expected dimensions with the actual file size. Maps have been resized
    /// across client versions (Felucca went from 6144 to 7168 tiles wide) while keeping their
    /// block height, so when the block count disagrees we trust the height and re-derive the
    /// width. Returns false when the file size cannot be explained that way at all.
    /// </summary>
    public static bool TryResolve(int mapIndex, long mapFileLength, out MapDimensions dimensions, out string? warning)
    {
        const int blockSize = 4 + 64 * 3;

        warning = null;
        dimensions = Default(mapIndex);

        if (mapFileLength % blockSize != 0)
        {
            warning = $"map{mapIndex}.mul length ({mapFileLength} bytes) is not a whole number of " +
                      $"{blockSize}-byte blocks; using default dimensions {dimensions.Width}x{dimensions.Height}.";
            return true;
        }

        long blocks = mapFileLength / blockSize;
        if (blocks == dimensions.BlockCount)
            return true;

        int blockHeight = dimensions.BlockHeight;
        if (blocks % blockHeight != 0)
        {
            warning = $"map{mapIndex}.mul holds {blocks} blocks, which is not a multiple of the expected " +
                      $"height of {blockHeight} blocks. Set Render:MapWidth and Render:MapHeight explicitly.";
            return false;
        }

        var resolved = new MapDimensions((int)(blocks / blockHeight) * 8, blockHeight * 8);
        warning = $"map{mapIndex}.mul is {resolved.Width}x{resolved.Height} tiles, not the expected " +
                  $"{dimensions.Width}x{dimensions.Height}; using the size derived from the file.";
        dimensions = resolved;
        return true;
    }
}
