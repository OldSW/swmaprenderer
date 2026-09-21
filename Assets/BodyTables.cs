using System.Globalization;

namespace SwMapRenderer.Assets;

/// <summary>
/// How a body is animated, which is what decides where "standing still" lives in its action
/// list. Read from mobtypes.txt; the names are the ones that file uses.
/// </summary>
public enum AnimationType
{
    Monster,
    SeaMonster,
    Animal,
    Human,
    Equipment,
}

/// <summary>Where one body's action and facing live: which anim file, and the entry in it.</summary>
public readonly record struct AnimationLookup(int File, int Index)
{
    public static readonly AnimationLookup None = new(0, -1);

    public bool Found => File != 0;
}

/// <summary>
/// The four text files that sit between a body id and the animation that draws it.
///
/// Bodies were added to Ultima Online for two decades without ever renumbering anything, so a
/// body id on the wire is not an index into anything. body.def redirects ids whose art was
/// never drawn onto a body that has some, bodyconv.def says which of the five anim files a body
/// ended up in and under what number there, mobtypes.txt gives the animation type that decides
/// how its actions are laid out, and equipconv.def substitutes equipment art for bodies that
/// have none of their own.
///
/// Ported from the Ultima SDK (BodyTable, BodyConverter, Animations.GetFileIndex) and, for the
/// pieces the SDK does not model, from ClassicUO's AnimationsLoader.
/// </summary>
public sealed class BodyTables
{
    private readonly record struct BodyDefEntry(int Body, int Hue);

    /// <summary>body.def, packed the way the SDK packs it: id, remap flag, replacement hue.</summary>
    private readonly int[] _translate;

    /// <summary>bodyconv.def, one lookup per target file: body id in anim{2..5}, or -1.</summary>
    private readonly int[][] _convert = new int[4][];

    private readonly Dictionary<int, AnimationType> _types = new();
    private readonly Dictionary<long, (int Graphic, int Color)> _equipConv = new();

    public BodyTables(DataFolder folder, int baseEntryCount)
    {
        var bodyDef = ReadBodyDef(folder);
        ReadBodyConv(folder);
        ReadMobTypes(folder);
        ReadEquipConv(folder);

        // Sized exactly as the SDK sizes it: the people range of anim.idx, 175 entries a body.
        int count = Math.Max(400, 400 + (baseEntryCount - 35000) / 175);
        _translate = new int[count];

        for (int i = 0; i < count; i++)
        {
            // A body that bodyconv.def places in a later file keeps its own id: the redirect in
            // body.def is there for bodies with no art at all, and the later files are art.
            if (!bodyDef.TryGetValue(i, out var entry) || IsConverted(i))
                _translate[i] = i;
            else
                _translate[i] = entry.Body | (1 << 31) | ((entry.Hue & 0xFFFF) << 15);
        }
    }

    /// <summary>The action index a body stands still in, which differs by animation type.</summary>
    public static int StandAction(AnimationType type) => type switch
    {
        AnimationType.Monster or AnimationType.SeaMonster => 1,
        AnimationType.Animal => 2,
        _ => 4,
    };

    /// <summary>
    /// The facing stored in the file for a direction on the wire, and whether it has to be
    /// mirrored to get there. Only five of the eight are drawn; the other three are the
    /// mirror image of one that is. Ported from ClassicUO's Animation.GetAnimDirection.
    /// </summary>
    public static int AnimDirection(int direction, out bool flip)
    {
        flip = false;

        switch (direction & 7)
        {
            case 2: flip = true; return 1;
            case 4: return 1;
            case 1: flip = true; return 2;
            case 5: return 2;
            case 0: flip = true; return 3;
            case 6: return 3;
            case 3: return 0;
            default: return 4;
        }
    }

    public AnimationType TypeOf(int body)
    {
        if (_types.TryGetValue(body, out var type))
            return type;

        // mobtypes.txt stops well short of the highest body in use. The ranges it would have
        // given are the same ones the entry index is laid out in, so they are the fallback.
        return body < 200 ? AnimationType.Monster
            : body < 400 ? AnimationType.Animal
            : AnimationType.Human;
    }

    /// <summary>
    /// Locates one action of one body facing one way.
    /// </summary>
    /// <param name="hue">
    /// The mobile's own hue. A body redirected by body.def brings a hue with it, which is used
    /// only where the mobile has none of its own -- that is how a "red" variant of a creature
    /// shares the grey original's art.
    /// </param>
    public AnimationLookup Locate(int body, int action, int animDirection, ref int hue)
    {
        Translate(ref body, ref hue);

        int file = Convert(ref body);
        int index = EntryIndex(file, body);

        if (index < 0)
            return AnimationLookup.None;

        return new AnimationLookup(file, index + action * 5 + animDirection);
    }

    /// <summary>
    /// The equipment art a body wears in place of a piece's own, where the two were drawn
    /// apart -- most of it is women wearing art that was only ever drawn on a man.
    /// </summary>
    public bool TryConvertEquipment(int body, int animId, out int graphic, out int color)
    {
        if (_equipConv.TryGetValue(Key(body, animId), out var data))
        {
            (graphic, color) = data;
            return true;
        }

        graphic = animId;
        color = 0;
        return false;
    }

    private static long Key(int body, int animId) => ((long)body << 32) | (uint)animId;

