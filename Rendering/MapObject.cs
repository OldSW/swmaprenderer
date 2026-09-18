using SwMapRenderer.Assets;

namespace SwMapRenderer.Rendering;

/// <summary>
/// A drawable run of quads with the sprite they sample: one quad for a land tile, two for a
/// static.
///
/// CentrED's MapObject also carries a TextureBounds rectangle, because its sprites live inside
/// GPU texture atlases and need their sub-rect tracked. Sprites here are standalone, so the
/// sprite's own dimensions are the bounds and the field would only be able to disagree with
/// itself.
/// </summary>
public abstract class MapObject
{
    public Sprite Texture = Sprite.Empty;

    public MapVertex[] Vertices = new MapVertex[4];
}
