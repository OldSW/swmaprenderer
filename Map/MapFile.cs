using SwMapRenderer.Assets;

namespace SwMapRenderer.Map;

/// <summary>
/// map{N}.mul -- the terrain layer. A flat array of 8x8 blocks, each a 4-byte header followed
/// by 64 cells of (ushort id, sbyte z). Blocks are stored column-major: index = bx * blockHeight + by.
///
/// Blocks are decoded on demand and cached, because a render only ever touches the view range
/// plus a margin, not the whole 86MB facet.
/// </summary>
public sealed class MapFile : IDisposable
{
    private const int CellSize = 3;
    private const int BlockSize = 4 + 64 * CellSize;

    private readonly DataFile _data;
    private readonly BlockCache<LandTile[]> _blocks;

    public MapDimensions Dimensions { get; }

    public int Width => Dimensions.Width;
    public int Height => Dimensions.Height;

    public MapFile(string path, MapDimensions dimensions, int cachedBlocks)
    {
        _data = new DataFile(path);
        Dimensions = dimensions;
        _blocks = new BlockCache<LandTile[]>(cachedBlocks);
    }

    public bool IsValidX(int x) => x >= 0 && x < Width;
    public bool IsValidY(int y) => y >= 0 && y < Height;

    public ushort ClampX(int x) => (ushort)Math.Clamp(x, 0, Width - 1);
    public ushort ClampY(int y) => (ushort)Math.Clamp(y, 0, Height - 1);

    /// <summary>
    /// Fetches a terrain tile. Returns false outside the facet; callers that need a value
    /// regardless (normal and corner-height calculation at the map edge) substitute their own.
    /// </summary>
    public bool TryGetLandTile(int x, int y, out LandTile? tile)
    {
        if (!IsValidX(x) || !IsValidY(y))
        {
            tile = null;
            return false;
        }

        var block = GetBlock(x >> 3, y >> 3);
        if (block.Length == 0)
        {
            tile = null;
            return false;
        }

        tile = block[(y & 7) * 8 + (x & 7)];
        return true;
    }

    private LandTile[] GetBlock(int bx, int by) =>
        _blocks.GetOrAdd(bx * Dimensions.BlockHeight + by, DecodeBlock);

    private LandTile[] DecodeBlock(int index)
    {
        var span = _data.Span((long)index * BlockSize, BlockSize);
        if (span.IsEmpty)
            return [];

        int bx = index / Dimensions.BlockHeight;
        int by = index % Dimensions.BlockHeight;

        var tiles = new LandTile[64];
        for (int i = 0; i < 64; i++)
        {
            int offset = 4 + i * CellSize;
            ushort id = BitConverter.ToUInt16(span[offset..]);
            sbyte z = (sbyte)span[offset + 2];
            tiles[i] = new LandTile(id, (ushort)(bx * 8 + (i & 7)), (ushort)(by * 8 + (i >> 3)), z);
        }

        return tiles;
    }

    public void Dispose() => _data.Dispose();
}
