# Graph Report - swmaprenderer  (2026-11-25)

## Corpus Check
- 72 files · ~5,982,572 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 7 file(s) not represented in the graph (top: (none) 4, .gz 1, .comp 1)

## Summary
- 1117 nodes · 2071 edges · 62 communities (50 shown, 12 thin omitted)
- Extraction: 92% EXTRACTED · 8% INFERRED · 0% AMBIGUOUS · INFERRED: 163 edges (avg confidence: 0.84)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `e8e1222d`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- SliceGrid
- ProgressBar
- Vector4
- Vulkan Wrapper Types
- RenderOptions Config
- Paperdoll Layers
- ExportItemsMobilesCommand.cs
- Build Workflow
- JSON Node Handling
- OpenCL Interop
- Point and Rect Records
- Leaflet viewer (index.html)
- Tile Flags and Tiledata
- Docker Workflow
- Animation Lookup
- Export Command Example
- Body Tables
- TileData File
- Multi File
- IsoProjection
- Sprite
- Statics and Item Overlay
- SwMapRenderer.Assets
- MapFile
- UoFiles Container
- LeafletViewer.Write
- ImageFormat
- PyramidGenerator
- ProgressBar
- MapDimensions
- NuGet Dependencies
- PHP Tile Server
- Static Tile Data
- NorthUpSampler
- AnimationType
- PyramidPlan
- RenderOptions.Validate
- Overlay Records
- Level 5 Comparison A
- Level 5 Comparison B
- Coastline Fix Comparison A
- Coastline Fix Comparison B
- Vendor Offset Screenshot
- Zoom 8 Screenshot
- Diamond Artifacts Screenshot
- Discord Preview
- Town Screenshot
- Zoom 9 Screenshot
- SwMapRenderer
- Stone Tile Sprite A
- Stone Tile Sprite B
- Rhombus Parity
- Unsupported Formats
- RenderOptions.cs
- MapScene
- LandObject
- StaticObject
- TileRhombus

## God Nodes (most connected - your core abstractions)
1. `RenderOptions` - 68 edges
2. `TileFlag` - 36 edges
3. `VulkanDevice` - 36 edges
4. `Layer` - 34 edges
5. `PoiRecord` - 31 edges
6. `PyramidGenerator` - 30 edges
7. `SliceGrid` - 29 edges
8. `PointsOfInterest` - 29 edges
9. `UoFiles` - 28 edges
10. `SwMapRenderer.Assets` - 25 edges

## Surprising Connections (you probably didn't know these)
- `Builder` --references--> `AnimationFile`  [EXTRACTED]
  Map/MobileOverlay.cs → Assets/AnimationFile.cs
- `Builder` --references--> `BodyTables`  [EXTRACTED]
  Map/MobileOverlay.cs → Assets/BodyTables.cs
- `Builder` --references--> `TileDataFile`  [EXTRACTED]
  Map/MobileOverlay.cs → Assets/TileDataFile.cs
- `UoFiles` --references--> `MobileOverlay`  [EXTRACTED]
  Assets/UoFiles.cs → Map/MobileOverlay.cs
- `StaticsFile` --references--> `TileDataFile`  [EXTRACTED]
  Map/StaticsFile.cs → Assets/TileDataFile.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Shard data overlays fed from server export** — readme_shard_items, readme_mobiles, readme_customizable_houses, example_exportitemsmobilescommand [EXTRACTED 0.90]
- **POI source to map marker pipeline** — index_template_pollsource, index_template_readpois, index_template_listkinds, index_template_drawpois, index_template_search [EXTRACTED 0.95]
- **Invariants keeping independently rendered slices seamless** — claude_precision_invariants, readme_closed_form_camera, readme_land_corners_from_index, claude_rasterizer_edge_double, claude_slicegrid_integer_origin, claude_view_range_inverted [EXTRACTED 0.95]
- **Old, new and diff panels of the level 5 comparison** — tmp_cmp_level5_058ef90_vs_new_5_10_11_old_render_panel, tmp_cmp_level5_058ef90_vs_new_5_10_11_new_render_panel, tmp_cmp_level5_058ef90_vs_new_5_10_11_diff_panel [EXTRACTED 1.00]
- **Old / new / diff comparison triptych** — tmp_cmp_level5_058ef90_vs_new_5_12_17_old_render_panel, tmp_cmp_level5_058ef90_vs_new_5_12_17_new_render_panel, tmp_cmp_level5_058ef90_vs_new_5_12_17_diff_panel [EXTRACTED 1.00]
- **Before/after/diff render comparison** — tmp_cmp_level5_head_vs_new_5_10_11_head_panel, tmp_cmp_level5_head_vs_new_5_10_11_new_panel, tmp_cmp_level5_head_vs_new_5_10_11_diff_panel [EXTRACTED 1.00]
- **Before, after and diff panels of one comparison** — tmp_cmp_level5_head_vs_new_5_12_7_head_render, tmp_cmp_level5_head_vs_new_5_12_7_new_render, tmp_cmp_level5_head_vs_new_5_12_7_diff_panel [EXTRACTED 1.00]

