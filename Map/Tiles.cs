namespace SwMapRenderer.Map;

/// <summary>One cell of map{N}.mul.</summary>
public sealed class LandTile
{
    public readonly ushort Id;
    public readonly ushort X;
    public readonly ushort Y;
    public readonly sbyte Z;

    public LandTile(ushort id, ushort x, ushort y, sbyte z)
    {
        Id = id;
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>One 7-byte record of statics{N}.mul.</summary>
public sealed class StaticTile
{
    public readonly ushort Id;
    public readonly ushort X;
    public readonly ushort Y;
    public readonly sbyte Z;
    public readonly ushort Hue;

    /// <summary>
    /// Sort key within a cell. Ported from CentrED's StaticTile.UpdatePriority: background
    /// tiles drop below their Z and tiles with height rise above it, which is what keeps e.g.
    /// a floor underneath the wall standing on it.
    /// </summary>
    public int PriorityZ { get; private set; }

    /// <summary>
    /// Position within the cell's sorted list, counting down from the tile count. Scaled into a
    /// sub-pixel depth bias so co-located statics never z-fight.
    /// </summary>
    public int CellIndex { get; internal set; }

    public StaticTile(ushort id, ushort x, ushort y, sbyte z, ushort hue)
    {
        Id = id;
        X = x;
        Y = y;
        Z = z;
        Hue = hue;
        PriorityZ = z;
    }

    public void UpdatePriority(in Assets.StaticTileData tileData)
    {
        PriorityZ = Z;
        if (tileData.IsBackground)
            PriorityZ--;
        if (tileData.Height > 0)
            PriorityZ++;
    }
}
