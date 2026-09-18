using System.Numerics;

namespace SwMapRenderer.Rendering;

/// <summary>
/// Ported from CentrED's MapVertex. The channel packing is what the ported pixel shaders
/// expect, so it is kept exactly as the reference defines it:
///   Position - world space
///   Texture  - uv, plus a depth offset in z used to break ties between co-located tiles
///   Hue      - hue id (or rgb), unused, alpha, hue mode
///   Normal   - surface normal, for terrain lighting
/// </summary>
public struct MapVertex
{
    public Vector3 Position;
    public Vector3 Texture;
    public Vector4 Hue;
    public Vector3 Normal;

    public MapVertex(Vector3 position, Vector3 texture, Vector4 hue, Vector3 normal)
    {
        Position = position;
        Texture = texture;
        Hue = hue;
        Normal = normal;
    }
}
