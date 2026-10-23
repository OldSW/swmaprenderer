<?php
/*
 * Apache serves index.html for the bare directory unless told otherwise, so a link that names
 * no file never reaches this script. Add `DirectoryIndex index.php index.html` to .htaccess
 * for the share button's links (which use the page's own path) to get previews.
 *
 * Written beside the generated index.html by the renderer; serve it as the page's entry point (most
 * hosts prefer index.php over index.html when both exist).
 *
 * A static page cannot say what a shared link looks at: Discord's crawler reads the HTML and
 * runs nothing, and the same file is served for every query string. This script is the missing
 * server. For ?x=1420&y=1690&zoom=4.5 it
 *
 *   1. serves index.html (which reads the same query itself) with Open Graph tags that name
 *      the position and point at
 *   2. ?img=1&x=..&y=..&zoom=.., an 800x420 crop of the tiles centred on that position.
 *
 * Everything it needs to know about the pyramid is read from the CFG object that the renderer
 * wrote into index.html, so it has no configuration of its own. Needs PHP 8 with GD.
 */

const PAGE = __DIR__ . '/index.html';
const CACHE = __DIR__ . '/og-cache';
// Measured from a screenshot of Discord's card (discord.png): the image is drawn 798x419
// device pixels, which is 399x210 points on a 2x display -- the card is a fixed 400 points wide
// and keeps the image's aspect ratio. Anything larger is downscaled by the client, so it only
// costs bytes; this is the size that is sharp on a retina screen and no bigger.
const WIDTH = 800;
const HEIGHT = 420;
// 75 against 85 is a quarter of the bytes for a difference that is invisible in a screenshot
// of a game whose art is already dithered and low-colour.
const QUALITY = 75;
// How many rendered previews are kept at most. A request can name any position, so without a
// limit the cache grows by one file per link anyone ever pasted. Going over the limit cuts the
// cache back to KEEP_FILES rather than to the limit itself, so the next new previews do not
// each have to delete one.
const MAX_FILES = 1000;
const KEEP_FILES = 100;
const BACKGROUND = [0x10, 0x10, 0x14];

$page = @file_get_contents(PAGE);
if ($page === false || !preg_match('/const CFG = (\{.*?\n\});/s', $page, $m)
    || !is_array($cfg = json_decode($m[1], true))) {
    http_response_code(500);
    exit('index.html is missing or has no pyramid configuration.');
}

// The same two spellings the page accepts. Rounded as the share button rounds, which is also
// what keeps the cache from growing without bound: a client cannot ask for 4.123456.
function number(string $key): ?float
{
    $v = $_GET[$key] ?? null;
    return is_string($v) && is_numeric($v) ? (float) $v : null;
}

$x = number('x');
$y = number('y');
$zoom = number('zoom') ?? number('z');
$hasPosition = $x !== null && $y !== null
    && abs($x) <= $cfg['mapWidth'] * 2 && abs($y) <= $cfg['mapHeight'] * 2;

if ($hasPosition) {
    $x = round($x);
    $y = round($y);
    $zoom = round($zoom ?? $cfg['maxZoom'] - 1, 1);
    $zoom = max($cfg['minZoom'], min($cfg['maxZoom'] + 2, $zoom));
}

if (isset($_GET['img'])) {
    $hasPosition ? serveImage($cfg, $x, $y, $zoom) : serveOverview($cfg);
    exit;
}

servePage($page, $cfg, $hasPosition ? [$x, $y, $zoom] : null);

// --- the page -----------------------------------------------------------------------------

function baseUrl(): string
{
    $https = ($_SERVER['HTTPS'] ?? '') !== '' && $_SERVER['HTTPS'] !== 'off'
        || ($_SERVER['HTTP_X_FORWARDED_PROTO'] ?? '') === 'https';
    $dir = rtrim(dirname($_SERVER['SCRIPT_NAME']), '/\\');
    return ($https ? 'https' : 'http') . '://' . $_SERVER['HTTP_HOST'] . $dir . '/';
}

