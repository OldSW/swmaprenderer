using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwMapRenderer.Tiling;

/// <summary>
/// Writes the browsable page for a generated pyramid.
///
/// The page uses a custom CRS whose units are canvas pixels at the deepest level, so that
/// Leaflet's tile grid lines up with the slices exactly as generated and no coordinate fudging
/// is needed anywhere. Because the same projection is available in reverse, the page can also
/// translate the cursor back into the tile coordinates a player would recognise.
///
/// Points of interest ride on top of that as a DOM overlay rather than as pixels in the slices.
/// That is what makes them searchable, and it is why a shard can move a marker without a single
/// tile being re-rendered.
/// </summary>
public static class LeafletViewer
{
    public const string FileName = "index.html";

    private static readonly JsonSerializerOptions ConfigOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Markers are written on one line each. They are data rather than configuration, there can
    /// be thousands of them, and indenting them buries the page's own code in the middle of the
    /// file.
    /// </summary>
    private static readonly JsonSerializerOptions PoiOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="pois">Markers to embed in the page. Null when there are none, or when the
    /// page is to fetch them from <paramref name="poiUrl"/> instead.</param>
    /// <param name="poiUrl">Endpoint the page fetches markers from on load.</param>
    public static string Write(SliceGrid grid, PyramidPlan plan, int mapIndex, string outputDirectory,
        ImageFormat format, IReadOnlyList<Poi>? pois = null, string? poiUrl = null)
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
            poiUrl,
        };

        string html = Template
            .Replace("/*__CONFIG__*/null", JsonSerializer.Serialize(config, ConfigOptions))
            .Replace("/*__POIS__*/null", Markers(pois));

        string path = Path.Combine(outputDirectory, FileName);
        File.WriteAllText(path, html);
        return path;
    }

    private static string Markers(IReadOnlyList<Poi>? pois)
    {
        if (pois is not { Count: > 0 })
            return "null";

        var lines = pois.Select(p => "  " + JsonSerializer.Serialize(p, PoiOptions));
        return "[" + Environment.NewLine + string.Join("," + Environment.NewLine, lines) +
               Environment.NewLine + "]";
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
          .find { width: 15em; }
          .find input[type=text] {
            font: 12px ui-monospace, Menlo, monospace; flex: 1; min-width: 0;
            background: #1c1c22; color: #e8e8ea; border: 1px solid #34343c;
            border-radius: 3px; padding: 3px 5px;
          }
          .find button {
            font: 12px ui-monospace, Menlo, monospace; margin-left: 4px; cursor: pointer;
            background: #2a2a34; color: #e8e8ea; border: 1px solid #44444e;
            border-radius: 3px; padding: 3px 7px;
          }
          .find .row { display: flex; }
          .leaflet-container { background: #101014; }

          /* --- points of interest --- */
          .find .kinds { margin-top: 5px; max-height: 11em; overflow-y: auto; }
          .find .kinds.off { display: none; }
          .find .kinds label {
            display: flex; align-items: center; gap: 5px; padding: 1px 0;
            color: #9a9aa4; cursor: pointer; user-select: none;
          }
          .find .kinds input { margin: 0; flex: none; accent-color: #ffd479; }
          .find .kinds .of {
            flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
          }
          .find .kinds .n { color: #7c7c88; padding-left: 8px; }
          .find .note { color: #7c7c88; }
          .find ul {
            list-style: none; margin: 5px 0 0; padding: 0;
            max-height: 16em; overflow-y: auto;
          }
          .find ul:empty { display: none; }
          .find li {
            padding: 3px 5px; border-radius: 3px; cursor: pointer;
            white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
          }
          .find li:hover, .find li.on { background: #2f2f3a; }
          .find li .at { color: #7c7c88; margin-left: 6px; }

          /* A marker is a zero-sized anchor its parts hang off, so that the dot sits exactly on
             the tile whatever the label beside it is doing. */
          .leaflet-div-icon.poi { background: none; border: none; }
          .poi i {
            position: absolute; left: -4px; top: -4px; width: 7px; height: 7px;
            border-radius: 50%; background: #ffd479; border: 1px solid #14141a;
            box-shadow: 0 0 0 1px rgba(255, 212, 121, 0.4);
          }
          .poi span {
            position: absolute; left: 7px; top: -9px; white-space: nowrap; pointer-events: none;
            font: 11px/1.4 ui-sans-serif, system-ui, -apple-system, sans-serif; color: #f6ead0;
            text-shadow: 0 0 3px #000, 0 0 3px #000, 0 0 5px #000;
          }
          .leaflet-popup-content-wrapper, .leaflet-popup-tip {
            background: #1c1c22; color: #e8e8ea; border: 1px solid #34343c;
            box-shadow: 0 2px 12px rgba(0, 0, 0, 0.6);
          }
          .leaflet-popup-content {
            margin: 8px 12px; font: 12px/1.6 ui-monospace, SFMono-Regular, Menlo, monospace;
          }
          .leaflet-popup-content b { color: #ffd479; }
          .leaflet-container a.leaflet-popup-close-button { color: #7c7c88; }
          .pop .cat { color: #9fd0ff; }
          .pop .desc { color: #c8c8d0; white-space: normal; max-width: 18em; }
          .pop .at { color: #7c7c88; }
        </style>
        </head>
        <body>
        <div id="map"></div>
        <script>
        const CFG = /*__CONFIG__*/null;

        // Markers embedded at generation time, from a --pois file. Null when the page is to
        // fetch them from CFG.poiUrl instead, or when the run had none.
        const POIS = /*__POIS__*/null;

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

        // Map tile per screen pixel at the current zoom, which is what decides whether a label
        // beside a marker is legible or a thousand of them are one smear.
        function tilePixels() {
          return 2 * CFG.halfTile * Math.pow(2, map.getZoom() - CFG.gridMaxZoom);
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

        // --- points of interest ------------------------------------------------------------

        // Markers may be embedded in this page or fetched from a shard's API, and an API's own
        // record shape is not ours to dictate, so everything goes through one reader. What it
        // needs from a record is a name and somewhere to put it; the rest is decoration.
        function readPois(raw) {
          const list = Array.isArray(raw) ? raw
            : !raw || typeof raw !== 'object' ? []
            : ['pois', 'points', 'items', 'results', 'data']
                .map(k => raw[k]).find(Array.isArray) || [];

          const out = [];
          for (const r of list) {
            if (!r || typeof r !== 'object') continue;

            // A record that names a facet and means a different one is not ours to draw. One
            // that names none is taken to be on the facet this pyramid covers.
            const facet = typeof r.mapId === 'number' ? r.mapId
              : typeof r.map === 'number' ? r.map : null;
            if (facet !== null && facet !== CFG.mapIndex) continue;

            const name = text(r.name) || text(r.title) || text(r.label);
            if (!name) continue;

            const at = place(r.position) || place(r.go) || place(r) || middle(r.coords);
            if (!at) continue;

            const category = text(r.category) || text(r.type);
            out.push({
              name, category,
              x: at.x, y: at.y, z: at.z,
              description: text(r.description) || text(r.desc),
              key: fold(name + ' ' + category),
            });
          }

          out.sort((a, b) => a.name.localeCompare(b.name));
          return out;
        }

        const text = v => typeof v === 'string' ? v.trim() : '';

        function place(o) {
          if (!o || typeof o !== 'object') return null;
          const x = Number(o.x), y = Number(o.y), z = Number(o.z);
          if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
          // (0, 0) is the corner of the void, and what an export writes for a record that has no
          // position of its own. Read as absent rather than placed in the ocean.
          if (x === 0 && y === 0) return null;
          return { x: Math.round(x), y: Math.round(y), z: Number.isFinite(z) ? Math.round(z) : 0 };
        }

        // A record that covers an area rather than naming a spot -- a region export -- gets its
        // marker in the middle of the first rectangle it lists.
        function middle(coords) {
          for (const c of Array.isArray(coords) ? coords : []) {
            if (!c || !c.start || !c.end) continue;
            const p = place({
              x: (Number(c.start.x) + Number(c.end.x)) / 2,
              y: (Number(c.start.y) + Number(c.end.y)) / 2,
            });
            if (p) return p;
          }
          return null;
        }

        // Case and accents are noise when someone is typing a place name from memory, and this
        // is a German shard: "Stuetzpunkt" should find "Stützpunkt", and ss should find ß.
        const fold = s => s.toLowerCase()
          .replace(/ß/g, 'ss')
          .normalize('NFD').replace(/[̀-ͯ]/g, '');

        // Names arrive from an API, so nothing from a record reaches the DOM unescaped.
        const esc = s => String(s).replace(/[&<>"']/g,
          c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

        let pois = [];

        // One checkbox per kind of place the data turned out to hold. What kinds those are is
        // the shard's business rather than ours, so the list is discovered, not declared.
        let kinds = [];
        let enabled = new Set();
        let pending = null;
        const poiLayer = L.layerGroup().addTo(map);

        // Labels below this are illegible en masse: at 0.1px per tile the whole facet is 400px
        // across and every name on it is stacked in the same place. A handful of markers never
        // reach that state though, so a sparse view is labelled at any zoom -- eighteen towns
        // on a whole facet are exactly what you want named.
        const LABEL_FROM = 0.4;
        const LABEL_ANY_ZOOM_UNDER = 40;

        // More markers than this in view is a smear rather than information. The ones nearest
        // the middle of the screen win, because that is what the view is pointed at.
        const MAX_MARKERS = 400;

        function drawPois() {
          poiLayer.clearLayers();
          if (pois.length === 0 || enabled.size === 0) return;

          const view = map.getBounds().pad(0.1);
          const centre = map.getCenter();

          const near = [];
          for (const p of pois) {
            if (!enabled.has(p.category)) continue;
            const at = canvasAt(p.x, p.y);
            if (!view.contains(at)) continue;
            const dx = at.lng - centre.lng, dy = at.lat - centre.lat;
            near.push({ poi: p, at, d: dx * dx + dy * dy });
          }
          near.sort((a, b) => a.d - b.d);

          // Whatever was just searched for is drawn even if the cap would have cut it; being
          // sent somewhere and finding no marker there is worse than one pin too many.
          if (pending && !near.slice(0, MAX_MARKERS).some(n => n.poi === pending)) {
            near.unshift({ poi: pending, at: canvasAt(pending.x, pending.y), d: -1 });
          }

          const drawing = near.slice(0, MAX_MARKERS);
          const labelled = drawing.length <= LABEL_ANY_ZOOM_UNDER || tilePixels() >= LABEL_FROM;

          for (const n of drawing) {
            const marker = markerFor(n.poi, n.at, labelled).addTo(poiLayer);
            if (n.poi === pending) {
              marker.openPopup();
              pending = null;
            }
          }
        }

        function markerFor(p, at, labelled) {
          const marker = L.marker(at, {
            keyboard: false,
            riseOnHover: true,
            icon: L.divIcon({
              className: 'poi',
              html: '<i></i>' + (labelled ? `<span>${esc(p.name)}</span>` : ''),
              iconSize: null,
              iconAnchor: [0, 0],
              popupAnchor: [0, -8],
            }),
          });

          if (!labelled)
            marker.bindTooltip(esc(p.name), { direction: 'top', offset: [0, -8] });

          // autoPan would move the map, which would redraw the markers, which would reopen this
          // popup: the view is already centred on it by the time it opens.
          marker.bindPopup(
            `<div class="pop"><b>${esc(p.name)}</b>` +
            (p.category ? `<div class="cat">${esc(p.category)}</div>` : '') +
            (p.description ? `<div class="desc">${esc(p.description)}</div>` : '') +
            `<div class="at">${p.x}, ${p.y}, ${p.z}</div></div>`,
            { autoPan: false });

          return marker;
        }

        // --- find: a tile, or a place by name ------------------------------------------------

        const find = L.control({ position: 'topright' });
        find.onAdd = function () {
          const div = L.DomUtil.create('div', 'readout find');
          div.innerHTML =
            '<div class="row">' +
              '<input type="text" id="q" placeholder="x, y or name" autocomplete="off" spellcheck="false">' +
              '<button id="go" type="button">go</button>' +
            '</div>' +
            '<div class="kinds off" id="kinds"></div>' +
            '<ul id="hits"></ul>';
          L.DomEvent.disableClickPropagation(div);
          L.DomEvent.disableScrollPropagation(div);
          return div;
        };
        find.addTo(map);

        const $q = document.getElementById('q');
        const $hits = document.getElementById('hits');
        const $kinds = document.getElementById('kinds');

        /// Two numbers mean a tile; anything else is a name to look up.
        function asTile(value) {
          const parts = value.split(/[,\s]+/).filter(Boolean);
          if (parts.length !== 2) return null;
          const x = Number(parts[0]), y = Number(parts[1]);
          return Number.isFinite(x) && Number.isFinite(y) ? { x, y } : null;
        }

        function search(query) {
          const q = fold(query.trim());
          if (!q) return [];

          const hits = [];
          for (const p of pois) {
            // A kind that has been switched off is one the reader has said they do not care
            // about, so it should not be what a search sends them to either.
            if (!enabled.has(p.category)) continue;
            const i = p.key.indexOf(q);
            if (i < 0) continue;
            // What someone types is far more often the start of a name than the middle of one,
            // and a match at a word boundary beats one buried inside a word.
            const rank = i === 0 ? 0 : /[\s'(\-]/.test(p.key[i - 1]) ? 1 : 2;
            hits.push({ rank, p });
          }

          hits.sort((a, b) =>
            a.rank - b.rank || a.p.name.length - b.p.name.length || a.p.name.localeCompare(b.p.name));
          return hits.slice(0, 12).map(h => h.p);
        }

        let shown = [];
        let cursor = -1;

        function showHits(list) {
          shown = list;
          cursor = -1;
          $hits.innerHTML = list.map((p, i) =>
            `<li data-i="${i}">${esc(p.name)}<span class="at">${p.x}, ${p.y}</span></li>`).join('');
        }

        // Arrowing past either end comes back round, which is the least surprising thing a
        // twelve-item list can do.
        const wrap = i => shown.length === 0 ? -1 : (i + shown.length) % shown.length;

        function highlight(i) {
          cursor = wrap(i);
          for (const li of $hits.children) li.classList.remove('on');
          if (cursor >= 0) $hits.children[cursor].classList.add('on');
        }

        function goToTile(x, y) {
          map.setView(canvasAt(x, y), Math.max(map.getZoom(), CFG.maxZoom - 1));
        }

        function goToPoi(p) {
          $q.value = p.name;
          showHits([]);
          pending = p;
          goToTile(p.x, p.y);
          // setView does nothing when the view is already there, and then no moveend arrives to
          // draw the marker whose popup is waiting to open.
          drawPois();
        }

        function go() {
          const tile = asTile($q.value);
          if (tile) { showHits([]); goToTile(tile.x, tile.y); return; }
          const hit = cursor >= 0 ? shown[cursor] : shown[0];
          if (hit) goToPoi(hit);
        }

        $q.addEventListener('input', () => {
          showHits(asTile($q.value) ? [] : search($q.value));
        });

        $q.addEventListener('keydown', e => {
          if (e.key === 'Enter') { go(); e.preventDefault(); }
          else if (e.key === 'Escape') { showHits([]); $q.blur(); }
          else if (e.key === 'ArrowDown') { highlight(cursor + 1); e.preventDefault(); }
          else if (e.key === 'ArrowUp') { highlight(cursor - 1); e.preventDefault(); }
        });

        $hits.addEventListener('click', e => {
          const li = e.target.closest('li');
          if (li) goToPoi(shown[Number(li.dataset.i)]);
        });

        document.getElementById('go').addEventListener('click', go);

        function listKinds() {
          const counts = new Map();
          for (const p of pois) counts.set(p.category, (counts.get(p.category) || 0) + 1);

          kinds = [...counts]
            .map(([category, count]) => ({ category, count, on: true }))
            // The bucket for records that named no kind is not itself a kind, so it goes last.
            .sort((a, b) => (!a.category) - (!b.category) || a.category.localeCompare(b.category));

          // Data with no kinds in it at all reduces to the single switch this replaced, and
          // calling that one row "uncategorised" would be pedantry.
          const solo = kinds.length === 1 && !kinds[0].category;

          $kinds.innerHTML = kinds.map((k, i) =>
            '<label><input type="checkbox" checked data-i="' + i + '">' +
            `<span class="of">${esc(solo ? 'markers' : k.category || 'uncategorised')}</span>` +
            `<span class="n">${k.count}</span></label>`).join('');

          $kinds.classList.remove('off');
          syncKinds();
        }

        function syncKinds() {
          enabled = new Set(kinds.filter(k => k.on).map(k => k.category));
        }

        $kinds.addEventListener('change', e => {
          const i = e.target.dataset.i;
          if (i === undefined) return;

          kinds[i].on = e.target.checked;
          syncKinds();
          // The result list is drawn through the same filter, so it cannot be left showing hits
          // for a kind that was just switched off.
          showHits(asTile($q.value) ? [] : search($q.value));
          drawPois();
        });

        map.on('moveend zoomend', drawPois);

        async function loadPois() {
          let raw = POIS;

          if (!raw && CFG.poiUrl) {
            try {
              const response = await fetch(CFG.poiUrl, { headers: { Accept: 'application/json' } });
              if (!response.ok) throw new Error(`HTTP ${response.status}`);
              raw = await response.json();
            } catch (e) {
              // A page served from file:// cannot fetch cross-origin, and neither can one whose
              // API sends no CORS header, so say what happened rather than showing nothing.
              $kinds.innerHTML = `<div class="note">markers unavailable (${esc(e.message)})</div>`;
              $kinds.classList.remove('off');
              return;
            }
          }

          if (!raw) return;

          pois = readPois(raw);
          if (pois.length === 0) return;

          listKinds();
          drawPois();
        }

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

        loadPois();
        </script>
        </body>
        </html>
        """;
}
