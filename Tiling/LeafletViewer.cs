using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwMapRenderer.Tiling;

/// <summary>
/// Writes the browsable page for a generated pyramid.
///
/// The page uses a custom CRS whose units are canvas pixels at the deepest level, so that
/// Leaflet's tile grid lines up with the slices exactly as generated and no coordinate fudging
/// is needed anywhere. Because the same projection is available in reverse, the page can also
/// translate the cursor back into the tile coordinates a player would recognise.
///
/// Points of interest ride on top of that as a DOM overlay rather than as pixels in the slices.
/// That is what makes them searchable, and it is why a shard can move a marker without a single
/// tile being re-rendered.
/// </summary>
public static class LeafletViewer
{
    public const string FileName = "index.html";

    /// <summary>
    /// What a template has to contain for the page to know anything about the pyramid. The
    /// markers placeholder is optional: a template that never shows markers can leave it out.
    /// </summary>
    public const string ConfigPlaceholder = "/*__CONFIG__*/null";
    public const string PoisPlaceholder = "/*__POIS__*/null";

    private static readonly JsonSerializerOptions ConfigOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Markers are written on one line each. They are data rather than configuration, there can
    /// be thousands of them, and indenting them buries the page's own code in the middle of the
    /// file.
    /// </summary>
    private static readonly JsonSerializerOptions PoiOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="embedded">Markers read at generation time, to embed in the page. Null when
    /// no source was a file.</param>
    /// <param name="fetched">Sources the page fetches itself, each on its own interval.</param>
    /// <param name="start">Where the page opens when its URL says nothing. Null fits the facet.</param>
    /// <param name="templatePath">A page to use in place of the built-in one, with the same
    /// placeholders. Null for the built-in.</param>
    public static string Write(SliceGrid grid, PyramidPlan plan, int mapIndex, string outputDirectory,
        ImageFormat format, PointsOfInterest? embedded = null, IReadOnlyList<PoiSource>? fetched = null,
        string? templatePath = null, (int X, int Y, double? Zoom)? start = null)
    {
        var config = new
        {
            mapIndex,
            sliceSize = grid.SliceSize,
            canvasWidth = grid.CanvasWidth,
            canvasHeight = grid.CanvasHeight,
            originX = grid.OriginX,
            originY = grid.OriginY,
            nativeZoom = grid.NativeZoom,
            halfTile = Rendering.IsoProjection.HalfTileWidth * grid.NativeZoom,

            // The level at which one canvas unit is one image pixel. This is a property of the
            // grid, not of how deep the run went, and it is what the CRS scales against -- a
            // pyramid capped with --max-zoom still has its slices laid out on the full grid.
            gridMaxZoom = grid.MaxZoom,

            minZoom = plan.MinZoom,
            maxZoom = plan.MaxZoom,
            tilePixels = grid.TilePixelsAtLevel(plan.MaxZoom),
            mapWidth = grid.Map.Width,
            mapHeight = grid.Map.Height,
            tilesPerSlice = grid.TilesPerSlice,
            start = start is { } s ? new { x = s.X, y = s.Y, zoom = s.Zoom } : null,
            extension = format.Extension,
            format = format.Name,
            poiSources = (fetched ?? []).Select(s => new
            {
                name = s.Name, url = s.Location, refreshSeconds = s.RefreshSeconds,
                key = s.Key, category = s.Category, mapId = s.MapId, types = s.Types, fields = s.Fields,
            }),
        };

        string template = templatePath is null ? DefaultTemplate() : File.ReadAllText(templatePath);
        string html = template
            .Replace(ConfigPlaceholder, JsonSerializer.Serialize(config, ConfigOptions))
            .Replace(PoisPlaceholder, Markers(embedded));

        string path = Path.Combine(outputDirectory, FileName);
        File.WriteAllText(path, html);
        return path;
    }

    /// <summary>
    /// The same envelope the file and an API use, <c>{ types, pois }</c>, so that the page has
    /// one reader whether the markers were embedded or fetched.
    /// </summary>
    private static string Markers(PointsOfInterest? embedded)
    {
        if (embedded is not { Items.Count: > 0 })
            return "null";

        var lines = embedded.Items.Select(p => "  " + JsonSerializer.Serialize(p, PoiOptions));
        return "{" + Environment.NewLine +
               "\"types\": " + JsonSerializer.Serialize(embedded.Types, PoiOptions) + "," + Environment.NewLine +
               "\"pois\": [" + Environment.NewLine + string.Join("," + Environment.NewLine, lines) +
               Environment.NewLine + "]" + Environment.NewLine + "}";
    }

    /// <summary>
    /// The built-in page, <c>index.template.html</c> in the repository root, compiled into the
    /// binary so that a run needs nothing beside it. It is one file on purpose: it is both what
    /// gets written and what someone copies to start their own with --template.
    /// </summary>
    private static string DefaultTemplate()
    {
        using var stream = typeof(LeafletViewer).Assembly.GetManifestResourceStream(DefaultTemplateResource)
            ?? throw new InvalidOperationException($"The embedded page '{DefaultTemplateResource}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private const string DefaultTemplateResource = "index.template.html";
        }