    private void Translate(ref int body, ref int hue)
    {
        if (body <= 0 || body >= _translate.Length)
        {
            body = 0;
            return;
        }

        int packed = _translate[body];

        if ((packed & (1 << 31)) == 0)
            return;

        body = packed & 0x7FFF;

        if (hue <= 0)
            hue = (packed >> 15) & 0xFFFF;
    }

    private bool IsConverted(int body)
    {
        foreach (var table in _convert)
        {
            if (table != null && body >= 0 && body < table.Length && table[body] != -1)
                return true;
        }

        return false;
    }

    /// <summary>Rewrites a body to its id in a later anim file, and says which file that is.</summary>
    private int Convert(ref int body)
    {
        for (int i = 0; i < _convert.Length; i++)
        {
            var table = _convert[i];
            if (table == null || body < 0 || body >= table.Length || table[body] == -1)
                continue;

            body = table[body];
            return i + 2;
        }

        return 1;
    }

    /// <summary>
    /// The first entry of a body, before action and facing are added.
    ///
    /// Each file packs three body ranges at fixed strides -- monsters at 110 entries, animals
    /// at 65, people at 175 -- but not the same three, and anim3 puts them in a different order
    /// than the rest. Verbatim from the SDK's GetFileIndex, quirks included.
    /// </summary>
    private static int EntryIndex(int file, int body) => file switch
    {
        1 or 4 => body < 200 ? body * 110
            : body < 400 ? 22000 + ((body - 200) * 65)
            : 35000 + ((body - 400) * 175),

        2 => body < 200 ? body * 110 : 22000 + ((body - 200) * 65),

        3 => body < 300 ? body * 65
            : body < 400 ? 33000 + ((body - 300) * 110)
            : 35000 + ((body - 400) * 175),

        5 => body < 200 && body != 34 ? body * 110
            : body < 400 ? 22000 + ((body - 200) * 65)
            : 35000 + ((body - 400) * 175),

        _ => -1,
    };

    private static Dictionary<int, BodyDefEntry> ReadBodyDef(DataFolder folder)
    {
        var entries = new Dictionary<int, BodyDefEntry>();

        foreach (string line in ReadLines(folder, "body.def"))
        {
            int open = line.IndexOf('{');
            int close = line.IndexOf('}');

            if (open < 0 || close < open)
                continue;

            // The braces hold a list of bodies this one may be drawn as; the client takes the
            // first and so does the SDK.
            if (!TryParse(line.AsSpan(0, open), out int id) ||
                !TryParse(line[(open + 1)..close].Split(',')[0], out int body) ||
                !TryParse(line.AsSpan(close + 1), out int hue))
            {
                continue;
            }

            entries[id] = new BodyDefEntry(body, hue);
        }

        return entries;
    }

    private void ReadBodyConv(DataFolder folder)
    {
        var lists = new List<(int Body, int Target)>[4];
        for (int i = 0; i < 4; i++)
            lists[i] = [];

        foreach (string line in ReadLines(folder, "bodyconv.def"))
        {
            var fields = Split(line);
            if (fields.Length < 2 || !TryParse(fields[0], out int body))
                continue;

            for (int i = 0; i < 4; i++)
            {
                if (i + 1 >= fields.Length || !TryParse(fields[i + 1], out int target) || target < 0)
                    continue;

                // An SDK fix-up for a single mislabelled body in anim2.
                if (i == 0 && target == 68)
                    target = 122;

                lists[i].Add((body, target));
            }
        }

        for (int i = 0; i < 4; i++)
        {
            int max = 0;
            foreach (var (body, _) in lists[i])
                max = Math.Max(max, body);

            var table = new int[max + 1];
            Array.Fill(table, -1);

            foreach (var (body, target) in lists[i])
                table[body] = target;

            _convert[i] = table;
        }
    }

    private void ReadMobTypes(DataFolder folder)
    {
        foreach (string line in ReadLines(folder, "mobtypes.txt"))
        {
            var fields = Split(line);
            if (fields.Length < 2 || !TryParse(fields[0], out int body))
                continue;

            AnimationType? type = fields[1].ToUpperInvariant() switch
            {
                "MONSTER" => AnimationType.Monster,
                "SEA_MONSTER" => AnimationType.SeaMonster,
                "ANIMAL" => AnimationType.Animal,
                "HUMAN" => AnimationType.Human,
                "EQUIPMENT" => AnimationType.Equipment,
                _ => null,
            };

            if (type is { } resolved)
                _types[body] = resolved;
        }
    }

    private void ReadEquipConv(DataFolder folder)
    {
        foreach (string line in ReadLines(folder, "equipconv.def"))
        {
            var fields = Split(line);

            if (fields.Length < 5 ||
                !TryParse(fields[0], out int body) ||
                !TryParse(fields[1], out int graphic) ||
                !TryParse(fields[2], out int replacement) ||
                !TryParse(fields[4], out int color))
            {
                continue;
            }

            _equipConv[Key(body, graphic)] = (replacement, color);
        }
    }

    private static IEnumerable<string> ReadLines(DataFolder folder, string name)
    {
        if (!folder.TryGet(name, out string path))
            yield break;

        // The def files are ASCII with the odd Latin-1 name in a trailing comment; reading them
        // as UTF-8 would throw away a line on the first stray byte.
        foreach (string raw in File.ReadLines(path, System.Text.Encoding.Latin1))
        {
            int comment = raw.IndexOf('#');
            string line = (comment >= 0 ? raw[..comment] : raw).Trim();

            if (line.Length > 0)
                yield return line;
        }
    }

    private static string[] Split(string line) =>
        line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static bool TryParse(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
