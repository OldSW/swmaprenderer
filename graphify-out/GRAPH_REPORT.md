# Graph Report - swmaprenderer  (2026-11-05)

## Corpus Check
- Large corpus: 83 files · ~5,976,691 words. Semantic extraction will be expensive (many Claude tokens). Consider running on a subfolder.

## Summary
- 1075 nodes · 1990 edges · 60 communities (47 shown, 13 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 146 edges (avg confidence: 0.84)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Core Types and Cascade
- Animation File Reader
- Hues File Reader
- Vulkan Wrapper Types
- RenderOptions Config
- Paperdoll Layers
- Architecture Docs
- Build Workflow
- JSON Node Handling
- OpenCL Interop
- Point and Rect Records
- Leaflet Viewer Page
- Tile Flags and Tiledata
- Docker Workflow
- Animation Lookup
- Export Command Example
- Body Tables
- TileData File
- Multi File
- MapScene Visibility
- Map File and Block Cache
- Statics and Item Overlay
- Tiling Namespace Usings
- GPU Rasterizer Files
- IsoProjection
- UoFiles Container
- Rendering Files
- MapRenderer Draw Path
- Image Output Formats
- Land and Static Objects
- CommandLine Parsing
- Map and Assets Files
- DataFolder and Dimensions
- NuGet Dependencies
- PHP Tile Server
- Static Tile Data
- Constants and Entry
- TileRhombus
- Animation Types
- Projection Depth and Transform
- Hue Modes
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
- Skia Output
- Zoom 9 Screenshot
- Render Techniques
- Stone Tile Sprite A
- Stone Tile Sprite B
- Rhombus Parity
- Unsupported Formats

## God Nodes (most connected - your core abstractions)
1. `RenderOptions` - 66 edges
2. `TileFlag` - 36 edges
3. `VulkanDevice` - 36 edges
4. `Layer` - 34 edges
5. `PointsOfInterest` - 31 edges
6. `PoiRecord` - 31 edges
7. `UoFiles` - 28 edges
8. `SwMapRenderer.Assets` - 27 edges
9. `SliceGrid` - 27 edges
10. `PyramidGenerator` - 25 edges

## Surprising Connections (you probably didn't know these)
- `Builder` --references--> `AnimationFile`  [EXTRACTED]
  Map/MobileOverlay.cs → Assets/AnimationFile.cs
- `Builder` --references--> `BodyTables`  [EXTRACTED]
  Map/MobileOverlay.cs → Assets/BodyTables.cs
- `MapFile` --references--> `DataFile`  [EXTRACTED]
  Map/MapFile.cs → Assets/DataFile.cs
- `StaticsFile` --references--> `IndexedMulFile`  [EXTRACTED]
  Map/StaticsFile.cs → Assets/MulIndex.cs
- `MapObject` --references--> `Sprite`  [EXTRACTED]
  Rendering/MapObject.cs → Assets/Sprite.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Invariants keeping independently rendered slices seamless** — claude_precision_invariants, readme_closed_form_camera, readme_land_corners_from_index, claude_rasterizer_edge_double, claude_slicegrid_integer_origin, claude_view_range_inverted [EXTRACTED 0.95]
- **POI source to map marker pipeline** — index_template_pollsource, index_template_readpois, index_template_listkinds, index_template_drawpois, index_template_search [EXTRACTED 0.95]
- **Shard data overlays fed from server export** — readme_shard_items, readme_mobiles, readme_customizable_houses, example_exportitemsmobilescommand [EXTRACTED 0.90]
- **Old, new and diff panels of the level 5 comparison** — tmp_cmp_level5_058ef90_vs_new_5_10_11_old_render_panel, tmp_cmp_level5_058ef90_vs_new_5_10_11_new_render_panel, tmp_cmp_level5_058ef90_vs_new_5_10_11_diff_panel [EXTRACTED 1.00]
- **Old / new / diff comparison triptych** — tmp_cmp_level5_058ef90_vs_new_5_12_17_old_render_panel, tmp_cmp_level5_058ef90_vs_new_5_12_17_new_render_panel, tmp_cmp_level5_058ef90_vs_new_5_12_17_diff_panel [EXTRACTED 1.00]
- **Before/after/diff render comparison** — tmp_cmp_level5_head_vs_new_5_10_11_head_panel, tmp_cmp_level5_head_vs_new_5_10_11_new_panel, tmp_cmp_level5_head_vs_new_5_10_11_diff_panel [EXTRACTED 1.00]
- **Before, after and diff panels of one comparison** — tmp_cmp_level5_head_vs_new_5_12_7_head_render, tmp_cmp_level5_head_vs_new_5_12_7_new_render, tmp_cmp_level5_head_vs_new_5_12_7_diff_panel [EXTRACTED 1.00]

## Communities (60 total, 13 thin omitted)

### Community 0 - "Core Types and Cascade"
Cohesion: 0.05
Nodes (25): LeafletViewer, ProgressBar, Done, Total, Cascade, Child, Node, PyramidGenerator (+17 more)

### Community 1 - "Animation File Reader"
Cohesion: 0.05
Nodes (24): AnimationFile, BaseEntryCount, AnimationFrame, CenterX, CenterY, IsEmpty, Sprite, AnimationFrameSize (+16 more)

### Community 2 - "Hues File Reader"
Cohesion: 0.06
Nodes (24): HuesFile, HueCount, Sprite, IsEmpty, GpuRasterizerBase, Height, TilesX, TilesY (+16 more)

### Community 3 - "Vulkan Wrapper Types"
Cohesion: 0.07
Nodes (15): VulkanDevice, Api, DescriptorSetLayout, Device, Instance, Name, PhysicalDevice, Pipeline (+7 more)

### Community 4 - "RenderOptions Config"
Cohesion: 0.04
Nodes (51): RenderOptions, Background, BackgroundColor, BaseUrl, CachedBlocks, DataPath, DefaultBackground, DryRun (+43 more)

### Community 5 - "Paperdoll Layers"
Cohesion: 0.06
Nodes (38): Layer, Arms, Backpack, Beard, Bracelet, Cloak, Earrings, Face (+30 more)

### Community 6 - "Architecture Docs"
Cohesion: 0.06
Nodes (27): Architecture layers (Assets, Map, Rendering, Tiling), Configuration flow and CommandLine.Normalize, Draw order: terrain, statics, mobiles (depth-coupled), GPU rasterizer (GpuRasterizerBase, IRasterizer), MapScene (replaces CEDGame.MapManager singleton), CLAUDE.md project guidance, Shard data merged into StaticsFile, SliceGrid whole-pixel origin (+19 more)

### Community 7 - "Build Workflow"
Cohesion: 0.05
Nodes (40): name, runs-on, steps, strategy, env, DOTNET_CLI_TELEMETRY_OPTOUT, DOTNET_NOLOGO, DOTNET_SKIP_FIRST_TIME_EXPERIENCE (+32 more)

### Community 8 - "JSON Node Handling"
Cohesion: 0.10
Nodes (13): Poi, PointsOfInterest, Items, Stats, Types, PoiStats, PoiType, PoiSource (+5 more)

### Community 9 - "OpenCL Interop"
Cohesion: 0.12
Nodes (7): OpenCl, OpenClDevice, Context, Device, Name, Program, OpenClRasterizer

### Community 10 - "Point and Rect Records"
Cohesion: 0.06
Nodes (29): PointRecord, X, Y, Z, PoiRecord, Category, Coords, Desc (+21 more)

### Community 11 - "Leaflet Viewer Page"
Cohesion: 0.08
Nodes (34): Premultiplied alpha colour conventions, boundaryOf (merge rectangles into rings), __CONFIG__ / __POIS__ / __OG__ placeholders, Custom Leaflet CRS (canvas pixels), Add-markers draft panel and submitDrafts, drawOutlines / drawShapes, drawPois / markerFor, Icon scaling with zoom (iconScale) (+26 more)

### Community 12 - "Tile Flags and Tiledata"
Cohesion: 0.06
Nodes (34): TileFlag, Animation, Armor, ArticleA, ArticleAn, Background, Bridge, Container (+26 more)

### Community 13 - "Docker Workflow"
Cohesion: 0.06
Nodes (32): env, REGISTRY, REPOSITORY, runs-on, steps, jobs, image, name (+24 more)

### Community 14 - "Animation Lookup"
Cohesion: 0.13
Nodes (16): AnimationLookup, Found, Builder, EquipmentRecord, MobileOverlay, MaxSpriteHeight, MaxSpriteWidth, Stats (+8 more)

### Community 16 - "Body Tables"
Cohesion: 0.24
Nodes (3): BodyDefEntry, BodyTables, DataFolder

### Community 17 - "TileData File"
Cohesion: 0.15
Nodes (11): LandTileData, IsImpassable, IsWet, TileDataFile, Format, LandData, StaticData, TileDataFormat (+3 more)

### Community 18 - "Multi File"
Cohesion: 0.21
Nodes (8): MultiComponent, MultiFile, DesignTileRecord, ItemOverlay, Stats, ItemOverlayStats, Placement, PositionRecord

### Community 19 - "MapScene Visibility"
Cohesion: 0.21
Nodes (4): MapScene, Files, Options, MobileObject

### Community 20 - "Map File and Block Cache"
Cohesion: 0.19
Nodes (7): BlockCache, Count, MaxEntries, MapFile, Dimensions, Height, Width

### Community 21 - "Statics and Item Overlay"
Cohesion: 0.27
Nodes (5): DesignRecord, StaticsFile, StaticTile, CellIndex, PriorityZ

### Community 24 - "IsoProjection"
Cohesion: 0.21
Nodes (4): IsoProjection, OriginX, OriginY, Zoom

### Community 25 - "UoFiles Container"
Cohesion: 0.17
Nodes (12): UoFiles, Animations, Art, Dimensions, Hues, Items, Map, Mobiles (+4 more)

### Community 27 - "MapRenderer Draw Path"
Cohesion: 0.24
Nodes (4): MapRenderer, SceneRenderer, CentredProjection, Output

### Community 28 - "Image Output Formats"
Cohesion: 0.27
Nodes (5): ImageFormat, SizeFactor, TimeFactor, ImageOutput, Names

### Community 29 - "Land and Static Objects"
Cohesion: 0.24
Nodes (3): LandTile, StaticObject, Tile

### Community 30 - "CommandLine Parsing"
Cohesion: 0.20
Nodes (3): CommandLine, OptionsException, Errors

### Community 32 - "DataFolder and Dimensions"
Cohesion: 0.25
Nodes (4): MapDimensions, BlockCount, BlockHeight, BlockWidth

### Community 33 - "NuGet Dependencies"
Cohesion: 0.20
Nodes (10): net10.0, Microsoft.Extensions.Configuration.Binder (9.0.0), Microsoft.Extensions.Configuration.CommandLine (9.0.0), Microsoft.Extensions.Configuration.EnvironmentVariables (9.0.0), Microsoft.Extensions.Configuration.Json (9.0.0), Silk.NET.Vulkan (2.23.0), SkiaSharp (4.153.1), SkiaSharp.NativeAssets.Linux.NoDependencies (4.153.1) (+2 more)

### Community 34 - "PHP Tile Server"
Cohesion: 0.27
Nodes (5): baseUrl(), crop(), prune(), serveImage(), servePage()

### Community 35 - "Static Tile Data"
Cohesion: 0.25
Nodes (8): StaticTileData, IsAnimated, IsBackground, IsFoliage, IsLight, IsPartialHue, IsSurface, IsTranslucent

### Community 37 - "TileRhombus"
Cohesion: 0.25
Nodes (7): TileRhombus, AMax, AMin, ApproximateCount, BMax, BMin, RenderStats

### Community 38 - "Animation Types"
Cohesion: 0.29
Nodes (6): AnimationType, Animal, Equipment, Human, Monster, SeaMonster

### Community 41 - "Hue Modes"
Cohesion: 0.33
Nodes (6): HueMode, Hued, Light, None, Partial, Rgb

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

### Community 54 - "Render Techniques"
Cohesion: 0.67
Nodes (3): Technique, Statics, Terrain

### Community 55 - "Stone Tile Sprite A"
Cohesion: 0.67
Nodes (3): s71e.png: isometric diamond stone tile sprite, Grey-teal cobblestone paving texture with dark shaded edges, Isometric diamond tile shape (2:1 rhombus, pixelated)

### Community 56 - "Stone Tile Sprite B"
Cohesion: 0.67
Nodes (3): Isometric cobblestone floor tile sprite (s736), Teal-grey cobblestone texture with dark mortar, Isometric diamond projection of rendered tile

## Knowledge Gaps
- **324 isolated node(s):** `CenterX`, `CenterY`, `IsEmpty`, `BaseEntryCount`, `MaxStaticWidth` (+319 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 451 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **13 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `RenderOptions` connect `RenderOptions Config` to `DataFolder and Dimensions`, `Core Types and Cascade`, `Hues File Reader`, `Option Parsing`, `JSON Node Handling`, `TileData File`, `MapScene Visibility`, `MapRenderer Draw Path`, `Image Output Formats`, `CommandLine Parsing`?**
  _High betweenness centrality (0.155) - this node is a cross-community bridge._
- **What connects `CenterX`, `CenterY`, `IsEmpty` to the rest of the system?**
  _324 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Core Types and Cascade` be split into smaller, more focused modules?**
  _Cohesion score 0.053313587560162905 - nodes in this community are weakly interconnected._
- **Should `Animation File Reader` be split into smaller, more focused modules?**
  _Cohesion score 0.05179982440737489 - nodes in this community are weakly interconnected._
- **Should `Hues File Reader` be split into smaller, more focused modules?**
  _Cohesion score 0.06013986013986014 - nodes in this community are weakly interconnected._
- **Should `Vulkan Wrapper Types` be split into smaller, more focused modules?**
  _Cohesion score 0.06892230576441102 - nodes in this community are weakly interconnected._
- **Should `RenderOptions Config` be split into smaller, more focused modules?**
  _Cohesion score 0.0392156862745098 - nodes in this community are weakly interconnected._