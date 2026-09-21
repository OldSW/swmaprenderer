using System.Numerics;
using SwMapRenderer.Assets;
using SwMapRenderer.Rendering;

namespace SwMapRenderer.Map;

public enum HueMode
{
    None = 0,
    Hued = 1,
    Partial = 2,
    Light = 3,
    Rgb = 255,
}

/// <summary>
/// The data-access surface the tile objects build their geometry against: client files, render
/// options, and the visibility rules. CentrED reaches this through the CEDGame.MapManager
/// singleton; passing it explicitly keeps the ported geometry code testable and thread-safe.
/// </summary>
public sealed class MapScene
{
    public UoFiles Files { get; }
    public RenderOptions Options { get; }

    private const float TranslucentAlpha = 178 / 255.0f;

    public MapScene(UoFiles files, RenderOptions options)
    {
        Files = files;
        Options = options;
    }

    public bool TryGetLandTile(int x, int y, out LandTile? tile) => Files.Map.TryGetLandTile(x, y, out tile);

    public LandObject CreateLand(LandTile tile) => new(this, tile);

    public StaticObject CreateStatic(StaticTile tile) => new(this, tile);

    public MobileObject CreateMobile(MobilePlacement placement, AnimationFrame frame, ushort hue,
        bool partialHue, int layer) => new(this, placement, frame, hue, partialHue, layer);

    private bool WithinZRange(sbyte z) => z >= Options.MinZ && z <= Options.MaxZ;

    /// <summary>
    /// Ported from CentrED's CanDrawLand. Ids 0-2 are the unused/void/nodraw placeholders.
    /// </summary>
    public bool CanDrawLand(LandTile tile)
    {
        if (!Options.ShowLand)
            return false;

        if (tile.Id <= 2 && !Options.ShowNoDraw)
            return false;

        if (tile.Id >= Files.TileData.LandData.Length)
            return false;

        return WithinZRange(tile.Z);
    }

    /// <summary>
    /// Ported from CentrED's CanDrawStatic. The id list is the set of placeholder and
    /// cave/"hidden" graphics that the client itself never draws.
    /// </summary>
    public bool CanDrawStatic(StaticTile tile)
    {
        if (!Options.ShowStatics)
            return false;

        ushort id = tile.Id;
        if (id >= Files.TileData.StaticData.Length)
            return false;

        ref readonly var data = ref Files.TileData.StaticData[id];

        if (!Options.ShowNoDraw)
        {
            switch (id)
            {
                case 0x0001:
                case 0x21BC:
                case 0x63D3:
                    return false;

                case 0x9E4C:
                case 0x9E64:
                case 0x9E65:
                case 0x9E7D:
                    return (data.Flags & TileFlag.Background) == 0 && (data.Flags & TileFlag.Surface) == 0;

                case 0x2198:
                case 0x2199:
                case 0x21A0:
                case 0x21A1:
                case 0x21A2:
                case 0x21A3:
                case 0x21A4:
                    return false;
            }
        }

        if (!Files.Map.IsValidX(tile.X) || !Files.Map.IsValidY(tile.Y))
            return false;

        return WithinZRange(tile.Z);
    }

    /// <summary>
    /// Mobiles carry no tile data of their own, so the z range is all there is to test. Their
    /// frames are clipped to the facet by the tile they stand on, which the overlay checks.
    /// </summary>
    public bool CanDrawMobile(MobilePlacement placement) => WithinZRange(placement.Z);

    public float GetDefaultAlpha(ushort tileId) =>
        Files.TileData.GetStatic(tileId).IsTranslucent ? TranslucentAlpha : 1.0f;

    /// <summary>
    /// Ported from CentrED's HuesManager.GetHueVector. Packs the hue into the vertex channel
    /// the pixel shader reads: (zero-based hue id, unused, alpha, mode).
    /// </summary>
    public Vector4 GetHueVector(ushort tileId, ushort hue) =>
        GetHueVector(hue, Files.TileData.GetStatic(tileId).IsPartialHue, GetDefaultAlpha(tileId));

    /// <summary>
    /// The same packing for callers that know the hue and its mode without a tile to read them
    /// from -- an animation frame, whose partial hueing comes from the worn item rather than
    /// from the frame itself.
    /// </summary>
    public Vector4 GetHueVector(ushort hue, bool partial, float alpha = 1.0f)
    {
        // The high bit forces partial hueing regardless of the tile's own flags.
        if ((hue & 0x8000) != 0)
        {
            partial = true;
            hue &= 0x7FFF;
        }

        HueMode mode;
        if (hue != 0)
        {
            hue -= 1;
            mode = partial ? HueMode.Partial : HueMode.Hued;
        }
        else
        {
            mode = HueMode.None;
        }

        return new Vector4(hue, 0, alpha, (int)mode);
    }
}