function servePage(string $page, array $cfg, ?array $at): void
{
    $base = baseUrl();
    // Named, not left to the directory URL: when index.html and index.php sit side by side
    // the host may well serve index.html for the bare directory, and the image request would
    // then get the page back instead of a picture.
    $script = basename($_SERVER['SCRIPT_NAME']);
    $e = fn(string $s) => htmlspecialchars($s, ENT_QUOTES);

    $title = 'Ultima Online map';
    $description = 'Browse the map: search places, zoom in, and share a position.';
    $query = '?img=1';
    if ($at) {
        [$x, $y, $zoom] = $at;
        $title .= " \u{2014} $x, $y";
        $description = "Tile $x, $y at zoom $zoom.";
        $query = '?' . http_build_query(['img' => 1, 'x' => $x, 'y' => $y, 'zoom' => $zoom]);
        $url = $base . $script . '?' . http_build_query(['x' => $x, 'y' => $y, 'zoom' => $zoom]);
    } else {
        $url = $base . $script;
    }

    $tags = implode("\n", [
        '<meta property="og:type" content="website">',
        '<meta property="og:title" content="' . $e($title) . '">',
        '<meta property="og:description" content="' . $e($description) . '">',
        '<meta property="og:url" content="' . $e($url) . '">',
        '<meta property="og:image" content="' . $e($base . $script . $query) . '">',
        '<meta property="og:image:width" content="' . WIDTH . '">',
        '<meta property="og:image:height" content="' . HEIGHT . '">',
        '<meta name="twitter:card" content="summary_large_image">',
    ]);

    // Replace whatever the renderer wrote (a --base-url build has its own, for the
    // overview) so that there is exactly one set; two og:image tags leave the choice to the
    // crawler.
    $page = preg_replace('/^\s*<meta (?:property="og:|name="twitter:)[^>]*>\s*$/m', '', $page);
    $page = preg_replace('#<title>.*?</title>#s', '<title>' . $e($title) . "</title>\n$tags", $page, 1);

    // A crawler reads the tags above and runs nothing; a browser runs this and lands on the
    // static page with the query turned into the hash, which is the form the page itself
    // writes as the view moves. An HTTP redirect would do the same for browsers but would
    // send crawlers away from the tags too, and a preview is the reason this script exists.
    // Whatever else the link carried (?preview=...) is the page's business, not ours, and goes
    // along; the position is the part that becomes the hash, so it is not repeated in the query.
    $rest = array_diff_key($_GET, array_flip(['x', 'y', 'z', 'zoom', 'img']));
    $target = basename(PAGE)
        . ($rest ? '?' . http_build_query($rest, '', '&', PHP_QUERY_RFC3986) : '')
        . ($at ? '#' . implode(',', $at) : '');
    $page = str_replace('</head>',
        '<script>location.replace(' . json_encode($target) . ');</script>' . "\n</head>", $page);

    header('Content-Type: text/html; charset=utf-8');
    echo $page;
}

// --- the image ----------------------------------------------------------------------------

function serveOverview(array $cfg): void
{
    // No position: the whole-facet slice, as the renderer's own tags would use.
    $file = __DIR__ . "/{$cfg['minZoom']}/0/0{$cfg['extension']}";
    if (!is_file($file)) {
        http_response_code(404);
        return;
    }
    header('Content-Type: ' . (str_ends_with($cfg['extension'], 'png') ? 'image/png' : 'image/jpeg'));
    readfile($file);
}

function serveImage(array $cfg, float $x, float $y, float $zoom): void
{
    $name = CACHE . '/' . sprintf('%d_%d_%.1f_%d_%dx%d', $x, $y, $zoom, $cfg['mapIndex'], WIDTH, HEIGHT) . '.jpg';
    $rendered = false;
    if (!is_file($name)) {
        $image = crop($cfg, $x, $y, $zoom);
        if (!is_dir(CACHE)) {
            @mkdir(CACHE, 0755, true);
        }
        // Written to a temporary name and renamed, so a second request never reads half a file.
        $tmp = $name . '.' . getmypid();
        if (@imagejpeg($image, $tmp, QUALITY) && @rename($tmp, $name)) {
            $rendered = true;
        } else {
            // The cache is not writable: serve the picture anyway, there is just nothing to prune.
            header('Content-Type: image/jpeg');
            imagejpeg($image, null, QUALITY);
            return;
        }
    }
    header('Content-Type: image/jpeg');
    header('Cache-Control: public, max-age=86400');
    readfile($name);

    // After the response, so the crawler is not kept waiting on housekeeping. Pruning only when
    // something was written keeps a cache hit down to reading one file.
    if ($rendered) {
        if (function_exists('fastcgi_finish_request')) {
            fastcgi_finish_request();
        } else {
            flush();
        }
        prune();
    }
}

