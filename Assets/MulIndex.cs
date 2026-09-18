using System.Runtime.InteropServices;

namespace SwMapRenderer.Assets;

/// <summary>One 12-byte record of a *idx.mul lookup table.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct MulIndexEntry
{
    public readonly int Lookup;
    public readonly int Length;
    public readonly int Extra;

    /// <summary>
    /// Unused slots are stored as -1 (0xFFFFFFFF) throughout the UO index files; the high bit
    /// of Length is a compression flag in some files and is masked off here.
    /// </summary>
    public bool IsValid => Lookup >= 0 && (Length & 0x7FFFFFFF) > 0;

    public int DataLength => Length & 0x7FFFFFFF;
}

/// <summary>An index file paired with the data file it points into (artidx.mul + art.mul, etc).</summary>
public sealed class IndexedMulFile : IDisposable
{
    private readonly MulIndexEntry[] _entries;
    private readonly DataFile _data;

    public int Count => _entries.Length;

    public IndexedMulFile(string indexPath, string dataPath)
    {
        var raw = File.ReadAllBytes(indexPath);
        _entries = MemoryMarshal.Cast<byte, MulIndexEntry>(raw).ToArray();
        _data = new DataFile(dataPath);
    }

    public MulIndexEntry GetEntry(int id) =>
        id >= 0 && id < _entries.Length ? _entries[id] : default;

    public bool IsValid(int id) => GetEntry(id).IsValid;

    /// <summary>Returns the raw bytes for an entry, or an empty span if the entry is unused.</summary>
    public ReadOnlySpan<byte> GetData(int id)
    {
        var entry = GetEntry(id);
        return entry.IsValid ? _data.Span(entry.Lookup, entry.DataLength) : ReadOnlySpan<byte>.Empty;
    }

    public void Dispose() => _data.Dispose();
}
