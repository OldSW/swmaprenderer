using System.Text.Json;
using System.Text.Json.Serialization;
using SwMapRenderer.Assets;

namespace SwMapRenderer.Map;

/// <summary>What <see cref="ItemOverlay.Load"/> made of the file, for the verbose log.</summary>
public readonly record struct ItemOverlayStats(
    int Items,
    int Tiles,
    int Multis,
    int Designs,
    int OffMap,
    int Unresolved);

/// <summary>
/// Statics injected into a facet from a JSON export of a live shard.
///
/// statics{N}.mul holds the world as the client shipped it; everything players have since put
/// down -- locked-down furniture, decorations, the houses themselves -- lives only in the
/// server's save. Reading that export and folding it into the statics layer is what makes a map
/// rendered from client files look like the shard people actually play on.
///
/// Placements are grouped by map block and keyed exactly as <see cref="StaticsFile"/> keys its
/// own, so a block is looked up once and the extra tiles sort against the real ones -- a
/// locked-down chair resolves against the floor it stands on the same way a shipped chair does.
///
/// Customizable houses are the one kind of multi the client files cannot describe. multi.mul
/// holds only the blank foundation a house deed places; every wall, floor and roof the owner
/// built is in the server's design, so a record that carries one is expanded from that instead.
/// </summary>
public sealed class ItemOverlay
{
    /// <summary>One item resolved to facet coordinates, ready to become a <see cref="StaticTile"/>.</summary>
    private readonly record struct Placement(ushort Id, ushort X, ushort Y, sbyte Z, ushort Hue);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// A house design's own origin tile. It is the nodraw graphic and carries no art, the same
    /// marker <see cref="MultiFile"/> drops from a multi's component list.
    /// </summary>
    private const int DesignMarkerId = 1;

    private static readonly List<StaticTile> EmptyBlock = [];

    private readonly Dictionary<int, List<Placement>> _blocks;

    public ItemOverlayStats Stats { get; }

    private ItemOverlay(Dictionary<int, List<Placement>> blocks, ItemOverlayStats stats)
    {
        _blocks = blocks;
        Stats = stats;
    }

    /// <summary>
    /// Reads the export. The file carries no facet of its own, so every item in it is placed on
    /// whichever map is being rendered; pointing it at the wrong one scatters furniture at random.
    /// </summary>
    /// <param name="multis">Used to expand multi items. Without it they are skipped.</param>
    public static ItemOverlay Load(string path, MapDimensions dimensions, MultiFile? multis,
        Action<string>? warn = null)
    {
        List<ItemRecord?>? records;

        try
        {
            using var stream = File.OpenRead(path);
            records = JsonSerializer.Deserialize<List<ItemRecord?>>(stream, JsonOptions);
        }
        catch (JsonException e)
        {
            // The binder's own message names the CLR type it failed to build, which tells the
            // caller nothing about the file. The position in the file does.
            string where = e.LineNumber is { } line ? $", at line {line + 1}" : string.Empty;

            throw new InvalidDataException(
                $"'{path}' is not a readable item list{where}. Expected an array of " +
                $"{{ itemId, hue, multi, position: {{ x, y, z }} }} objects.", e);
        }

        if (records == null)
            throw new InvalidDataException($"'{path}' holds null where an array of items was expected.");

        return Build(records, dimensions, multis, path, warn);
    }

