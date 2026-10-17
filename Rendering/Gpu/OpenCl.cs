using System.Reflection;
using System.Runtime.InteropServices;

namespace SwMapRenderer.Rendering.Gpu;

/// <summary>
/// The dozen OpenCL entry points the rasterizer needs, bound directly rather than through a
/// package: it keeps the dependency list as it was, and the optional feature costs nothing to
/// anyone who never passes --gpu. The library is only loaded on first use.
/// </summary>
internal static unsafe partial class OpenCl
{
    private const string Library = "OpenCL";

    public const ulong DeviceTypeGpu = 4;
    public const uint DeviceName = 0x102B;
    public const uint ProgramBuildLog = 0x1183;
    public const ulong MemReadWrite = 1;
    public const ulong MemReadOnly = 4;

    static OpenCl()
    {
        NativeLibrary.SetDllImportResolver(typeof(OpenCl).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library)
            return IntPtr.Zero;

        string[] candidates = OperatingSystem.IsMacOS()
            ? ["/System/Library/Frameworks/OpenCL.framework/OpenCL"]
            : OperatingSystem.IsWindows()
                ? ["OpenCL.dll"]
                : ["libOpenCL.so.1", "libOpenCL.so"];

        foreach (var candidate in candidates)
            if (NativeLibrary.TryLoad(candidate, out var handle))
                return handle;

        return IntPtr.Zero;
    }

    public static void Check(int status, string what)
    {
        if (status != 0)
            throw new InvalidOperationException($"OpenCL {what} failed with error {status}.");
    }

    [LibraryImport(Library)]
    public static partial int clGetPlatformIDs(uint count, nint* platforms, out uint found);

    [LibraryImport(Library)]
    public static partial int clGetDeviceIDs(nint platform, ulong type, uint count, nint* devices, out uint found);

    [LibraryImport(Library)]
    public static partial int clGetDeviceInfo(nint device, uint param, nuint size, void* value, out nuint returned);

    [LibraryImport(Library)]
    public static partial nint clCreateContext(nint* properties, uint count, nint* devices, nint notify, nint user, out int error);

    [LibraryImport(Library)]
    public static partial nint clCreateCommandQueue(nint context, nint device, ulong properties, out int error);

    [LibraryImport(Library)]
    public static partial nint clCreateProgramWithSource(nint context, uint count, byte** strings, nuint* lengths, out int error);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int clBuildProgram(nint program, uint count, nint* devices, string options, nint notify, nint user);

    [LibraryImport(Library)]
    public static partial int clGetProgramBuildInfo(nint program, nint device, uint param, nuint size, void* value, out nuint returned);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint clCreateKernel(nint program, string name, out int error);

    [LibraryImport(Library)]
    public static partial nint clCreateBuffer(nint context, ulong flags, nuint size, nint host, out int error);

    [LibraryImport(Library)]
    public static partial int clSetKernelArg(nint kernel, uint index, nuint size, void* value);

    [LibraryImport(Library)]
    public static partial int clEnqueueNDRangeKernel(nint queue, nint kernel, uint dimensions, nuint* offset,
        nuint* global, nuint* local, uint events, nint* waitList, nint* evt);

    [LibraryImport(Library)]
    public static partial int clEnqueueWriteBuffer(nint queue, nint buffer, uint blocking, nuint offset, nuint size,
        void* source, uint events, nint* waitList, nint* evt);

    [LibraryImport(Library)]
    public static partial int clEnqueueReadBuffer(nint queue, nint buffer, uint blocking, nuint offset, nuint size,
        void* destination, uint events, nint* waitList, nint* evt);

    [LibraryImport(Library)]
    public static partial int clFinish(nint queue);

    [LibraryImport(Library)]
    public static partial int clReleaseMemObject(nint buffer);

    [LibraryImport(Library)]
    public static partial int clReleaseKernel(nint kernel);

    [LibraryImport(Library)]
    public static partial int clReleaseCommandQueue(nint queue);
}
