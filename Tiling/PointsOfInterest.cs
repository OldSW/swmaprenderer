using System.Text.Json;
using System.Text.Json.Serialization;
using SwMapRenderer.Map;

namespace SwMapRenderer.Tiling;

/// <summary>One marker, in the canonical shape the viewer consumes.</summary>
public readonly record struct Poi(string Name, int X, int Y, int Z, string? Category, string? Description);

/// <summary>What <see cref="PointsOfInterest.Load"/> made of the file, for the run's log.</summary>
public readonly record struct PoiStats(
    int Records,
    int Kept,
    int OtherMap,
    int Unnamed,
    int NoPosition,
    int OffMap);

/// <summary>
/// Named places to mark on the browsable pyramid: towns, dungeons, shops -- whatever the shard
/// knows about that a rendered tile cannot say for itself.
///
/// These never reach the rasterizer. They are a DOM overlay on the viewer, which is what makes
/// them searchable and what lets them change without re-rendering a single slice.
///
/// The file names no facet of its own unless a record says otherwise: a record carrying a
/// <c>mapId</c> that disagrees with the map being rendered is dropped, and one carrying none is
/// placed on whichever map that is -- the same bargain <see cref="ItemOverlay"/> strikes, but
/// with an opt-out, because a POI list is usually a whole shard's rather than one facet's.
///
/// Several shapes are accepted, because the source is generally an export rather than something
/// written for this tool. A shard's region list is the useful one to hand: it carries a name, a
/// <c>go</c> point, and the rectangles the region covers, any of which can place a marker.
/// </summary>
public sealed class PointsOfInterest
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public IReadOnlyList<Poi> Items { get; }
    public PoiStats Stats { get; }

    private PointsOfInterest(IReadOnlyList<Poi> items, PoiStats stats)
    {
        Items = items;
        Stats = stats;
    }

    /// <summary>Whether the configured source is fetched by the page rather than read here.</summary>
    public static bool IsUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static PointsOfInterest Load(string path, int mapIndex, MapDimensions dimensions)
    {
        List<PoiRecord?> records = ReadRecords(path);

        var kept = new List<Poi>(records.Count);
        int otherMap = 0, unnamed = 0, noPosition = 0, offMap = 0;

        foreach (var record in records)
        {
            if (record == null)
                continue;

            if (record.Facet is { } facet && facet != mapIndex)
            {
                otherMap++;
                continue;
            }

            string? name = First(record.Name, record.Title, record.Label);
            if (name == null)
            {
                unnamed++;
                continue;
            }

            if (record.Place() is not var (x, y, z))
            {
                noPosition++;
                continue;
            }

            if (x < 0 || y < 0 || x >= dimensions.Width || y >= dimensions.Height)
            {
                offMap++;
                continue;
            }

            kept.Add(new Poi(name, x, y, z,
                First(record.Category, record.Type),
                First(record.Description, record.Desc)));
        }

        // Sorted so that the page this ends up embedded in is stable across runs: a resume that
        // re-renders nothing should leave index.html byte-identical too.
        kept.Sort(static (a, b) =>
        {
            int byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : a.X != b.X ? a.X - b.X : a.Y - b.Y;
        });

        return new PointsOfInterest(kept,
            new PoiStats(records.Count, kept.Count, otherMap, unnamed, noPosition, offMap));
    }

    private static List<PoiRecord?> ReadRecords(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            // An API's own response is as likely to wrap its array in an envelope as to return
            // it bare, and a file saved from one carries the envelope with it.
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var array = Unwrap(document.RootElement)
                        ?? throw new InvalidDataException(
                            $"'{path}' holds {Describe(document.RootElement)} where an array of points " +
                            $"of interest was expected, either bare or under a \"pois\" key.");

            return array.Deserialize<List<PoiRecord?>>(ReadOptions) ?? [];
        }
        catch (JsonException e)
        {
            // The binder's message names the CLR type it failed to build, which tells the caller
            // nothing about their file. The position in it does.
            string where = e.LineNumber is { } line ? $", at line {line + 1}" : string.Empty;

            throw new InvalidDataException(
                $"'{path}' is not a readable point-of-interest list{where}. Expected an array of " +
                $"{{ name, position: {{ x, y, z }} }} objects.", e);
        }
    }

    private static JsonElement? Unwrap(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (string key in (string[])["pois", "points", "items", "results", "data"])
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }

        return null;
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "an object with no array in it",
        JsonValueKind.Null => "null",
        _ => $"a {element.ValueKind.ToString().ToLowerInvariant()}",
    };

    /// <summary>First of the alternatives that carries anything, trimmed.</summary>
    private static string? First(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    /// <summary>
    /// One record as it sits in the file. Every spelling this accepts is a property here rather
    /// than a converter, so an unexpected key is ignored instead of failing the whole file.
    /// </summary>
    private sealed class PoiRecord
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Label { get; set; }

        public string? Category { get; set; }
        public string? Type { get; set; }

        public string? Description { get; set; }
        public string? Desc { get; set; }

        public int? MapId { get; set; }

        /// <summary>
        /// Held loosely because a region export names its facet here ("Felucca") while a
        /// purpose-built list numbers it. Only a number decides anything.
        /// </summary>
        public JsonElement? Map { get; set; }

        public PointRecord? Position { get; set; }

        /// <summary>Where a region export puts the spot a game master would teleport to.</summary>
        public PointRecord? Go { get; set; }

        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Z { get; set; }

        public List<RectRecord>? Coords { get; set; }

        public int? Facet =>
            MapId ?? (Map is { ValueKind: JsonValueKind.Number } m && m.TryGetInt32(out int n) ? n : null);

        /// <summary>
        /// Where the marker goes, from whichever of the four spellings the record carries.
        /// Ordered by how deliberate each one is: an explicit position beats a teleport target,
        /// which beats the middle of the area the record covers.
        /// </summary>
        public (int X, int Y, int Z)? Place() =>
            Usable(Position) ?? Usable(Go) ?? Usable(Loose()) ?? Centre();

        private PointRecord? Loose() => X is { } x && Y is { } y ? new PointRecord { X = x, Y = y, Z = Z ?? 0 } : null;

        /// <summary>
        /// (0, 0) is the corner of the void and the value an export writes for a record that has
        /// no position of its own -- a third of a region list, in the one to hand. Read as
        /// absent rather than dropped in the ocean.
        /// </summary>
        private static (int X, int Y, int Z)? Usable(PointRecord? p) =>
            p is { X: not 0 } or { Y: not 0 } ? (p.X, p.Y, p.Z) : null;

        private (int X, int Y, int Z)? Centre()
        {
            foreach (var rect in Coords ?? [])
            {
                if (rect is { Start: { } start, End: { } end })
                    return ((start.X + end.X) / 2, (start.Y + end.Y) / 2, 0);
            }

            return null;
        }
    }

    private sealed class PointRecord
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    private sealed class RectRecord
    {
        public PointRecord? Start { get; set; }
        public PointRecord? End { get; set; }
    }
}
