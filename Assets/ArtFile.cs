using System.Runtime.InteropServices;

namespace SwMapRenderer.Assets;

/// <summary>
/// art.mul / artidx.mul. Entries 0x0000-0x3FFF are land tiles, 0x4000-0xFFFF are statics.
/// The two use completely different encodings.
/// </summary>
public sealed class ArtFile : IDisposable
{
    public const int StaticOffset = 0x4000;
    public const int LandTileSize = 44;

    /// <summary>Sanity bound for decoding; unused index slots hold arbitrary bytes.</summary>
    private const int MaxSpriteDimension = 1024;

    private readonly IndexedMulFile _file;
    private readonly SpriteCache _cache;

    /// <summary>
    /// Largest static sprite in the file, in pixels.
    ///
    /// The renderer needs this to know how far outside its own tile a static can draw, which
    /// sets the margin on a view range. It is read from the entry headers rather than by
    /// decoding, so the whole scan is a few bytes per entry.
    /// </summary>
    public int MaxStaticWidth { get; }
    public int MaxStaticHeight { get; }

    public ArtFile(string indexPath, string dataPath, long cacheBudgetBytes)
    {
        _file = new IndexedMulFile(indexPath, dataPath);
        _cache = new SpriteCache(cacheBudgetBytes);

        for (int id = StaticOffset; id < _file.Count; id++)
        {
            var data = _file.GetData(id);
            if (data.Length < 8)
                continue;

            int width = BitConverter.ToUInt16(data[4..]);
            int height = BitConverter.ToUInt16(data[6..]);

            // Unused slots hold arbitrary bytes; the same sanity bound the decoder applies.
            if (width <= 0 || width > MaxSpriteDimension || height <= 0 || height > MaxSpriteDimension)
                continue;

            if (width > MaxStaticWidth) MaxStaticWidth = width;
            if (height > MaxStaticHeight) MaxStaticHeight = height;
        }
    }

    public bool IsValidLand(ushort id) => _file.IsValid(id);

    public bool IsValidStatic(ushort id) => _file.IsValid(StaticOffset + id);

    public Sprite GetLand(ushort id) => _cache.GetOrAdd(id, key => DecodeLand(_file.GetData(key)));

    public Sprite GetStatic(ushort id) =>
        _cache.GetOrAdd(StaticOffset + id, key => DecodeStatic(_file.GetData(key)));

    /// <summary>
    /// Land art is an untagged 44x44 diamond: 22 rows widening from 2 to 44 pixels, then 22
    /// rows narrowing back, each centred in the row. Pixels outside the diamond stay
    /// transparent, which is what gives terrain its rhombus shape.
    /// </summary>
    private static Sprite DecodeLand(ReadOnlySpan<byte> data)
    {
        // 2 * (2 + 4 + ... + 44) = 1012 pixels
        const int requiredPixels = 1012;
        if (data.Length < requiredPixels * 2)
            return Sprite.Empty;

        var src = MemoryMarshal.Cast<byte, ushort>(data);
        var sprite = new Sprite(LandTileSize, LandTileSize);
        var dst = sprite.Pixels;

        int read = 0;

        for (int y = 0; y < 22; y++)
        {
            int start = 22 - (y + 1);
            int count = (y + 1) * 2;
            int row = y * LandTileSize;
            for (int x = 0; x < count; x++)
                dst[row + start + x] = Color16.ToRgbaKeyed(src[read++]);
        }

        for (int y = 0; y < 22; y++)
        {
            int start = y;
            int count = (22 - y) * 2;
            int row = (y + 22) * LandTileSize;
            for (int x = 0; x < count; x++)
                dst[row + start + x] = Color16.ToRgbaKeyed(src[read++]);
        }

        return sprite;
    }

    /// <summary>
    /// Static art is run-length encoded: a header, a per-row offset table, then for each row a
    /// sequence of (xSkip, runLength) pairs followed by that many pixels, terminated by a pair
    /// of zeroes. Gaps between runs stay transparent.
    /// </summary>
    private static Sprite DecodeStatic(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return Sprite.Empty;

        int width = BitConverter.ToUInt16(data[4..]);
        int height = BitConverter.ToUInt16(data[6..]);

        // Guard against garbage entries; no legitimate static art exceeds these bounds.
        if (width <= 0 || width > MaxSpriteDimension || height <= 0 || height > MaxSpriteDimension)
            return Sprite.Empty;

        int lookupBytes = height * 2;
        if (data.Length < 8 + lookupBytes)
            return Sprite.Empty;

        var lookup = MemoryMarshal.Cast<byte, ushort>(data.Slice(8, lookupBytes));
        var words = MemoryMarshal.Cast<byte, ushort>(data);
        int dataStartWord = (8 + lookupBytes) / 2;

        var sprite = new Sprite(width, height);
        var dst = sprite.Pixels;

        for (int y = 0; y < height; y++)
        {
            int p = dataStartWord + lookup[y];
            int x = 0;

            while (true)
            {
                if (p + 1 >= words.Length)
                    break;

                int xSkip = words[p++];
                int runLength = words[p++];

                if (xSkip == 0 && runLength == 0)
                    break;

                x += xSkip;
                if (p + runLength > words.Length)
                    break;

                int row = y * width;
                for (int i = 0; i < runLength; i++, x++)
                {
                    if (x >= 0 && x < width)
                        dst[row + x] = Color16.ToRgbaKeyed(words[p + i]);
                }
                p += runLength;
            }
        }

        return sprite;
    }

    public void Dispose() => _file.Dispose();
}