## Communities (62 total, 12 thin omitted)

### Community 0 - "SliceGrid"
Cohesion: 0.11
Nodes (15): SliceGrid, CanvasHeight, CanvasWidth, Map, MaxZoom, NativeZoom, NorthUp, NorthUpReach (+7 more)

### Community 1 - "ProgressBar"
Cohesion: 0.25
Nodes (4): RenderStats, ProgressBar, Done, Total

### Community 2 - "Vector4"
Cohesion: 0.06
Nodes (32): HuesFile, HueCount, Frame, BinList, BinStart, Clear, Height, HueCount (+24 more)

### Community 3 - "Vulkan Wrapper Types"
Cohesion: 0.07
Nodes (15): VulkanDevice, Api, DescriptorSetLayout, Device, Instance, Name, PhysicalDevice, Pipeline (+7 more)

### Community 4 - "RenderOptions Config"
Cohesion: 0.04
Nodes (52): RenderOptions, Background, BackgroundColor, BaseUrl, CachedBlocks, DataPath, DefaultBackground, DryRun (+44 more)

### Community 5 - "Paperdoll Layers"
Cohesion: 0.09
Nodes (27): Layer, Arms, Backpack, Beard, Bracelet, Cloak, Earrings, Face (+19 more)

### Community 6 - "ExportItemsMobilesCommand.cs"
Cohesion: 0.06
Nodes (25): Architecture layers (Assets, Map, Rendering, Tiling), Configuration flow and CommandLine.Normalize, Draw order: terrain, statics, mobiles (depth-coupled), GPU rasterizer (GpuRasterizerBase, IRasterizer), MapScene (replaces CEDGame.MapManager singleton), CLAUDE.md project guidance, Shard data merged into StaticsFile, SliceGrid whole-pixel origin (+17 more)

### Community 7 - "Build Workflow"
Cohesion: 0.05
Nodes (40): name, runs-on, steps, strategy, env, DOTNET_CLI_TELEMETRY_OPTOUT, DOTNET_NOLOGO, DOTNET_SKIP_FIRST_TIME_EXPERIENCE (+32 more)

### Community 8 - "JSON Node Handling"
Cohesion: 0.08
Nodes (14): SwMapRenderer.Tiling, Poi, PointsOfInterest, Items, Stats, Types, PoiStats, PoiType (+6 more)

### Community 9 - "OpenCL Interop"
Cohesion: 0.12
Nodes (7): OpenCl, OpenClDevice, Context, Device, Name, Program, OpenClRasterizer

### Community 10 - "Point and Rect Records"
Cohesion: 0.06
Nodes (29): PointRecord, X, Y, Z, PoiRecord, Category, Coords, Desc (+21 more)

### Community 11 - "Leaflet viewer (index.html)"
Cohesion: 0.08
Nodes (36): Premultiplied alpha colour conventions, boundaryOf (merge rectangles into rings), canvasAt / tileAt projection helpers, __CONFIG__ / __POIS__ / __OG__ placeholders, Custom Leaflet CRS (canvas pixels), Add-markers draft panel and submitDrafts, drawOutlines / drawShapes, drawPois / markerFor (+28 more)

### Community 12 - "Tile Flags and Tiledata"
Cohesion: 0.06
Nodes (34): TileFlag, Animation, Armor, ArticleA, ArticleAn, Background, Bridge, Container (+26 more)

### Community 13 - "Docker Workflow"
Cohesion: 0.06
Nodes (32): env, REGISTRY, REPOSITORY, runs-on, steps, jobs, image, name (+24 more)

### Community 14 - "Animation Lookup"
Cohesion: 0.17
Nodes (14): Builder, EquipmentRecord, MobileOverlay, MaxSpriteHeight, MaxSpriteWidth, Stats, MobileOverlayStats, MobilePlacement (+6 more)

### Community 16 - "Body Tables"
Cohesion: 0.19
Nodes (3): BodyDefEntry, BodyTables, DataFolder

### Community 17 - "TileData File"
Cohesion: 0.15
Nodes (11): LandTileData, IsImpassable, IsWet, TileDataFile, Format, LandData, StaticData, TileDataFormat (+3 more)

