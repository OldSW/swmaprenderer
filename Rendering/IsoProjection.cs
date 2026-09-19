using static SwMapRenderer.Constants;

namespace SwMapRenderer.Rendering;

/// <summary>
/// The camera transform, in closed form, plus its inverse.
///
/// CentrED builds this as a matrix product -- look-at, mirror in x, an oblique shear folding
/// height into the screen's y axis, a translation, an orthographic projection and a zoom. That
/// is the right shape for handing to a vertex stage, but it is the wrong shape here for two
/// reasons, so the algebra is worked through once instead.
///
/// The first is that a matrix tells you nothing about which tiles land on a given piece of
/// canvas, which is what <see cref="VisibleTiles"/> needs.
///
/// The second is precision. Evaluated per slice in single precision, the matrix chain places
/// the same world point a fraction of a pixel differently depending on the canvas it was built
/// for -- the viewport step alone, (ndc * 0.5 + 0.5) * width, rounds differently at different
/// widths. Tile edges run at exactly 45 degrees, so a shift of 1e-4 px is enough to hand a
/// whole diagonal of pixel centres to the neighbouring tile, and independently rendered slices
/// stop agreeing along their shared edge. Expressed in closed form the origin is an explicit
/// term, so a slice is the global canvas shifted by a whole number of pixels and identical
/// geometry lands on identical sub-pixel positions everywhere.
///
/// Substituting the camera's basis into its own matrices leaves:
///
///     screenX = RSQRT2 * (wx - wy) * zoom + originX
///     screenY = (RSQRT2 * (wx + wy) - wz) * zoom + originY
///     depth   = (CameraHeight - wz) / DepthRange
///
/// Depth carries no zoom or canvas term at all, which is why it can be compared across slices.
/// In tile units RSQRT2 * TILE_SIZE is exactly 22 -- half a tile's 44px width -- so one step in
/// tx moves a tile 22px right and 22px down, and one step in ty moves it 22px left and 22px
/// down. The projection therefore only ever sees a tile through the two diagonals
///
///     A = tx - ty        (across the screen)
///     B = tx + ty        (down the screen, before height)
///
/// which always share a parity, since A + B = 2 * tx.
/// </summary>
public readonly struct IsoProjection
{
    /// <summary>Half a tile's on-screen width; the screen distance of one step in A or B.</summary>
    public const double HalfTileWidth = 22.0;

    /// <summary>Height the camera sits at, and the far plane. CentrED's 128 * 6 and 128 * 12.</summary>
    private const double CameraHeight = 128 * 6;
    private const double DepthRange = 128 * 12;

    /// <summary>Canvas pixel that world (0, 0, 0) projects to.</summary>
    public double OriginX { get; }
    public double OriginY { get; }
    public double Zoom { get; }

    private IsoProjection(double originX, double originY, double zoom)
    {
        OriginX = originX;
        OriginY = originY;
        Zoom = zoom;
    }

    /// <summary>
    /// The projection for a canvas of the given size centred on a world position, equivalent to
    /// pointing CentrED's camera at that position.
    /// </summary>
    public static IsoProjection Centred(double centerWorldX, double centerWorldY, double zoom,
        int canvasWidth, int canvasHeight) =>
        new(canvasWidth / 2.0 - RSQRT2 * (centerWorldX - centerWorldY) * zoom,
            canvasHeight / 2.0 - RSQRT2 * (centerWorldX + centerWorldY) * zoom,
            zoom);

    /// <summary>
    /// The projection for a window onto a larger canvas. Both offsets being whole pixels is
    /// what makes slices agree.
    /// </summary>
    public static IsoProjection AtOrigin(double originX, double originY, double zoom) =>
        new(originX, originY, zoom);

    /// <summary>This projection shifted so it renders the given window of the same canvas.</summary>
    public IsoProjection Translate(double dx, double dy) => new(OriginX - dx, OriginY - dy, Zoom);

    public double ScreenX(double worldX, double worldY) =>
        RSQRT2 * (worldX - worldY) * Zoom + OriginX;

    public double ScreenY(double worldX, double worldY, double worldZ) =>
        (RSQRT2 * (worldX + worldY) - worldZ) * Zoom + OriginY;

    /// <summary>
    /// Depth in the [0, 1] range the depth buffer compares in. Larger is further away, which is
    /// why taller geometry wins under a Less test.
    /// </summary>
    public double Depth(double worldZ) => (CameraHeight - worldZ) / DepthRange;

    public double ScreenXFromA(double a) => HalfTileWidth * a * Zoom + OriginX;

    public double ScreenYFromB(double b, double worldZ) => (HalfTileWidth * b - worldZ) * Zoom + OriginY;

    public double DiagonalA(double screenX) => (screenX - OriginX) / (HalfTileWidth * Zoom);

    public double DiagonalB(double screenY, double worldZ) =>
        ((screenY - OriginY) / Zoom + worldZ) / HalfTileWidth;

    /// <summary>
    /// Every tile whose geometry can reach the given canvas rectangle.
    ///
    /// A tile draws well outside its own footprint. Its land quad is built from the corners it
    /// shares with the neighbours at +x and +y, so it reaches a tile back in each diagonal; and
    /// a static standing on it is drawn with its base at the tile and its sprite rising up the
    /// screen, so a tall graphic far below the canvas still paints into it. The rectangle is
    /// therefore widened by the largest sprite in art.mul and by the whole permitted height
    /// range before being inverted -- on this client a 180x485 sprite over a z range of
    /// -512..508 world units, which reaches about a thousand pixels up the screen.
    ///
    /// Stopping short is exactly what makes independently rendered neighbours disagree along a
    /// shared edge, so the bound is deliberately conservative: it costs vertex setup for tiles
    /// that turn out to miss the canvas, which the rasterizer then rejects cheaply.
    /// </summary>
    public TileRhombus VisibleTiles(
        double x0, double y0, double x1, double y1,
        int minZ, int maxZ,
        int maxSpriteWidth, int maxSpriteHeight)
    {
        double worldZLo = minZ * TILE_Z_SCALE;
        double worldZHi = maxZ * TILE_Z_SCALE;

        // A static is centred on its tile and so overhangs by half a sprite either side; a land
        // quad overhangs by a whole tile. Whichever reaches further sets the bound.
        double overhangA = Math.Max(1.0, maxSpriteWidth / (2.0 * HalfTileWidth));

        double aMin = DiagonalA(x0) - overhangA;
        double aMax = DiagonalA(x1) + overhangA;

        // Downward: the lowest tile that can still poke up into the rectangle.
        double bMin = DiagonalB(y0, worldZLo);

        // Upward: a land quad's far corners sit two B steps back, and a sprite's base sits its
        // own height below the top of the rectangle.
        double bMax = DiagonalB(y1, worldZHi + maxSpriteHeight) + 2.0;

        return new TileRhombus(
            (int)Math.Floor(aMin), (int)Math.Ceiling(aMax),
            (int)Math.Floor(bMin), (int)Math.Ceiling(bMax));
    }
}

