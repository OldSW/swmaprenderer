using System.Numerics;

namespace SwMapRenderer.Rendering;

/// <summary>
/// CPU port of CentrED's MapRenderer.
///
/// The reference wraps a set of DrawBatchers that group tiles by texture to cut GPU state
/// changes; that exists purely to keep the driver busy and has no analogue here, so this port
/// keeps the Begin/DrawMapObject/End shape and the quad-to-triangle index pattern, and drops
/// the batching. What it does keep is the vertex stage: the WorldViewProj transform, the
/// per-tile depth offset, and the viewport mapping.
/// </summary>
public sealed class MapRenderer
{
    private readonly Rasterizer _rasterizer;

    private MapEffect? _effect;
    private Matrix4x4 _worldViewProj;
    private bool _beginCalled;

    public MapRenderer(Rasterizer rasterizer)
    {
        _rasterizer = rasterizer;
    }

    public void Begin(MapEffect effect, Matrix4x4 worldViewProj)
    {
        if (_beginCalled)
            throw new InvalidOperationException("Mismatched Begin and End calls");

        _beginCalled = true;
        _effect = effect;
        _worldViewProj = worldViewProj;
    }

    public void End()
    {
        if (!_beginCalled)
            throw new InvalidOperationException("Mismatched Begin and End calls");

        _beginCalled = false;
        _effect = null;
    }

    /// <summary>
    /// Draws an object's vertices as quads. A land tile is one quad (4 vertices); a static is
    /// two (8). Triangles follow the reference's index pattern: (0,1,2) and (3,2,1) per quad.
    /// </summary>
    public void DrawMapObject(MapObject o, Vector4 hueOverride)
    {
        if (!_beginCalled || _effect == null)
            throw new InvalidOperationException("DrawMapObject called outside of Begin/End");

        if (o.Texture.IsEmpty)
            return;

        Span<ScreenVertex> quad = stackalloc ScreenVertex[4];

        for (int quadStart = 0; quadStart + 4 <= o.Vertices.Length; quadStart += 4)
        {
            for (int i = 0; i < 4; i++)
                quad[i] = Transform(o.Vertices[quadStart + i], hueOverride);

            _rasterizer.DrawTriangle(quad[0], quad[1], quad[2], o.Texture, _effect);
            _rasterizer.DrawTriangle(quad[3], quad[2], quad[1], o.Texture, _effect);
        }
    }

    /// <summary>
    /// Port of the shared TileVSMain vertex shader, plus the viewport transform the GPU would
    /// apply afterwards. Texture.z is added to clip-space z, which is how co-located tiles are
    /// separated in depth without moving them on screen.
    /// </summary>
    private ScreenVertex Transform(in MapVertex vertex, Vector4 hueOverride)
    {
        var clip = Vector4.Transform(new Vector4(vertex.Position, 1.0f), _worldViewProj);
        clip.Z += vertex.Texture.Z;

        // Orthographic projection, so w is 1; the divide is kept for correctness, not effect.
        float invW = clip.W == 0f ? 1f : 1f / clip.W;
        float ndcX = clip.X * invW;
        float ndcY = clip.Y * invW;
        float ndcZ = clip.Z * invW;

        ScreenVertex result;
        result.X = (ndcX * 0.5f + 0.5f) * _rasterizer.Width;
        result.Y = (0.5f - ndcY * 0.5f) * _rasterizer.Height;
        result.Z = ndcZ;
        result.Texture = vertex.Texture;
        result.Hue = hueOverride != default ? hueOverride : vertex.Hue;
        result.Normal = vertex.Normal;

        return result;
    }
}
