// OpenCL port of Rasterizer.DrawTriangle and MapEffect's pixel stages.
//
// One work item owns one pixel and walks the triangles binned to its 16x16 tile in draw order,
// keeping colour and depth in registers. That is what preserves the software rasterizer's
// semantics -- depth compared with Less, premultiplied alpha blending, order mattering -- with
// no atomics and no depth buffer in memory.
//
// Coverage is decided in integers. Vertices arrive snapped to 1/256 px, so an edge function is
// an exact 64-bit integer, with no float whose sign could go arbitrary near an edge, and none
// of the double precision that Apple's GPUs do not have. It is also invariant under shifting
// the vertices and the pixel by whole pixels, which is exactly what separates one slice from
// the next: the barycentrics below are bit-identical in both, so shared edges agree.

#define FIXED_ONE 256
#define TILE_SHIFT 4

typedef struct
{
    int x[3];
    int y[3];
    float z[3];
    float texture[9];   // u, v, flag/depth offset per vertex
    float hue[12];      // id/r, unused/g, alpha/b, mode per vertex
    float normal[9];
    int spriteOffset;
    int spriteWidth;
    int spriteHeight;
    int flags;          // bit 0: statics technique; bit 1: hue is the same at all three vertices
    int pad;
} Tri;

inline long edge(int ax, int ay, int bx, int by, int cx, int cy)
{
    return (long)(bx - ax) * (long)(cy - ay) - (long)(by - ay) * (long)(cx - ax);
}

inline float getLight(float3 n)
{
    float lengthSquared = dot(n, n);
    if (lengthSquared < 1e-12f)
        return 1.0f;

    n = n / sqrt(lengthSquared);
    const float3 lightDirection = (float3)(0.0f, 0.70710678f, 0.70710678f);
    float baseLight = fmax(dot(n, lightDirection), 0.0f) / 2.0f + 0.5f;
    float delta = baseLight - 0.85355339f;
    return baseLight + (1.5f * delta - delta);
}

inline float3 getRgb(float gray, int hue, __global const uint* hues, int hueCount)
{
    if (hue < 0 || hue >= hueCount)
        return (float3)(0.0f, 0.0f, 0.0f);

    int index = (int)(gray * 32.0f);
    index = clamp(index, 0, 31);
    uint rgba = hues[hue * 32 + index];
    return (float3)((rgba & 0xFF) / 255.0f, ((rgba >> 8) & 0xFF) / 255.0f, ((rgba >> 16) & 0xFF) / 255.0f);
}

inline uchar toByte(float v)
{
    return (uchar)clamp((int)(v * 255.0f + 0.5f), 0, 255);
}

