using SwMapRenderer.Assets;

namespace SwMapRenderer.Map;

/// <summary>
/// staidx{N}.mul + statics{N}.mul -- everything standing on the terrain. The index is one
/// 12-byte record per map block (same column-major ordering as map{N}.mul) pointing at a run
/// of 7-byte static records.
/// </summary>
public sealed class StaticsFile : IDisposable
{
    private const int RecordSize = 7;

    private readonly IndexedMulFile _file;
    private readonly TileDataFile _tileData;
    private readonly MapDimensions _dimensions;
    private readonly Dictionary<int, List<StaticTile>[]> _blocks = new();

    private static readonly List<StaticTile> EmptyCell = [];

    public StaticsFile(string indexPath, string dataPath, MapDimensions dimensions, TileDataFile tileData)
    {
        _file = new IndexedMulFile(indexPath, dataPath);
        _dimensions = dimensions;
        _tileData = tileData;
    }

    /// <summary>
    /// Statics at one map cell, ordered back to front. Never null; empty for cells with nothing on them.
    /// </summary>
    public List<StaticTile> Get(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _dimensions.Width || y >= _dimensions.Height)
            return EmptyCell;

        var block = GetBlock(x >> 3, y >> 3);
        return block[(y & 7) * 8 + (x & 7)] ?? EmptyCell;
    }

    private List<StaticTile>[] GetBlock(int bx, int by)
    {
        int index = bx * _dimensions.BlockHeight + by;
        if (_blocks.TryGetValue(index, out var cached))
            return cached;

        var cells = new List<StaticTile>[64];
        var span = _file.GetData(index);

        for (int offset = 0; offset + RecordSize <= span.Length; offset += RecordSize)
        {
            ushort id = BitConverter.ToUInt16(span[offset..]);
            byte localX = span[offset + 2];
            byte localY = span[offset + 3];
            sbyte z = (sbyte)span[offset + 4];
            ushort hue = BitConverter.ToUInt16(span[(offset + 5)..]);

            // Records with out-of-range local coordinates exist in shipped client files.
            if (localX > 7 || localY > 7)
                continue;

            var tile = new StaticTile(id, (ushort)(bx * 8 + localX), (ushort)(by * 8 + localY), z, hue);
            (cells[localY * 8 + localX] ??= []).Add(tile);
        }

        SortCells(cells);
        _blocks[index] = cells;
        return cells;
    }

    /// <summary>
    /// Ported from CentrED's StaticBlock.SortTiles: order each cell by PriorityZ, then number
    /// the tiles downwards from the cell count. CellIndex becomes a depth bias in the vertex
    /// data, so the first (lowest priority) tile is pushed furthest back.
    /// </summary>
    private void SortCells(List<StaticTile>[] cells)
    {
        foreach (var cell in cells)
        {
            if (cell == null)
                continue;

            foreach (var tile in cell)
                tile.UpdatePriority(_tileData.GetStatic(tile.Id));

            cell.Sort(static (a, b) => a.PriorityZ.CompareTo(b.PriorityZ));

            int i = cell.Count;
            foreach (var tile in cell)
                tile.CellIndex = i--;
        }
    }

    public void Dispose() => _file.Dispose();
}
