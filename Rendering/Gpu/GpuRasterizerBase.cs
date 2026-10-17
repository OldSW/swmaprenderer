using System.Numerics;
using System.Runtime.InteropServices;
using SwMapRenderer.Assets;

namespace SwMapRenderer.Rendering.Gpu;

/// <summary>
/// Everything a GPU <see cref="IRasterizer"/> does that is not talking to a particular API.
/// Drawing a triangle only records it; the pixels are produced in one dispatch when the
/// framebuffer is read back, which is the only time anything needs them.
///
/// Recording does the part that has to stay exact on the CPU, in double: vertices are snapped to
/// 1/256 px. A shared canvas shifted by whole pixels snaps identically, so the kernel's integer
/// edge functions give two slices the same coverage and the same barycentrics along their shared
/// edge. See raster.cl and raster.comp, which are the same program in two languages.
///
/// Triangles are binned into 16x16 pixel tiles in draw order, which is what lets a work item
/// replay just the triangles that can touch its pixel, in the order the software rasterizer
/// would have drawn them.
/// </summary>
internal abstract unsafe class GpuRasterizerBase : IRasterizer
{
    protected const int TileShift = 4;
    private const int Fixed = 256;

    /// <summary>
    /// Mirrors <c>Tri</c> in the kernels field for field; all four-byte members, so there is no
    /// padding to disagree about.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    protected struct Tri
    {
        public int X0, X1, X2;
        public int Y0, Y1, Y2;
        public float Z0, Z1, Z2;
        public Vector3 Texture0, Texture1, Texture2;
        public Vector4 Hue0, Hue1, Hue2;
        public Vector3 Normal0, Normal1, Normal2;
        public int SpriteOffset, SpriteWidth, SpriteHeight, Flags, Pad;
    }