/**
 * If more than MAX_FILES previews are cached, deletes everything but the newest KEEP_FILES.
 * Done in PHP rather than by shelling out to `ls -t | tail | xargs rm`: exec() is routinely
 * disabled on shared hosts, and a file name built from a request is exactly what should never
 * reach a shell. Everything in the folder counts, which also sweeps up a temporary file left
 * behind by a request that died mid-write.
 */
function prune(): void
{
    // Counted from the directory entries alone: no list of paths, no stat per file. The list is
    // only worth building in the rare case that there is something to delete.
    if (!is_dir(CACHE)
        || iterator_count(new FilesystemIterator(CACHE, FilesystemIterator::SKIP_DOTS)) <= MAX_FILES) {
        return;
    }
    $files = array_values(array_filter(glob(CACHE . '/*') ?: [], 'is_file'));
    array_multisort(array_map('filemtime', $files), SORT_DESC, $files);
    foreach (array_slice($files, KEEP_FILES) as $old) {
        @unlink($old);
    }
}

/**
 * The tiles around a position, stitched and scaled to the preview size.
 *
 * The projection is IsoProjection's ground-level closed form, as the page's canvasAt has it.
 * Slices at level n are sliceSize pixels, and one canvas pixel is 2^(n - gridMaxZoom) of them.
 */
function crop(array $cfg, float $x, float $y, float $zoom): GdImage
{
    $level = (int) max($cfg['minZoom'], min($cfg['maxZoom'], round($zoom)));
    $scale = 2 ** ($zoom - $level);           // output pixels per tile-image pixel
    $toLevel = 2 ** ($level - $cfg['gridMaxZoom']);

    $cx = ($cfg['halfTile'] * ($x - $y) + $cfg['originX']) * $toLevel;
    $cy = ($cfg['halfTile'] * ($x + $y) + $cfg['originY']) * $toLevel;

    // The window in tile-image pixels that ends up WIDTH x HEIGHT after scaling.
    $w = (int) ceil(WIDTH / $scale);
    $h = (int) ceil(HEIGHT / $scale);
    $left = (int) floor($cx - $w / 2);
    $top = (int) floor($cy - $h / 2);

    $source = imagecreatetruecolor($w, $h);
    imagefill($source, 0, 0, imagecolorallocate($source, ...BACKGROUND));
    imagealphablending($source, true);

    $size = $cfg['sliceSize'];
    for ($ty = intdiv($top, $size) - ($top < 0 ? 1 : 0); $ty * $size < $top + $h; $ty++) {
        for ($tx = intdiv($left, $size) - ($left < 0 ? 1 : 0); $tx * $size < $left + $w; $tx++) {
            $file = __DIR__ . "/$level/$tx/$ty{$cfg['extension']}";
            // A slice that was never written is ocean, and shows the background.
            $data = $tx >= 0 && $ty >= 0 && is_file($file) ? file_get_contents($file) : false;
            $tile = $data === false ? false : @imagecreatefromstring($data);
            if ($tile === false) {
                continue;
            }
            imagecopy($source, $tile, $tx * $size - $left, $ty * $size - $top, 0, 0,
                imagesx($tile), imagesy($tile));
        }
    }

    $out = imagecreatetruecolor(WIDTH, HEIGHT);
    // Progressive JPEGs come out smaller at this size, and show up sooner on a slow link.
    imageinterlace($out, true);
    imagecopyresampled($out, $source, 0, 0, 0, 0, WIDTH, HEIGHT, $w, $h);
    return $out;
}
