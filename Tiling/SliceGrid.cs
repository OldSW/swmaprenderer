using SwMapRenderer.Map;
using SwMapRenderer.Rendering;
using static SwMapRenderer.Constants;

namespace SwMapRenderer.Tiling;

/// <summary>
/// Maps a facet onto one absolute isometric canvas and cuts that canvas into square slices.
///
/// Slicing happens in canvas space rather than tile space, and that is a deliberate choice. A
/// 16x16-tile region is a *diamond* on screen, not a square: one step in tx moves a tile 22px
/// right and 22px down, one step in ty moves it 22px left and 22px down. Diamonds interlock on
/// a staggered grid, which no standard web map layer can address. The square that bounds a
/// 16x16-tile diamond is 704x704 px, so that is the slice: each one carries 16x16 tiles' worth
/// of canvas and the grid is one a tile layer can index directly.
///
/// The canvas is sized to hold every pixel a tile can paint, which is more than the tiles' own
/// footprint -- a land quad reaches a tile back along each diagonal, and a static reaches half a
/// sprite either side and a sprite's height plus the whole z range up the screen.
///
/// The origin is deliberately a whole number of pixels. Every slice is then the same canvas
/// shifted by a whole number of pixels, which is the condition <see cref="IsoProjection"/>
/// needs to place geometry identically in neighbouring slices.
/// </summary>
public sealed class SliceGrid
{
    /// <summary>Extremes of world z, in world units. Fixed rather than taken from the z filter,
    /// so that narrowing the z range does not move the canvas underneath an existing pyramid.</summary>
    private const int WorldZLo = -128 * (int)TILE_Z_SCALE;
    private const int WorldZHi = 127 * (int)TILE_Z_SCALE;

    public MapDimensions Map { get; }
    public int TilesPerSlice { get; }
    public double NativeZoom { get; }

    /// <summary>Slice edge in pixels: the bounding square of a TilesPerSlice-square diamond.</summary>
    public int SliceSize { get; }

    public int CanvasWidth { get; }
    public int CanvasHeight { get; }

    /// <summary>Canvas pixel that world (0, 0, 0) projects to. In a north-up grid, the canvas
    /// pixel of tile (0, 0) at ground level.</summary>
    public int OriginX { get; }
    public int OriginY { get; }

    /// <summary>Deepest level, at which one canvas pixel is one image pixel.</summary>
    public int MaxZoom { get; }

    /// <summary>
    /// Whether the canvas is turned to north-up. See <see cref="NorthUpSampler"/> for how the
    /// slices are drawn; this class only lays out the canvas they are cut from.
    /// </summary>
    public bool NorthUp { get; }

    /// <summary>
    /// How far, in canvas pixels, a north-up canvas keeps clear of the facet on every side. A
    /// north-up grid has no isometric margin to borrow from, so it carries its own. Zero for the
    /// isometric grid.
    /// </summary>
    public double NorthUpReach { get; }

    /// <summary>Canvas pixels between the centres of two neighbouring tiles in one direction at
    /// ground level. Twice the half-width of an isometric tile, or √2 times it when turned.</summary>
    public double TileStep { get; }

    public SliceGrid(MapDimensions map, int tilesPerSlice, double nativeZoom,
        int maxStaticWidth, int maxStaticHeight, bool northUp = false)
    {
        Map = map;
        TilesPerSlice = tilesPerSlice;
        NativeZoom = nativeZoom;
        NorthUp = northUp;
        SliceSize = Math.Max(1, (int)Math.Round(tilesPerSlice * (double)ArtTileWidth * nativeZoom));

        double halfTile = IsoProjection.HalfTileWidth * nativeZoom;

        // Across the screen the extremes are A = tx - ty, over [-(H-1), W-1], widened by the one
        // tile a land quad reaches back and by half the widest sprite.
        double spriteHalfWidth = maxStaticWidth / 2.0 * nativeZoom;

        if (northUp)
        {
            // Turning the isometric plane by a quarter-octant makes the tile grid axis-aligned,
            // with tile (tx, ty) at (step * tx, step * ty) from the origin. That makes the facet a
            // rectangle, so no slice is void for the diamond's sake, as it is in the isometric grid.
            //
            // What the facet paints outside its own footprint is bounded in the isometric plane by
            // one tile across and the sprite's width, and by the whole z range plus the tallest
            // sprite up and down. Turning that box can only grow each axis by the sum of its two
            // extents over √2, so that is the reach.
            TileStep = Math.Sqrt(2) * halfTile;
            double reachX = 2 * halfTile + spriteHalfWidth;
            double reachY = 2 * halfTile + Math.Max((WorldZHi + maxStaticHeight) * nativeZoom, -WorldZLo * nativeZoom);
            NorthUpReach = Math.Ceiling((reachX + reachY) / Math.Sqrt(2));

            OriginX = (int)NorthUpReach;
            OriginY = (int)NorthUpReach;
            CanvasWidth = (int)Math.Ceiling(2 * NorthUpReach + TileStep * map.Width);
            CanvasHeight = (int)Math.Ceiling(2 * NorthUpReach + TileStep * map.Height);
        }
        else
        {
            // Down the screen it is B = tx + ty over [0, W + H - 2], less the two steps a land quad
            // reaches back, and offset by height: tall geometry paints up the screen, low geometry
            // down it.
            double minX = -halfTile * map.Height - spriteHalfWidth;
            double maxX = halfTile * map.Width + spriteHalfWidth;
            double minY = -2 * halfTile - (WorldZHi + maxStaticHeight) * nativeZoom;
            double maxY = halfTile * (map.Width + map.Height - 2) - WorldZLo * nativeZoom;

            TileStep = 2 * halfTile;
            OriginX = (int)Math.Ceiling(-minX);
            OriginY = (int)Math.Ceiling(-minY);
            CanvasWidth = OriginX + (int)Math.Ceiling(maxX);
            CanvasHeight = OriginY + (int)Math.Ceiling(maxY);
        }

        // Deepest level is the one at which the whole canvas still needs more than a single
        // slice to cover it.
        int span = Math.Max(CanvasWidth, CanvasHeight);
        MaxZoom = 0;
        while (SliceSize * (1L << MaxZoom) < span)
            MaxZoom++;
    }

