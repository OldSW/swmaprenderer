using System.Numerics;
using SwMapRenderer.Assets;
using SwMapRenderer.Map;
using static SwMapRenderer.Constants;

namespace SwMapRenderer.Rendering;

/// <summary>
/// One frame of one mobile: a body, or a piece of what it is wearing.
///
/// Unlike a static this is a flat billboard -- a single quad facing the camera -- and that is
/// what makes a dressed body work. A static is built as two quads meeting at its tile's centre
/// line, one receding along -x and one along -y, so that a wall interleaves with the scenery
/// around it; the price is that its depth varies across its own width, falling away by half a
/// sprite towards either edge. Drawing one sprite that way is fine. Stacking a dozen of them is
/// not: every frame of a mobile is a different width with its own anchor, so at any given pixel
/// each layer would sit at a different depth, differing by far more than the bias that is
/// supposed to order them, and the body would win the depth test through whatever it is wearing.
///
/// Flat, every layer of a mobile has the same depth at the same pixel -- the height of that
/// screen row above the ground, exactly as for the scenery around it -- and the paint order is
/// decided by the layer bias alone, which is what it is for.
///
/// The frame's own anchor is applied by moving the quad through the world rather than on the
/// screen. Stepping the same distance along +x and -x slides a sprite sideways without moving
/// it up or down or changing its depth; the vertical anchor goes into z, where a step is a
/// pixel up the screen.
/// </summary>
public sealed class MobileObject : MapObject
{
    /// <summary>
    /// Depth bias between the frames of one mobile, so each paints over the last.
    ///
    /// Statics bias themselves apart by their position in a cell's sorted list, always
    /// positive; a mobile's frames step the other way, which also puts the whole mobile in
    /// front of the scenery of the tile it is standing on.
    /// </summary>
    private const float LayerDepthStep = 0.00001f;

    public MobileObject(MapScene scene, MobilePlacement placement, AnimationFrame frame, ushort hue,
        bool partialHue, int layer)
    {
        Texture = frame.Sprite;
        Vertices = new MapVertex[4];

        if (frame.IsEmpty)
            return;

        SetGeometry(scene, placement, frame);

        float depthOffset = -(layer + 1) * LayerDepthStep;
        var hueVector = scene.GetHueVector(hue, partialHue);

        Span<Vector2> uv = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)];

        for (int i = 0; i < Vertices.Length; i++)
        {
            Vertices[i].Texture = new Vector3(uv[i], depthOffset);
            Vertices[i].Hue = hueVector;

            // Mobiles are unlit; the terrain shader's lighting path is keyed off a non-zero normal.
            Vertices[i].Normal = Vector3.Zero;
        }
    }

    private void SetGeometry(MapScene scene, MobilePlacement placement, AnimationFrame frame)
    {
        // The frame's anchor sits somewhere inside the image rather than at a corner, so the
        // two edges are at different distances from the tile. A screen-space step of n pixels
        // to the right is n along +x and n back along -y, which cancels in the sum the vertical
        // axis is built from and so moves the sprite sideways only.
        float left = -frame.CenterX * RSQRT2;
        float right = (Texture.Width - frame.CenterX) * RSQRT2;

        float tileX = placement.X * TILE_SIZE;
        float tileY = placement.Y * TILE_SIZE;

        // Up off the corner of the tile and onto the middle of it, then the frame's own
        // vertical anchor. One unit of z is one pixel up the screen.
        float bottom = (scene.Options.FlatView ? 0 : placement.Z * TILE_Z_SCALE)
                       + MobileOverlay.FeetOffset + frame.CenterY;
        float top = bottom + Texture.Height;

        Vertices[0].Position = new Vector3(tileX + left, tileY - left, top);
        Vertices[1].Position = new Vector3(tileX + right, tileY - right, top);
        Vertices[2].Position = new Vector3(tileX + left, tileY - left, bottom);
        Vertices[3].Position = new Vector3(tileX + right, tileY - right, bottom);
    }
}
