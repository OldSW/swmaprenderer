using Microsoft.Extensions.Configuration;

namespace SwMapRenderer;

/// <summary>
/// Maps command-line switches onto configuration keys and normalises the shapes that the
/// configuration command-line provider does not accept on its own: bare boolean flags
/// (<c>--flat</c>) and their negations (<c>--no-statics</c>).
/// </summary>
public static class CommandLine
{
    public static readonly Dictionary<string, string> SwitchMappings = new()
    {
        ["--data"] = "Render:DataPath",
        ["-d"] = "Render:DataPath",
        ["--map"] = "Render:Map",
        ["-m"] = "Render:Map",
        ["--x"] = "Render:X",
        ["-x"] = "Render:X",
        ["--y"] = "Render:Y",
        ["-y"] = "Render:Y",
        ["--width"] = "Render:Width",
        ["-w"] = "Render:Width",
        ["--height"] = "Render:Height",
        ["-h"] = "Render:Height",
        ["--zoom"] = "Render:Zoom",
        ["-z"] = "Render:Zoom",
        ["--out"] = "Render:Output",
        ["-o"] = "Render:Output",
        ["--min-z"] = "Render:MinZ",
        ["--max-z"] = "Render:MaxZ",
        ["--map-width"] = "Render:MapWidth",
        ["--map-height"] = "Render:MapHeight",
        ["--tiledata-format"] = "Render:TileDataFormat",
        ["--items"] = "Render:Items",
        ["--background"] = "Render:Background",
        ["-b"] = "Render:Background",
        ["--format"] = "Render:Format",
        ["-f"] = "Render:Format",
        ["--quality"] = "Render:Quality",
        ["-q"] = "Render:Quality",
        ["--land"] = "Render:ShowLand",
        ["--statics"] = "Render:ShowStatics",
        ["--nodraw"] = "Render:ShowNoDraw",
        ["--flat"] = "Render:FlatView",
        ["--prefer-texmaps"] = "Render:PreferTexmaps",
        ["--verbose"] = "Render:Verbose",
        ["-v"] = "Render:Verbose",
        ["--tiles"] = "Render:Tiles",
        ["--slice-tiles"] = "Render:SliceTiles",
        ["--min-zoom"] = "Render:MinZoom",
        ["--max-zoom"] = "Render:MaxZoom",
        ["--region"] = "Render:Region",
        ["--threads"] = "Render:Threads",
        ["--overwrite"] = "Render:Overwrite",
        ["--dry-run"] = "Render:DryRun",
        ["--sprite-cache"] = "Render:SpriteCacheMegabytes",
        ["--cached-blocks"] = "Render:CachedBlocks",
    };

    /// <summary>Switches that may be given without a value, meaning "true".</summary>
    private static readonly HashSet<string> BooleanSwitches =
    [
        "--land", "--statics", "--nodraw", "--flat", "--prefer-texmaps", "--verbose", "-v",
        "--overwrite", "--dry-run",
    ];

    public static bool WantsHelp(string[] args) =>
        args.Any(a => a is "--help" or "-?" or "/?");

    /// <summary>
    /// Rewrites the raw arguments into the strict <c>--switch value</c> form.
    /// Throws <see cref="OptionsException"/> listing every unrecognised or incomplete switch.
    /// </summary>
    public static string[] Normalize(string[] args)
    {
        var result = new List<string>(args.Length * 2);
        var errors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (!arg.StartsWith('-'))
            {
                errors.Add($"Unexpected argument '{arg}'. Values must follow a switch, e.g. --map 0.");
                continue;
            }

            // Split --switch=value so the name can be validated on its own.
            string name = arg;
            string? inlineValue = null;
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                name = arg[..eq];
                inlineValue = arg[(eq + 1)..];
            }

            // --no-statics is the negation of --statics.
            bool negated = false;
            if (name.StartsWith("--no-") && BooleanSwitches.Contains("--" + name[5..]))
            {
                name = "--" + name[5..];
                negated = true;
            }

            if (!SwitchMappings.ContainsKey(name))
            {
                errors.Add($"Unknown option '{name}'. Run with --help to see the available options.");
                continue;
            }

            if (negated)
            {
                if (inlineValue != null)
                {
                    errors.Add($"Option '{arg}' must not be given a value.");
                    continue;
                }

                result.Add(name);
                result.Add("false");
                continue;
            }

            if (inlineValue != null)
            {
                result.Add(name);
                result.Add(inlineValue);
                continue;
            }

            // A boolean switch takes the next argument only if it looks like a value.
            bool isBoolean = BooleanSwitches.Contains(name);
            bool hasNext = i + 1 < args.Length && !args[i + 1].StartsWith('-');

