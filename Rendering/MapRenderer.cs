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
    private IsoProjection _projection;
    private bool _beginCalled;

    public MapRenderer(Rasterizer rasterizer)
    {
        _rasterizer = rasterizer;
    }

    public void Begin(MapEffect effect, IsoProjection projection)
    {
        if (_beginCalled)
            throw new InvalidOperationException("Mismatched Begin and End calls");

        _beginCalled = true;
        _effect = effect;
        _projection = projection;
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
    /// Port of the shared TileVSMain vertex shader, and of the viewport transform the GPU would
    /// apply after it, evaluated through <see cref="IsoProjection"/> rather than the matrix
    /// chain. See that type for why: the two are algebraically the same, but only the closed
    /// form places a world point identically regardless of the canvas it is being drawn into,
    /// which is what independently rendered slices need.
    ///
    /// Texture.z is added to depth, which is how tiles sharing a position are separated in the
    /// depth buffer without moving on screen.
    /// </summary>
    private ScreenVertex Transform(in MapVertex vertex, Vector4 hueOverride)
    {
        var position = vertex.Position;

        ScreenVertex result;
        result.X = _projection.ScreenX(position.X, position.Y);
        result.Y = _projection.ScreenY(position.X, position.Y, position.Z);
        result.Z = (float)_projection.Depth(position.Z) + vertex.Texture.Z;
        result.Texture = vertex.Texture;
        result.Hue = hueOverride != default ? hueOverride : vertex.Hue;
        result.Normal = vertex.Normal;

        return result;
    }
}