    /// <summary>
    /// The square window of the isometric plane a north-up slice is resampled from. A slice is
    /// SliceSize square and turning it by 45 degrees gives a bounding square of SliceSize·√2, so
    /// that plus a margin of three pixels either side -- enough for the bilinear taps at the edge.
    /// </summary>
    public int SourceSize => (int)Math.Ceiling(SliceSize * Math.Sqrt(2)) + 2 * SourceMargin;

    /// <summary>Pixels of isometric window beyond the slice's own bounding square.</summary>
    public const int SourceMargin = 3;

    /// <summary>
    /// Canvas pixels the renderer fills for one slice, relative to the slice's own pixels. A
    /// north-up slice is not rendered on its own: it is sampled from isometric slices that are
    /// rendered once for both pyramids, so its render cost is the isometric one.
    /// </summary>
    public double RenderCostFactor => 1.0;

    /// <summary>A land tile's art is 44px wide, which is what sets the slice's pixel size.</summary>
    private const int ArtTileWidth = 44;

    /// <summary>Slices across and down at the given level.</summary>
    public int SlicesAcross(int zoom) => Divide(CanvasWidth, PixelsPerSlice(zoom));
    public int SlicesDown(int zoom) => Divide(CanvasHeight, PixelsPerSlice(zoom));

    /// <summary>Canvas pixels one slice covers at the given level.</summary>
    public long PixelsPerSlice(int zoom) => (long)SliceSize << (MaxZoom - zoom);

    private static int Divide(long total, long per) => (int)((total + per - 1) / per);

    /// <summary>
    /// Scale between the canvas and an image at the given level. 1 at the deepest level, and
    /// halving with each level above it.
    /// </summary>
    public double ScaleAtLevel(int level) => Math.Pow(2, level - MaxZoom);

    /// <summary>Canvas pixels between neighbouring tile centres at the given level.</summary>
    public double TilePixelsAtLevel(int level) => TileStep * ScaleAtLevel(level);

    /// <summary>
    /// Map tiles one slice covers at the given level. Constant in pixels but not in tiles: a
    /// slice above the deepest level is the same image covering four times the ground per step,
    /// so its geometry cost grows even though its fill cost does not.
    /// </summary>
    public long TilesCoveredPerSlice(int level)
    {
        long edge = (long)TilesPerSlice << (MaxZoom - level);
        return edge * edge;
    }

    /// <summary>
    /// The projection that renders slice (x, y) of the given level directly.
    ///
    /// Above the deepest level the whole canvas is scaled down, which leaves the origin on a
    /// fractional pixel. That is harmless: the fraction is the same for every slice of the
    /// level, so neighbours are still offset from one another by whole multiples of the slice
    /// size, which is the property that keeps them seamless.
    /// </summary>
    public IsoProjection ProjectionFor(int level, int sliceX, int sliceY)
    {
        double scale = ScaleAtLevel(level);

        return IsoProjection.AtOrigin(
            OriginX * scale - (double)sliceX * SliceSize,
            OriginY * scale - (double)sliceY * SliceSize,
            NativeZoom * scale);
    }