### Community 18 - "Multi File"
Cohesion: 0.16
Nodes (7): DesignRecord, DesignTileRecord, ItemOverlay, Stats, ItemOverlayStats, Placement, PositionRecord

### Community 19 - "IsoProjection"
Cohesion: 0.16
Nodes (4): IsoProjection, OriginX, OriginY, Zoom

### Community 20 - "Sprite"
Cohesion: 0.05
Nodes (29): AnimationFile, BaseEntryCount, AnimationFrame, CenterX, CenterY, IsEmpty, Sprite, AnimationFrameSize (+21 more)

### Community 21 - "Statics and Item Overlay"
Cohesion: 0.35
Nodes (4): StaticsFile, StaticTile, CellIndex, PriorityZ

### Community 23 - "SwMapRenderer.Assets"
Cohesion: 0.09
Nodes (13): SwMapRenderer.Map, SwMapRenderer.Rendering.Gpu, SwMapRenderer.Assets, SwMapRenderer.Rendering, HueMode, Hued, Light, None (+5 more)

### Community 24 - "MapFile"
Cohesion: 0.21
Nodes (4): MapFile, Dimensions, Height, Width

### Community 25 - "UoFiles Container"
Cohesion: 0.15
Nodes (12): UoFiles, Animations, Art, Dimensions, Hues, Items, Map, Mobiles (+4 more)

### Community 28 - "ImageFormat"
Cohesion: 0.23
Nodes (5): ImageFormat, SizeFactor, TimeFactor, ImageOutput, Names

### Community 29 - "PyramidGenerator"
Cohesion: 0.15
Nodes (4): Child, Node, PyramidGenerator, Backdrop

### Community 32 - "MapDimensions"
Cohesion: 0.21
Nodes (6): MultiComponent, MultiFile, MapDimensions, BlockCount, BlockHeight, BlockWidth

### Community 33 - "NuGet Dependencies"
Cohesion: 0.20
Nodes (10): net10.0, Microsoft.Extensions.Configuration.Binder (9.0.0), Microsoft.Extensions.Configuration.CommandLine (9.0.0), Microsoft.Extensions.Configuration.EnvironmentVariables (9.0.0), Microsoft.Extensions.Configuration.Json (9.0.0), Silk.NET.Vulkan (2.23.0), SkiaSharp (4.153.1), SkiaSharp.NativeAssets.Linux.NoDependencies (4.153.1) (+2 more)

### Community 34 - "PHP Tile Server"
Cohesion: 0.27
Nodes (5): baseUrl(), crop(), prune(), serveImage(), servePage()

### Community 35 - "Static Tile Data"
Cohesion: 0.25
Nodes (8): StaticTileData, IsAnimated, IsBackground, IsFoliage, IsLight, IsPartialHue, IsSurface, IsTranslucent

### Community 38 - "AnimationType"
Cohesion: 0.25
Nodes (6): AnimationType, Animal, Equipment, Human, Monster, SeaMonster

### Community 39 - "PyramidPlan"
Cohesion: 0.24
Nodes (5): PyramidPlan, AveragedSliceCount, CpuSecondsPerAveragedSlice, CpuSecondsPerRenderedSlice, MegabytesPerSlice

### Community 40 - "RenderOptions.Validate"
Cohesion: 0.29
Nodes (3): CommandLine, OptionsException, Errors

### Community 43 - "Level 5 Comparison A"
Cohesion: 0.40
Nodes (5): Three-panel render comparison at level 5 (058ef90 vs new), Difference panel (red marks changed pixels in forest), New render panel, Old render (commit 058ef90) panel, Render-comparison verification method

### Community 44 - "Level 5 Comparison B"
Cohesion: 0.60
Nodes (5): Level 5 render comparison: commit 058ef90 vs new (three panels), Right panel: pixel difference overlay with red marks, Middle panel: render from new build, visually near-identical, Left panel: render from commit 058ef90 (forest, village, rocks), Render-comparison verification method (no unit tests)

### Community 45 - "Coastline Fix Comparison A"
Cohesion: 0.60
Nodes (4): Level 5 map render comparison: HEAD vs new vs diff (tile 5/10/11), Pixel diff panel (red marks changed coastline pixels and dock area), HEAD render panel (dark seabed outlines along coast, grey dock), New render panel (clean coastline, dock rendered without grey overlay)

### Community 46 - "Coastline Fix Comparison B"
Cohesion: 0.50
Nodes (4): Three-panel render comparison: HEAD vs new vs diff (level 5, tile 12/7), Difference panel: changed pixels highlighted in red along shorelines and water, HEAD render: dark seabed lines showing through water along coasts and piers, New render: clean water, seabed artifacts removed

