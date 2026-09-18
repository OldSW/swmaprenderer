using System.IO.MemoryMappedFiles;

namespace SwMapRenderer.Assets;

/// <summary>
/// A memory-mapped, read-only view over a UO data file. The client files are large
/// (art.mul is 64MB, map0.mul is 86MB) and accessed at random offsets, so mapping beats
/// buffered reads and keeps resident memory down to the pages we actually touch.
/// </summary>
public sealed unsafe class DataFile : IDisposable
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _ptr;

    public long Length { get; }
    public string Path { get; }

    public DataFile(string path)
    {
        Path = path;
        Length = new FileInfo(path).Length;
        if (Length == 0)
            throw new InvalidDataException($"'{path}' is empty.");

        _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);
    }

    /// <summary>Returns a window into the file, or an empty span if it falls outside the file.</summary>
    public ReadOnlySpan<byte> Span(long offset, int length)
    {
        if (offset < 0 || length <= 0 || offset + length > Length)
            return ReadOnlySpan<byte>.Empty;

        return new ReadOnlySpan<byte>(_ptr + offset, length);
    }

    public void Dispose()
    {
        if (_ptr != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _ptr = null;
        }
        _view.Dispose();
        _file.Dispose();
    }
}
