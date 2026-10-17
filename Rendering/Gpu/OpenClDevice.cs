using System.Text;

namespace SwMapRenderer.Rendering.Gpu;

/// <summary>
/// The GPU, its context and the compiled rasterizer program, shared by every
/// <see cref="GpuRasterizer"/> in the run. Each rasterizer brings its own queue, kernel object
/// and buffers, so slices rendered on different threads never touch each other's state.
/// </summary>
internal sealed unsafe class OpenClDevice
{
    public nint Device { get; }
    public nint Context { get; }
    public nint Program { get; }
    public string Name { get; }

    private OpenClDevice(nint device, nint context, nint program, string name)
    {
        Device = device;
        Context = context;
        Program = program;
        Name = name;
    }

    /// <exception cref="InvalidOperationException">No OpenCL GPU, or the kernel would not build.</exception>
    public static OpenClDevice Open()
    {
        uint platformCount;
        try
        {
            OpenCl.Check(OpenCl.clGetPlatformIDs(0, null, out platformCount), "platform query");
        }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException("No OpenCL runtime is installed.");
        }

        if (platformCount == 0)
            throw new InvalidOperationException("No OpenCL platform was found.");

        var platforms = new nint[platformCount];
        fixed (nint* p = platforms)
            OpenCl.Check(OpenCl.clGetPlatformIDs(platformCount, p, out _), "platform query");

        nint device = 0;
        foreach (var platform in platforms)
        {
            nint found = 0;
            if (OpenCl.clGetDeviceIDs(platform, OpenCl.DeviceTypeGpu, 1, &found, out uint n) == 0 && n > 0)
            {
                device = found;
                break;
            }
        }

        if (device == 0)
            throw new InvalidOperationException("OpenCL is present but reports no GPU.");

        nuint size;
        OpenCl.clGetDeviceInfo(device, OpenCl.DeviceName, 0, null, out size);
        var nameBytes = new byte[size];
        fixed (byte* b = nameBytes)
            OpenCl.clGetDeviceInfo(device, OpenCl.DeviceName, size, b, out _);
        string name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0').Trim();

        int error;
        nint context = OpenCl.clCreateContext(null, 1, &device, 0, 0, out error);
        OpenCl.Check(error, "context creation");

        var source = Encoding.UTF8.GetBytes(Embedded("raster.cl"));
        nint program;
        fixed (byte* s = source)
        {
            byte* text = s;
            nuint length = (nuint)source.Length;
            program = OpenCl.clCreateProgramWithSource(context, 1, &text, &length, out error);
        }
        OpenCl.Check(error, "program creation");

        int build = OpenCl.clBuildProgram(program, 1, &device, "", 0, 0);
        if (build != 0)
        {
            OpenCl.clGetProgramBuildInfo(program, device, OpenCl.ProgramBuildLog, 0, null, out size);
            var log = new byte[size];
            fixed (byte* l = log)
                OpenCl.clGetProgramBuildInfo(program, device, OpenCl.ProgramBuildLog, size, l, out _);
            throw new InvalidOperationException(
                $"The rasterizer kernel did not build on {name} (error {build}):{Environment.NewLine}" +
                Encoding.UTF8.GetString(log).TrimEnd('\0'));
        }

        return new OpenClDevice(device, context, program, name);
    }

    private static string Embedded(string name)
    {
        using var stream = typeof(OpenClDevice).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded file '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
