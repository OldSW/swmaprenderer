using System.Text.Json;
using System.Text.Json.Serialization;
using SwMapRenderer.Assets;

namespace SwMapRenderer.Map;

/// <summary>What <see cref="MobileOverlay.Load"/> made of the file, for the verbose log.</summary>
public readonly record struct MobileOverlayStats(
    int Mobiles,
    int Drawn,
    int Dressed,
    int Sprites,
    int OffMap,
    int Unresolved);

/// <summary>One animation frame in a mobile's stack, in paint order.</summary>
public readonly record struct MobileSprite(int File, int Index, bool Flip, ushort Hue, bool PartialHue);

/// <summary>A mobile resolved to the frames that draw it, standing on one tile.</summary>
public sealed class MobilePlacement(ushort x, ushort y, sbyte z, MobileSprite[] sprites)
{
    public ushort X { get; } = x;
    public ushort Y { get; } = y;
    public sbyte Z { get; } = z;

    /// <summary>The naked body first, then each worn layer over it.</summary>
    public MobileSprite[] Sprites { get; } = sprites;
}

/// <summary>
/// The shard's creatures and people, from a JSON export of the server's world.
///
/// Nothing alive is in the client files: statics are the scenery, and every mobile standing in
/// it belongs to the running shard. Each is drawn the way the client draws one standing still --
/// the first frame of its body's idle action, facing the way the export says, with whatever it
/// is wearing stacked over it in the client's own layer order.
///
/// Resolving all of that is done once here, at load: by the time a frame is drawn a mobile is
/// already a list of (file, entry) pairs into <see cref="AnimationFile"/>, so the render pass
/// does no lookups beyond decoding the frames themselves.
/// </summary>
public sealed class MobileOverlay
{
    /// <summary>
    /// How far above its tile's projected point a mobile's feet sit, in pixels.
    ///
    /// A static's art is anchored at the bottom corner of its tile; a mobile stands in the
    /// middle of one, half a tile -- 22 pixels -- further up the screen. The remaining three
    /// are the client's own nudge, kept so that a mobile lines up with the same scenery here
    /// as it does in the client.
    /// </summary>
    public const int FeetOffset = 25;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly List<MobilePlacement> EmptyCell = [];

    private readonly Dictionary<int, List<MobilePlacement>> _tiles;
    private readonly int _height;

    public MobileOverlayStats Stats { get; }

    /// <summary>Widest reach either side of a tile, and highest above it, over every frame used.</summary>
    public int MaxSpriteWidth { get; }
    public int MaxSpriteHeight { get; }

    private MobileOverlay(Dictionary<int, List<MobilePlacement>> tiles, int height,
        MobileOverlayStats stats, int maxWidth, int maxHeight)
    {
        _tiles = tiles;
        _height = height;
        Stats = stats;
        MaxSpriteWidth = maxWidth;
        MaxSpriteHeight = maxHeight;
    }

    /// <summary>
    /// Reads the export. As with the item overlay the file names no facet, so every mobile in
    /// it is placed on whichever map is being rendered.
    /// </summary>
    public static MobileOverlay Load(string path, MapDimensions dimensions, AnimationFile animations,
        BodyTables bodies, TileDataFile tileData, Action<string>? warn = null)
    {
        List<MobileRecord?>? records;

        try
        {
            using var stream = File.OpenRead(path);
            records = JsonSerializer.Deserialize<List<MobileRecord?>>(stream, JsonOptions);
        }
        catch (JsonException e)
        {
            string where = e.LineNumber is { } line ? $", at line {line + 1}" : string.Empty;

            throw new InvalidDataException(
                $"'{path}' is not a readable mobile list{where}. Expected an array of " +
                $"{{ body, hue, direction, position: {{ x, y, z }} }} objects.", e);
        }

        if (records == null)
            throw new InvalidDataException($"'{path}' holds null where an array of mobiles was expected.");

        return new Builder(dimensions, animations, bodies, tileData).Build(records, path, warn);
    }

    /// <summary>The mobiles standing on one tile. Never null; empty for tiles with none.</summary>
    public IReadOnlyList<MobilePlacement> Get(int x, int y) =>
        _tiles.TryGetValue(x * _height + y, out var list) ? list : EmptyCell;

