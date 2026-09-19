using System.Diagnostics;
using System.Numerics;
using SwMapRenderer.Map;
using static SwMapRenderer.Constants;

namespace SwMapRenderer.Rendering;

public readonly record struct RenderStats(
    TileRhombus Range,
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
    private readonly Rasterizer _rasterizer;
    private readonly MapRenderer _renderer;
    private readonly MapEffect _effect;

    public Rasterizer Output => _rasterizer;

    /// <param name="width">Canvas width. Defaults to the configured output width.</param>
    /// <param name="height">Canvas height. Defaults to the configured output height.</param>
    public SceneRenderer(MapScene scene, int? width = null, int? height = null)
    {
        _scene = scene;
        _options = scene.Options;

        _rasterizer = new Rasterizer(width ?? _options.Width, height ?? _options.Height);
        _renderer = new MapRenderer(_rasterizer);
        _effect = new MapEffect(scene.Files.Hues);
    }

    /// <summary>The projection that centres this canvas on the configured tile.</summary>
    public IsoProjection CentredProjection => IsoProjection.Centred(
        _options.X * TILE_SIZE, _options.Y * TILE_SIZE, _options.Zoom, _rasterizer.Width, _rasterizer.Height);

    /// <summary>
    /// Draws one canvas. The renderer is reusable: the framebuffer is cleared each time, so a
    /// single instance can render slice after slice without reallocating it.
    /// </summary>
    public RenderStats Render(IsoProjection projection)
    {
        var stopwatch = Stopwatch.StartNew();

        var range = CalculateViewRange(projection);

        _rasterizer.Clear(_options.BackgroundColor);

        int landDrawn = DrawLand(range, projection);
        int staticsDrawn = DrawStatics(range, projection);

        stopwatch.Stop();
        return new RenderStats(range, landDrawn, staticsDrawn, stopwatch.Elapsed);
    }

    private int DrawLand(TileRhombus range, IsoProjection projection)
    {
        if (!_options.ShowLand)
            return 0;

        int drawn = 0;
        _effect.CurrentTechnique = Technique.Terrain;
        _renderer.Begin(_effect, projection);

        var map = _scene.Files.Map;
        foreach (var (x, y) in range.Iterate(map.Width, map.Height))
        {
            if (!_scene.TryGetLandTile(x, y, out var tile) || tile == null)
                continue;

            if (!_scene.CanDrawLand(tile))
                continue;

            _renderer.DrawMapObject(_scene.CreateLand(tile), default);
            drawn++;
        }

        _renderer.End();
        return drawn;
    }

    private int DrawStatics(TileRhombus range, IsoProjection projection)
    {
        if (!_options.ShowStatics)
            return 0;

        int drawn = 0;
        _effect.CurrentTechnique = Technique.Statics;
        _renderer.Begin(_effect, projection);

        var map = _scene.Files.Map;
        foreach (var (x, y) in range.Iterate(map.Width, map.Height))
        {
            foreach (var tile in _scene.Files.Statics.Get(x, y))
            {
                if (!_scene.CanDrawStatic(tile))
                    continue;

                _renderer.DrawMapObject(_scene.CreateStatic(tile), default);
                drawn++;
            }
        }

        _renderer.End();
        return drawn;
    }

    /// <summary>
    /// The tiles that can reach this canvas.
    ///
    /// CentrED derives its range from a heuristic -- a diamond of (width + height) / zoom / 2.6
    /// pixels plus eight tiles of slack -- which is sized for an interactive window, where a
    /// tile appearing a frame late as you scroll goes unnoticed. Slices rendered independently
    /// have no such tolerance: anything the range misses along a shared edge shows up as a hard
    /// discontinuity between neighbours. So the range is inverted from the projection instead,
    /// over the configured z range and the largest sprite in art.mul.
    /// </summary>
    private TileRhombus CalculateViewRange(IsoProjection projection)
    {
        var art = _scene.Files.Art;

        return projection.VisibleTiles(
            0, 0, _rasterizer.Width, _rasterizer.Height,
            _options.MinZ, _options.MaxZ,
            art.MaxStaticWidth, art.MaxStaticHeight);
    }
}
