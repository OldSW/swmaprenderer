# SW Map renderer

Renders a region of an Ultima Online map to an image. The renderer is a CPU port of
[CentrED#](https://github.com/kaczy93/centredsharp)'s `MapRenderer` and its `MapEffect.fx`
shader, reading the client's `.mul` files directly.

```
swmaprenderer --data ./client --map 0 --x 1420 --y 1690 -w 1024 -h 768 -o britain.png
```

## Motivation

The initial idea was to have a browsable map for our shard [Schattenwelt](https://alte-schattenwelt.de). There is a lot of tooling out
there for working with Ultima Online client files and one thing that many have in common
they are written in C#. So we decided to use these tools as a starting base for this map
renderer. The whole project has been built with Claude Code and Centred# as a reference implementation for
rendering a map.

## Why open source?

The whole project wouldn't have been possible without other open source tools. And the UO community
is also alot about sharing experiences else the whole freeshard scene wouldn't be possible.

## Special Thanks

Thanks to the [CentrED#](https://github.com/kaczy93/centredsharp) project. This gave us the initial idea
how to begin with this whole project.

Thanks to [ClassicUO](https://github.com/ClassicUO/ClassicUO). Without ClassicUO this project would not exist.

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

### GPU rendering

`--gpu` (`Render:Gpu`) swaps the rasterizer, and only the rasterizer, for one that runs on the
GPU (`Rendering/Gpu/`). It is optional: nothing is loaded without the switch, and if no GPU can be
set up the run says why once and carries on on the CPU. Geometry, culling, the view range and the
sprite caches are untouched and still run on the CPU, so the gain is in the pixel work and is
largest for big canvases and dense views; a pyramid run is also bound by geometry and image
encoding.

`--gpu-api` (`Render:GpuApi`) picks the API: `vulkan`, `opencl`, or `auto`, the default, which tries
Vulkan and then OpenCL. Vulkan goes through Silk.NET and needs a Vulkan runtime: your driver
on Windows and Linux, and on a Mac MoltenVK (`brew install molten-vk`), which the program finds
under Homebrew or `$VULKAN_SDK` without a loader. OpenCL needs nothing extra and is bound directly,
but Apple has deprecated it. Both run the same program, `raster.cl` and `raster.comp`, which have
to be changed together. The Vulkan one is embedded as SPIR-V (`raster.spv`, checked in) so nothing
needs a shader compiler to build; after editing `raster.comp` rebuild it with
`glslc -O --target-env=vulkan1.1 Rendering/Gpu/raster.comp -o Rendering/Gpu/raster.spv`
(`brew install shaderc`). Vulkan buffers are host-visible and mapped, which costs nothing on
unified memory (Apple, integrated GPUs) and is slower than staging on a discrete card.

How it keeps the properties the software rasterizer was built around:

- **Draw order.** Drawing a triangle only records it. At read-back the triangles are binned into
  16x16 pixel tiles in draw order, and one work item per pixel replays its tile's list, holding
  colour and depth in registers. Depth `Less`, premultiplied blending and the terrain, statics and
  mobile passes behave as they do on the CPU, with no atomics.
- **Edges without double precision.** Apple's GPUs have none, so coverage is decided in integers:
  vertices are snapped to 1/256 px on the CPU (in double) and the edge functions are exact 64-bit
  integers. Those are unchanged by shifting vertices and pixel by whole pixels, which is how one
  slice differs from the next, so every barycentric, and therefore every pixel, is the same
  whatever canvas it was drawn into. A small render is bit-identical to the centre of a larger one.
- **Not bit-identical to the CPU.** Sampling at the exact texel boundaries that the 45 degree land
  mapping lands on is decided by float rounding noise (CentrED's epsilon inset is smaller than the
  noise), and the CPU and GPU round differently. In a pyramid slice, where tile corners sit on
  whole pixels, about a third of terrain pixels pick the neighbouring texel; in a centred single
  image about 1% do. It is not visible, but a pyramid resumed with `--gpu` over slices rendered
  without it will mix the two, and neither is more right.

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
-- 61 gigapixels. The estimates below are `--dry-run`'s for map 1 on twelve threads; the measured
column is only filled where a full run was done (WebP, 2.0 GB of the 3.5 GB the dry run
predicts, because the facet is mostly water and open ground, and those slices compress far
better than the dense terrain the estimate is fitted to):

| `--max-zoom` | px per tile | slices | estimate PNG | estimate WebP | measured WebP |
| --- | --- | --- | --- | --- | --- |
| 9 (default) | 44 | 78,812 | ~11m, ~57 GB | ~12m, ~14 GB | |
| 8 | 22 | 20,059 | ~3m, ~15 GB | ~3.2m, ~3.5 GB | **2.6m, 2.0 GB** |
| 7 | 11 | 5,134 | ~51s, ~3.7 GB | ~55s, ~0.9 GB | |
| 6 | 5.5 | 1,375 | ~19s, ~1.0 GB | ~21s, ~245 MB | |

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

Markers sit above the tiles as ordinary DOM rather than as pixels, which is what lets them be
searched and changed without re-rendering; see [Points of interest](#points-of-interest).

`--start <x,y[,zoom]>` (`Render:Start`) opens the page at a tile instead of fitting the whole
facet. The zoom is the level the page's readout shows and may be fractional; left out it is one
level short of the deepest, as for the search box. A position in the URL hash still wins, so
shared links keep working. The page also accepts `?x=1420&y=1690&zoom=4.5` (`z` works for `zoom`;
the zoom is optional); the hash, which the page rewrites as you pan, takes precedence over it.

`?preview=<json>` draws a shape on the map and fits the view to it, so a submission can be
checked by eye. The JSON is the element format the add-markers panel submits (see
`--submit-url`): one element, a list of them, or `{ "elements": [...] }`, e.g.
`?preview={"type":"outline","name":"Forest","color":"#6bcb77","points":[{"x":1400,"y":1700},{"x":1450,"y":1700},{"x":1450,"y":1750}]}`
(URL-encode it when generating links). It is drawn in a layer of its own and never added to
the panel's drawing, so it is not submitted. Elements that are malformed are skipped. A position
in the hash still wins over the fit, so panning after opening a link behaves as usual.

`--template <file>` (`Render:Template`) swaps the built-in page for your own. A template is
the built-in page's contract and nothing more: `/*__CONFIG__*/null` is replaced with the
pyramid's settings as a JSON object (grid, zoom levels, extension, marker sources) and
`/*__POIS__*/null` with the markers embedded from files, or `null`. The first is required, the
second optional. The built-in page is `index.template.html` in the repository root, compiled into the binary,
and is the place to start from: edit it to change the default viewer, or copy it for your own.

`--base-url <url>` (`Render:BaseUrl`) is where the pyramid will be served from, e.g.
`https://maps.example.com/felucca/`. The page carries Open Graph tags so Discord and similar
can preview a pasted link; crawlers do not run scripts, so they are written into the HTML at
generation time, and Discord ignores a relative `og:image`, which is why the URL is needed. The
image is the tile holding `--start` at its zoom level, or `<min-zoom>/0/0` (the whole facet at
`--min-zoom 0`) without one. It cannot follow a visitor's own position: the `#x,y,zoom` hash never
reaches a server, so a preview of an arbitrary shared link would need a server rendering on demand. Without it the title and
description are still there but no image. A custom template opts in with `<!--__OG__-->` in its
`<head>`.

`--submit-url <url>` (`Render:SubmitUrl`) switches on the submit button of the viewer's "+ add
markers" panel in the lower right corner. The panel lets a visitor click out points, lines
(two points or more), curves (a smooth path through three points or more) and outlines (closed shapes, three corners or more), give each a name and colour, and send them on. They are
drawn in a layer of their own, never mixed with the shard's markers, and are lost on reload. The
page POSTs `{ "mapId": 1, "elements": [ { "type": "point", "name", "color", "x", "y" }, { "type":
"line", "curve" or "outline", "name", "color", "points": [ { "x", "y" } ] } ] }` as `application/json`, with
coordinates in tiles (line, curve and outline points are tile corners; a curve is the points you clicked, and the
curve is a Catmull-Rom spline through them). Each element may also carry an optional `category` and
`subtype` (text, up to 50 characters each), which the panel offers as two fields under the name: a suggestion
for where the shard files it in the legend, left out of the JSON when empty. A failed submit keeps the drawing. The
browser makes the request, so an endpoint on another origin has to answer with CORS headers. Without
`--submit-url` the panel still draws and offers "copy json" instead.

`index.php` in the repository root is the server-side half of that, for hosts running PHP 8 with
GD. It is compiled into the binary and written next to `index.html` on every pyramid run; serve
it as the entry point. Apache serves `index.html` for the bare directory by default, so add
`DirectoryIndex index.php index.html` to `.htaccess` or links without `index.php` get no preview. For a link with
`?x=&y=&zoom=` (what the share button copies) it serves `index.html` with Open Graph tags naming
that position and an `og:image` that is a 800x420 crop of the tiles centred on it, cached in
`og-cache/`. It reads the pyramid's settings out of `index.html`, so it needs no `--base-url`
and no configuration; it replaces any tags the renderer wrote. The hash form is not seen by a
server, so only query links get a position-specific preview.

Tiles that were never written -- ocean, or off the edge of the facet -- simply 404 and show
through to the page background, which is why pyramid slices default to a transparent background
where a single image defaults to black.

### Points of interest

A rendered tile cannot say what a place is called. `--pois` hands the viewer a list of named
places to mark and to search, drawn as a DOM overlay rather than into the slices -- which is
what makes them searchable, and why moving one costs nothing.

```
swmaprenderer --map 1 --tiles ./web/map1 --pois https://shard.example/api/pois
```

The value is a path or an `http(s)` URL, and the difference is who reads it:

- **A path** is read when the pyramid is written and embedded in `index.html`. The page then
  needs nothing to serve it, so markers survive opening the file straight off disk.
- **A URL** is left to the page, which fetches it on load. Markers follow the API without a
  single slice being re-rendered -- but the endpoint has to send a permissive
  `Access-Control-Allow-Origin`, and a page opened over `file://` cannot fetch cross-origin at
  all. The viewer says so in place of the marker count rather than showing an empty map.

By default a URL is fetched once, on load. `--pois-refresh <seconds>` (`Render:PoisRefresh`)
has the open page fetch it again on that interval, so a marker that moves on the shard moves
on the map without anyone reloading. The floor is 5 seconds, and it is an error with a file,
which is embedded and cannot change under the page. A poll that fails leaves the markers as
they were and tries again next time; one that succeeds replaces them, keeping whichever types
were switched off. Hidden tabs skip their polls.

`appsettings.json` can carry either under `Render:Pois`; `--pois ""` turns it off for a run.
A single image ignores it -- there is no viewer to put a marker in.

The canonical record is a name and a position, and everything else is optional:

```json
[
  { "name": "Britain", "category": "town", "mapId": 1,
    "position": { "x": 1427, "y": 1756, "z": 16 },
    "description": "Town stone" }
]
```

`category` is shown under the name and is searched alongside it, so `dungeon` finds every
dungeon. `mapId` is the one field with teeth: a record that names a facet and means a different
one is dropped, and a record that names none is placed on whichever `--map` is being rendered.
That is the opposite default from `--items`, because a POI list is usually a whole shard's
rather than one facet's.

Since the file is normally an export rather than something written for this tool, several
shapes are read. The array may be bare or under a `pois`, `points`, `items`, `results` or
`data` key; the name may be `name`, `title` or `label`; the position may be `position`, `go`,
a bare `x`/`y`/`z` on the record, or -- failing all of those -- the middle of the first
rectangle in a `coords` list. A shard's region export therefore works unchanged:

```
swmaprenderer --map 1 --tiles ./web/map1 --pois regions.json
```

`(0, 0)` is read as *no position* rather than as the corner of the map, because that is what an
export writes for a record that has none; a third of the region list to hand is that. Records
skipped for that, for an empty name, for naming another facet or for falling off the map are
counted and reported, so a file that yields nothing says why.

#### Several sources

`--pois` names one source. `--pois-config <file>` (`Render:PoisConfig`) names as many as you
like in a JSON file, each stating whether it is a file to embed or a URL for the page to fetch
-- and, for a URL, how often:

```json
{
  "sources": [
    { "name": "towns", "type": "file", "path": "pois.json" },
    { "name": "live",  "type": "url",  "url": "https://shard.example/api/pois", "refresh": 30 },
    { "name": "quests", "type": "url", "url": "https://shard.example/api/quests" }
  ]
}
```

- `type` is `file` or `url`, and may be left out: it follows whichever of `path` and `url` is
  present. When given it has to agree with them -- a `file` carrying a `url` is an error.
- `refresh` is seconds between fetches, 0 or absent for once on load, at least 5 otherwise.
  It is an error on a `file`, which is embedded when the pyramid is written.
- A relative `path` is relative to the configuration file, not to the working directory.
- The list may be bare, without the `{ "sources": }` wrapper. Every problem in it is reported
  at once, before any slice is rendered.

Each source is itself a list in any of the shapes above, with its own `types` block. All of
them end up on one map, searchable together, with one checkbox per category across the lot.
Where two sources define a look for the same category, the later one in the list wins -- the
order is the configuration's, not the order the responses arrive in. Each URL polls on its own
schedule, and one that is down does not hold up the others: the page names the source it could
not reach. When set, `--pois-config` replaces `--pois` and `--pois-refresh`.

A source that is somebody else's API rather than a list written for this tool can say how to
read it, because the API cannot be asked to change:

- `key` is the property holding the array of records, for a response that has several
  (`"players"`), and is tried before the usual `pois`, `points`, `items`, `results` and `data`.
  Everything else in the response is ignored.
- `category` is what records that name none are called, `mapId` the facet records that name
  none are on (so a source for another facet is dropped rather than drawn on this one), and
  `types` gives looks to categories, in the same form as the `types` block of a file.
  A `types` entry here wins over one in the source's own response.

- `fields` says where in a record each property is, as dotted paths (`"pos.east"`, `"loc.0"`).
  It can map `name`, `category`, `description`, `icon`, `url`, `mapId`, `position` (the path of an object
  with `x`, `y`, `z`), and `x`, `y`, `z` separately for an API that keeps the coordinates
  apart:

  ```json
  "fields": { "name": "who.nick", "x": "loc.east", "y": "loc.north", "z": "loc.alt" }
  ```

  A mapped property is read from its path alone: if the path is not there, the record has
  none, rather than falling back to a same-named property that means something else. `x`, `y`
  and `z` win over `position` axis by axis. Coordinates may be numbers or numeric text, and
  fractions are rounded to the tile. An unknown name in `fields` is an error, listed with the
  ones that exist.

The shard's status API is read this way: its `players` array has a `name` and a `position`
each, and its `cities` and counters are none of the map's business. `poi-sources.json` in this
repository wires it up next to the town stones, polling every 15 seconds, with players in
blue. The API names no facet and only lists who is online, so `mapId` is the facet you put them
on -- a player who is actually on another facet is drawn on this one, at their coordinates.

#### Icons per type

A marker is a yellow dot unless its category says otherwise. A type can name an `icon`, or
failing that a `color` for the dot, in a `types` block beside the list, so the file becomes
`{ types, pois }`:

```json
{
  "types": {
    "town":    { "icon": "icons/town.png" },
    "dungeon": { "icon": "https://shard.example/icons/dungeon.png" },
    "shop":    { "color": "#6cc4ff" }
  },
  "pois": [ { "name": "Britain", "category": "town", "position": { "x": 1427, "y": 1756, "z": 16 } } ]
}
```

The icon is a URL the *browser* resolves, so a relative one is relative to `index.html` -- put
the images in the pyramid folder -- and it is neither read nor copied at generation time. A
bare string (`"town": "icons/town.png"`) is accepted for `{ "icon": ... }`. Type names match
categories regardless of case. An icon wins over a colour; a type with only a colour keeps the
dot and paints it, and an unlisted category keeps the default yellow. A colour is any CSS colour
the browser accepts (`#6cc4ff`, `crimson`, `rgb(...)`); one it rejects is ignored. The icon or
colour also appears beside the type's checkbox. It works the same whether the file is embedded or fetched from a URL,
since both carry the same envelope. A bare array has no place for `types` and so no icons.

A type can also say `"label": false` to keep its markers' names off the map: the name then shows
only in the tooltip on hover (and in the popup), however far the view is zoomed in. It suits a
category with hundreds of markers, where the labels would bury the icons.

A single marker can carry its own `icon` too, in the record itself, and it wins over the icon of
the marker's type. It is a URL resolved by the browser like the type's, and a source's `fields`
can map it from the API's own property. Only the marker on the map uses it; the legend still
shows the type's.

A marker can also carry a `url` (or `link`), a page about it. Its popup makes the marker's name that
link, opening in a new tab. Only `http(s)` and relative URLs are followed: anything else, `javascript:`
above all, is dropped, because a record from someone else's API must not be able to put a script
in the popup. A source's `fields` can map it from the API's own property.

#### Subtypes

A record can refine its category with a `subtype` (`subType` and `subcategory` are accepted
too, and a source's `fields` can map it): `"category": "vendor", "subtype": "alchemist"`. A
type lists the looks of its subtypes under `subtypes`:

```json
{
  "types": {
    "vendor": {
      "color": "#f5c542",
      "label": false,
      "subtypes": {
        "alchemist": { "icon": "icons/alchemist.gif" },
        "smith":     { "icon": "icons/smith.gif" },
        "healer":    { "color": "#7be07b" }
      }
    }
  },
  "pois": [ { "name": "Haro", "category": "vendor", "subtype": "alchemist", "position": { "x": 6054, "y": 1312, "z": 55 } } ]
}
```

A subtype takes what it says and inherits the rest from its category, so `label: false` above
reaches all three. The one exception is that a subtype with a `color` and no `icon` drops the
category's icon -- it asked for a dot, and an icon would win over it. A marker's own `icon`
still beats both. Subtypes are one level deep, and one that is not listed still gets a legend
row, drawn with its category's look. A subtype is shown in the popup after the category and is
searched along with the name.

The `pois.json` in this repository is the shard's eighteen town stones -- `Townstone` items in
the item export, which all carry that same name -- each named after the region it stands in,
from `regions.json`.

#### Outlines

A type can say `"outline": true` to have the area its markers stand for drawn on the map as
well, in the type's `color` (gold if it has none):

```json
{ "name": "town-regions", "type": "file", "path": "town-regions.json", "category": "town-region",
  "types": { "town-region": { "color": "#ffd479", "outline": true, "label": false } } }
```

The area is the record's `coords` -- the rectangles of a region export -- or an `area` of
`[x1, y1, x2, y2]` arrays. A rectangle runs from its start tile up to, not through, its end
tile, which is how the shard's own `Rectangle2D` reads. The rectangles of one marker are
merged into a single outline: where they touch or overlap there is no line, so Britain's sixteen
are one shape. Rectangles that do not meet stay separate rings of the same outline, and a hole
is drawn as one. The marker is still drawn and is still
what a search lands on; the outline is in addition to it, filled faintly and not clickable so
that it never covers the marker. It is a property of the type rather than of a subtype, and
only types that ask for it have their rectangles embedded in the page: a region list holds
thousands, and the rest would be dead weight.

`town-regions.json` in this repository is the town regions of Schattenwelt from `regions.json`:
the ones whose `prefix` is `the town of`, which is how the shard's own region list marks a town,
plus Umrazim, whose region carries no prefix. That is more than the towns in `pois.json` -- Cove,
Skara Brae, Papua, Moonglow and a few camps and villages have regions but no town stone -- so
they get an outline and a dot but no town icon. Matching on the names of the stones was the first
attempt, and it is what missed Cove.

#### Lines, curves and outlines

A fetched source may carry `shapes` next to its `pois` -- the elements the add-markers panel
submits, in the same format:

```json
{ "pois": [ ... ],
  "shapes": [ { "type": "outline", "name": "Forest", "color": "#6bcb77", "mapId": 1,
                "points": [ { "x": 1400, "y": 1700 }, { "x": 1450, "y": 1700 }, { "x": 1450, "y": 1750 } ] } ] }
```

`type` is `line` (two points or more), `curve` (three or more, a Catmull-Rom spline through
them, as in the panel) or `outline` (three or more, closed and filled faintly); a `point` is
just a marker. `color` is `#rrggbb`, gold when missing or anything else. A shape is a marker
like the others: it needs a `name`, it takes `category` and `subtype` (`shape` and its `type`
when absent), it sits at the middle of its bounding box for the search to land on, and it has
its row in the legend. Its geometry is drawn while that row is on, and its name shows on hover.
The fill of an outline is not clickable, so it never covers a marker; the stroke is what is
hovered. As for markers, a shape that names another facet with `mapId` is dropped, and one that
names none takes the source's `mapId`. They refresh with the source, and a malformed one is
skipped rather than failing the rest. A record in `pois` may also have a `color`, which paints
its dot when it has no icon.

Only a `url` source can carry them: a `file` source is read and embedded by the tool, which
keeps the `pois` of it. `?preview=` draws a shape the same way.

### Finding a place

The viewer's box takes either `x, y` or a name. Typing a name lists up to twelve matches with
their coordinates; arrow keys and Enter pick one, which centres the map on it and opens its
marker. Matching ignores case and accents and folds `ß` to `ss`, so `stutzpunkt` finds
*Stützpunkt*, and a name that *starts* with what was typed outranks one that merely contains it.

Markers are drawn only where the view can see them, nearest the middle of the screen first and
capped at 400, because more than that is a smear rather than information. Names appear beside
the dots once a tile is wide enough to read them by -- or at any zoom when there are fewer than
forty in view, since eighteen towns on a whole facet are exactly what you want named.

Under the box is a checkbox per `category`, with how many carry it, so dungeons can be hidden
while banks stay. The list is discovered from the data rather than declared -- whatever kinds
of place the file turned out to hold are the kinds offered -- sorted by name, with the bucket
for records that named no category last. Unchecking a kind hides its markers *and* takes it out
of the search, since being sent to a place you have just said you do not care about is not
helpful. Data with no categories at all collapses to a single row, which is then just labelled
`markers`.

A category whose records name subtypes gets a row per subtype, indented beneath it with its
own count and look. Each subtype can be switched off alone; the category's own box switches all
of them together and shows half-checked when only some are on. Records of the category that
named no subtype are the `other` row. A category with no subtypes in the data has no rows
under it. The subtype rows start folded, since a category can have dozens; the arrow beside
the category opens them, and which are open survives a refresh.

## Shard items

`statics{N}.mul` holds the world as the client shipped it. Everything players have since put
down — locked-down furniture, decorations, the houses themselves — lives only in the server's
save, so a map rendered from client files alone shows an empty town. `--items <file>` folds a
JSON export of those items back in.

```
swmaprenderer --map 1 --items items.json --x 1550 --y 1690 -z 2 -o tavern.png
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

## Mobiles

`--mobiles <file>` adds the shard's creatures and people. Nothing alive is in the client files,
so this is the difference between a town and an empty stage set.

```
swmaprenderer --map 1 --mobiles mobiles.json --x 5531 --y 1292 -z 2 -o crowd.png
```

As with `--items`, `appsettings.json` can carry the path under `Render:Mobiles`, and
`--mobiles ""` switches it off for a single run. The file names no facet either.

```json
[
  { "body": 400, "hue": 33771, "direction": 2, "female": false,
    "position": { "x": 3655, "y": 2644, "z": 0 },
    "equipment": [
      { "itemId": 5397, "hue": 1624, "layerId": 20 },
      { "itemId": 5914, "hue": 1890, "layerId": 6 }
    ] }
]
```

`body` is an animation body id, not an art index — mobiles come from `anim.mul` rather than
`art.mul`, and `hue` is applied the same way an item's is. `direction` is the facing on the
wire, 0-7 clockwise from north. `equipment` is optional and only means anything on a human
body; `layerId` is the layer the piece is worn on, and `itemId` is the world graphic, whose
`tiledata.mul` entry names the animation that draws it worn.

Each mobile is drawn the way the client draws one standing still: the **first frame of its
idle action**, facing where the export says, with its equipment stacked over it.

Getting from a body id to a frame takes four of the client's text files, all of which must be
in the data folder alongside `anim.idx`/`anim.mul`:

| File | Question it answers |
| --- | --- |
| `mobtypes.txt` | Is this a monster, an animal or a human? Which decides where "standing" sits in its action list — action 1, 2 and 4 respectively |
| `body.def` | This body was never drawn; which one should stand in for it, and in what colour? |
| `bodyconv.def` | Which of `anim.mul` … `anim5.mul` is it in, and under what number there? |
| `equipconv.def` | This body has no art for that item; which piece should it wear instead? |

Only `anim.idx`/`anim.mul` are required. `anim2`-`anim5` were added by later expansions, and a
data folder without one simply has no bodies that need it; without any of them `--mobiles` warns
and draws nothing.

### How a mobile is placed

An `anim.mul` frame is not anchored like a static. A static's art hangs from the bottom corner
of its tile; a frame carries a reference point of its own, and the client subtracts that, having
first moved half a tile up the screen — a mobile stands in the *middle* of its tile rather than
at the corner.

Both offsets are applied by moving the sprite's geometry through the world rather than on the
screen. Stepping the same distance along `+x` and `-x` slides a sprite sideways without moving
it up or down or changing its depth; the vertical anchor goes into `z`, where one unit is one
pixel up the screen. Within one mobile the frames then step towards the camera a fraction each,
which is what makes a hat cover the head and a mobile stand in front of the floor it is on.

**A mobile is a flat billboard, and a static is not.** A static is two quads meeting at its
tile's centre line, one receding along `-x` and one along `-y`, so that a wall interleaves with
the scenery around it. The price is that its depth falls away by half a sprite towards either
edge:

```
wz(x, y) = groundZ + (screen row) - |x - spriteCentreX|
```

Drawing one sprite that way is fine. Stacking a dozen is not. Every frame of a dressed body is
a different width with its own anchor, so that last term differs per layer — by tens of world
units, swamping the bias that is supposed to order them — and the naked body wins the depth
test through whatever it is wearing. Flat, the term is gone: every layer of a mobile has the
same depth at the same pixel, the height of that screen row above the ground, and the paint
order is decided by the layer bias alone.

The trade is that a mobile no longer recedes towards its own edges, so it sits a little further
forward against scenery on neighbouring tiles than a static of the same size would. That reads
better than the alternative: a creature shredded by its own depth wedge.

### Layer order

Equipment is a painter's algorithm, so the order *is* the result: get it wrong and the shirt
covers the breastplate over it. There is no rule to derive it from. The client carries a table,
picks between three variants on what is worn, then shuffles individual layers for a handful of
graphics whose art was drawn out of order — and moves the cloak by facing, since a cloak hangs
behind someone walking towards you and in front of someone walking away. `PaperdollOrder` is a
port of ClassicUO's table and rules.

One thing the client does that this does not: it culls layers an occluder is expected to hide
completely, on top of the paint order, because some art paints outside the bounds of the item
meant to cover it. Without that a boot or a legging occasionally peeks out from under a robe.

## Producing the exports

`--items` and `--mobiles` read what a server chose to write out, and the renderer takes no
position on how that happens. `Example/ExportItemsMobilesCommand.cs` is the command our shard
uses, kept here as a worked example of the shape the two files are expected to have. It is not
compiled into this program — it builds against a UO server's own assemblies, so
`swmaprenderer.csproj` drops it from the compile glob — and it is written against a
RunUO-lineage codebase, so treat it as a reference to adapt rather than a drop-in.

It writes `items.json` and `mobiles.json` into the server's web directory, both on `WorldLoad`
and on demand as `exportitemsmobiles`, an administrator command. What it leaves out is the
interesting part, and each omission matches something documented above:

- **Items inside containers are skipped** (`item.Parent != null`), which is the filtering the
  section on shard items says belongs in the export rather than in the renderer.
- **A `HouseFoundation` carries its current design**, with tile positions made absolute and the
  multi bit masked off, which is exactly what [Customizable houses](#customizable-houses)
  expects. Tiles the client would not draw are dropped, except the first, which is the origin
  marker the renderer then discards itself.
- **Equipment stops at the layers a mobile is actually drawn wearing** — the backpack, bank box
  and anything outside the valid layer range never reach the paperdoll.
- **Player mobiles are never exported.** They stay in the world after logout, so writing them
  out would publish where people log off.

One facet is exported, hard-coded rather than taken as an argument, and neither file records
which one — matching the renderer's own rule that items and mobiles land on whichever `--map`
is being drawn. Names, titles and type names are written too; the renderer ignores them, but
they make the files worth reading on their own.

## Image formats

`--format` picks the encoder: `png`, `jpg`, `webp` or `webp-lossless`. A single image
otherwise follows its output extension; a pyramid defaults to PNG. `--quality` (1-100, default
85) applies to the lossy two. Slices are read and written through SkiaSharp's native codecs,
including the child slices a pyramid averages from. Skia's WebP encoder is libwebp with no effort
setting. GIF is not written, because Skia does not encode it.

Measured on a level-8 pyramid (476 slices) of 600x500 tiles of Britain, twelve threads, relative
to PNG:

| format | size vs PNG | time vs PNG | alpha | lossless | notes |
| --- | --- | --- | --- | --- | --- |
| `png` | 1.00 | 1.00 | yes | yes | the default; zlib makes it the slowest encoder here |
| `webp-lossless` | 0.58 | 0.86 | yes | yes | **byte-identical pixels at 58% of the size, and faster than PNG** |
| `webp` | 0.24 | 1.08 | yes | no | smallest with transparency intact |
| `jpg` | 0.22 | 0.76 | **no** | no | smallest and fastest, but see below |

`webp-lossless` was verified genuinely lossless rather than taken on trust: decoded back, it is
bit-identical to the PNG across every channel of every pixel. For a whole facet that is
about 40% off the disk and no extra time, so it is worth preferring over the PNG default unless
something in the chain cannot read WebP.

**JPEG has no alpha.** Pyramid slices default to a transparent background, so with `--format jpg`
they are flattened onto an opaque colour first (`--background` chooses it, black otherwise) and
the renderer says so. Flattening is done properly rather than by dropping the alpha channel:
discarding it would encode whatever colour sat beneath a fully transparent pixel -- black, for a
discarded texel -- and put a dark fringe around every sprite. The consequence is still that
partly covered slices become black squares instead of showing the page through, so JPEG suits a
pyramid that covers its whole area and PNG or WebP suits a `--region`.

Shallower pyramid levels are averaged from the pixels of the level below *before* they are
encoded, so a lossy format does not compound its artefacts level over level. Only a slice left
by an earlier run is read back from disk, and that one has been through the encoder once.

Changing format on an existing pyramid is detected: a resume only looks for the extension it is
writing now, so it would re-render everything and leave two formats interleaved. The renderer
warns and names the format already there.

### Performance baseline

Time and disk estimates (`Rough cost`) are fitted to a matrix of 39 runs: levels 5 to 9, two,
four and twelve threads, PNG and WebP, plus six, eight and ten threads for levels 7 to 9 in PNG, over one fixed region (`--region 1200,1450,1800,1950`,
600x500 tiles of Britain) on a 12-core Apple machine, with the shard's `--items` and `--mobiles`
loaded. Wall seconds, by thread count:

| level | slices (rendered + averaged) | 2 thr | 4 | 6 | 8 | 10 | 12 | PNG | WebP |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 9 | 1,368 + 476 | 70 | 36.5 | 25.5 | 20.0 | 18.5 | 17.9 | 1,389 MB | 331 MB |
| 8 | 342 + 134 | 18.5 | 10.5 | 7.8 | 6.7 | 6.2 | 6.3 | 359 MB | 86 MB |
| 7 | 90 + 44 | 7.0 | 4.7 | 3.6 | 3.6 | 3.5 | 3.4 | 107 MB | 23 MB |
| 6 | 25 + 19 | 3.4 | 2.7 | | | | 2.5 | 34 MB | 6.7 MB |
| 5 | 9 + 10 | 2.6 | 2.5 | | | | 2.7 | 13 MB | 2.5 MB |

Levels 5 and 6 were run at 2, 4 and 12 threads only; 6, 8 and 10 were added for levels 7 to 9, in
PNG (WebP times match within a few percent at every cell measured).

What the fit says, and where it is rough:

- **A rendered slice costs about 0.085 CPU-seconds plus 3 microseconds per tile it covers**, so
  the shallower levels cost more per slice: a level-5 slice covers 65,536 tiles and takes about
  0.28 s, over three times a native one. An averaged slice, built from memory, costs about
  0.04 s. Both are measured at 704x704; other `--slice-tiles` values scale the pixel part
  linearly and are untested.
- **PNG and WebP take the same time.** The old assumption that WebP encoding dominates a run no
  longer holds with SkiaSharp; only the file sizes differ.
- **Threads scale well up to the performance cores, then barely.** The machine is an M2 Max with
  8 performance and 4 efficiency cores. At level 9 the speedup over two threads is 3.8x on four,
  5.4x on six and 7.0x on eight (87% per-thread efficiency), but only 7.5x on ten and 7.8x on
  twelve: each of the last four threads lands on an efficiency core and adds about a fifth of a
  performance core. Twelve is still the fastest, 10% ahead of eight, but it spends about 17%
  more CPU and a third more memory to get there. The default of every core is therefore right for
  wall time; `--threads 8` is the setting for sharing the machine. macOS cannot pin threads, so
  this is the scheduler's choice and not a controlled split.
- **Start-up is about a second and a half**, so small runs do not speed up past a few threads: at
  level 5, twelve threads is slower than four.
- **Accuracy:** the estimator lands within -18% to +13% of the 27 runs at levels 7 to 9 (mean -1%),
  with no trend in the thread count, and about 10% over a full-facet WebP run. It runs low on small runs, where
  start-up and the last few slices dominate. Its knee at two thirds of the logical cores is exact
  here and a guess on other machines. Disk is within 13% of the matrix but overstates a sparse facet.
  Peak memory is about 0.4 GB at the smallest runs, 1.2 GB at 1,368 slices and 2.4 GB at 15,000.

## Docker

The `Dockerfile` builds the linux binary from source (`linux-x64` or `linux-arm64`, following the
platform being built) and ships it alone on `runtime-deps`. The working directory is `/data`,
and `--data` and every other path resolve against it, so mount the client folder there:

The container runs as an idle service and the renderer is started inside it with `docker exec`,
so the client folder is mounted once and a scheduler can drive it:

```bash
docker build -t swmaprenderer .
docker run -d --name swmaprenderer -v "$PWD:/data" swmaprenderer
docker exec swmaprenderer swmaprenderer --data client --map 1 -x 1440 -y 1688 -o map.png
```

`swmaprenderer` is on the `PATH` in the image. The image has no entrypoint, so a one-off run
without the service names the binary itself: `docker run --rm -v "$PWD:/data" swmaprenderer
swmaprenderer --data client ...`.

A tagged release is also published to `ghcr.io/<owner>/<repo>` (tagged `x.y.z`, `x.y` and `latest`) for
`linux/amd64` and `linux/arm64`, by `.github/workflows/docker.yml`; it can also be run by hand from the
Actions tab, which tags the image with the branch and commit and leaves `latest` alone.

Add `--user "$(id -u):$(id -g)"` (to `run`, or `exec` for one render) so the output is not owned by root. The image has no GPU
drivers, so `--gpu` is not available in it.

## Configuration

Defaults live in `appsettings.json` under `Render`. A file named with `-c, --config <file>` is
layered over it, environment variables (`SWMAP_Render__X=1420`) over that, and command-line
switches win over everything. Run `--help` for the full list. The frequently used ones:

Every switch can be set in a `--config` file under its long name, without the leading dashes
and in lowercase: `--min-zoom` is `"min-zoom"`, `--out` is `"out"`, `--data` is `"data"`.
`config.example.json` lists them all with their defaults; copy it, delete what you do not need,
and run with `--config mine.json`. Boolean switches take `true`/`false`, so `--no-statics` is
`"statics": false`. An unknown key is an error. The file holds the keys at the top level; the
`Render` section and PascalCase names (`DataPath`, `MinZoom`) belong to `appsettings.json` and
the `SWMAP_Render__` environment variables.

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
| `--pois <file\|url>` | Named places to mark on the viewer and search by |
| `--pois-config <file>` | JSON list of marker sources, each a file or a URL with its own refresh; replaces `--pois` |
| `--template <file>` | Page to write as `index.html` in place of the built-in viewer |
| `--start <x,y[,zoom]>` | Where the viewer opens: a tile and optionally a zoom level (default: the whole facet) |
| `--base-url <url>` | Public URL the tiles are served from; gives the page an absolute `og:image` for chat previews |
| `--submit-url <url>` | Endpoint the viewer's "add markers" panel POSTs drawn points, lines, curves and outlines to (JSON) |
| `--pois-refresh <s>` | Re-fetch a `--pois` URL every `<s>` seconds (min 5; default 0, once on load) |
| `--min-z`, `--max-z` | Restrict the z range |
| `-v, --verbose` | Report the view range, tile counts and timings |
| `--tiles <dir>` | Write a browsable tile pyramid here instead of one image |
| `--slice-tiles <n>` | Tiles per slice edge (default 16, giving 704px slices) |
| `--region <x1,y1,x2,y2>` | Cover only this tile rectangle |
| `--min-zoom`, `--max-zoom` | Pyramid levels to write (`--max-zoom` sets the depth; `--zoom` sets the detail) |
| `--threads <n>` | Slices rendered at once (default: every core) |
| `--gpu` | Rasterize on the GPU instead of on the CPU (see "GPU rendering") |
| `--gpu-api <api>` | `vulkan`, `opencl` or `auto` (default: Vulkan, then OpenCL) |
| `--overwrite`, `--dry-run` | Re-render existing slices / report the plan and stop |
| `-b, --background <c>` | `transparent`, `black`, `white` or `#RRGGBB[AA]` |
| `-f, --format <fmt>` | `png`, `jpg`, `webp`, `webp-lossless` |
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
| `Assets/` | `.mul` readers: tiledata, art, texmaps, hues, multis, animations and the body tables, plus the memory-mapped file and index primitives |
| `Map/` | Terrain and statics layers, the shard item and mobile overlays, equipment layer order, facet dimensions, and `MapScene` — the data and visibility rules the geometry is built against |
| `Rendering/` | The port: `IsoProjection`, `LandObject`, `StaticObject`, `MobileObject`, `MapRenderer`, `MapEffect`, `Rasterizer` |
| `Tiling/` | `SliceGrid` (canvas geometry), `PyramidGenerator`, `PointsOfInterest`, `LeafletViewer` |
| `Example/` | Not built: the server-side command that writes the `--items` and `--mobiles` files |

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
- **Statics well below their tile's land are pushed behind it in depth.** Depth follows true
  height, so a wall built at z=-20 under open ground has a top that reaches above the land
  and is painted over the grass; ClassicUO draws back to front and the nearer land covers it.
  `StaticObject.BuryBelowLand` clamps the depth (not the screen position) of any static more
  than 5 units under its own tile's land to just below the lowest corner of that tile (the
  surface can slope away under the tile's own z). Where there is no land to
  draw (a nodraw hole) the static still shows.
  Water statics are held to a stricter rule -- buried when below *dry* land, or level with it while
  a corner of that land climbs above the water -- because they are flat floor tiles and otherwise
  show as diamonds of sea along a beach. Level with a flat seabed whose neighbours are lower they
  are left alone, or the seabed shows through the sea as brown diamonds.
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
