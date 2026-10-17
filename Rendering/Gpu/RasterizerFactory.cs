namespace SwMapRenderer.Rendering.Gpu;

/// <summary>
/// Picks the rasterizer for a run. The GPU is opened once and shared; if it cannot be, the run
/// says why once and carries on in software, because a missing driver is no reason to throw
/// away an hour of rendering that would have worked.
///
/// "auto" tries Vulkan and then OpenCL: Vulkan is the one that is not being retired on macOS, and
/// OpenCL is what is left where there is no Vulkan runtime.
/// </summary>
public static class RasterizerFactory
{
    public static readonly string[] Apis = ["auto", "vulkan", "opencl"];

    private static readonly object Gate = new();
    private static bool _tried;
    private static Func<int, int, IRasterizer>? _create;

    public static IRasterizer Create(RenderOptions options, int width, int height)
    {
        if (options.Gpu && Open(options) is { } create)
            return create(width, height);

        return new Rasterizer(width, height);
    }

    private static Func<int, int, IRasterizer>? Open(RenderOptions options)
    {
        lock (Gate)
        {
            if (_tried)
                return _create;

            _tried = true;

            string api = options.GpuApi.ToLowerInvariant();
            var failures = new List<string>();

            if (api is "auto" or "vulkan")
            {
                try
                {
                    var device = VulkanDevice.Open();
                    _create = (w, h) => new VulkanRasterizer(device, w, h);
                    Announce(options, device.Name, "Vulkan");
                    return _create;
                }
                catch (Exception e)
                {
                    failures.Add($"Vulkan: {e.Message}");
                }
            }

            if (api is "auto" or "opencl")
            {
                try
                {
                    var device = OpenClDevice.Open();
                    _create = (w, h) => new OpenClRasterizer(device, w, h);
                    Announce(options, device.Name, "OpenCL");
                    return _create;
                }
                catch (Exception e)
                {
                    failures.Add($"OpenCL: {e.Message}");
                }
            }

            Console.Error.WriteLine($"warning: --gpu is unavailable ({string.Join(" ", failures)}) " +
                                    "Rendering on the CPU instead.");
            return null;
        }
    }

    private static void Announce(RenderOptions options, string device, string api)
    {
        if (options.Verbose)
            Console.WriteLine($"GPU         : {device} ({api})");
    }
}
