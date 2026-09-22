# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A single-project .NET 10 CLI that renders a region of an Ultima Online map to an image, or
slices a whole facet into a Leaflet-browsable tile pyramid. It reads the client's `.mul` files
directly and rasterizes on the CPU.

The renderer is a **port of [CentrED#](https://github.com/kaczy93/centredsharp)'s `MapRenderer`
and its `MapEffect.fx` shader**, with the fixed-function GPU pipeline underneath them
reimplemented in software. That reference relationship is the single most important thing to
know: much of the code is deliberately shaped like the original (`Begin`/`DrawMapObject`/`End`,
the quad-to-triangle index pattern, the technique names, `Constants.cs` ported verbatim) even
where a from-scratch design would differ. Divergences from the reference are intentional and
documented in `README.md` under "Divergences from the reference" — read that before
"simplifying" anything that looks odd.

`README.md` is unusually complete and explains the *why* behind most decisions. Prefer reading
it over inferring from code.

## Build and run

```bash
dotnet build                                  # Debug
dotnet run -- --help                          # full switch list
dotnet run -- --data ./client --map 1 -x 1440 -y 1688 -o map.png
dotnet build -c Release                       # see licence note below
```

There is **no test project and no test framework.** Correctness of the geometry is established
by rendering comparisons rather than unit tests — a small render is checked to be bit-identical
to the centre crop of a larger one, and a 32-tile slice to its four 16-tile slices stitched
together. If you change the projection, the rasterizer, or slice geometry, verify that way
(render twice, compare pixels) rather than assuming.

`Example/` is dropped from the compile glob by `swmaprenderer.csproj` and is not part of the
program. It holds the server-side command that produces the `--items` and `--mobiles` JSON, and
compiles against a UO server's assemblies, not against anything here — do not try to make it
build.

Rendering anything requires a folder of client `.mul` files, which is **not in the repository**
(`client/` and `vanilla_client/` are gitignored). Without one, only `--help` and `--dry-run`
paths are exercisable.

## The Six Labors licence

ImageSharp 4.x validates a licence key at build time, and the validation task uses
`ContinueOnError="$(Configuration.StartsWith('Debug'))"`. So:

- **Debug builds warn** when no licence is found and succeed anyway.
- **Release builds fail.**

Locally the key comes from `sixlabors.lic` in the repository root (gitignored). In CI it is
supplied as an environment variable named exactly `SixLaborsLicenseKey` — MSBuild picks
environment variables up as properties, and the task reads the property `$(SixLaborsLicenseKey)`.
The `SIXLABORS_LICENSE_KEY` name that Six Labors' blog mentions does **not** work with 4.1.2. A
key set this way takes precedence over the file; the targets only look for a file when the key
property is empty.

`swmaprenderer.csproj` sets `SixLaborsLicenseFile` to an explicit path on purpose: the default
is a recursive `**/sixlabors.lic` glob, which would walk the enormous `client/` data folder on
every build. Do not remove it.

## Architecture

Configuration flows `appsettings.json` (read from the *binary's* directory) → `SWMAP_`-prefixed
environment variables → command line, bound onto `RenderOptions`. `CommandLine.Normalize`
pre-processes argv because the configuration provider cannot handle bare boolean flags
(`--flat`) or negations (`--no-statics`). Note the asymmetry: `--data` resolves against the
current working directory while `appsettings.json` does not.

`Program.cs` then branches once, on `options.IsTiling`: one image via `SceneRenderer`, or a
pyramid via `PyramidGenerator`.

| Layer | Role |
| --- | --- |
| `Assets/` | `.mul` readers (art, texmaps, hues, tiledata, multis, animations, body tables) over memory-mapped files. `UoFiles` owns them all and stands in for CentrED's `UOFileManager`; `DataFolder` resolves names case-insensitively because shipped clients mix cases. |
| `Map/` | Terrain and statics layers, the shard item and mobile overlays, `MapDimensions`, and `MapScene`. |
| `Rendering/` | The port: `IsoProjection`, `LandObject`/`StaticObject`/`MobileObject`, `MapRenderer`, `MapEffect`, `Rasterizer`. |
| `Tiling/` | `SliceGrid` (canvas geometry), `PyramidGenerator`, `PointsOfInterest`, `LeafletViewer`. |

**`MapScene` is the seam that replaces CentrED's `CEDGame.MapManager` singleton.** The ported
geometry code reaches through it for tile data, visibility rules (`CanDrawLand`,
`CanDrawStatic`) and hue packing. It is passed explicitly rather than being static, which is
what makes the geometry thread-safe — `PyramidGenerator` renders slices in parallel over one
shared `MapScene`, pooling `SceneRenderer` instances in a `ConcurrentBag`.

**Draw order is load-bearing, not cosmetic.** `SceneRenderer.Render` does terrain, then statics,
then mobiles, and the passes are coupled through the depth buffer: statics rely on the depth the
terrain pass left behind to be occluded by hills in front of them.

**Shard data is merged, not overlaid.** `--items` placements are grouped by map block and merged
into `StaticsFile` before it sorts each cell, so a locked-down chair sorts, occludes and is
view-ranged exactly like a real static.

### The precision invariants

Several pieces of this code are in double precision, or written in an unobvious closed form,
specifically so that independently rendered tile slices agree along their shared edges. These
are the things most likely to be "cleaned up" into bugs:

- **`IsoProjection` is a closed form, not a matrix chain.** A matrix cannot answer "which tiles
  reach this canvas" (`VisibleTiles` needs that), and evaluated per slice in single precision it
  places the same world point sub-pixel-differently depending on canvas size. Tile edges run at
  exactly 45°, so 1e-4 px is enough to hand a whole diagonal of pixel centres to the wrong tile.
- **`ScreenVertex.X`/`.Y` are `double`.** Same reason. `Z` need not be.
- **`Rasterizer.Edge` is evaluated in double.** In single precision the signs go arbitrary near
  an edge, both triangles reject the pixel, and the dropouts line up into one-pixel seams.
- **`SliceGrid`'s origin is a whole number of pixels**, so every slice is the shared canvas
  shifted by an integer offset.
- **Land tile corners are derived from the tile index**, not by adding `TILE_SIZE` to the near
  edge — see `LandObject.UpdateCorners`.

The projection only ever sees a tile through the diagonals `A = tx - ty` and `B = tx + ty`,
which always share a parity (`A + B = 2tx`). `TileRhombus.Iterate` relies on that; starting a
row on the wrong parity steps over every tile in it.

**The view range is inverted from the projection, not heuristic.** CentrED's
`(w + h) / zoom / 2.6 + 8 tiles` is sized for an interactive window where a late tile goes
unnoticed; here anything the range misses is a hard discontinuity between neighbouring slices.

### Colour conventions

The framebuffer is **premultiplied** alpha, because that is what the blend equation produces.
`Rasterizer.CopyTo` divides it back out for straight-alpha output, and pyramid downsampling
averages *in* premultiplied space — averaging straight alpha drags transparent pixels' colour
into every sprite edge. Both directions matter; getting either wrong is a subtle fringe rather
than an obvious break.

## Conventions

The prose style in this codebase is distinctive and worth matching: comments explain *why* a
thing is the way it is, usually naming the failure that made it necessary, and the README
documents trade-offs rather than features. Several comments exist specifically to stop a future
reader from reverting a fix. When you change something these comments describe, update them.