    /// <summary>One frame's worth of input, laid out as the kernels read it.</summary>
    protected readonly ref struct Frame
    {
        public ReadOnlySpan<byte> Triangles { get; init; }
        public ReadOnlySpan<int> BinStart { get; init; }
        public ReadOnlySpan<int> BinList { get; init; }
        public ReadOnlySpan<uint> Sprites { get; init; }

        /// <summary>
        /// The 32-colour gradients, RGBA, hue after hue. Empty if nothing was drawn. The same
        /// array for the whole run, so a backend can tell by reference that it is already there.
        /// </summary>
        public uint[] Hues { get; init; }

        public int HueCount { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int TilesX { get; init; }
        public Vector4 Clear { get; init; }
    }

    private Tri[] _tris = new Tri[4096];
    private int _triCount;

    // Each triangle's inclusive tile range, kept from the counting pass for the filling one.
    private int[] _tiles = new int[4 * 4096];

    private uint[] _pixels = new uint[1 << 16];
    private int _pixelCount;
    private readonly Dictionary<Sprite, int> _spriteOffsets = new();
    private Sprite? _lastSprite;
    private int _lastOffset;

    private Vector4 _clear;
    private HuesFile? _huesSource;
    private HuesFile? _huesBuilt;
    private uint[] _hueTable = [];

    public int Width { get; }
    public int Height { get; }

    protected int TilesX => (Width + (1 << TileShift) - 1) >> TileShift;
    protected int TilesY => (Height + (1 << TileShift) - 1) >> TileShift;

    protected GpuRasterizerBase(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Runs the kernel over the frame and writes straight-alpha RGBA, Width x Height x 4 bytes.</summary>
    protected abstract void Dispatch(in Frame frame, Span<byte> rgba);

    public void Clear(Vector4 color)
    {
        _clear = color;
        _triCount = 0;
        _pixelCount = 0;
        _spriteOffsets.Clear();
        _lastSprite = null;
    }

    public void DrawTriangle(in ScreenVertex v0, in ScreenVertex v1, in ScreenVertex v2, Sprite texture,
        MapEffect effect)
    {
        if (texture.IsEmpty)
            return;

        // The same rejection the software rasterizer makes, in the same precision.
        double area = (v1.X - v0.X) * (v2.Y - v0.Y) - (v1.Y - v0.Y) * (v2.X - v0.X);
        if (Math.Abs(area) < 1e-9)
            return;

        // Anything this far off the canvas cannot be a sprite's own corner; skip it rather than
        // overflow the fixed-point range.
        if (!InRange(v0) || !InRange(v1) || !InRange(v2))
            return;

        int x0 = Snap(v0.X), y0 = Snap(v0.Y);
        int x1 = Snap(v1.X), y1 = Snap(v1.Y);
        int x2 = Snap(v2.X), y2 = Snap(v2.Y);

        long snapped = (long)(x1 - x0) * (y2 - y0) - (long)(y1 - y0) * (x2 - x0);
        if (snapped == 0)
            return;

        // Pixels whose centres can lie inside: 256x + 128 within [min, max].
        int px0 = Math.Max(0, (Math.Min(x0, Math.Min(x1, x2)) + (Fixed / 2 - 1)) >> 8);
        int px1 = Math.Min(Width - 1, (Math.Max(x0, Math.Max(x1, x2)) - Fixed / 2) >> 8);
        int py0 = Math.Max(0, (Math.Min(y0, Math.Min(y1, y2)) + (Fixed / 2 - 1)) >> 8);
        int py1 = Math.Min(Height - 1, (Math.Max(y0, Math.Max(y1, y2)) - Fixed / 2) >> 8);
        if (px0 > px1 || py0 > py1)
            return;

        if (_triCount == _tris.Length)
        {
            Array.Resize(ref _tris, _tris.Length * 2);
            Array.Resize(ref _tiles, _tiles.Length * 2);
        }

        int t = _triCount++;
        ref var tri = ref _tris[t];
        tri.X0 = x0; tri.X1 = x1; tri.X2 = x2;
        tri.Y0 = y0; tri.Y1 = y1; tri.Y2 = y2;
        tri.Z0 = v0.Z; tri.Z1 = v1.Z; tri.Z2 = v2.Z;
        tri.Texture0 = v0.Texture; tri.Texture1 = v1.Texture; tri.Texture2 = v2.Texture;
        tri.Hue0 = v0.Hue; tri.Hue1 = v1.Hue; tri.Hue2 = v2.Hue;
        tri.Normal0 = v0.Normal; tri.Normal1 = v1.Normal; tri.Normal2 = v2.Normal;
        tri.SpriteOffset = Register(texture);
        tri.SpriteWidth = texture.Width;
        tri.SpriteHeight = texture.Height;
        tri.Flags = (effect.CurrentTechnique == Technique.Statics ? 1 : 0)
                    | (v0.Hue == v1.Hue && v0.Hue == v2.Hue ? 2 : 0);

        _tiles[t * 4] = px0 >> TileShift;
        _tiles[t * 4 + 1] = py0 >> TileShift;
        _tiles[t * 4 + 2] = px1 >> TileShift;
        _tiles[t * 4 + 3] = py1 >> TileShift;

        _huesSource = effect.Hues;
    }

    private static bool InRange(in ScreenVertex v) => Math.Abs(v.X) < 1e5 && Math.Abs(v.Y) < 1e5;

    private static int Snap(double v) => (int)Math.Floor(v * Fixed + 0.5);

    /// <summary>Where a sprite's pixels start in the frame's sprite buffer, adding them on first use.</summary>
    private int Register(Sprite sprite)
    {
        if (ReferenceEquals(sprite, _lastSprite))
            return _lastOffset;

        if (!_spriteOffsets.TryGetValue(sprite, out int offset))
        {
            offset = _pixelCount;
            if (offset + sprite.Pixels.Length > _pixels.Length)
                Array.Resize(ref _pixels, Math.Max(_pixels.Length * 2, offset + sprite.Pixels.Length));

            Array.Copy(sprite.Pixels, 0, _pixels, offset, sprite.Pixels.Length);
            _pixelCount += sprite.Pixels.Length;
            _spriteOffsets[sprite] = offset;
        }

        _lastSprite = sprite;
        _lastOffset = offset;
        return offset;
    }

    public void CopyTo(Span<byte> rgba)
    {
        long bytes = (long)Width * Height * 4;
        if (rgba.Length < bytes)
            throw new ArgumentException("Destination buffer is too small.", nameof(rgba));

        int tilesX = TilesX, tilesY = TilesY, tileCount = tilesX * tilesY;

        // Counting pass, prefix sum, filling pass. Triangles are visited in draw order in the
        // last one, so each tile's list is in draw order too.
        var binStart = new int[tileCount + 1];
        for (int t = 0; t < _triCount; t++)
        {
            int tx0 = _tiles[t * 4], ty0 = _tiles[t * 4 + 1], tx1 = _tiles[t * 4 + 2], ty1 = _tiles[t * 4 + 3];
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                    binStart[ty * tilesX + tx + 1]++;
        }

        for (int i = 0; i < tileCount; i++)
            binStart[i + 1] += binStart[i];

        var binList = new int[Math.Max(binStart[tileCount], 1)];
        var cursor = new int[tileCount];
        Array.Copy(binStart, cursor, tileCount);
        for (int t = 0; t < _triCount; t++)
        {
            int tx0 = _tiles[t * 4], ty0 = _tiles[t * 4 + 1], tx1 = _tiles[t * 4 + 2], ty1 = _tiles[t * 4 + 3];
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                    binList[cursor[ty * tilesX + tx]++] = t;
        }

        // Rebuilt only when the hue file changes, which is never within a run.
        if (_huesSource != null && !ReferenceEquals(_huesSource, _huesBuilt))
        {
            var hues = _huesSource;
            var table = new uint[hues.HueCount * HuesFile.ColorsPerHue];
            for (int h = 0; h < hues.HueCount; h++)
                for (int i = 0; i < HuesFile.ColorsPerHue; i++)
                    table[h * HuesFile.ColorsPerHue + i] = hues.GetColor(h, i);

            _hueTable = table;
            _huesBuilt = hues;
        }

        var frame = new Frame
        {
            Triangles = MemoryMarshal.AsBytes(_tris.AsSpan(0, _triCount)),
            BinStart = binStart,
            BinList = binList,
            Sprites = _pixels.AsSpan(0, _pixelCount),
            Hues = _hueTable,
            HueCount = _huesSource == null ? 0 : _huesSource.HueCount,
            Width = Width,
            Height = Height,
            TilesX = tilesX,
            Clear = _clear,
        };

        Dispatch(frame, rgba);
    }
}
