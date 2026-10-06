using System.Text.Json;

namespace SwMapRenderer.Tiling;

public enum PoiSourceKind
{
    /// <summary>Read when the pyramid is written and embedded in the page.</summary>
    File,

    /// <summary>Fetched by the page itself, once or on an interval.</summary>
    Url,
}

/// <summary>
/// One place markers come from. <see cref="RefreshSeconds"/> is only ever non-zero for a URL.
///
/// The rest says how to read what is there, for the source that is somebody else's API rather
/// than a list written for this tool: <see cref="Key"/> names the array holding the records,
/// <see cref="Category"/> is what records with no category of their own are called,
/// <see cref="MapId"/> is the facet records with none are on, and <see cref="Types"/> gives
/// looks to categories. <see cref="Fields"/> says where in a record each of its properties is,
/// for an API whose records are not shaped the way this tool expects.
/// </summary>
public sealed record PoiSource(string Name, PoiSourceKind Kind, string Location, int RefreshSeconds,
    string? Key = null, string? Category = null, int? MapId = null,
    IReadOnlyDictionary<string, PoiType>? Types = null,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// The list of places the markers come from, read from a JSON file so that a shard can have its
/// static towns in a file and its live ones behind an API, each on its own schedule.
///
/// <code>
/// { "sources": [
///     { "name": "towns", "type": "file", "path": "pois.json" },
///     { "name": "live",  "type": "url",  "url": "https://shard.example/api/pois", "refresh": 30 }
/// ] }
/// </code>
///
/// A source that is a third party's API can say where its records are with <c>key</c>, name
/// them with <c>category</c>, place them on a facet with <c>mapId</c> and look them up in a
/// <c>types</c> block of its own -- the one thing such an API cannot be asked to supply. A
/// <c>fields</c> block maps the record's properties to dotted paths into it, so that
/// <c>{ "x": "pos.east", "y": "pos.north" }</c> reads a position kept under other names.
///
/// The list may be bare. <c>type</c> is optional and is taken from whichever of <c>path</c> and
/// <c>url</c> is present; when it is given it has to agree with them, because a source that says
/// "file" and carries a URL is a mistake worth stopping for rather than guessing at. A relative
/// <c>path</c> is relative to the configuration file, not to wherever the tool was started, so
/// that the pair can be moved together.
/// </summary>
public static class PoiSources
{
    /// <summary>A floor, so that a typo does not turn every open page into a request storm.</summary>
    public const int MinRefreshSeconds = 5;

    /// <summary>The source a lone <c>--pois</c> value stands for.</summary>
    public static PoiSource Single(string location, int refreshSeconds) =>
        new(location, PointsOfInterest.IsUrl(location) ? PoiSourceKind.Url : PoiSourceKind.File,
            location, refreshSeconds);

    /// <exception cref="InvalidDataException">Every problem found, one per line.</exception>
    public static IReadOnlyList<PoiSource> Read(string configPath)
    {
        if (!File.Exists(configPath))
            throw new InvalidDataException($"Point-of-interest configuration '{configPath}' does not exist.");

        JsonDocument document;
        try
        {
            using var stream = File.OpenRead(configPath);
            document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException e)
        {
            string where = e.LineNumber is { } line ? $", at line {line + 1}" : string.Empty;
            throw new InvalidDataException(
                $"Point-of-interest configuration '{configPath}' is not readable JSON{where}.", e);
        }

        using (document)
        {
            var root = document.RootElement;
            JsonElement list = root;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.TryGetProperty("sources", out list))
                    throw new InvalidDataException(
                        $"'{configPath}' has no \"sources\" array. Expected " +
                        $"{{ \"sources\": [ {{ \"type\": \"file\", \"path\": ... }}, ... ] }}.");
            }

            if (list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"'{configPath}': \"sources\" must be an array.");

            string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            var errors = new List<string>();
            var sources = new List<PoiSource>();

            int index = 0;
            foreach (var entry in list.EnumerateArray())
            {
                index++;
                string label = $"'{configPath}', source {index}";

                if (entry.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{label}: expected an object.");
                    continue;
                }

                string? type = Text(entry, "type")?.ToLowerInvariant();
                string? path = Text(entry, "path");
                string? url = Text(entry, "url");
                string? name = Text(entry, "name");

                if (type is not (null or "file" or "url"))
                {
                    errors.Add($"{label}: type must be \"file\" or \"url\" (got \"{type}\").");
                    continue;
                }

                if (path != null && url != null)
                {
                    errors.Add($"{label}: give either \"path\" or \"url\", not both.");
                    continue;
                }

                var kind = type switch
                {
                    "file" => PoiSourceKind.File,
                    "url" => PoiSourceKind.Url,
                    _ => url != null ? PoiSourceKind.Url : PoiSourceKind.File,
                };

                string? location = kind == PoiSourceKind.Url ? url : path;
                if (location == null)
                {
                    errors.Add($"{label}: a {kind.ToString().ToLowerInvariant()} source needs " +
                               $"\"{(kind == PoiSourceKind.Url ? "url" : "path")}\".");
                    continue;
                }

                if (kind == PoiSourceKind.Url && !PointsOfInterest.IsUrl(location))
                {
                    errors.Add($"{label}: \"{location}\" is not an http:// or https:// URL.");
                    continue;
                }

                int refresh = 0;
                if (entry.TryGetProperty("refresh", out var value))
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out refresh))
                    {
                        errors.Add($"{label}: refresh is a whole number of seconds.");
                        continue;
                    }

                    if (refresh != 0 && kind == PoiSourceKind.File)
                    {
                        errors.Add($"{label}: a file is embedded when the pyramid is written and cannot " +
                                   $"be refreshed; refresh needs a url source.");
                        continue;
                    }

                    if (refresh != 0 && refresh < MinRefreshSeconds)
                    {
                        errors.Add($"{label}: refresh is 0 for never or at least {MinRefreshSeconds} seconds (got {refresh}).");
                        continue;
                    }
                }

                int? mapId = null;
                if (entry.TryGetProperty("mapId", out var facet))
                {
                    if (facet.ValueKind != JsonValueKind.Number || !facet.TryGetInt32(out int id))
                    {
                        errors.Add($"{label}: mapId is a whole number.");
                        continue;
                    }

                    mapId = id;
                }

                Dictionary<string, string>? fields = null;
                if (entry.TryGetProperty("fields", out var block))
                {
                    if (block.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"{label}: fields is an object mapping property names to paths.");
                        continue;
                    }

                    fields = new Dictionary<string, string>();
                    bool bad = false;

                    foreach (var field in block.EnumerateObject())
                    {
                        if (!PointsOfInterest.MappableFields.Contains(field.Name))
                        {
                            errors.Add($"{label}: fields has no \"{field.Name}\". It can map: " +
                                       $"{string.Join(", ", PointsOfInterest.MappableFields.Order())}.");
                            bad = true;
                        }
                        else if (field.Value.ValueKind != JsonValueKind.String ||
                                 string.IsNullOrWhiteSpace(field.Value.GetString()))
                        {
                            errors.Add($"{label}: fields.{field.Name} is the path of a property, as text.");
                            bad = true;
                        }
                        else
                        {
                            fields[field.Name] = field.Value.GetString()!.Trim();
                        }
                    }

                    if (bad)
                        continue;
                }

                if (kind == PoiSourceKind.File)
                    location = Path.GetFullPath(location, baseDirectory);

                sources.Add(new PoiSource(name ?? location, kind, location, refresh,
                    Text(entry, "key"), Text(entry, "category"), mapId,
                    PointsOfInterest.ReadTypes(entry), fields));
            }

            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            return sources;
        }
    }

    private static string? Text(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        string text = value.GetString()!.Trim();
        return text.Length > 0 ? text : null;
    }
}
