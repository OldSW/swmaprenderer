using System.Globalization;
using System.Numerics;
using SwMapRenderer.Assets;

namespace SwMapRenderer;

/// <summary>
/// Everything the renderer needs to produce one image. Bound from appsettings.json's "Render"
/// section and overridable per run on the command line.
/// </summary>
public sealed class RenderOptions
{
    /// <summary>Folder holding the client's .mul files.</summary>
    public string DataPath { get; set; } = "vanilla_client";

    /// <summary>Facet index; selects map{N}.mul / staidx{N}.mul / statics{N}.mul.</summary>
    public int Map { get; set; }

    /// <summary>Tile the camera is centred on.</summary>
    public int X { get; set; } = 1420;
    public int Y { get; set; } = 1690;

    /// <summary>Output image size in pixels.</summary>
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 768;

    /// <summary>Larger zooms in. Clamped to the same 0.2-4.0 range CentrED allows.</summary>
    public float Zoom { get; set; } = 1.0f;

    public string Output { get; set; } = "map.png";

    /// <summary>
    /// Canvas colour before anything is drawn: "transparent", "black", or #RGB / #RRGGBB /
    /// #RRGGBBAA.
    ///
    /// Left unset it follows the mode. A single image clears to black, matching the reference's
    /// Clear(Color.Black). Pyramid slices clear to transparent instead, so that a slice off the
    /// edge of the facet is indistinguishable from one that was never written, and so that
    /// averaging a slice into the level above does not drag black into its neighbours.
    /// </summary>
    public string? Background { get; set; }

    /// <summary>Colour used when <see cref="Background"/> is unset.</summary>
    public string DefaultBackground => IsTiling ? "transparent" : "black";

    public bool ShowLand { get; set; } = true;
    public bool ShowStatics { get; set; } = true;

    /// <summary>Draw the "nodraw" placeholder tiles that are normally suppressed.</summary>
    public bool ShowNoDraw { get; set; }

    /// <summary>Flatten every tile to z=0, as CentrED's flat view does.</summary>
    public bool FlatView { get; set; }

    /// <summary>Use terrain textures even where unstretched land art would do.</summary>
    public bool PreferTexmaps { get; set; }

    /// <summary>Z range to include; tiles outside are skipped.</summary>
    public int MinZ { get; set; } = -128;
    public int MaxZ { get; set; } = 127;

