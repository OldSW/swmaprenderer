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

    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(DataPath))
            errors.Add("DataPath must be set.");
        else if (!Directory.Exists(DataPath))
            errors.Add($"Data folder '{DataPath}' does not exist.");

        if (Map is < 0 or > 5)
            errors.Add($"Map must be between 0 and 5 (got {Map}).");

        if (Width is < 1 or > 32768)
            errors.Add($"Width must be between 1 and 32768 (got {Width}).");

        if (Height is < 1 or > 32768)
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

        if (errors.Count > 0)
            throw new OptionsException(errors);
    }
}

public sealed class OptionsException(IReadOnlyList<string> errors)
    : Exception("Invalid options:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
