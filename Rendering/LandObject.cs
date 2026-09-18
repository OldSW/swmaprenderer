using System.Numerics;
using SwMapRenderer.Assets;
using SwMapRenderer.Map;
using static SwMapRenderer.Constants;

namespace SwMapRenderer.Rendering;

/// <summary>
/// One terrain tile, ported from CentrED's LandObject.
///
/// A land tile is a single quad whose four corners take their height from this tile and its
/// three neighbours to the south and east -- that shared-corner rule is what makes terrain
/// continuous instead of a field of floating diamonds. When those corners disagree the quad is
/// stretched, and a square terrain texture is used in place of the pre-drawn 44x44 diamond.
/// </summary>
public sealed class LandObject : MapObject
{
    private readonly MapScene _scene;

    public LandTile Tile { get; }

    public LandObject(MapScene scene, LandTile tile)
    {
        _scene = scene;
        Tile = tile;

        UpdateCorners(tile.Id);
        UpdateId(tile.Id);
    }

    /// <summary>Water and textureless tiles are never stretched, however their neighbours sit.</summary>
    private bool AlwaysFlat(ushort id)
    {
        ref readonly var tileData = ref _scene.Files.TileData.LandData[id];
        return tileData.TexId == 0 || tileData.IsWet;
    }

    private static bool IsFlat(float x, float y, float z, float w) => x == y && x == z && x == w;

    private void UpdateCorners(ushort id)
    {
        bool alwaysFlat = AlwaysFlat(id);
        bool flatView = _scene.Options.FlatView;

        Vector4 cornerZ = flatView
            ? Vector4.Zero
            : alwaysFlat
                ? new Vector4(Tile.Z * TILE_Z_SCALE)
                : GetCornerZ();

        // Tiles are drawn one tile up and to the left of their coordinate, matching the client.
        //
        // Each edge coordinate is derived straight from the tile index rather than as
        // "near edge + TILE_SIZE", which is how CentrED writes it. The two are not the same in
        // single precision: at Britain's coordinates (x-1)*TILE_SIZE + TILE_SIZE and
        // x*TILE_SIZE land one ULP apart, so neighbouring tiles disagreed about where their
        // shared corner was by about 0.005 of a pixel. Tile edges run at exactly 45 degrees,
        // which puts that sliver on top of a whole diagonal of pixel centres at once, and every
        // pixel along it fell outside both tiles -- drawing a one-pixel black seam clean across
        // the terrain. Deriving both tiles' shared coordinate from the same expression makes
        // them bit-identical, so the edge is genuinely shared.
        float nearX = (Tile.X - 1) * TILE_SIZE;
        float nearY = (Tile.Y - 1) * TILE_SIZE;
        float farX = Tile.X * TILE_SIZE;
        float farY = Tile.Y * TILE_SIZE;

        Vertices[0].Position = new Vector3(nearX, nearY, cornerZ.X);
        Vertices[1].Position = new Vector3(farX, nearY, cornerZ.Y);
        Vertices[2].Position = new Vector3(nearX, farY, cornerZ.Z);
        Vertices[3].Position = new Vector3(farX, farY, cornerZ.W);
    }

    private void UpdateId(ushort newId)
    {
        var files = _scene.Files;
        ushort texId = files.TileData.LandData[newId].TexId;

        bool isStretched = !IsFlat(
            Vertices[0].Position.Z, Vertices[1].Position.Z, Vertices[2].Position.Z, Vertices[3].Position.Z);

        // Divergence from CentrED, which tests the texmap entry at the *land id* here rather
        // than at the land tile's TexID. Land ids and texmap ids are unrelated, so that check
        // can pick a stretched texture for a tile whose real texmap is missing.
        bool isTexMapValid = texId != 0 && files.Texmaps.IsValid(texId);
        bool isLandTileValid = files.Art.IsValidLand(newId);
        bool alwaysFlat = AlwaysFlat(newId);

        if (_scene.Options.FlatView)
        {
            isStretched = false;
            for (int i = 0; i < 4; i++)
                Vertices[i].Normal = Vector3.UnitY;
        }
        else if (isTexMapValid && !alwaysFlat)
        {
            Span<Vector3> normals = stackalloc Vector3[4];
            isStretched |= CalculateNormals(normals);
            for (int i = 0; i < 4; i++)
                Vertices[i].Normal = normals[i];
        }

        bool useTexMap = !alwaysFlat && isTexMapValid &&
                         (_scene.Options.PreferTexmaps || isStretched || !isLandTileValid);

        Sprite sprite = useTexMap ? files.Texmaps.Get(texId) : files.Art.GetLand(newId);

        if (sprite.IsEmpty)
        {
            // Texmap 1 is the all-pink VOID texture, so a missing tile is obvious rather than silent.
            sprite = files.Texmaps.Get(0x0001);
        }

        Texture = sprite;

        if (sprite.IsEmpty)
            return;

        // Each sprite is its own surface here, so the UV rect always spans the whole texture.
        // The epsilon inset is CentrED's, guarding against sampling a neighbouring texel.
        float texX = 0f + Epsilon;
        float texY = 0f + Epsilon;
        float texWidth = 1f - Epsilon;
        float texHeight = 1f - Epsilon;

        // Non-zero z tells the pixel shader to light this tile. Land art already has lighting
        // baked in; terrain textures do not.
        float applyLightingFlag = useTexMap ? 0.00001f : 0f;

        Span<Vector3> texCoords = stackalloc Vector3[4];
        if (useTexMap)
        {
            // Square texture mapped corner to corner across the stretched quad.
            texCoords[0] = new Vector3(texX, texY, applyLightingFlag);
            texCoords[1] = new Vector3(texX + texWidth, texY, applyLightingFlag);
            texCoords[2] = new Vector3(texX, texY + texHeight, applyLightingFlag);
            texCoords[3] = new Vector3(texX + texWidth, texY + texHeight, applyLightingFlag);
        }
        else
        {
            // The diamond's tips sit at the edge midpoints of the art, so the quad corners map
            // to those midpoints instead.
            texCoords[0] = new Vector3(texX + texWidth / 2f, texY, applyLightingFlag);
            texCoords[1] = new Vector3(texX + texWidth, texY + texHeight / 2f, applyLightingFlag);
            texCoords[2] = new Vector3(texX, texY + texHeight / 2f, applyLightingFlag);
            texCoords[3] = new Vector3(texX + texWidth / 2f, texY + texHeight, applyLightingFlag);
        }

        for (int i = 0; i < 4; i++)
            Vertices[i].Texture = texCoords[i];
    }

