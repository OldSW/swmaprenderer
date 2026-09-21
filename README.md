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

## Browsable whole-map pyramid

`--tiles <dir>` switches from writing one image to slicing the facet into a slippy-map tile
pyramid, and writes an `index.html` beside it that browses the result in Leaflet.

```
swmaprenderer --data ./client --map 0 --tiles ./web/map0 --max-zoom 7
```

Slices are cut in **canvas space**, not tile space, and that is deliberate. A 16x16-tile region
is a *diamond* on screen, not a square -- one step in `x` moves a tile 22px right and 22px down,
one step in `y` moves it 22px left and 22px down. Diamonds interlock on a staggered grid, which
no standard web map layer can address. The square that *bounds* a 16x16-tile diamond is
704x704px, so that is the slice: each one carries 16x16 tiles' worth of canvas, and the grid is
one Leaflet can index directly. `--slice-tiles` changes that edge.

### Choosing a depth

Two separate knobs control the cost, and they are easy to confuse:

- **`--max-zoom` sets how many levels**, and so how many slices. Each level down is four times
  as many. It does *not* change how big a slice is.
- **`--zoom` sets how big a slice is**, and so the detail. The number of levels follows
  `--slice-tiles` and the facet size, and does not move with `--zoom` at all: halving it halves
  the canvas and the slice together, leaving the same 0..9 grid at half the resolution.

At full depth one map tile is 44 pixels, which for Felucca is a 247,988 x 249,313 pixel canvas
-- 61 gigapixels. Measured on twelve threads:

| `--max-zoom` | px per tile | slices | estimate | measured |
| --- | --- | --- | --- | --- |
| 9 (default) | 44 | 78,812 | ~22m, ~38 GB | |
| 8 | 22 | 20,059 | ~6m, ~10 GB | |
| 7 | 11 | 5,134 | ~1.7m, ~2.5 GB | **1.6m, 2.1 GB** |
| 6 | 5.5 | 1,375 | ~42s, ~681 MB | **55s, 680 MB** |

Slice counts exclude the void: they are laid out on the bounding box of the region while the
facet inside it is a diamond, so for a whole-facet run about half are rejected geometrically and
never rendered.

The usual shape is a shallow pyramid over the whole facet plus a deep one over the places you
care about: `--region x1,y1,x2,y2` restricts which slices are rendered, and a later deeper run
over the same directory fills in without redoing the first. Runs are resumable -- existing
slices are skipped unless `--overwrite` is given, and each slice is written to a temporary file
and moved into place, so an interrupted run never leaves a half-written tile for the resume to
mistake for finished work. `--dry-run` reports the plan and stops.

### How it stays seamless

Slices are rendered independently and in parallel, so nothing coordinates their shared edges;
they have to agree by construction. Two things make that true, and both were bugs first:

- **The view range is exact.** A tile paints well outside its own footprint -- a land quad is
  built from the corners it shares with its neighbours, and a static is drawn with its base on
  the tile and its sprite rising up the screen, so a 485px graphic standing 508 units up still
  reaches about a thousand pixels into frame. The range is inverted from the projection over the
  full z range and the largest sprite in `art.mul`, rather than using CentrED's
  `(w + h) / zoom / 2.6 + 8 tiles` heuristic, which is sized for an interactive window where a
  tile arriving a frame late goes unnoticed.
- **Geometry is canvas-independent.** Positions come from the projection's closed form in double
  precision, with the canvas origin as an explicit integer term, so a slice is bit-for-bit a
  window onto one shared canvas.

Both are checked rather than asserted. A small render is bit-identical to the centre crop of a
9x larger one across every zoom and location tried, and each 32-tile slice is bit-identical to
its four 16-tile slices stitched together.

Shallower levels are built by averaging the four slices below rather than re-rendering at a
smaller zoom: it costs a third as much again instead of doubling, and averaging four pixels
antialiases art that rendering small would merely alias. The averaging is done on premultiplied
alpha, or transparent pixels would drag their colour into every sprite edge.

### The viewer

