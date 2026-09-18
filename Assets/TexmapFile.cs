using System.Runtime.InteropServices;

namespace SwMapRenderer.Assets;

/// <summary>
/// texmaps.mul / texidx.mul. Terrain textures, used instead of the 44x44 land art whenever a
/// tile's corners sit at different heights: a stretched quad needs a square texture, not a
/// pre-drawn diamond. Stored raw (no RLE) as either 64x64 or 128x128, flagged by index Extra.
/// </summary>
public sealed class TexmapFile : IDisposable
{
    private readonly IndexedMulFile _file;
    private readonly Dictionary<int, Sprite> _cache = new();

    public TexmapFile(string indexPath, string dataPath)
    {
        _file = new IndexedMulFile(indexPath, dataPath);
    }

    public bool IsValid(ushort id) => _file.IsValid(id);

    public Sprite Get(ushort id)
    {
        if (_cache.TryGetValue(id, out var cached))
            return cached;

        var sprite = Decode(_file.GetEntry(id), _file.GetData(id));
        _cache[id] = sprite;
        return sprite;
    }

    private static Sprite Decode(MulIndexEntry entry, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return Sprite.Empty;

        int size = entry.Extra == 0 ? 64 : 128;
        if (data.Length < size * size * 2)
            return Sprite.Empty;

        var src = MemoryMarshal.Cast<byte, ushort>(data);
        var sprite = new Sprite(size, size);

        // Terrain textures are fully opaque; colour 0 here means black, not transparent.
        for (int i = 0; i < size * size; i++)
            sprite.Pixels[i] = Color16.ToRgbaOpaque(src[i]);

        return sprite;
    }

    public void Dispose() => _file.Dispose();
}