    /// <summary>
    /// Corner heights come from this tile plus the neighbours at +x, +y and +x+y. At the facet
    /// edge the missing neighbour falls back to this tile, flattening the border.
    /// </summary>
    private Vector4 GetCornerZ()
    {
        var map = _scene.Files.Map;
        int x = Tile.X;
        int y = Tile.Y;

        map.TryGetLandTile(Math.Min(map.Width - 1, x + 1), y, out var rightTile);
        map.TryGetLandTile(x, Math.Min(map.Height - 1, y + 1), out var leftTile);
        map.TryGetLandTile(Math.Min(map.Width - 1, x + 1), Math.Min(map.Height - 1, y + 1), out var bottomTile);

        var top = Tile;
        var right = rightTile ?? Tile;
        var left = leftTile ?? Tile;
        var bottom = bottomTile ?? Tile;

        return new Vector4(top.Z, right.Z, left.Z, bottom.Z) * TILE_Z_SCALE;
    }

    /// <summary>
    /// Ported from CentrED (which credits ClassicUO). Each corner gets its own normal, derived
    /// from the heights around it, so lighting is smooth across tile boundaries. Returns true
    /// if any corner is uneven, which also forces the stretched-texture path.
    /// </summary>
    private bool CalculateNormals(Span<Vector3> normals)
    {
        var map = _scene.Files.Map;
        int x = Tile.X;
        int y = Tile.Y;

        /*  _____ _____ _____ _____
         * |     | t10 | t20 |     |
         * |_____|_____|_____|_____|
         * | t01 |  z  | t21 | t31 |
         * |_____|_____|_____|_____|
         * | t02 | t12 | t22 | t32 |
         * |_____|_____|_____|_____|
         * |     | t13 | t23 |     |
         * |_____|_____|_____|_____|
         */
        map.TryGetLandTile(x, y - 1, out var t10);
        map.TryGetLandTile(x + 1, y - 1, out var t20);
        map.TryGetLandTile(x - 1, y, out var t01);
        map.TryGetLandTile(x + 1, y, out var t21);
        map.TryGetLandTile(x + 2, y, out var t31);
        map.TryGetLandTile(x - 1, y + 1, out var t02);
        map.TryGetLandTile(x, y + 1, out var t12);
        map.TryGetLandTile(x + 1, y + 1, out var t22);
        map.TryGetLandTile(x + 2, y + 1, out var t32);
        map.TryGetLandTile(x, y + 2, out var t13);
        map.TryGetLandTile(x + 1, y + 2, out var t23);

        bool isStretched = false;
        isStretched |= CalculateNormal(Tile, t10, t21, t12, t01, out normals[0]);
        isStretched |= CalculateNormal(t21 ?? Tile, t20, t31, t22, Tile, out normals[1]);
        isStretched |= CalculateNormal(t12 ?? Tile, Tile, t22, t13, t02, out normals[2]);
        isStretched |= CalculateNormal(t22 ?? Tile, t21, t32, t23, t12, out normals[3]);
        return isStretched;
    }

    private static bool CalculateNormal(LandTile tile, LandTile? top, LandTile? right, LandTile? bottom,
        LandTile? left, out Vector3 normal)
    {
        sbyte tileZ = tile.Z;
        LandTile topTile = top ?? tile;
        LandTile rightTile = right ?? tile;
        LandTile bottomTile = bottom ?? tile;
        LandTile leftTile = left ?? tile;

        if (tileZ == topTile.Z && tileZ == rightTile.Z && tileZ == bottomTile.Z && tileZ == leftTile.Z)
        {
            normal = new Vector3(0, 0, 1f);
            return false;
        }


        // Sum the cross products of the four neighbour pairs going around the corner. Unrolled
        // rather than looping over an array, since this runs for every corner of every tile.
        normal = Cross(tile, leftTile, topTile)
               + Cross(tile, topTile, rightTile)
               + Cross(tile, rightTile, bottomTile)
               + Cross(tile, bottomTile, leftTile);

        normal = Vector3.Normalize(normal);
        return true;
    }

    private static Vector3 Cross(LandTile origin, LandTile a, LandTile b)
    {
        var u = new Vector3(
            (a.X - origin.X) * TILE_SIZE,
            (a.Y - origin.Y) * TILE_SIZE,
            (a.Z - origin.Z) * TILE_Z_SCALE);
        var v = new Vector3(
            (b.X - origin.X) * TILE_SIZE,
            (b.Y - origin.Y) * TILE_SIZE,
            (b.Z - origin.Z) * TILE_Z_SCALE);

        return Vector3.Cross(u, v);
    }
}