__kernel void rasterize(
    __global const Tri* tris,
    __global const int* binStart,
    __global const int* binList,
    __global const uint* sprites,
    __global const uint* hues,
    int hueCount,
    int width,
    int height,
    int tilesX,
    float4 clearColor,
    __global uchar* output)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    if (x >= width || y >= height)
        return;

    int tile = (y >> TILE_SHIFT) * tilesX + (x >> TILE_SHIFT);
    int px = x * FIXED_ONE + FIXED_ONE / 2;
    int py = y * FIXED_ONE + FIXED_ONE / 2;

    float4 color = clearColor;
    float depthBuffer = 1.0f;

    for (int i = binStart[tile]; i < binStart[tile + 1]; i++)
    {
        __global const Tri* t = &tris[binList[i]];

        int x0 = t->x[0], y0 = t->y[0];
        int x1 = t->x[1], y1 = t->y[1];
        int x2 = t->x[2], y2 = t->y[2];

        long area = edge(x0, y0, x1, y1, x2, y2);
        long e0 = edge(x1, y1, x2, y2, px, py);
        long e1 = edge(x2, y2, x0, y0, px, py);
        long e2 = area - e0 - e1;

        // Either winding is drawn (CullNone). The test is inclusive: a pixel centre on an edge
        // belongs to both triangles, not to neither.
        if (area < 0)
        {
            area = -area; e0 = -e0; e1 = -e1; e2 = -e2;
        }
        if (e0 < 0 || e1 < 0 || e2 < 0)
            continue;

        float farea = (float)area;
        float w0 = (float)e0 / farea;
        float w1 = (float)e1 / farea;
        float w2 = (float)e2 / farea;

        float depth = w0 * t->z[0] + w1 * t->z[1] + w2 * t->z[2];
        if (depth < 0.0f || depth > 1.0f)
            continue;
        if (depth >= depthBuffer)
            continue;

        float u = w0 * t->texture[0] + w1 * t->texture[3] + w2 * t->texture[6];
        float v = w0 * t->texture[1] + w1 * t->texture[4] + w2 * t->texture[7];
        float flag = w0 * t->texture[2] + w1 * t->texture[5] + w2 * t->texture[8];

        // PointClamp sampling.
        int sx = clamp((int)(u * t->spriteWidth), 0, t->spriteWidth - 1);
        int sy = clamp((int)(v * t->spriteHeight), 0, t->spriteHeight - 1);
        uint texel = sprites[t->spriteOffset + sy * t->spriteWidth + sx];

        float4 c = (float4)(
            (texel & 0xFF) / 255.0f,
            ((texel >> 8) & 0xFF) / 255.0f,
            ((texel >> 16) & 0xFF) / 255.0f,
            ((texel >> 24) & 0xFF) / 255.0f);
        if (c.w == 0.0f)
            continue;

        // A hue that is constant over the triangle is read as it is. Interpolated, it would
        // come back as 1.4999999 rather than 1.5 and truncate to the wrong id or mode.
        float4 hue;
        if (t->flags & 2)
        {
            hue = (float4)(t->hue[0], t->hue[1], t->hue[2], t->hue[3]);
        }
        else
        {
            hue = w0 * (float4)(t->hue[0], t->hue[1], t->hue[2], t->hue[3])
                + w1 * (float4)(t->hue[4], t->hue[5], t->hue[6], t->hue[7])
                + w2 * (float4)(t->hue[8], t->hue[9], t->hue[10], t->hue[11]);
        }

        int mode = (int)hue.w;

        if (t->flags & 1)
        {
            // Statics technique.
            if (mode == 1 || (mode == 2 && c.x == c.y && c.x == c.z))
            {
                float3 rgb = getRgb(c.x, (int)hue.x, hues, hueCount);
                c.x = rgb.x; c.y = rgb.y; c.z = rgb.z;
            }
            else if (mode == 255)
            {
                c.x += hue.x; c.y += hue.y; c.z += hue.z;
            }

            if (mode != 255)
                c *= hue.z;
        }
        else
        {
            // Terrain technique. The flag doubles as "this is a texmapped tile": art.mul land
            // already has its lighting painted in.
            if (flag > 0.0f)
            {
                float3 n = w0 * (float3)(t->normal[0], t->normal[1], t->normal[2])
                         + w1 * (float3)(t->normal[3], t->normal[4], t->normal[5])
                         + w2 * (float3)(t->normal[6], t->normal[7], t->normal[8]);
                float light = getLight(n);
                c.x *= light; c.y *= light; c.z *= light;
            }

            if (mode == 255)
            {
                c.x += hue.x; c.y += hue.y; c.z += hue.z;
            }
        }

        // BlendState.AlphaBlend, premultiplied: dst = src + dst * (1 - src.a)
        color = c + color * (1.0f - c.w);
        depthBuffer = depth;
    }

    // Back to straight alpha for the image file.
    if (color.w > 0.0f && color.w < 1.0f)
    {
        float inv = 1.0f / color.w;
        color.x *= inv; color.y *= inv; color.z *= inv;
    }

    int o = (y * width + x) * 4;
    output[o] = toByte(color.x);
    output[o + 1] = toByte(color.y);
    output[o + 2] = toByte(color.z);
    output[o + 3] = toByte(color.w);
}
