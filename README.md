# swmaprenderer

Renders a region of an Ultima Online map to an image. The renderer is a CPU port of
[CentrED#](https://github.com/kaczy93/centredsharp)'s `MapRenderer` and its `MapEffect.fx`
shader, reading the client's `.mul` files directly.

```
swmaprenderer --data ./vanilla_client --map 0 --x 1420 --y 1690 -w 1024 -h 768 -o britain.png
```

## Why a software rasterizer

CentrED# is an FNA application: it batches tiles into GPU vertex buffers and shades them with a
compiled `MapEffect.fxc`. Reproducing that stack means vendoring FNA and its native libraries,
the unpublished `ClassicUO.Assets` assemblies, and an `fxc` shader build that needs Windows or
Wine. For a tool whose whole job is to write one image and exit, none of that earns its keep.

So the geometry and shading are ported faithfully and the fixed-function pipeline underneath
them is reimplemented in `Rendering/Rasterizer.cs`: triangle setup, a depth buffer compared with
`Less`, point-clamp sampling and premultiplied alpha blending. The projection is orthographic,
so `w` is 1 everywhere and attribute interpolation is plain affine barycentric — there is no
perspective correction to get subtly wrong. The result has no native dependencies and runs
anywhere .NET does.

## Configuration

Defaults live in `appsettings.json` under `Render`, can be overridden by environment variables
(`SWMAP_Render__X=1420`), and command-line switches win over both. Run `--help` for the full
list. The frequently used ones:

| Switch | Meaning |
| --- | --- |
| `-d, --data <dir>` | Folder holding the client's `.mul` files |
| `-m, --map <0-5>` | Facet to render (`map{N}.mul`) |
| `-x`, `-y` | Tile the view is centred on |
| `-w`, `-h` | Output size in pixels |
| `-z, --zoom` | 0.2 to 4.0; larger zooms in |
| `-o, --out <file>` | Output path; the format follows the extension |
| `--no-statics`, `--no-land` | Draw only one layer |
| `--flat` | Flatten every tile to z=0, exposing building interiors |
| `--nodraw` | Include the placeholder tiles the client hides |
| `--prefer-texmaps` | Use terrain textures even where land art would do |
| `--min-z`, `--max-z` | Restrict the z range |
| `-v, --verbose` | Report the view range, tile counts and timings |

Note that `--data` is resolved against the current working directory, while `appsettings.json`
is read from the binary's directory.

Facet dimensions are detected from the map file's length. Maps were resized between client
versions while keeping their block height, so when the block count disagrees with the expected
size the height is trusted and the width re-derived — `vanilla_client`'s `map0.mul` comes out as
7168x4096 rather than the older 6144x4096, and says so under `--verbose`. Use `--map-width` and
`--map-height` to override.

## Layout

| Path | Contents |
| --- | --- |
| `Assets/` | `.mul` readers: tiledata, art, texmaps, hues, plus the memory-mapped file and index primitives |
| `Map/` | Terrain and statics layers, facet dimensions, and `MapScene` — the data and visibility rules the geometry is built against |
| `Rendering/` | The port: `Camera`, `LandObject`, `StaticObject`, `MapRenderer`, `MapEffect`, `Rasterizer` |

`MapScene` replaces CentrED's `CEDGame.MapManager` singleton, which the geometry code reaches
through for tile data and options. Passing it explicitly keeps that code testable.

## Divergences from the reference

- **Shared tile corners are derived from the tile index, not by adding `TILE_SIZE` to the near
  edge.** In single precision the two differ by an ULP, so neighbouring tiles disagreed about
  where their shared corner was by ~0.005px. Tile edges run at exactly 45 degrees, which lays
  that sliver over a whole diagonal of pixel centres at once, and every pixel along it fell
  outside both tiles — a one-pixel black seam clean across the terrain. See `LandObject.UpdateCorners`.
- **Terrain texture validity is tested at the tile's `TexID`, not at its land id.** CentrED
  checks the texmap entry at the land id, which is an unrelated index.
- **The texture-grouping `DrawBatcher`s are gone.** They exist to cut GPU state changes and have
  no CPU analogue; `MapRenderer` keeps the `Begin`/`DrawMapObject`/`End` shape and the
  quad-to-triangle index pattern.
- **Editor-only techniques are not ported** (`Selection`, `TerrainGrid`, `VirtualLayer`,
  `ImageOverlay`), nor is the lighting pass — `Light` hueing and `light.mul` go with it. Only
  `Terrain` and `Statics` are needed for a still image.

## Not implemented

- **`verdata.mul` patches and `mapdif`/`stadif` overlays are ignored.** Files are read as they
  sit on disk. Statics also skip CentrED's `AnimOffset`, so animated statics render on their
  first frame.
- **UOP (`.uop`) containers are not read**, only `.mul`. The `vanilla_client` data here is `.mul`.
- Both legacy (32-bit flag) and extended (64-bit flag, 7.0.9+) `tiledata.mul` layouts are
  supported, detected from the file length; `--tiledata-format` overrides the detection.
- The background is opaque black, matching the reference's `Clear(Color.Black)`.

## Licensing

Image encoding uses [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) under the
Six Labors Split License. Version 4.x validates a licence key at build time; `sixlabors.lic` in
the repository root supplies it, wired up through the `SixLaborsLicenseFile` property in
`swmaprenderer.csproj`.
