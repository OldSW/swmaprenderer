namespace SwMapRenderer.Assets;

/// <summary>One piece of a multi: a static at an offset from the multi's own origin tile.</summary>
public readonly record struct MultiComponent(ushort Id, short X, short Y, short Z);

/// <summary>
/// multi.idx + multi.mul -- the prefabricated groups of statics (houses, boats, moongates) that
/// an item refers to by id instead of carrying art of its own.
///
/// A record is id, x, y, z and a flag. High Seas grew it to sixteen bytes by appending a cliloc
/// count, leaving the first twelve unchanged, so the stride is picked per entry from its length
/// and the fields are read the same way either way.
/// </summary>
public sealed class MultiFile : IDisposable
{
    /// <summary>Item ids at or above this are a multi id in disguise; the rest is the index.</summary>
    public const int IdOffset = 0x4000;

    private readonly IndexedMulFile _file;

    public MultiFile(string indexPath, string dataPath) => _file = new IndexedMulFile(indexPath, dataPath);

    /// <summary>The visible components of one multi. Empty for an unused or unreadable id.</summary>
    public IReadOnlyList<MultiComponent> Get(int multiId)
    {
        var span = _file.GetData(multiId);

        int stride = span.Length % 12 == 0 ? 12 : span.Length % 16 == 0 ? 16 : 0;
        if (span.Length == 0 || stride == 0)
            return [];

        var components = new List<MultiComponent>(span.Length / stride);

        for (int offset = 0; offset + stride <= span.Length; offset += stride)
        {
            // Every multi carries an invisible marker at its origin, flagged zero. It is the
            // nodraw graphic, so dropping it here only saves the work of filtering it later.
            if (BitConverter.ToInt32(span[(offset + 8)..]) == 0)
                continue;

            components.Add(new MultiComponent(
                BitConverter.ToUInt16(span[offset..]),
                BitConverter.ToInt16(span[(offset + 2)..]),
                BitConverter.ToInt16(span[(offset + 4)..]),
                BitConverter.ToInt16(span[(offset + 6)..])));
        }

        return components;
    }

    public void Dispose() => _file.Dispose();
}