/// <summary>
/// A tile range expressed in the projection's diagonals: a screen-aligned rectangle, which in
/// tile space is a rhombus. Iterating it visits each tile exactly once, ordered down the screen.
///
/// The order depends only on the tiles' own coordinates and not on the rectangle that produced
/// the range, which is the other half of making slices agree. Two overlapping slices visit any
/// two shared tiles in the same relative order, and a tile one of them omits is one that could
/// not have reached its canvas anyway.
/// </summary>
public readonly struct TileRhombus(int aMin, int aMax, int bMin, int bMax)
{
    public int AMin { get; } = aMin;
    public int AMax { get; } = aMax;
    public int BMin { get; } = bMin;
    public int BMax { get; } = bMax;

    /// <summary>Upper bound on the tiles visited, before map bounds are applied.</summary>
    public int ApproximateCount => Math.Max(0, (AMax - AMin + 1) * (BMax - BMin + 1) / 2);

    /// <summary>
    /// Whether any tile of the facet can fall in this range, decided without iterating.
    ///
    /// Worth having because the facet is a diamond on screen while slices are laid out on its
    /// bounding box: for a whole facet about half of them are pure ocean-less void, and
    /// discovering that by rendering one and finding it empty costs a full canvas each time.
    ///
    /// In these diagonals the range is an axis-aligned rectangle and the facet is a
    /// parallelogram, bounded by A + B (which is 2x) and B - A (which is 2y). Both are convex
    /// and between them they have only these four edge directions, so overlapping on all four
    /// is exactly equivalent to intersecting. Parity is ignored, which can only keep a slice
    /// that turns out to be empty -- never discard one that is not.
    /// </summary>
    public bool CouldContainFacetTile(int mapWidth, int mapHeight)
    {
        if (AMax < -(mapHeight - 1) || AMin > mapWidth - 1)
            return false;

        if (BMax < 0 || BMin > mapWidth + mapHeight - 2)
            return false;

        if (AMax + BMax < 0 || AMin + BMin > 2 * (mapWidth - 1))
            return false;

        if (BMax - AMin < 0 || BMin - AMax > 2 * (mapHeight - 1))
            return false;

        return true;
    }

    /// <summary>
    /// Visits the tiles of the rhombus, clipped to a facet of the given size, ordered by B and
    /// then A -- down the screen, then across it.
    /// </summary>
    public IEnumerable<(int X, int Y)> Iterate(int mapWidth, int mapHeight)
    {
        for (int b = BMin; b <= BMax; b++)
        {
            // A and B share a parity, since A + B = 2 * tx. Starting on the wrong one would
            // step over every tile in the row.
            int aStart = AMin;
            if (((aStart + b) & 1) != 0)
                aStart++;

            for (int a = aStart; a <= AMax; a += 2)
            {
                int x = (a + b) / 2;
                int y = (b - a) / 2;

                if (x >= 0 && x < mapWidth && y >= 0 && y < mapHeight)
                    yield return (x, y);
            }
        }
    }
}