    /// <summary>
    /// Whether slice (x, y) of a level can hold any of the facet, decided geometrically.
    /// Slices are laid out on the bounding box of the canvas, and the facet inside it is a
    /// diamond, so for a whole-facet run about half of them are void.
    /// </summary>
    public bool SliceTouchesFacet(int level, int sliceX, int sliceY, int minZ, int maxZ,
        int maxStaticWidth, int maxStaticHeight)
    {
        if (NorthUp)
        {
            // The facet is an axis-aligned rectangle here, so the test is a rectangle overlap.
            // The z range is not narrowed for this, which only ever keeps a slice that is empty.
            double per = PixelsPerSlice(level);
            double x0 = sliceX * per, y0 = sliceY * per;
            return x0 < OriginX + TileStep * Map.Width + NorthUpReach
                && x0 + per > OriginX - NorthUpReach
                && y0 < OriginY + TileStep * Map.Height + NorthUpReach
                && y0 + per > OriginY - NorthUpReach;
        }

        var projection = ProjectionFor(level, sliceX, sliceY);
        var range = projection.VisibleTiles(
            0, 0, SliceSize, SliceSize, minZ, maxZ, maxStaticWidth, maxStaticHeight);

        return range.CouldContainFacetTile(Map.Width, Map.Height);
    }

    /// <summary>
    /// Tile coordinates under a canvas pixel, assuming ground level. Used by the viewer to turn
    /// a cursor position back into something a player would recognise.
    /// </summary>
    public (double X, double Y) TileAt(double canvasX, double canvasY)
    {
        if (NorthUp)
            return ((canvasX - OriginX) / TileStep, (canvasY - OriginY) / TileStep);

        double a = (canvasX - OriginX) / (IsoProjection.HalfTileWidth * NativeZoom);
        double b = (canvasY - OriginY) / (IsoProjection.HalfTileWidth * NativeZoom);
        return ((a + b) / 2.0, (b - a) / 2.0);
    }

    /// <summary>Canvas pixel a tile's centre projects to at ground level.</summary>
    public (double X, double Y) CanvasAt(double tileX, double tileY)
    {
        if (NorthUp)
            return (TileStep * tileX + OriginX, TileStep * tileY + OriginY);

        double half = IsoProjection.HalfTileWidth * NativeZoom;
        return (half * (tileX - tileY) + OriginX, half * (tileX + tileY) + OriginY);
    }

    /// <summary>
    /// The isometric-plane point under a north-up canvas pixel: the inverse of the turn that
    /// takes the isometric plane to the north-up canvas, about the canvas origin. Canvas pixels
    /// and isometric pixels are the same size, so this is a pure rotation.
    /// </summary>
    public (double X, double Y) IsoAt(double northX, double northY)
    {
        const double RSqrt2 = 0.70710678118654752440;
        double dx = northX - OriginX, dy = northY - OriginY;
        return ((dx - dy) * RSqrt2, (dx + dy) * RSqrt2);
    }

    /// <summary>
    /// The slices of the given level that a tile rectangle can paint into. Inclusive.
    /// Conservative: it takes the bounding box of the region's four projected corners and pads
    /// by the same reach a view range does.
    /// </summary>
    public (int X0, int Y0, int X1, int Y1) SlicesForRegion(int level, int tileX0, int tileY0,
        int tileX1, int tileY1, int maxStaticWidth, int maxStaticHeight)
    {
        double per = PixelsPerSlice(level);

        if (NorthUp)
        {
            double nx0 = OriginX + TileStep * tileX0 - NorthUpReach, nx1 = OriginX + TileStep * (tileX1 + 1) + NorthUpReach;
            double ny0 = OriginY + TileStep * tileY0 - NorthUpReach, ny1 = OriginY + TileStep * (tileY1 + 1) + NorthUpReach;

            return (
                Math.Max(0, (int)Math.Floor(nx0 / per)),
                Math.Max(0, (int)Math.Floor(ny0 / per)),
                Math.Min(SlicesAcross(level) - 1, (int)Math.Floor(nx1 / per)),
                Math.Min(SlicesDown(level) - 1, (int)Math.Floor(ny1 / per)));
        }

        double half = IsoProjection.HalfTileWidth * NativeZoom;

        // A = tx - ty is extreme at opposite corners, B = tx + ty at the same ones.
        double minA = tileX0 - tileY1 - 1;
        double maxA = tileX1 - tileY0 + 1;
        double minB = tileX0 + tileY0 - 2;
        double maxB = tileX1 + tileY1;

        double x0 = half * minA + OriginX - maxStaticWidth / 2.0 * NativeZoom;
        double x1 = half * maxA + OriginX + maxStaticWidth / 2.0 * NativeZoom;
        double y0 = half * minB + OriginY - (WorldZHi + maxStaticHeight) * NativeZoom;
        double y1 = half * maxB + OriginY - WorldZLo * NativeZoom;

        // Expressed in canvas pixels, so the divisor is what one slice covers at this level.
        return (
            Math.Max(0, (int)Math.Floor(x0 / per)),
            Math.Max(0, (int)Math.Floor(y0 / per)),
            Math.Min(SlicesAcross(level) - 1, (int)Math.Floor(x1 / per)),
            Math.Min(SlicesDown(level) - 1, (int)Math.Floor(y1 / per)));
    }
}