    private sealed class Builder(MapDimensions dimensions, AnimationFile animations, BodyTables bodies,
        TileDataFile tileData)
    {
        private readonly List<MobileSprite> _sprites = [];
        private readonly Dictionary<int, List<MobilePlacement>> _tiles = [];

        private int _maxWidth;
        private int _maxHeight;
        private int _spriteCount;
        private int _dressed;

        public MobileOverlay Build(List<MobileRecord?> records, string path, Action<string>? warn)
        {
            int mobiles = 0, drawn = 0, offMap = 0, malformed = 0, unresolved = 0;

            foreach (var record in records)
            {
                if (record?.Position is not { } position)
                {
                    malformed++;
                    continue;
                }

                mobiles++;

                if (position.X < 0 || position.Y < 0 ||
                    position.X >= dimensions.Width || position.Y >= dimensions.Height)
                {
                    offMap++;
                    continue;
                }

                var sprites = Resolve(record);
                if (sprites.Length == 0)
                {
                    unresolved++;
                    continue;
                }

                var placement = new MobilePlacement(
                    (ushort)position.X, (ushort)position.Y,
                    (sbyte)Math.Clamp(position.Z, sbyte.MinValue, sbyte.MaxValue),
                    sprites);

                int key = position.X * dimensions.Height + position.Y;
                if (!_tiles.TryGetValue(key, out var list))
                    _tiles[key] = list = [];

                list.Add(placement);
                drawn++;
            }

            if (offMap > 0)
            {
                warn?.Invoke($"{offMap:N0} of the {mobiles:N0} mobiles in '{Path.GetFileName(path)}' stand " +
                             $"outside this {dimensions.Width}x{dimensions.Height} facet and were dropped. " +
                             $"The file names no facet, so check it belongs to the map being rendered.");
            }

            if (malformed > 0)
                warn?.Invoke($"{malformed:N0} entries in '{Path.GetFileName(path)}' carry no position and were skipped.");

            if (unresolved > 0)
            {
                warn?.Invoke($"{unresolved:N0} mobiles in '{Path.GetFileName(path)}' name a body the " +
                             $"anim files do not draw standing still and were skipped.");
            }

            var stats = new MobileOverlayStats(mobiles, drawn, _dressed, _spriteCount, offMap,
                malformed + unresolved);

            return new MobileOverlay(_tiles, dimensions.Height, stats, _maxWidth, _maxHeight);
        }

        /// <summary>
        /// One mobile's frames. Empty where the body has no idle art at all, which is the only
        /// case worth dropping the mobile over -- a missing sleeve is not.
        /// </summary>
        private MobileSprite[] Resolve(MobileRecord record)
        {
            int animDirection = BodyTables.AnimDirection(record.Direction, out bool flip);
            var type = bodies.TypeOf(record.Body);
            int action = BodyTables.StandAction(type);

            int bodyHue = record.Hue;
            var body = bodies.Locate(record.Body, action, animDirection, ref bodyHue);

            if (!Add(body, flip, bodyHue, partialHue: false))
                return [];

            if (type == AnimationType.Human && record.Equipment is { Count: > 0 })
                AddEquipment(record, action, animDirection, flip);

            var sprites = _sprites.ToArray();
            _sprites.Clear();

            _spriteCount += sprites.Length;
            if (sprites.Length > 1)
                _dressed++;

            return sprites;
        }

        private void AddEquipment(MobileRecord record, int action, int animDirection, bool flip)
        {
            // The order rules read the equipment art of every layer at once, so the whole set
            // has to be indexed before any of it can be drawn.
            Span<ushort> animIds = stackalloc ushort[PaperdollOrder.LayerCount];
            var worn = new EquipmentRecord?[PaperdollOrder.LayerCount];

            foreach (var item in record.Equipment!)
            {
                if (item == null || item.LayerId <= 0 || item.LayerId >= PaperdollOrder.LayerCount)
                    continue;

                if (worn[item.LayerId] != null)
                    continue;

                worn[item.LayerId] = item;
                animIds[item.LayerId] = AnimIdOf(item.ItemId);
            }

            // Gargoyles wear the same art women do.
            bool alternateTorso = record.Female || record.Body is 666 or 667;

            Span<Layer> order = stackalloc Layer[PaperdollOrder.LayerCount];
            int count = PaperdollOrder.BuildInWorld(animIds, alternateTorso, record.Direction & 7, order);

            for (int i = 0; i < count; i++)
            {
                if (worn[(int)order[i]] is not { } item)
                    continue;

                ushort animId = animIds[(int)order[i]];
                if (animId == 0)
                    continue;

                bodies.TryConvertEquipment(record.Body, animId, out int graphic, out int color);

                int hue = item.Hue;
                var lookup = bodies.Locate(graphic, action, animDirection, ref hue);
                bool partialHue = tileData.GetStatic((ushort)item.ItemId).IsPartialHue;

                // A substituted piece brings the colour that keeps it looking like the original.
                if (hue == 0 && color != 0)
                {
                    hue = color;
                    partialHue = false;
                }

                Add(lookup, flip, hue, partialHue);
            }
        }

        private ushort AnimIdOf(int itemId) =>
            itemId >= 0 && itemId < tileData.StaticData.Length
                ? tileData.StaticData[itemId].AnimId
                : (ushort)0;

        private bool Add(AnimationLookup lookup, bool flip, int hue, bool partialHue)
        {
            if (!lookup.Found || animations.Measure(lookup.File, lookup.Index) is not { } size)
                return false;

            _sprites.Add(new MobileSprite(lookup.File, lookup.Index, flip,
                (ushort)Math.Clamp(hue, 0, ushort.MaxValue), partialHue));

            // How far the frame reaches past its tile, which is what the view range is widened
            // by. A mirrored frame reaches the other way, so both edges count either way.
            int centerX = flip ? size.Width - size.CenterX : size.CenterX;
            int overhang = Math.Max(centerX, size.Width - centerX);

            _maxWidth = Math.Max(_maxWidth, overhang * 2);
            _maxHeight = Math.Max(_maxHeight, size.Height + FeetOffset + size.CenterY);

            return true;
        }
    }

    private sealed record MobileRecord(
        int Body,
        int Hue,
        int Direction,
        bool Female,
        PositionRecord? Position,
        List<EquipmentRecord?>? Equipment);

    private sealed record EquipmentRecord(int ItemId, int Hue, int LayerId);

    private sealed record PositionRecord(int X, int Y, int Z);
}
