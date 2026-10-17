using System.Numerics;
using System.Runtime.InteropServices;

namespace SwMapRenderer.Rendering.Gpu;

/// <summary>The OpenCL half of the GPU rasterizer: buffers, a queue, and one dispatch of raster.cl.</summary>
internal sealed unsafe class OpenClRasterizer : GpuRasterizerBase, IDisposable
{
    private readonly OpenClDevice _gpu;
    private readonly nint _queue;
    private readonly nint _kernel;

    private nint _trisBuffer, _binStartBuffer, _binListBuffer, _spritesBuffer, _huesBuffer, _outputBuffer;
    private long _trisCapacity, _binStartCapacity, _binListCapacity, _spritesCapacity, _huesCapacity, _outputCapacity;
    private uint[]? _uploadedHues;

    public OpenClRasterizer(OpenClDevice gpu, int width, int height) : base(width, height)
    {
        _gpu = gpu;

        int error;
        _queue = OpenCl.clCreateCommandQueue(gpu.Context, gpu.Device, 0, out error);
        OpenCl.Check(error, "queue creation");

        // A kernel object keeps its arguments, so it is not shared between threads.
        _kernel = OpenCl.clCreateKernel(gpu.Program, "rasterize", out error);
        OpenCl.Check(error, "kernel creation");

        Ensure(ref _outputBuffer, ref _outputCapacity, (long)width * height * 4, OpenCl.MemReadWrite);
    }

    protected override void Dispatch(in Frame frame, Span<byte> rgba)
    {
        long bytes = (long)Width * Height * 4;

        Upload(ref _trisBuffer, ref _trisCapacity, frame.Triangles, OpenCl.MemReadOnly);
        Upload(ref _binStartBuffer, ref _binStartCapacity, MemoryMarshal.AsBytes(frame.BinStart), OpenCl.MemReadOnly);
        Upload(ref _binListBuffer, ref _binListCapacity, MemoryMarshal.AsBytes(frame.BinList), OpenCl.MemReadOnly);
        Upload(ref _spritesBuffer, ref _spritesCapacity, MemoryMarshal.AsBytes(frame.Sprites), OpenCl.MemReadOnly);

        // The hue table is read-only and the same for the whole run, so it goes up once. With
        // nothing drawn, the kernel still needs a buffer to be handed.
        if (frame.HueCount == 0)
        {
            Ensure(ref _huesBuffer, ref _huesCapacity, 4, OpenCl.MemReadOnly);
        }
        else if (Ensure(ref _huesBuffer, ref _huesCapacity, frame.Hues.Length * 4, OpenCl.MemReadOnly)
                 || !ReferenceEquals(frame.Hues, _uploadedHues))
        {
            Upload(ref _huesBuffer, ref _huesCapacity, MemoryMarshal.AsBytes(frame.Hues.AsSpan()), OpenCl.MemReadOnly);
            _uploadedHues = frame.Hues;
        }

        int hueCount = frame.HueCount, width = frame.Width, height = frame.Height, tilesX = frame.TilesX;
        Vector4 clear = frame.Clear;
        nint trisBuffer = _trisBuffer, binStartBuffer = _binStartBuffer, binListBuffer = _binListBuffer;
        nint spritesBuffer = _spritesBuffer, huesBuffer = _huesBuffer, outputBuffer = _outputBuffer;

        uint arg = 0;
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, (nuint)sizeof(nint), &trisBuffer), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, (nuint)sizeof(nint), &binStartBuffer), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, (nuint)sizeof(nint), &binListBuffer), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, (nuint)sizeof(nint), &spritesBuffer), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, (nuint)sizeof(nint), &huesBuffer), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, sizeof(int), &hueCount), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, sizeof(int), &width), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, sizeof(int), &height), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, sizeof(int), &tilesX), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, (nuint)sizeof(Vector4), &clear), "argument");
        OpenCl.Check(OpenCl.clSetKernelArg(_kernel, arg++, (nuint)sizeof(nint), &outputBuffer), "argument");

        nuint* global = stackalloc nuint[2] { (nuint)width, (nuint)height };
        OpenCl.Check(OpenCl.clEnqueueNDRangeKernel(_queue, _kernel, 2, null, global, null, 0, null, null), "dispatch");

        fixed (byte* destination = rgba)
            OpenCl.Check(OpenCl.clEnqueueReadBuffer(_queue, _outputBuffer, 1, 0, (nuint)bytes, destination, 0, null, null),
                "read-back");
    }

    private void Upload(ref nint buffer, ref long capacity, ReadOnlySpan<byte> data, ulong flags)
    {
        Ensure(ref buffer, ref capacity, Math.Max(data.Length, 4), flags);
        if (data.Length == 0)
            return;

        fixed (byte* source = data)
            OpenCl.Check(OpenCl.clEnqueueWriteBuffer(_queue, buffer, 0, 0, (nuint)data.Length, source, 0, null, null),
                "upload");
    }

    /// <summary>Grows a device buffer if it is too small. Contents are not preserved; every frame rewrites them.</summary>
    private bool Ensure(ref nint buffer, ref long capacity, long bytes, ulong flags)
    {
        if (buffer != 0 && capacity >= bytes)
            return false;

        if (buffer != 0)
        {
            // Writes already queued may still be reading it.
            OpenCl.clFinish(_queue);
            OpenCl.clReleaseMemObject(buffer);
        }

        long size = Math.Max(bytes, capacity * 3 / 2);
        buffer = OpenCl.clCreateBuffer(_gpu.Context, flags, (nuint)size, 0, out int error);
        OpenCl.Check(error, "buffer allocation");
        capacity = size;
        return true;
    }

    public void Dispose()
    {
        OpenCl.clFinish(_queue);
        foreach (var buffer in new[] { _trisBuffer, _binStartBuffer, _binListBuffer, _spritesBuffer, _huesBuffer, _outputBuffer })
            if (buffer != 0)
                OpenCl.clReleaseMemObject(buffer);

        OpenCl.clReleaseKernel(_kernel);
        OpenCl.clReleaseCommandQueue(_queue);
    }
}