`index.html` uses a custom CRS whose units are canvas pixels at the grid's deepest level, so
Leaflet's tile grid lines up with the slices exactly as generated. That reference level is a
property of the grid and not of how deep the run went, so a pyramid capped with `--max-zoom`
stays aligned; its finest tiles are simply magnified a couple of levels further rather than the
viewer refusing to zoom. Because the projection inverts, the
page also shows the tile coordinates under the cursor, takes an `x, y` to jump to, and keeps the
position in the URL hash so a view can be linked. Leaflet itself is loaded from a CDN, so the
page wants a network connection the first time.

Tiles that were never written -- ocean, or off the edge of the facet -- simply 404 and show
through to the page background, which is why pyramid slices default to a transparent background
where a single image defaults to black.

## Shard items

`statics{N}.mul` holds the world as the client shipped it. Everything players have since put
down — locked-down furniture, decorations, the houses themselves — lives only in the server's
save, so a map rendered from client files alone shows an empty town. `--items <file>` folds a
JSON export of those items back in.

```
swmaprenderer --map 1 --items lockedDownItems.json --x 1550 --y 1690 -z 2 -o tavern.png
```

`appsettings.json` can carry the path under `Render:Items` so that every run includes them;
`--items ""` turns the overlay back off for a single run.

The file is an array of objects; anything beyond the keys below is ignored, so a server's own
export usually needs no reshaping.

```json
[
  { "itemId": 2896, "hue": 0, "multi": false, "position": { "x": 1547, "y": 1682, "z": 0 } },
  { "itemId": 16427, "hue": 0, "multi": true,  "position": { "x": 3782, "y": 2251, "z": 50 } }
]
```

`itemId` is an art index, the same one `statics{N}.mul` stores, and `hue` is the same 1-based
hue index, so both pass through untouched. An item with `multi` set names a multi instead:
`itemId - 0x4000` is looked up in `multi.idx`/`multi.mul` and expanded into its components,
each offset from the item's own position. Without those two files in the data folder, multis
are skipped with a warning.

### Customizable houses

A house built on a foundation is the one multi `multi.mul` cannot describe. The id it carries
resolves to the blank plot a deed places — the perimeter and the front steps, nothing else.
Every wall, floor, roof and stair the owner added lives in the server's design, so a record
that carries one is expanded from that instead of from `multi.mul`:

```json
{
  "itemId": 21549, "hue": 0, "multi": true,
  "position": { "x": 2457, "y": 111, "z": 0 },
  "design": {
    "revision": 725, "width": 12, "height": 13,
    "tiles": [
      { "itemId": 1873,  "x": 2453, "y": 118, "z": 0 },
      { "itemId": 10578, "x": 2453, "y": 117, "z": 7 }
    ]
  }
}
```

It is the `design` key that decides this, not the item's type: a record carrying one is drawn
from its tiles, and everything else with `multi` set still goes through `multi.mul`.

A design's tiles are in facet coordinates already, not offsets from the house, and they cover
the foundation too — down to the graphics an owner who changed the foundation type picked,
which is why the design replaces the stock multi rather than sitting on top of it. The design's
own origin tile, `itemId` 1, is the nodraw marker and is dropped. `revision`, `width` and
`height` describe the plot and the edit that produced it; only the tiles are drawn.

Storeys separate themselves: each floor's tiles carry the z it was built at, so the depth sort
handles a three-storey house the same way it handles a hill.

### How placements are used

The placements are grouped by map block and merged into `StaticsFile` before it sorts each
cell, which is what makes them behave like real statics rather than stickers: a locked-down
chair sorts against the floor it stands on, occludes and is occluded correctly, and is picked
up by the view range and the tile pyramid without either knowing it is there.

Two things the format cannot tell the renderer:

- **It names no facet.** Every item is placed on whichever `--map` is being rendered. Pointing
  an export at the wrong one scatters furniture across it; `--verbose` reports how many tiles
  landed off the map, which is the symptom.
- **It does not say what is inside something else.** Items held in a container are typically
  exported with their position inside the container's gump rather than a world position, and
  those land in a heap near tile 0,0. Filtering them out belongs in the export.

## Image formats

`--format` picks the encoder: `png`, `jpg`, `gif`, `webp` or `webp-lossless`. A single image
otherwise follows its output extension; a pyramid defaults to PNG. `--quality` (1-100, default
85) applies to the lossy two.

