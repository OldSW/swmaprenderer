using System.Numerics;

namespace SwMapRenderer.Rendering;

/// <summary>
/// Ported from CentrED's Camera. This is what makes the view isometric: the projection mirrors
/// x, shears z into y so height reads as "up the screen", then applies an orthographic
/// transform. Nothing here is perspective, which is why attribute interpolation in the
/// rasterizer can stay affine.
/// </summary>
public sealed class Camera
{
    /// <summary>1.0 is standard. Larger zooms in.</summary>
    public float Zoom = 1.0f;

    public int ScreenWidth;
    public int ScreenHeight;

    private readonly Matrix4x4 _mirrorX = Matrix4x4.CreateReflection(new Plane(-1, 0, 0, 0));

    private readonly Vector3 _up = new(-1, -1, 0);

    /* Takes (x, y, z) to the screen point (x, y + z, z) */
    private readonly Matrix4x4 _oblique = new(1, 0, 0, 0,
                                              0, 1, 0, 0,
                                              0, 1, 1, 0,
                                              0, 0, 0, 1);

    private readonly Matrix4x4 _translation = Matrix4x4.CreateTranslation(new Vector3(0, 128 * 6, 0));

    public Vector3 Position = new(0, 0, 128 * 6);

    /// <summary>The camera always looks straight down at the tile plane.</summary>
    public Vector3 LookAt => new(Position.X, Position.Y, 0);

    public Matrix4x4 WorldViewProj { get; private set; }

    public void Update()
    {
        // Tiles are already in world coordinates.
        var world = Matrix4x4.Identity;
        var view = Matrix4x4.CreateLookAt(Position, LookAt, _up);

        var ortho = Matrix4x4.CreateOrthographic(ScreenWidth, ScreenHeight, 0, 128 * 12);
        var scale = Matrix4x4.CreateScale(Zoom, Zoom, 1f);

        var proj = _mirrorX * _oblique * _translation * ortho * scale;

        WorldViewProj = Matrix4x4.Multiply(Matrix4x4.Multiply(world, view), proj);
    }
}
