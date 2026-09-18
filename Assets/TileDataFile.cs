namespace SwMapRenderer.Assets;

[Flags]
public enum TileFlag : ulong
{
    None = 0,
    Background = 0x00000001,
    Weapon = 0x00000002,
    Transparent = 0x00000004,
    Translucent = 0x00000008,
    Wall = 0x00000010,
    Damaging = 0x00000020,
    Impassable = 0x00000040,
    Wet = 0x00000080,
    Unknown1 = 0x00000100,
    Surface = 0x00000200,
    Bridge = 0x00000400,
    Generic = 0x00000800,
    Window = 0x00001000,
    NoShoot = 0x00002000,
    ArticleA = 0x00004000,
    ArticleAn = 0x00008000,
    Internal = 0x00010000,
    Foliage = 0x00020000,
    PartialHue = 0x00040000,
    NoHouse = 0x00080000,
    Map = 0x00100000,
    Container = 0x00200000,
    Wearable = 0x00400000,
    LightSource = 0x00800000,
    Animation = 0x01000000,
    HoverOver = 0x02000000,
    NoDiagonal = 0x04000000,
    Armor = 0x08000000,
    Roof = 0x10000000,
    Door = 0x20000000,
    StairBack = 0x40000000,
    StairRight = 0x80000000,
}

public readonly struct LandTileData
{
    public readonly TileFlag Flags;
    public readonly ushort TexId;
    public readonly string Name;

    public LandTileData(TileFlag flags, ushort texId, string name)
    {
        Flags = flags;
        TexId = texId;
        Name = name;
    }

    public bool IsWet => (Flags & TileFlag.Wet) != 0;
    public bool IsImpassable => (Flags & TileFlag.Impassable) != 0;
}

public readonly struct StaticTileData
{
    public readonly TileFlag Flags;
    public readonly byte Weight;
    public readonly byte Layer;
    public readonly int Count;
    public readonly ushort AnimId;
    public readonly ushort Hue;
    public readonly ushort LightIndex;
    public readonly byte Height;
    public readonly string Name;

    public StaticTileData(TileFlag flags, byte weight, byte layer, int count, ushort animId, ushort hue,
        ushort lightIndex, byte height, string name)
    {
        Flags = flags;
        Weight = weight;
        Layer = layer;
        Count = count;
        AnimId = animId;
        Hue = hue;
        LightIndex = lightIndex;
        Height = height;
        Name = name;
    }

    public bool IsBackground => (Flags & TileFlag.Background) != 0;
    public bool IsSurface => (Flags & TileFlag.Surface) != 0;
    public bool IsPartialHue => (Flags & TileFlag.PartialHue) != 0;
    public bool IsTranslucent => (Flags & TileFlag.Translucent) != 0;
    public bool IsAnimated => (Flags & TileFlag.Animation) != 0;
    public bool IsLight => (Flags & TileFlag.LightSource) != 0;
    public bool IsFoliage => (Flags & TileFlag.Foliage) != 0;
}

public enum TileDataFormat
{
    /// <summary>Pick based on the file's length.</summary>
    Auto,
    /// <summary>Pre-7.0.9: 32-bit flags.</summary>
    Legacy,
    /// <summary>7.0.9 and later: 64-bit flags.</summary>
    Extended,
}

/// <summary>
/// tiledata.mul. Land data is always 512 blocks of 32 entries (0x0000-0x3FFF); the number of
/// static blocks varies with the client, so it is derived from the remaining bytes.
/// </summary>
public sealed class TileDataFile
{
    public const int LandCount = 0x4000;

    public LandTileData[] LandData { get; }
    public StaticTileData[] StaticData { get; }
    public TileDataFormat Format { get; }

    public TileDataFile(string path, TileDataFormat requestedFormat = TileDataFormat.Auto)
    {
        var raw = File.ReadAllBytes(path);
        Format = requestedFormat == TileDataFormat.Auto ? DetectFormat(raw.Length) : requestedFormat;

        int flagSize = Format == TileDataFormat.Extended ? 8 : 4;
        int landEntrySize = flagSize + 2 + 20;
        int landBlockSize = 4 + 32 * landEntrySize;
        int staticEntrySize = flagSize + 1 + 1 + 4 + 2 + 2 + 2 + 1 + 20;
        int staticBlockSize = 4 + 32 * staticEntrySize;

        int landBlocks = LandCount / 32;
        int landTotal = landBlocks * landBlockSize;
        if (raw.Length < landTotal)
            throw new InvalidDataException($"'{path}' is too short to hold land tile data ({raw.Length} bytes).");

        LandData = new LandTileData[LandCount];
        for (int id = 0; id < LandCount; id++)
        {
            int offset = (id / 32) * landBlockSize + 4 + (id % 32) * landEntrySize;
            var flags = (TileFlag)ReadFlags(raw, offset, flagSize);
            ushort texId = BitConverter.ToUInt16(raw, offset + flagSize);
            LandData[id] = new LandTileData(flags, texId, ReadName(raw, offset + flagSize + 2));
        }

        int staticBlocks = (raw.Length - landTotal) / staticBlockSize;
        StaticData = new StaticTileData[staticBlocks * 32];
        for (int id = 0; id < StaticData.Length; id++)
        {
            int offset = landTotal + (id / 32) * staticBlockSize + 4 + (id % 32) * staticEntrySize;
            var flags = (TileFlag)ReadFlags(raw, offset, flagSize);
            int p = offset + flagSize;
            StaticData[id] = new StaticTileData(
                flags,
                raw[p],
                raw[p + 1],
                BitConverter.ToInt32(raw, p + 2),
                BitConverter.ToUInt16(raw, p + 6),
                BitConverter.ToUInt16(raw, p + 8),
                BitConverter.ToUInt16(raw, p + 10),
                raw[p + 12],
                ReadName(raw, p + 13));
        }
    }

    /// <summary>
    /// The two layouts differ only in flag width, so the file length gives them away: the
    /// legacy layout leaves no remainder once the land section and whole static blocks are
    /// accounted for, and the extended layout does not (and vice versa).
    /// </summary>
    private static TileDataFormat DetectFormat(int length)
    {
        if (FitsLayout(length, 4)) return TileDataFormat.Legacy;
        if (FitsLayout(length, 8)) return TileDataFormat.Extended;

        throw new InvalidDataException(
            $"tiledata.mul has an unexpected length ({length} bytes) and matches neither the " +
            "legacy nor the extended layout. Set Render:TileDataFormat to override detection.");
    }

    private static bool FitsLayout(int length, int flagSize)
    {
        int landTotal = (LandCount / 32) * (4 + 32 * (flagSize + 2 + 20));
        int staticBlockSize = 4 + 32 * (flagSize + 1 + 1 + 4 + 2 + 2 + 2 + 1 + 20);
        int rest = length - landTotal;
        return rest > 0 && rest % staticBlockSize == 0;
    }

    private static ulong ReadFlags(byte[] raw, int offset, int size) =>
        size == 8 ? BitConverter.ToUInt64(raw, offset) : BitConverter.ToUInt32(raw, offset);

    private static string ReadName(byte[] raw, int offset)
    {
        var span = raw.AsSpan(offset, 20);
        int end = span.IndexOf((byte)0);
        if (end < 0) end = span.Length;
        return System.Text.Encoding.ASCII.GetString(span[..end]);
    }

    public LandTileData GetLand(ushort id) =>
        id < LandData.Length ? LandData[id] : default;

    public StaticTileData GetStatic(ushort id) =>
        id < StaticData.Length ? StaticData[id] : default;
}
