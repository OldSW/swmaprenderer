using System.Collections.Concurrent;

namespace SwMapRenderer.Assets;

/// <summary>
/// Decoded-sprite cache, safe for concurrent readers and bounded by decoded size.
///
/// A single image needs only the graphics in view, but a whole-facet tile run walks the entire
/// art file, and decoded RGBA runs several times the size of the packed originals. The budget
/// stops that from growing without limit.
///
/// Eviction is a wholesale clear rather than an LRU. Slices are rendered in a spatially
/// coherent order, so the working set turns over in bands rather than uniformly, and the
/// bookkeeping an LRU needs on every lookup costs more here than occasionally re-decoding. A
/// clear racing a lookup is harmless: the loser just decodes its sprite again.
/// </summary>
public sealed class SpriteCache(long budgetBytes)
{
    private readonly ConcurrentDictionary<int, Sprite> _sprites = new();
    private long _bytes;

    public long BudgetBytes { get; } = budgetBytes;

    public int Count => _sprites.Count;

    public Sprite GetOrAdd(int key, Func<int, Sprite> decode)
    {
        if (_sprites.TryGetValue(key, out var cached))
            return cached;

        var sprite = decode(key);

        if (_sprites.TryAdd(key, sprite))
        {
            long size = (long)sprite.Pixels.Length * sizeof(uint);
            if (Interlocked.Add(ref _bytes, size) > BudgetBytes)
            {
                _sprites.Clear();
                Interlocked.Exchange(ref _bytes, 0);
            }
        }

        return sprite;
    }
}

/// <summary>
/// Cache of decoded map blocks, safe for concurrent readers and bounded by entry count.
///
/// Sized in blocks rather than bytes because the entries are object graphs whose real footprint
/// is hard to attribute; a block is 64 tiles, so the count is a good enough proxy. As above,
/// eviction clears rather than evicting one entry at a time.
/// </summary>
public sealed class BlockCache<T>(int maxEntries)
{
    private readonly ConcurrentDictionary<int, T> _blocks = new();

    public int MaxEntries { get; } = maxEntries;

    public int Count => _blocks.Count;

    public T GetOrAdd(int key, Func<int, T> load)
    {
        if (_blocks.TryGetValue(key, out var cached))
            return cached;

        var block = load(key);

        if (_blocks.Count >= MaxEntries)
            _blocks.Clear();

        _blocks.TryAdd(key, block);
        return block;
    }
}
