using System.Globalization;
using System.Text.Json;

namespace SwMapRenderer.Tiling;

/// <summary>
/// Writes the browsable page for a generated pyramid.
///
/// The page uses a custom CRS whose units are canvas pixels at the deepest level, so that
/// Leaflet's tile grid lines up with the slices exactly as generated and no coordinate fudging
/// is needed anywhere. Because the same projection is available in reverse, the page can also
/// translate the cursor back into the tile coordinates a player would recognise.
/// </summary>
public static class LeafletViewer
{
    public const string FileName = "index.html";

    public static string Write(SliceGrid grid, PyramidPlan plan, int mapIndex, string outputDirectory,
        ImageFormat format)
    {
        var config = new
        {
            mapIndex,
            sliceSize = grid.SliceSize,
            canvasWidth = grid.CanvasWidth,
            canvasHeight = grid.CanvasHeight,
            originX = grid.OriginX,
            originY = grid.OriginY,
            nativeZoom = grid.NativeZoom,
            halfTile = Rendering.IsoProjection.HalfTileWidth * grid.NativeZoom,

            // The level at which one canvas unit is one image pixel. This is a property of the
            // grid, not of how deep the run went, and it is what the CRS scales against -- a
            // pyramid capped with --max-zoom still has its slices laid out on the full grid.
            gridMaxZoom = grid.MaxZoom,

            minZoom = plan.MinZoom,
            maxZoom = plan.MaxZoom,
            tilePixels = grid.TilePixelsAtLevel(plan.MaxZoom),
            mapWidth = grid.Map.Width,
            mapHeight = grid.Map.Height,
            tilesPerSlice = grid.TilesPerSlice,
            extension = format.Extension,
            format = format.Name,
        };

        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        string html = Template.Replace("/*__CONFIG__*/null", json);

        string path = Path.Combine(outputDirectory, FileName);
        File.WriteAllText(path, html);
        return path;
    }