    /// <summary>Override facet dimensions in tiles. 0 means detect from the file size.</summary>
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }

    public TileDataFormat TileDataFormat { get; set; } = TileDataFormat.Auto;

    /// <summary>Log per-stage timings and tile counts.</summary>
    public bool Verbose { get; set; }

    // ---- Tile pyramid ----

    /// <summary>
    /// Output directory for a browsable tile pyramid. Setting it switches from writing a single
    /// image to slicing the facet.
    /// </summary>
    public string? Tiles { get; set; }

    /// <summary>
    /// Tiles along the edge of one slice. A 16x16-tile region bounds a 704x704 pixel square at
    /// full zoom, which is the slice size.
    /// </summary>
    public int SliceTiles { get; set; } = 16;

    /// <summary>Shallowest pyramid level to write. 0 is the whole facet in a single slice.</summary>
    public int MinZoom { get; set; }

    /// <summary>
    /// Deepest level to write; -1 means the level at which one tile is 44 pixels. Capping this
    /// is the practical way to cover a whole facet: each level down is four times the slices.
    /// </summary>
    public int MaxZoom { get; set; } = -1;

    /// <summary>Tile rectangle to cover, as "x1,y1,x2,y2". Empty means the whole facet.</summary>
    public string? Region { get; set; }

    /// <summary>Slices to render at once. 0 uses every core.</summary>
    public int Threads { get; set; }

    /// <summary>Re-render slices that already exist instead of resuming past them.</summary>
    public bool Overwrite { get; set; }

    /// <summary>Report what would be written and stop.</summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Decoded sprite budget, in MB, per art and texmap file. A single image needs only what is
    /// in view, but a whole-facet tile run walks the entire art file, and decoded RGBA is
    /// several times the size of the packed original.
    /// </summary>
    public int SpriteCacheMegabytes { get; set; } = 512;

    /// <summary>
    /// Map blocks kept decoded, per terrain and statics layer. A block is 8x8 tiles, so the
    /// default is a working set of roughly 130k tiles -- far more than any one slice touches,
    /// and small enough that a whole-facet run does not accumulate all 458k blocks of a facet.
    /// </summary>
    public int CachedBlocks { get; set; } = 2048;

    /// <summary>Parsed form of <see cref="Region"/>, or null when the whole facet is wanted.</summary>
    public (int X0, int Y0, int X1, int Y1)? ParsedRegion => ParseRegion(Region);

    /// <summary>Returns null for an absent region and throws for a malformed one.</summary>
    public static (int X0, int Y0, int X1, int Y1)? ParseRegion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !parts.All(p => int.TryParse(p, out _)))
            return null;

        var n = parts.Select(int.Parse).ToArray();
        return (Math.Min(n[0], n[2]), Math.Min(n[1], n[3]), Math.Max(n[0], n[2]), Math.Max(n[1], n[3]));
    }

    /// <summary>True when the run should write a pyramid rather than a single image.</summary>
    public bool IsTiling => !string.IsNullOrWhiteSpace(Tiles);

    /// <summary>Parsed form of <see cref="Background"/>, premultiplied to match the blend mode.</summary>
    public Vector4 BackgroundColor =>
        ParseColor(Background ?? DefaultBackground) ?? new Vector4(0f, 0f, 0f, 1f);

    /// <summary>Returns null if the value is not a colour this understands.</summary>
    public static Vector4? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        switch (value.Trim().ToLowerInvariant())
        {
            case "transparent" or "none": return Vector4.Zero;
            case "black": return new Vector4(0f, 0f, 0f, 1f);
            case "white": return new Vector4(1f, 1f, 1f, 1f);
        }

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        // #RGB is expanded by doubling each digit, as in CSS.
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));

        if (hex.Length is not (6 or 8) || !hex.All(Uri.IsHexDigit))
            return null;

        int Component(int i) => int.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber);

        float a = hex.Length == 8 ? Component(3) / 255f : 1f;

        // Premultiplied: the blend is dst = src + dst * (1 - src.a), so a cleared canvas has to
        // already carry its colour scaled by its own alpha.
        return new Vector4(Component(0) / 255f * a, Component(1) / 255f * a, Component(2) / 255f * a, a);
    }

    public void Validate()
    {
        var errors = new List<string>();

        if (Background != null && ParseColor(Background) == null)
            errors.Add($"Background '{Background}' is not a colour. Use transparent, black, white, or #RRGGBB[AA].");

        if (string.IsNullOrWhiteSpace(DataPath))
            errors.Add("DataPath must be set.");
        else if (!Directory.Exists(DataPath))
            errors.Add($"Data folder '{DataPath}' does not exist.");

        if (Map is < 0 or > 5)
            errors.Add($"Map must be between 0 and 5 (got {Map}).");

        if (!IsTiling && Width is < 1 or > 32768)
            errors.Add($"Width must be between 1 and 32768 (got {Width}).");

        if (!IsTiling && Height is < 1 or > 32768)
            errors.Add($"Height must be between 1 and 32768 (got {Height}).");

        if (Zoom is < 0.2f or > 4.0f)
            errors.Add($"Zoom must be between 0.2 and 4.0 (got {Zoom}).");

        if (MinZ < -128 || MinZ > 127)
            errors.Add($"MinZ must be between -128 and 127 (got {MinZ}).");

        if (MaxZ < -128 || MaxZ > 127)
            errors.Add($"MaxZ must be between -128 and 127 (got {MaxZ}).");

        if (MinZ > MaxZ)
            errors.Add($"MinZ ({MinZ}) must not exceed MaxZ ({MaxZ}).");

        if (string.IsNullOrWhiteSpace(Output))
            errors.Add("Output must be set.");

        if ((MapWidth == 0) != (MapHeight == 0))
            errors.Add("MapWidth and MapHeight must be set together, or both left at 0 to autodetect.");

        if (MapWidth % 8 != 0 || MapHeight % 8 != 0)
            errors.Add("MapWidth and MapHeight must be multiples of 8 (maps are stored as 8x8 blocks).");

        if (!ShowLand && !ShowStatics)
            errors.Add("Nothing to draw: both ShowLand and ShowStatics are disabled.");

        if (SpriteCacheMegabytes < 16)
            errors.Add($"SpriteCacheMegabytes must be at least 16 (got {SpriteCacheMegabytes}).");

        if (CachedBlocks < 64)
            errors.Add($"CachedBlocks must be at least 64 (got {CachedBlocks}).");

        if (Region != null && ParseRegion(Region) == null)
            errors.Add($"Region '{Region}' is not four comma-separated tile coordinates, e.g. 1350,1600,1500,1750.");

        if (SliceTiles is < 1 or > 256)
            errors.Add($"SliceTiles must be between 1 and 256 (got {SliceTiles}).");

        if (MinZoom < 0)
            errors.Add($"MinZoom must not be negative (got {MinZoom}).");

        if (MaxZoom >= 0 && MaxZoom < MinZoom)
            errors.Add($"MaxZoom ({MaxZoom}) must not be below MinZoom ({MinZoom}).");

        if (Threads < 0)
            errors.Add($"Threads must not be negative (got {Threads}).");

        if (errors.Count > 0)
            throw new OptionsException(errors);
    }
}

public sealed class OptionsException(IReadOnlyList<string> errors)
    : Exception("Invalid options:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