            if (!hasNext)
            {
                if (!isBoolean)
                {
                    errors.Add($"Option '{name}' requires a value.");
                    continue;
                }

                result.Add(name);
                result.Add("true");
                continue;
            }

            result.Add(name);
            result.Add(args[++i]);
        }

        if (errors.Count > 0)
            throw new OptionsException(errors);

        return result.ToArray();
    }

    /// <summary>
    /// Turns the configuration binder's "failed to convert value at 'Render:Zoom'" into
    /// something that names the switch and shows what was actually read.
    /// </summary>
    public static string DescribeBindingFailure(Exception error, IConfiguration configuration)
    {
        var match = System.Text.RegularExpressions.Regex.Match(error.Message, @"'(Render:[A-Za-z]+)'");
        if (!match.Success)
            return error.Message;

        string key = match.Groups[1].Value;
        string? value = configuration[key];
        string name = key["Render:".Length..];

        string? switchName = SwitchMappings
            .Where(kv => kv.Value == key)
            .OrderByDescending(kv => kv.Key.Length)
            .Select(kv => kv.Key)
            .FirstOrDefault();

        string label = switchName ?? name;
        string hint = value != null && value.Contains(',')
            ? " Numbers are read with a dot as the decimal separator, so write 0.5 rather than 0,5."
            : string.Empty;

        return $"'{value}' is not a valid value for {label}.{hint}";
    }

    public const string HelpText = """
        swmaprenderer - renders a region of an Ultima Online map to an image.

        Usage:
          swmaprenderer [options]

        Options:
          -d, --data <dir>        Folder holding the client's .mul files
          -m, --map <0-5>         Facet to render (map{N}.mul)
          -x, --x <tile>          Tile the view is centred on
          -y, --y <tile>          Tile the view is centred on
          -w, --width <px>        Output image width
          -h, --height <px>       Output image height
          -z, --zoom <0.2-4.0>    Larger zooms in
          -o, --out <file>        Output image path; format follows the extension
          -f, --format <fmt>      png | jpg | gif | webp | webp-lossless
                                  (default: the output extension, or png for --tiles)
          -q, --quality <1-100>   Encoder quality for jpg and webp (default 85)

              --min-z <-128..127> Skip tiles below this z
              --max-z <-128..127> Skip tiles above this z
              --map-width <n>     Override facet width in tiles (default: detect)
              --map-height <n>    Override facet height in tiles (default: detect)
              --tiledata-format   Auto | Legacy | Extended
              --items <file>      JSON export of a shard's items to draw on top of the
                                  statics in the client files. It names no facet, so it
                                  is placed on whichever --map is being rendered.
                                  Pass "" to switch off a path set in appsettings.json
          -b, --background <c>    transparent | black | white | #RRGGBB[AA]

              --[no-]land         Draw terrain (default: on)
              --[no-]statics      Draw statics (default: on)
              --nodraw            Include the placeholder tiles the client hides
              --flat              Flatten all tiles to z=0
              --prefer-texmaps    Use terrain textures even where land art would do
          -v, --verbose           Report timings and tile counts

        Tile pyramid (browsable map):
              --tiles <dir>       Write a slippy-map pyramid here instead of one image,
                                  along with an index.html that browses it
              --slice-tiles <n>   Tiles per slice edge (default 16, giving 704px slices)
              --region <x1,y1,x2,y2>
                                  Cover only this tile rectangle (default: the whole facet)
              --min-zoom <z>      Shallowest level (default 0, the whole facet in one slice)
              --max-zoom <z>      Deepest level (default: 44px per tile). Each level down is
                                  four times the slices, so capping this bounds the run
              --threads <n>       Slices to render at once (default: every core)
              --overwrite         Re-render slices that already exist
              --dry-run           Report what would be written and stop

        Tuning:
              --sprite-cache <mb> Decoded sprite budget per file (default 512)
              --cached-blocks <n> Map blocks kept decoded per layer (default 2048)

              --help              Show this help

        Defaults come from appsettings.json and may also be set through the environment as
        SWMAP_Render__<Name>. Command-line switches win over both.

        Examples:
          swmaprenderer --data ./client --map 0 --x 1420 --y 1690 -w 1024 -h 768 -o britain.png

          swmaprenderer --data ./client --map 0 --tiles ./web/map0 \
                        --region 1350,1600,1500,1750

          swmaprenderer --map 1 --items lockedDownItems.json \
                        --x 1550 --y 1650 -o britain.png
        """;
}