### Community 47 - "Vendor Offset Screenshot"
Cohesion: 0.50
Nodes (5): vendor-offset.png: rendered Ultima Online market square, Stone fountain with statue, Isometric 45-degree tile projection (cobblestone diamonds), Thatched vendor stall with striped awning, Vendor mobile/NPC figures placed near stalls

### Community 48 - "Zoom 8 Screenshot"
Cohesion: 0.50
Nodes (5): zoom-8.png: isometric UO map render at zoom 8, Corpse mobile/item label at top right, Diagonal golden line overlay (possible region or slice boundary), Die Magierschaft POI marker (purple pentagram icon with label), Stone building and cobblestone walls rendered on grass terrain

### Community 49 - "Diamond Artifacts Screenshot"
Cohesion: 0.67
Nodes (4): diamonds.png: isometric UO map render of coastline with dark diamond artifacts, Crenellated stone castle wall with palm tree, Sand beach and sea coastline terrain, Dark diamond-shaped holes along sand/water shoreline

### Community 50 - "Discord Preview"
Cohesion: 0.50
Nodes (4): Discord link preview of UO map tile 1461, 1694, Pink marker icon on map, Rendered isometric Ultima Online town tile at zoom 7, Tile coordinates 1461, 1694 at zoom 7

### Community 51 - "Town Screenshot"
Cohesion: 0.50
Nodes (4): Ultima Online town isometric screenshot (Blackrocksyndikat), Blackrocksyndikat overlay label (green dot marker), Isometric town with timber-framed houses, stone castle and gardens, Blue moongate-like portal ellipse

### Community 53 - "Zoom 9 Screenshot"
Cohesion: 0.67
Nodes (4): Zoom level 9 map screenshot (Die Magierschaft), Die Magierschaft POI marker (purple hexagram icon with label), Stone castle/tower building rendered isometrically, Yellow diagonal line (region/boundary overlay)

### Community 54 - "SwMapRenderer"
Cohesion: 0.22
Nodes (3): Constants, SwMapRenderer, SkiaOutput

### Community 55 - "Stone Tile Sprite A"
Cohesion: 0.67
Nodes (3): s71e.png: isometric diamond stone tile sprite, Grey-teal cobblestone paving texture with dark shaded edges, Isometric diamond tile shape (2:1 rhombus, pixelated)

### Community 56 - "Stone Tile Sprite B"
Cohesion: 0.67
Nodes (3): Isometric cobblestone floor tile sprite (s736), Teal-grey cobblestone texture with dark mortar, Isometric diamond projection of rendered tile

### Community 67 - "MapScene"
Cohesion: 0.22
Nodes (4): MapScene, Files, Options, MobileObject

### Community 69 - "LandObject"
Cohesion: 0.23
Nodes (3): LandTile, LandObject, Tile

### Community 70 - "StaticObject"
Cohesion: 0.29
Nodes (3): MapObject, StaticObject, Tile

### Community 71 - "TileRhombus"
Cohesion: 0.17
Nodes (10): TileRhombus, AMax, AMin, ApproximateCount, BMax, BMin, MapRenderer, SceneRenderer (+2 more)

## Knowledge Gaps
- **330 isolated node(s):** `CanvasHeight`, `CanvasWidth`, `Map`, `MaxZoom`, `NativeZoom` (+325 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 465 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **12 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `RenderOptions` connect `RenderOptions Config` to `Vector4`, `MapScene`, `TileRhombus`, `Body Tables`, `RenderOptions.Validate`, `RenderOptions.ResolvePoiSources`, `RenderOptions.cs`, `PyramidGenerator`?**
  _High betweenness centrality (0.133) - this node is a cross-community bridge._
- **What connects `CanvasHeight`, `CanvasWidth`, `Map` to the rest of the system?**
  _330 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `SliceGrid` be split into smaller, more focused modules?**
  _Cohesion score 0.11333333333333333 - nodes in this community are weakly interconnected._
- **Why does `VulkanRasterizer` connect `Vulkan Wrapper Types` to `Vector4`, `Sprite`, `SwMapRenderer.Assets`?**
  _High betweenness centrality (0.075) - this node is a cross-community bridge._
- **Should `Vector4` be split into smaller, more focused modules?**
  _Cohesion score 0.05654761904761905 - nodes in this community are weakly interconnected._
- **Why does `TileDataFile` connect `TileData File` to `MapScene`, `Static Tile Data`, `Animation Lookup`, `Body Tables`, `Statics and Item Overlay`, `UoFiles Container`?**
  _High betweenness centrality (0.071) - this node is a cross-community bridge._
- **Should `Vulkan Wrapper Types` be split into smaller, more focused modules?**
  _Cohesion score 0.06715063520871144 - nodes in this community are weakly interconnected._