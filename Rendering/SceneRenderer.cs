using System.Diagnostics;
using System.Numerics;
using SwMapRenderer.Map;
using static SwMapRenderer.Constants;

namespace SwMapRenderer.Rendering;

public readonly record struct RenderStats(
    int MinTileX,
    int MinTileY,
    int MaxTileX,
    int MaxTileY,
    int LandDrawn,
    int StaticsDrawn,
    TimeSpan Elapsed);

/// <summary>
/// Drives one frame, following the order CentrED's MapManager.Draw uses: terrain first, then
/// statics. The passes are not independent -- statics rely on the depth buffer the terrain pass
/// leaves behind to be occluded by hills in front of them.
/// </summary>
public sealed class SceneRenderer
{
    private readonly MapScene _scene;
    private readonly RenderOptions _options;
    private readonly Camera _camera = new();
    private readonly Rasterizer _rasterizer;
    private readonly MapRenderer _renderer;
    private readonly MapEffect _effect;

    public Rasterizer Output => _rasterizer;

    public SceneRenderer(MapScene scene)
    {
        _scene = scene;
        _options = scene.Options;

        _rasterizer = new Rasterizer(_options.Width, _options.Height);
        _renderer = new MapRenderer(_rasterizer);
        _effect = new MapEffect(scene.Files.Hues);

        _camera.ScreenWidth = _options.Width;
        _camera.ScreenHeight = _options.Height;
        _camera.Zoom = _options.Zoom;
        _camera.Position = new Vector3(_options.X * TILE_SIZE, _options.Y * TILE_SIZE, 128 * 6);
        _camera.Update();
    }

    public RenderStats Render()
    {
        var stopwatch = Stopwatch.StartNew();

        var (minX, minY, maxX, maxY) = CalculateViewRange();

        // Opaque black, matching the reference's Clear(Color.Black).
        _rasterizer.Clear(new Vector4(0f, 0f, 0f, 1f));

        int landDrawn = DrawLand(minX, minY, maxX, maxY);
        int staticsDrawn = DrawStatics(minX, minY, maxX, maxY);

        stopwatch.Stop();
        return new RenderStats(minX, minY, maxX, maxY, landDrawn, staticsDrawn, stopwatch.Elapsed);
    }

    private int DrawLand(int minX, int minY, int maxX, int maxY)
    {
        if (!_options.ShowLand)
            return 0;

        int drawn = 0;
        _effect.CurrentTechnique = Technique.Terrain;
        _renderer.Begin(_effect, _camera.WorldViewProj);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (!_scene.TryGetLandTile(x, y, out var tile) || tile == null)
                    continue;

                if (!_scene.CanDrawLand(tile))
                    continue;

                _renderer.DrawMapObject(_scene.CreateLand(tile), default);
                drawn++;
            }
        }

        _renderer.End();
        return drawn;
    }

    private int DrawStatics(int minX, int minY, int maxX, int maxY)
    {
        if (!_options.ShowStatics)
            return 0;

        int drawn = 0;
        _effect.CurrentTechnique = Technique.Statics;
        _renderer.Begin(_effect, _camera.WorldViewProj);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                foreach (var tile in _scene.Files.Statics.Get(x, y))
                {
                    if (!_scene.CanDrawStatic(tile))
                        continue;

                    _renderer.DrawMapObject(_scene.CreateStatic(tile), default);
                    drawn++;
                }
            }
        }

        _renderer.End();
        return drawn;
    }

    /// <summary>
    /// Ported from CentrED's CalculateViewRange. The view is a diamond in tile space, so the
    /// range is derived from the screen's combined extent rather than its width and height
    /// separately. The eight extra rows on each side cover tiles whose geometry reaches into
    /// frame from outside it: terrain at low z, and tall statics at high z.
    /// </summary>
    private (int MinX, int MinY, int MaxX, int MaxY) CalculateViewRange()
    {
        float zoom = _camera.Zoom;

        // 2.0 is the geometric figure; the reference settled on 2.6, which trims the range
        // without anything visibly dropping out.
        float screenDiamondDiagonal = (_camera.ScreenWidth + _camera.ScreenHeight) / zoom / 2.6f;

        Vector3 center = _camera.Position;
        var map = _scene.Files.Map;

        int minTileX = map.ClampX((int)((center.X - screenDiamondDiagonal) / TILE_SIZE - 8));
        int minTileY = map.ClampY((int)((center.Y - screenDiamondDiagonal) / TILE_SIZE - 8));
        int maxTileX = map.ClampX((int)((center.X + screenDiamondDiagonal) / TILE_SIZE + 8));
        int maxTileY = map.ClampY((int)((center.Y + screenDiamondDiagonal) / TILE_SIZE + 8));

        return (minTileX, minTileY, maxTileX, maxTileY);
    }
}