    private const string Template = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Ultima Online map</title>
        <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
        <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
        <style>
          html, body { margin: 0; height: 100%; background: #101014; }
          #map { position: absolute; inset: 0; background: #101014; }
          .readout {
            font: 12px/1.5 ui-monospace, SFMono-Regular, Menlo, monospace;
            background: rgba(16, 16, 20, 0.85); color: #e8e8ea;
            padding: 6px 9px; border-radius: 4px; border: 1px solid #34343c;
          }
          .readout b { color: #9fd0ff; font-weight: 600; }
          .goto input {
            font: 12px ui-monospace, Menlo, monospace; width: 9em;
            background: #1c1c22; color: #e8e8ea; border: 1px solid #34343c;
            border-radius: 3px; padding: 3px 5px;
          }
          .goto button {
            font: 12px ui-monospace, Menlo, monospace; margin-left: 4px; cursor: pointer;
            background: #2a2a34; color: #e8e8ea; border: 1px solid #44444e;
            border-radius: 3px; padding: 3px 7px;
          }
          .leaflet-container { background: #101014; }
        </style>
        </head>
        <body>
        <div id="map"></div>
        <script>
        const CFG = /*__CONFIG__*/null;

        // A transparent 1x1 PNG, served for slices that were never written whatever the tiles
        // themselves are encoded as. Ocean and the area off the edge of the facet have no files
        // at all, and this keeps those quiet.
        const BLANK = 'data:image/png;base64,' +
          'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=';

        // Units are canvas pixels at the grid's deepest level, y increasing downward, so the
        // tile grid is addressed exactly as generated. latLng carries (y, x).
        const CRS = L.extend({}, L.CRS.Simple, {
          transformation: new L.Transformation(1, 0, 1, 0),
          scale: z => Math.pow(2, z - CFG.gridMaxZoom),
          zoom: s => Math.log(s) / Math.LN2 + CFG.gridMaxZoom,
          infinite: true,
        });

        // Let the deepest generated level be magnified a little. On a pyramid capped with
        // --max-zoom the finest tiles are still coarse, and upscaling them beats refusing to
        // zoom at all.
        const OVERZOOM = 2;
        const VIEW_MAX = CFG.maxZoom + OVERZOOM;

        const bounds = L.latLngBounds(
          L.latLng(0, 0),
          L.latLng(CFG.canvasHeight, CFG.canvasWidth));

        const map = L.map('map', {
          crs: CRS,
          minZoom: CFG.minZoom,
          maxZoom: VIEW_MAX,
          zoomControl: true,
          attributionControl: false,
          maxBounds: bounds.pad(0.05),
          zoomSnap: 0,
        });

        L.tileLayer('{z}/{x}/{y}' + CFG.extension, {
          tileSize: CFG.sliceSize,
          minZoom: CFG.minZoom,
          maxZoom: VIEW_MAX,
          minNativeZoom: CFG.minZoom,
          maxNativeZoom: CFG.maxZoom,
          bounds: bounds,
          noWrap: true,
          errorTileUrl: BLANK,
          keepBuffer: 2,
        }).addTo(map);

        // --- coordinates -------------------------------------------------------------------

        // Inverse of the renderer's projection at ground level. Height is unknowable from a
        // pixel alone, so anything standing on a hill reads as the tile its base would occupy
        // were the ground flat.
        function tileAt(latlng) {
          const a = (latlng.lng - CFG.originX) / CFG.halfTile;
          const b = (latlng.lat - CFG.originY) / CFG.halfTile;
          return { x: (a + b) / 2, y: (b - a) / 2 };
        }

        function canvasAt(tileX, tileY) {
          return L.latLng(
            CFG.halfTile * (tileX + tileY) + CFG.originY,
            CFG.halfTile * (tileX - tileY) + CFG.originX);
        }

        const readout = L.control({ position: 'bottomleft' });
        readout.onAdd = function () {
          this._div = L.DomUtil.create('div', 'readout');
          this.update();
          return this._div;
        };
        readout.update = function (latlng) {
          if (!latlng) {
            this._div.innerHTML = `map${CFG.mapIndex} &middot; ${CFG.mapWidth}&times;${CFG.mapHeight} tiles ` +
            `&middot; levels ${CFG.minZoom}-${CFG.maxZoom} @ ${CFG.tilePixels.toFixed(2)}px/tile`;
            return;
          }
          const t = tileAt(latlng);
          const inside = t.x >= 0 && t.y >= 0 && t.x < CFG.mapWidth && t.y < CFG.mapHeight;
          this._div.innerHTML = inside
            ? `map${CFG.mapIndex} &middot; <b>${Math.floor(t.x)}, ${Math.floor(t.y)}</b> &middot; zoom ${map.getZoom().toFixed(1)}`
            : `map${CFG.mapIndex} &middot; <b>off map</b> &middot; zoom ${map.getZoom().toFixed(1)}`;
        };
        readout.addTo(map);

        map.on('mousemove', e => readout.update(e.latlng));
        map.on('mouseout', () => readout.update());
        map.on('zoomend', () => readout.update());

        // --- go to a tile ------------------------------------------------------------------

        const goto = L.control({ position: 'topright' });
        goto.onAdd = function () {
          const div = L.DomUtil.create('div', 'readout goto');
          div.innerHTML = '<input id="xy" placeholder="x, y" size="9"><button id="go">go</button>';
          L.DomEvent.disableClickPropagation(div);
          return div;
        };
        goto.addTo(map);

        function go() {
          const parts = document.getElementById('xy').value.split(/[,\s]+/).filter(Boolean);
          if (parts.length < 2) return;
          const x = Number(parts[0]), y = Number(parts[1]);
          if (!Number.isFinite(x) || !Number.isFinite(y)) return;
          map.setView(canvasAt(x, y), Math.max(map.getZoom(), CFG.maxZoom - 1));
        }
        document.getElementById('go').addEventListener('click', go);
        document.getElementById('xy').addEventListener('keydown', e => { if (e.key === 'Enter') go(); });

        // --- shareable position ------------------------------------------------------------

        function applyHash() {
          const m = /^#(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)(?:,(\d+(?:\.\d+)?))?$/.exec(location.hash);
          if (!m) return false;
          map.setView(canvasAt(Number(m[1]), Number(m[2])),
            m[3] ? Math.min(VIEW_MAX, Number(m[3])) : CFG.maxZoom - 1);
          return true;
        }

        let writingHash = false;

        map.on('moveend zoomend', () => {
          const t = tileAt(map.getCenter());
          writingHash = true;
          location.replace(`#${Math.round(t.x)},${Math.round(t.y)},${map.getZoom().toFixed(1)}`);
          // hashchange is queued, so the flag has to outlive this turn of the event loop.
          setTimeout(() => { writingHash = false; }, 0);
        });

        if (!applyHash()) {
          map.fitBounds(bounds);
        }
        window.addEventListener('hashchange', () => { if (!writingHash) applyHash(); });
        </script>
        </body>
        </html>
        """;
}
