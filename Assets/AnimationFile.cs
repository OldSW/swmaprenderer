using System.Collections.Concurrent;

namespace SwMapRenderer.Assets;

/// <summary>
/// One decoded animation frame.
///
/// Unlike art.mul sprites, animation frames are not anchored at their own bottom centre: the
/// artist's reference point is stored with the frame, and the client subtracts it to find where
/// the image goes. <see cref="CenterX"/> is measured from the left edge and <see cref="CenterY"/>
/// up from the bottom, both in pixels.
/// </summary>
public sealed class AnimationFrame(Sprite sprite, int centerX, int centerY)
{
    public static readonly AnimationFrame Empty = new(Sprite.Empty, 0, 0);

    public Sprite Sprite { get; } = sprite;
    public int CenterX { get; } = centerX;
    public int CenterY { get; } = centerY;

    public bool IsEmpty => Sprite.IsEmpty;
}

/// <summary>The size and anchor of a frame, read from its header without decoding the pixels.</summary>
public readonly record struct AnimationFrameSize(int Width, int Height, int CenterX, int CenterY);

/// <summary>
/// anim.mul and its four successors -- every frame of every creature, and of every piece of
/// equipment a human body can wear.
///
/// An entry is one body in one action facing one way: a 256 colour palette, then a table of
/// frame offsets, then the frames. Only the first frame of each entry is ever wanted here,
/// since a still map has nothing to animate.
///
/// A frame is run-length encoded, but unlike art.mul the runs are not row-ordered: each carries
/// its own position, packed into a 32-bit header as (x, y, length) biased by 0x200 so the
/// terminator 0x7FFF7FFF cannot collide with a real run. Ported from the Ultima SDK's
/// Animations.Frame, the reader UOFiddler uses.
/// </summary>
public sealed class AnimationFile : IDisposable
{
    /// <summary>Unbiases both packed coordinates in a run header at once.</summary>
    private const int DoubleXor = (0x200 << 22) | (0x200 << 12);

    private const int Terminator = 0x7FFF7FFF;
    private const int PaletteBytes = 256 * 2;

    /// <summary>Sanity bound for decoding; unused index slots hold arbitrary bytes.</summary>
    private const int MaxFrameDimension = 1024;

    private readonly IndexedMulFile?[] _files;
    private readonly ConcurrentDictionary<int, AnimationFrame> _frames = new();
    private readonly long _budgetBytes;
    private long _bytes;

    /// <summary>Entries in anim.idx, which is what sizes the body translation table.</summary>
    public int BaseEntryCount => _files[0]?.Count ?? 0;

    /// <param name="paths">anim.idx/anim.mul pairs for files 1-5; null for any that is missing.</param>
    public AnimationFile(IReadOnlyList<(string Index, string Data)?> paths, long cacheBudgetBytes)
    {
        _budgetBytes = cacheBudgetBytes;
        _files = new IndexedMulFile?[5];

        for (int i = 0; i < 5 && i < paths.Count; i++)
        {
            if (paths[i] is { } pair)
                _files[i] = new IndexedMulFile(pair.Index, pair.Data);
        }
    }

    public bool HasFile(int file) => file >= 1 && file <= 5 && _files[file - 1] != null;

    public bool IsValid(int file, int index) =>
        HasFile(file) && index >= 0 && _files[file - 1]!.IsValid(index);

    /// <summary>
    /// The first frame of an entry, mirrored if asked. Empty for an entry the file does not
    /// define, which is the normal answer for an action a body was never drawn in.
    /// </summary>
    public AnimationFrame GetFrame(int file, int index, bool flip)
    {
        if (!HasFile(file) || index < 0)
            return AnimationFrame.Empty;

        int key = ((index * 5 + (file - 1)) << 1) | (flip ? 1 : 0);

        if (_frames.TryGetValue(key, out var cached))
            return cached;

        var frame = Decode(_files[file - 1]!.GetData(index), flip);

        if (_frames.TryAdd(key, frame))
        {
            long size = (long)frame.Sprite.Pixels.Length * sizeof(uint);
            if (Interlocked.Add(ref _bytes, size) > _budgetBytes)
            {
                _frames.Clear();
                Interlocked.Exchange(ref _bytes, 0);
            }
        }

        return frame;
    }

