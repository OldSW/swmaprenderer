using System.Numerics;
using SwMapRenderer.Map;
using static SwMapRenderer.Constants;

namespace SwMapRenderer.Rendering;

/// <summary>
/// One static item, ported from CentrED's StaticObject.
///
/// Statics are sprites standing upright in the world, but they are not billboards: each is
/// built as two quads meeting at the tile's centre line, one receding along -x and one along
/// -y. Under the isometric projection the pair lands back on screen as the original flat
/// sprite, while occupying real depth so that terrain and other statics interleave correctly.
/// </summary>
public sealed class StaticObject : MapObject
{
    private readonly MapScene _scene;

    public StaticTile Tile { get; }

    public StaticObject(MapScene scene, StaticTile tile)
    {
        _scene = scene;
        Tile = tile;

        // Two quads, so eight vertices rather than the four a land tile needs.
        Vertices = new MapVertex[8];

        UpdateId(tile.Id);
        UpdatePos(tile.X, tile.Y, tile.Z);
        UpdateHue(tile.Hue);

        // Statics are unlit; the terrain shader's lighting path is keyed off a non-zero normal.
        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i].Normal = Vector3.Zero;
    }

    private void UpdateId(ushort newId)
    {
        var files = _scene.Files;
        var sprite = files.Art.GetStatic(newId);

        if (sprite.IsEmpty)
        {
            // Texmap 1 is the all-pink VOID texture, so a missing sprite is obvious.
            sprite = files.Texmaps.Get(0x0001);
        }

        Texture = sprite;

        if (sprite.IsEmpty)
            return;

        // Each sprite is its own surface, so UVs span the full texture. The sprite is split
        // down the middle: the left half goes on the -x quad, the right half on the -y quad.
        const float texX = 0f;
        const float texY = 0f;
        const float texWidth = 1f;
        const float halfTexWidth = texWidth * 0.5f;
        const float texHeight = 1f;

        // Left half
        Vertices[0].Texture = new Vector3(texX, texY, 0f);
        Vertices[1].Texture = new Vector3(texX + halfTexWidth, texY, 0f);
        Vertices[2].Texture = new Vector3(texX, texY + texHeight, 0f);
        Vertices[3].Texture = new Vector3(texX + halfTexWidth, texY + texHeight, 0f);

        // Right half
        Vertices[4].Texture = new Vector3(texX + halfTexWidth, texY, 0f);
        Vertices[5].Texture = new Vector3(texX + texWidth, texY, 0f);
        Vertices[6].Texture = new Vector3(texX + halfTexWidth, texY + texHeight, 0f);
        Vertices[7].Texture = new Vector3(texX + texWidth, texY + texHeight, 0f);

        UpdateDepthOffset();
    }

    /// <summary>
    /// Nudges this static's depth by its position within the cell's sorted list, so items
    /// sharing a tile resolve in priority order instead of z-fighting.
    /// </summary>
    private void UpdateDepthOffset()
    {
        float depthOffset = Tile.CellIndex * 0.00001f;
        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i].Texture.Z = depthOffset;
    }

    private void UpdatePos(ushort newX, ushort newY, sbyte newZ)
    {
        float posX = newX * TILE_SIZE;
        float posY = newY * TILE_SIZE;
        float posZ = _scene.Options.FlatView ? 0 : newZ * TILE_Z_SCALE;

        // The sprite's width is split across two quads receding at 45 degrees, so each covers
        // width/sqrt(2) in world space and drops half a width in height across its span.
        float projectedWidth = Texture.Width * RSQRT2;
        float halfWidth = Texture.Width * 0.5f;

        // Left half
        Vertices[0].Position = new Vector3(posX - projectedWidth, posY, posZ + Texture.Height - halfWidth);
        Vertices[1].Position = new Vector3(posX, posY, posZ + Texture.Height);
        Vertices[2].Position = new Vector3(posX - projectedWidth, posY, posZ - halfWidth);
        Vertices[3].Position = new Vector3(posX, posY, posZ);

        // Right half
        Vertices[4].Position = new Vector3(posX, posY, posZ + Texture.Height);
        Vertices[5].Position = new Vector3(posX, posY - projectedWidth, posZ + Texture.Height - halfWidth);
        Vertices[6].Position = new Vector3(posX, posY, posZ);
        Vertices[7].Position = new Vector3(posX, posY - projectedWidth, posZ - halfWidth);
    }

    private void UpdateHue(ushort newHue)
    {
        var hueVec = _scene.GetHueVector(Tile.Id, newHue);
        for (int i = 0; i < Vertices.Length; i++)
            Vertices[i].Hue = hueVec;
    }
}
