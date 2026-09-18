using System.Numerics;
using SwMapRenderer.Assets;
using SwMapRenderer.Map;

namespace SwMapRenderer.Rendering;

public enum Technique
{
    Terrain,
    Statics,
}

/// <summary>Interpolated vertex attributes handed to a pixel shader.</summary>
public struct PixelInput
{
    public Vector3 Texture;
    public Vector4 Hue;
    public Vector3 Normal;
}

/// <summary>
/// CPU port of CentrED's MapEffect.fx. The Terrain and Statics techniques are ported; the
/// editor-only ones (Selection, TerrainGrid, VirtualLayer, ImageOverlay) have no meaning in a
/// still image, and the Light technique is omitted along with the rest of the lighting pass.
/// </summary>
public sealed class MapEffect
{
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.0f, 1.0f, 1.0f));

    /// <summary>Parametrizable in the original shader; 1.5 is its default.</summary>
    private const float Brightlight = 1.5f;

    /// <summary>The lighting value a flat tile comes out at: cos(45)/2 + 0.5.</summary>
    private const float FlatTileLight = 0.85355339f;

    private readonly HuesFile _hues;

    public Technique CurrentTechnique { get; set; } = Technique.Terrain;

    public MapEffect(HuesFile hues)
    {
        _hues = hues;
    }

    /// <summary>
    /// Runs the current technique for one pixel. Returns false where the shader would
    /// <c>discard</c>, which the rasterizer treats as "leave the framebuffer alone".
    /// </summary>
    public bool Shade(Sprite texture, in PixelInput pin, out Vector4 color)
    {
        var sampled = texture.Sample(pin.Texture.X, pin.Texture.Y);

        // Straight RGBA byte order, matching Color16.ToRgba.
        color = new Vector4(
            (sampled & 0xFF) / 255f,
            ((sampled >> 8) & 0xFF) / 255f,
            ((sampled >> 16) & 0xFF) / 255f,
            ((sampled >> 24) & 0xFF) / 255f);

        if (color.W == 0f)
            return false;

        return CurrentTechnique switch
        {
            Technique.Terrain => TerrainPs(pin, ref color),
            Technique.Statics => StaticsPs(pin, ref color),
            _ => throw new NotSupportedException($"Technique {CurrentTechnique} is not implemented."),
        };
    }

    private bool TerrainPs(in PixelInput pin, ref Vector4 color)
    {
        // Texture.z doubles as the "this is a terrain texture" flag. Land art in art.mul has
        // lighting painted in already, so only texmapped tiles get lit here.
        if (pin.Texture.Z > 0.0f)
        {
            float light = GetLight(pin.Normal);
            color.X *= light;
            color.Y *= light;
            color.Z *= light;
        }

        if ((int)pin.Hue.W == (int)HueMode.Rgb)
        {
            color.X += pin.Hue.X;
            color.Y += pin.Hue.Y;
            color.Z += pin.Hue.Z;
        }

        return true;
    }

    private bool StaticsPs(in PixelInput pin, ref Vector4 color)
    {
        var mode = (HueMode)(int)pin.Hue.W;

        // Partial hueing only recolours the greyscale part of a sprite, leaving already
        // coloured pixels (skin, metal trim) alone.
        if (mode == HueMode.Hued ||
            (mode == HueMode.Partial && color.X == color.Y && color.X == color.Z))
        {
            var rgb = GetRgb(color.X, (int)pin.Hue.X);
            color.X = rgb.X;
            color.Y = rgb.Y;
            color.Z = rgb.Z;
        }
        else if (mode == HueMode.Rgb)
        {
            color.X += pin.Hue.X;
            color.Y += pin.Hue.Y;
            color.Z += pin.Hue.Z;
        }

        if (mode != HueMode.Rgb)
        {
            // Scales colour and alpha together, i.e. premultiplied fade for translucent tiles.
            color *= pin.Hue.Z;
        }

        return true;
    }

    /// <summary>
    /// Replaces a sprite's grey level with the corresponding step of a hue gradient. The
    /// original samples a hue texture; this indexes the same 32-entry table directly.
    /// </summary>
    private Vector4 GetRgb(float gray, int hue)
    {
        int index = (int)(gray * HuesFile.ColorsPerHue);
        uint rgba = _hues.GetColor(hue, index);

        return new Vector4(
            (rgba & 0xFF) / 255f,
            ((rgba >> 8) & 0xFF) / 255f,
            ((rgba >> 16) & 0xFF) / 255f,
            1f);
    }

    /// <summary>
    /// Directional terrain lighting, ported from the shader (which credits ClassicUO). The
    /// result is normalised so a flat tile comes out unchanged and slopes brighten or darken
    /// around it.
    /// </summary>
    private static float GetLight(Vector3 norm)
    {
        // Degenerate normals are possible where the cross products around a corner cancel out;
        // treat those as flat rather than propagating a NaN into the framebuffer.
        if (norm.LengthSquared() < 1e-12f)
            return 1.0f;

        var normal = Vector3.Normalize(norm);
        float baseLight = MathF.Max(Vector3.Dot(normal, LightDirection), 0.0f) / 2.0f + 0.5f;

        float delta = baseLight - FlatTileLight;
        return baseLight + (Brightlight * delta - delta);
    }
}