Measured on one 704x704 slice of detailed terrain, and on a 76-slice pyramid of the same area:

| format | size vs PNG | alpha | lossless | notes |
| --- | --- | --- | --- | --- |
| `png` | 1.00 | yes | yes | the default; fastest to encode |
| `webp-lossless` | 0.45 | yes | yes | **byte-identical pixels at under half the size**, ~1.9x the encode time |
| `webp` | 0.28 | yes | no | smallest with transparency intact |
| `jpg` | 0.27 | **no** | no | smallest overall, but see below |
| `gif` | 0.54 | 1-bit | yes | 256 colours; holds up better than expected, because the source art is 16-bit anyway |

`webp-lossless` was verified genuinely lossless rather than taken on trust: decoded back, it is
bit-identical to the PNG across every channel of every pixel. For a whole facet that is the
difference between 2.5 GB and 1.1 GB for the same image data, so it is worth preferring over the
PNG default unless something in the chain cannot read WebP.

**JPEG has no alpha.** Pyramid slices default to a transparent background, so with `--format jpg`
they are flattened onto an opaque colour first (`--background` chooses it, black otherwise) and
the renderer says so. Flattening is done properly rather than by dropping the alpha channel:
discarding it would encode whatever colour sat beneath a fully transparent pixel -- black, for a
discarded texel -- and put a dark fringe around every sprite. The consequence is still that
partly covered slices become black squares instead of showing the page through, so JPEG suits a
pyramid that covers its whole area and PNG or WebP suits a `--region`.

Shallower pyramid levels are averaged from the level below, so a lossy format re-encodes at each
step. The averaging attenuates the artefacts it inherits, and at quality 85 the top levels hold
up, but `webp-lossless` avoids the question entirely.

Changing format on an existing pyramid is detected: a resume only looks for the extension it is
writing now, so it would re-render everything and leave two formats interleaved. The renderer
warns and names the format already there.

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
| `--items <file>` | JSON export of shard items to draw over the client's statics |
| `--min-z`, `--max-z` | Restrict the z range |
| `-v, --verbose` | Report the view range, tile counts and timings |
| `--tiles <dir>` | Write a browsable tile pyramid here instead of one image |
| `--slice-tiles <n>` | Tiles per slice edge (default 16, giving 704px slices) |
| `--region <x1,y1,x2,y2>` | Cover only this tile rectangle |
| `--min-zoom`, `--max-zoom` | Pyramid levels to write (`--max-zoom` sets the depth; `--zoom` sets the detail) |
| `--threads <n>` | Slices rendered at once (default: every core) |
| `--overwrite`, `--dry-run` | Re-render existing slices / report the plan and stop |
| `-b, --background <c>` | `transparent`, `black`, `white` or `#RRGGBB[AA]` |
| `-f, --format <fmt>` | `png`, `jpg`, `gif`, `webp`, `webp-lossless` |
| `-q, --quality <1-100>` | Encoder quality for `jpg` and `webp` (default 85) |

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
| `Assets/` | `.mul` readers: tiledata, art, texmaps, hues, multis, plus the memory-mapped file and index primitives |
| `Map/` | Terrain and statics layers, the shard-item overlay, facet dimensions, and `MapScene` — the data and visibility rules the geometry is built against |
| `Rendering/` | The port: `IsoProjection`, `LandObject`, `StaticObject`, `MapRenderer`, `MapEffect`, `Rasterizer` |
| `Tiling/` | `SliceGrid` (canvas geometry), `PyramidGenerator`, `LeafletViewer` |

`MapScene` replaces CentrED's `CEDGame.MapManager` singleton, which the geometry code reaches
through for tile data and options. Passing it explicitly keeps that code testable.

## Divergences from the reference

- **The camera is evaluated in closed form, not as a matrix product.** CentrED's `Camera` builds
  look-at, mirror, oblique shear, translation, orthographic and zoom matrices; `IsoProjection`
  substitutes its basis into them once and keeps the algebra, which agrees with the matrix chain
  to 1e-11. A matrix tells you nothing about which tiles reach a given canvas, and evaluated per
  slice in single precision it places the same world point a fraction of a pixel differently
  depending on the canvas size -- enough, at 45 degrees, to break slice agreement.
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