    /// <summary>
    /// A frame's dimensions without decoding it. The view range needs to know how far a mobile
    /// can draw outside its own tile before anything is drawn at all, and reading the header is
    /// a few bytes against a frame's worth of RLE.
    /// </summary>
    public AnimationFrameSize? Measure(int file, int index)
    {
        if (!HasFile(file) || index < 0)
            return null;

        var span = _files[file - 1]!.GetData(index);
        if (!TryReadFrameHeader(span, out int offset, out var size) || offset < 0)
            return null;

        return size;
    }

    private static bool TryReadFrameHeader(ReadOnlySpan<byte> span, out int pixelOffset,
        out AnimationFrameSize size)
    {
        pixelOffset = -1;
        size = default;

        if (span.Length < PaletteBytes + 8)
            return false;

        int frameCount = BitConverter.ToInt32(span[PaletteBytes..]);
        if (frameCount <= 0)
            return false;

        // Frame offsets are relative to the start of the count, not to the start of the entry.
        int start = PaletteBytes + BitConverter.ToInt32(span[(PaletteBytes + 4)..]);
        if (start < 0 || start + 8 > span.Length)
            return false;

        int centerX = BitConverter.ToInt16(span[start..]);
        int centerY = BitConverter.ToInt16(span[(start + 2)..]);
        int width = BitConverter.ToUInt16(span[(start + 4)..]);
        int height = BitConverter.ToUInt16(span[(start + 6)..]);

        if (width <= 0 || width > MaxFrameDimension || height <= 0 || height > MaxFrameDimension)
            return false;

        pixelOffset = start + 8;
        size = new AnimationFrameSize(width, height, centerX, centerY);
        return true;
    }

    private static AnimationFrame Decode(ReadOnlySpan<byte> span, bool flip)
    {
        if (!TryReadFrameHeader(span, out int p, out var size))
            return AnimationFrame.Empty;

        (int width, int height, int centerX, int centerY) = size;

        Span<ushort> palette = stackalloc ushort[256];
        for (int i = 0; i < 256; i++)
        {
            // The stored colour has its alpha bit cleared; the client flips it back on, which
            // is what makes an entry of 0x0000 opaque black rather than the transparency key.
            palette[i] = (ushort)(BitConverter.ToUInt16(span[(i * 2)..]) ^ 0x8000);
        }

        var sprite = new Sprite(width, height);
        var pixels = sprite.Pixels;

        // Runs are positioned relative to the frame's anchor, which sits outside the bitmap.
        int xBase = centerX - 0x200;
        int yBase = centerY + height - 0x200;

        while (p + 4 <= span.Length)
        {
            int header = BitConverter.ToInt32(span[p..]);
            p += 4;

            if (header == Terminator)
                break;

            header ^= DoubleXor;

            int run = header & 0xFFF;
            int y = ((header >> 12) & 0x3FF) + yBase;
            int x = ((header >> 22) & 0x3FF) + xBase;

            if (p + run > span.Length)
                break;

            if (y >= 0 && y < height)
            {
                int row = y * width;

                // Mirroring is done here rather than by sampling backwards later, so that the
                // rest of the renderer never has to know a sprite was flipped.
                if (flip)
                {
                    for (int i = 0, dx = width - 1 - x; i < run; i++, dx--)
                    {
                        if (dx >= 0 && dx < width)
                            pixels[row + dx] = Color16.ToRgbaOpaque(palette[span[p + i]]);
                    }
                }
                else
                {
                    for (int i = 0, dx = x; i < run; i++, dx++)
                    {
                        if (dx >= 0 && dx < width)
                            pixels[row + dx] = Color16.ToRgbaOpaque(palette[span[p + i]]);
                    }
                }
            }

            p += run;
        }

        // A mirrored frame's anchor is measured from the other edge.
        return new AnimationFrame(sprite, flip ? width - centerX : centerX, centerY);
    }

    public void Dispose()
    {
        foreach (var file in _files)
            file?.Dispose();
    }
}
