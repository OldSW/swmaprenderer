using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SwMapRenderer.Map;

namespace SwMapRenderer.Tiling;

/// <summary>One marker, in the canonical shape the viewer consumes.</summary>
public readonly record struct Poi(string Name, int X, int Y, int Z, string? Category, string? Description);

/// <summary>How markers of one category look: a picture, or failing that the colour of the dot.</summary>
public readonly record struct PoiType(string? Icon, string? Color);

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

    /// <summary>
    /// Appearance per category, from the file's <c>types</c> block. Keyed case-insensitively,
    /// because a category is typed by hand in one place and by a script in another.
    /// </summary>
    public IReadOnlyDictionary<string, PoiType> Types { get; }

    private PointsOfInterest(IReadOnlyList<Poi> items, PoiStats stats, IReadOnlyDictionary<string, PoiType> types)
    {
        Items = items;
        Stats = stats;
        Types = types;
    }

    /// <summary>Whether the configured source is fetched by the page rather than read here.</summary>
    public static bool IsUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <param name="source">What the configuration says about this file beyond where it is:
    /// which array holds the records, what to call ones that name no category, which facet
    /// ones that name none are on, and looks for categories.</param>
    public static PointsOfInterest Load(string path, int mapIndex, MapDimensions dimensions,
        PoiSource? source = null)
    {
        var (records, types) = ReadRecords(path, source?.Key, source?.Fields);

        // The configuration has the last word on how a category looks, since it is written
        // for this shard's map and the file may be somebody else's export.
        foreach (var (category, type) in source?.Types ?? new Dictionary<string, PoiType>())
            types[category] = type;

        var kept = new List<Poi>(records.Count);
        int otherMap = 0, unnamed = 0, noPosition = 0, offMap = 0;

        foreach (var record in records)
        {
            if (record == null)
                continue;

            if ((record.Facet ?? source?.MapId) is { } facet && facet != mapIndex)
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
                First(record.Category, record.Type, source?.Category),
                First(record.Description, record.Desc)));
        }

        Sort(kept);

        return new PointsOfInterest(kept,
            new PoiStats(records.Count, kept.Count, otherMap, unnamed, noPosition, offMap), types);
    }

    /// <summary>
    /// Sorted so that the page this ends up embedded in is stable across runs: a resume that
    /// re-renders nothing should leave index.html byte-identical too.
    /// </summary>
    private static void Sort(List<Poi> pois) =>
        pois.Sort(static (a, b) =>
        {
            int byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : a.X != b.X ? a.X - b.X : a.Y - b.Y;
        });

    /// <summary>
    /// Several files as one. A category two of them both describe takes the look the later one
    /// gives it, the way a later configuration entry overrides an earlier one.
    /// </summary>
    public static PointsOfInterest Combine(IReadOnlyList<PointsOfInterest> parts)
    {
        var items = parts.SelectMany(p => p.Items).ToList();
        Sort(items);

        var types = new Dictionary<string, PoiType>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
            foreach (var (category, type) in part.Types)
                types[category] = type;

        var stats = new PoiStats(
            parts.Sum(p => p.Stats.Records), parts.Sum(p => p.Stats.Kept),
            parts.Sum(p => p.Stats.OtherMap), parts.Sum(p => p.Stats.Unnamed),
            parts.Sum(p => p.Stats.NoPosition), parts.Sum(p => p.Stats.OffMap));

        return new PointsOfInterest(items, stats, types);
    }

    private static (List<PoiRecord?> Records, Dictionary<string, PoiType> Types) ReadRecords(string path, string? key, IReadOnlyDictionary<string, string>? fields)
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

            var array = Unwrap(document.RootElement, key)
                        ?? throw new InvalidDataException(
                            $"'{path}' holds {Describe(document.RootElement)} where an array of points " +
                            $"of interest was expected, either bare or under a \"pois\" key" +
                            (key != null ? $" or the configured \"{key}\" key." : "."));

            var records = fields is { Count: > 0 }
                ? Remap(array, fields).Deserialize<List<PoiRecord?>>(ReadOptions)
                : array.Deserialize<List<PoiRecord?>>(ReadOptions);

            return (records ?? [], ReadTypes(document.RootElement));
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

    /// <summary>
    /// What a source's <c>fields</c> block may name: the record's own properties, and the three
    /// coordinates for an API that keeps them apart rather than in a position object.
    /// </summary>
    public static readonly IReadOnlySet<string> MappableFields = new HashSet<string>
    {
        "name", "category", "description", "mapId", "position", "x", "y", "z",
    };

    /// <summary>
    /// Rewrites each record into the canonical shape according to <paramref name="fields"/>,
    /// whose values are dotted paths into the record (<c>"pos.east"</c>, <c>"loc.0"</c>).
    /// A property that is mapped is taken from its path alone -- one that is not there leaves
    /// the record without it, rather than quietly falling back to a same-named property that
    /// means something else.
    /// </summary>
    private static JsonArray Remap(JsonElement array, IReadOnlyDictionary<string, string> fields)
    {
        var result = new JsonArray();

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                result.Add(null);
                continue;
            }

            var record = JsonNode.Parse(element.GetRawText())!.AsObject();

            foreach (string name in (string[])["name", "category", "description"])
            {
                if (fields.TryGetValue(name, out string? path))
                    record[name] = Pluck(element, path) is { } v ? JsonNode.Parse(v.GetRawText()) : null;
            }

            if (fields.TryGetValue("mapId", out string? mapPath))
                record["mapId"] = Pluck(element, mapPath) is { } m && Whole(m) is { } id ? id : null;

            if (fields.TryGetValue("position", out string? positionPath))
                record["position"] = Pluck(element, positionPath) is { } p ? JsonNode.Parse(p.GetRawText()) : null;

            if (fields.ContainsKey("x") || fields.ContainsKey("y") || fields.ContainsKey("z"))
            {
                var position = record["position"] as JsonObject;
                var assembled = new JsonObject();

                foreach (string axis in (string[])["x", "y", "z"])
                {
                    JsonNode? value = fields.TryGetValue(axis, out string? axisPath)
                        ? (Pluck(element, axisPath) is { } a && Whole(a) is { } n ? n : null)
                        : position?[axis]?.DeepClone();

                    assembled[axis] = value;
                }

                record["position"] = assembled;
            }

            // Coordinates arrive as whatever the API's author found convenient: 12, "12", 12.5.
            if (record["position"] is JsonObject point)
            {
                foreach (string axis in (string[])["x", "y", "z"])
                {
                    if (point[axis] is { } raw && Whole(raw) is { } whole)
                        point[axis] = whole;
                    else
                        point.Remove(axis);
                }
            }

            result.Add(record);
        }

        return result;
    }

    /// <summary>The value at a dotted path, or null when any step of it is missing.</summary>
    private static JsonElement? Pluck(JsonElement element, string path)
    {
        var current = element;

        foreach (string step in path.Split('.'))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(step, out var next))
                current = next;
            else if (current.ValueKind == JsonValueKind.Array &&
                     int.TryParse(step, NumberStyles.None, CultureInfo.InvariantCulture, out int i) &&
                     i < current.GetArrayLength())
                current = current[i];
            else
                return null;
        }

        return current.ValueKind == JsonValueKind.Null ? null : current;
    }

    private static int? Whole(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return Whole(document.RootElement);
    }

    /// <summary>A number, or text that is one, rounded to a whole tile -- null if it is neither.</summary>
    private static int? Whole(JsonElement value)
    {
        double number;

        if (value.ValueKind == JsonValueKind.Number)
            number = value.GetDouble();
        else if (value.ValueKind != JsonValueKind.String ||
                 !double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return null;

        return double.IsFinite(number) ? (int)Math.Round(number) : null;
    }

    /// <summary>
    /// The <c>types</c> block of an enveloped file, or of a source in the configuration:
    /// <c>{ "town": { "icon": "icons/town.png", "color": "#ffd479" } }</c>. A bare string is
    /// taken as the icon. The colour is what the dot is painted when there is no icon, and it
    /// is left unchecked here: the page validates it, because it is the page that puts it
    /// into a style. A type with neither is left out rather than failing the file -- it only
    /// costs the marker its look.
    /// </summary>
    internal static Dictionary<string, PoiType> ReadTypes(JsonElement root)
    {
        var types = new Dictionary<string, PoiType>(StringComparer.OrdinalIgnoreCase);

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("types", out var block) ||
            block.ValueKind != JsonValueKind.Object)
            return types;

        foreach (var entry in block.EnumerateObject())
        {
            var value = entry.Value;
            string? icon = null, color = null;

            if (value.ValueKind == JsonValueKind.String)
            {
                icon = First(value.GetString());
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                icon = Text(value, "icon");
                color = Text(value, "color");
            }

            if (icon != null || color != null)
                types[entry.Name.Trim()] = new PoiType(icon, color);
        }

        return types;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? First(value.GetString())
            : null;

    private static JsonElement? Unwrap(JsonElement root, string? key)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        // The configured key goes first: an API with a "players" array may well have an "items"
        // one too, and the configuration is what says which of them is meant.
        string[] keys = key != null ? [key] : ["pois", "points", "items", "results", "data"];

        foreach (string candidate in keys)
        {
            if (root.TryGetProperty(candidate, out var value) && value.ValueKind == JsonValueKind.Array)
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