    private static ItemOverlay Build(List<ItemRecord?> records, MapDimensions dimensions, MultiFile? multis,
        string path, Action<string>? warn)
    {
        var blocks = new Dictionary<int, List<Placement>>();
        int items = 0, tiles = 0, multiCount = 0, designCount = 0, offMap = 0, malformed = 0, unknownMultis = 0;

        foreach (var record in records)
        {
            if (record?.Position is not { } position)
            {
                malformed++;
                continue;
            }

            items++;

            // A design supersedes the multi it was built on: its own tiles already include the
            // foundation, down to the graphics an owner who changed the foundation type picked,
            // so expanding multi.mul as well would lay the stock one back over the top.
            if (record.Design?.Tiles is { Count: > 0 } design)
            {
                designCount++;

                foreach (var tile in design)
                {
                    // Unlike a multi's components, design tiles are already in facet coordinates.
                    if (tile != null && tile.ItemId != DesignMarkerId)
                        Add(tile.ItemId, tile.X, tile.Y, tile.Z, record.Hue);
                }

                continue;
            }

            if (!record.Multi)
            {
                Add(record.ItemId, position.X, position.Y, position.Z, record.Hue);
                continue;
            }

            multiCount++;

            var components = multis?.Get(record.ItemId - MultiFile.IdOffset) ?? [];
            if (components.Count == 0)
            {
                unknownMultis++;
                continue;
            }

            foreach (var component in components)
            {
                Add(component.Id, position.X + component.X, position.Y + component.Y,
                    position.Z + component.Z, record.Hue);
            }
        }

        var stats = new ItemOverlayStats(items, tiles, multiCount, designCount, offMap, malformed + unknownMultis);

        if (offMap > 0)
        {
            warn?.Invoke($"{offMap:N0} of the {tiles + offMap:N0} tiles in '{Path.GetFileName(path)}' fall " +
                         $"outside this {dimensions.Width}x{dimensions.Height} facet and were dropped. " +
                         $"The file names no facet, so check it belongs to the map being rendered.");
        }

        if (malformed > 0)
            warn?.Invoke($"{malformed:N0} entries in '{Path.GetFileName(path)}' carry no position and were skipped.");

        if (unknownMultis > 0)
        {
            warn?.Invoke(multis == null
                ? $"{unknownMultis:N0} multi items were skipped: multi.idx/multi.mul are not in the data folder."
                : $"{unknownMultis:N0} multi items name an id that multi.mul does not define and were skipped.");
        }

        return new ItemOverlay(blocks, stats);

        void Add(int id, int x, int y, int z, int hue)
        {
            if (id is < 0 or > ushort.MaxValue)
            {
                offMap++;
                return;
            }

            if (x < 0 || y < 0 || x >= dimensions.Width || y >= dimensions.Height)
            {
                offMap++;
                return;
            }

            int index = (x >> 3) * dimensions.BlockHeight + (y >> 3);
            var placement = new Placement(
                (ushort)id, (ushort)x, (ushort)y,
                (sbyte)Math.Clamp(z, sbyte.MinValue, sbyte.MaxValue),
                (ushort)Math.Clamp(hue, 0, ushort.MaxValue));

            if (!blocks.TryGetValue(index, out var list))
                blocks[index] = list = [];

            list.Add(placement);
            tiles++;
        }
    }

    /// <summary>
    /// The overlay's tiles for one map block, freshly allocated. The statics cache clears itself
    /// wholesale under pressure, so a block can be decoded again while another thread is still
    /// drawing the previous copy; handing out shared tiles would let that re-decode rewrite the
    /// sort order underneath it.
    /// </summary>
    public List<StaticTile> Block(int index)
    {
        if (!_blocks.TryGetValue(index, out var placements))
            return EmptyBlock;

        var tiles = new List<StaticTile>(placements.Count);
        foreach (var p in placements)
            tiles.Add(new StaticTile(p.Id, p.X, p.Y, p.Z, p.Hue));

        return tiles;
    }

    private sealed record ItemRecord(int ItemId, int Hue, bool Multi, PositionRecord? Position,
        DesignRecord? Design);

    private sealed record PositionRecord(int X, int Y, int Z);

    /// <summary>
    /// A customizable house as its owner left it. The export's width, height and revision
    /// describe the plot and the edit that produced it; the tiles alone are what gets drawn.
    /// </summary>
    private sealed record DesignRecord(List<DesignTileRecord?>? Tiles);

    private sealed record DesignTileRecord(int ItemId, int X, int Y, int Z);
}
