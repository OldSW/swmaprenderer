using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace SwMapRenderer.Rendering.Gpu;

/// <summary>
/// The Vulkan device, queue and compiled compute pipeline, shared by every
/// <see cref="VulkanRasterizer"/> in the run. Each rasterizer brings its own command pool,
/// fence, descriptor set and buffers; only submitting to the queue is serialised.
///
/// On macOS this runs on MoltenVK, which sits on Metal. The loader is not required: the
/// MoltenVK library exports the Vulkan entry points itself, and is tried when no loader is found.
/// </summary>
internal sealed unsafe class VulkanDevice
{
    public Vk Api { get; }
    public Instance Instance { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public Device Device { get; }
    public Queue Queue { get; }
    public uint QueueFamily { get; }
    public DescriptorSetLayout DescriptorSetLayout { get; }
    public PipelineLayout PipelineLayout { get; }
    public Pipeline Pipeline { get; }
    public string Name { get; }

    /// <summary>Queue submission must be externally synchronised, and every rasterizer shares one queue.</summary>
    public object QueueLock { get; } = new();

    private readonly PhysicalDeviceMemoryProperties _memory;

    /// <summary>Bindings in raster.comp: triangles, bin starts, bin lists, sprites, hues, output.</summary>
    public const int BufferCount = 6;

    private VulkanDevice(Vk api, Instance instance, PhysicalDevice physical, Device device, Queue queue, uint family,
        DescriptorSetLayout setLayout, PipelineLayout pipelineLayout, Pipeline pipeline, string name)
    {
        Api = api;
        Instance = instance;
        PhysicalDevice = physical;
        Device = device;
        Queue = queue;
        QueueFamily = family;
        DescriptorSetLayout = setLayout;
        PipelineLayout = pipelineLayout;
        Pipeline = pipeline;
        Name = name;

        api.GetPhysicalDeviceMemoryProperties(physical, out _memory);
    }

    /// <exception cref="InvalidOperationException">No Vulkan runtime, no suitable GPU, or the shader would not build.</exception>
    public static VulkanDevice Open()
    {
        var api = LoadApi();

        // MoltenVK is a "portability" implementation, which a loader only reports if asked to.
        var available = InstanceExtensions(api);
        var extensions = new List<string>();
        InstanceCreateFlags flags = 0;
        if (available.Contains("VK_KHR_portability_enumeration"))
        {
            extensions.Add("VK_KHR_portability_enumeration");
            flags |= InstanceCreateFlags.EnumeratePortabilityBitKhr;
        }

        var appName = SilkMarshal.StringToPtr("swmaprenderer");
        var extensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        try
        {
            var app = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApiVersion = Vk.Version11,
            };
            var instanceInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                Flags = flags,
                PApplicationInfo = &app,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = extensionNames,
            };
            Check(api.CreateInstance(&instanceInfo, null, out var instance), "instance creation");
            return OpenDevice(api, instance);
        }
        finally
        {
            SilkMarshal.Free(appName);
            SilkMarshal.Free((nint)extensionNames);
        }
    }

    private static VulkanDevice OpenDevice(Vk api, Instance instance)
    {
        uint count = 0;
        Check(api.EnumeratePhysicalDevices(instance, &count, null), "device enumeration");
        if (count == 0)
            throw new InvalidOperationException("Vulkan is present but reports no device.");

        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* d = devices)
            Check(api.EnumeratePhysicalDevices(instance, &count, d), "device enumeration");

        // A discrete GPU over an integrated one, and never a software implementation.
        PhysicalDevice chosen = default;
        uint family = 0;
        int best = -1;
        string name = "";
        foreach (var candidate in devices)
        {
            api.GetPhysicalDeviceProperties(candidate, out var properties);
            api.GetPhysicalDeviceFeatures(candidate, out var features);
            if (!features.ShaderInt64 || properties.DeviceType == PhysicalDeviceType.Cpu)
                continue;

            int rank = properties.DeviceType == PhysicalDeviceType.DiscreteGpu ? 2 : 1;
            if (rank <= best || ComputeFamily(api, candidate) is not { } found)
                continue;

            best = rank;
            chosen = candidate;
            family = found;
            name = Marshal.PtrToStringAnsi((nint)properties.DeviceName) ?? "Vulkan device";
        }

        if (best < 0)
            throw new InvalidOperationException("No Vulkan GPU with compute and 64-bit integer support was found.");

        // A portability device insists that its subset extension is enabled, if it has one.
        var deviceExtensions = new List<string>();
        if (DeviceExtensions(api, chosen).Contains("VK_KHR_portability_subset"))
            deviceExtensions.Add("VK_KHR_portability_subset");

        float priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = family,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        var enabled = new PhysicalDeviceFeatures { ShaderInt64 = true };
        var extensionNames = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions);
        Device device;
        try
        {
            var deviceInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                PEnabledFeatures = &enabled,
                EnabledExtensionCount = (uint)deviceExtensions.Count,
                PpEnabledExtensionNames = extensionNames,
            };
            Check(api.CreateDevice(chosen, &deviceInfo, null, out device), "device creation");
        }
        finally
        {
            SilkMarshal.Free((nint)extensionNames);
        }

        api.GetDeviceQueue(device, family, 0, out var queue);

        var setLayout = CreateSetLayout(api, device);
        var pipelineLayout = CreatePipelineLayout(api, device, setLayout);
        var pipeline = CreatePipeline(api, device, pipelineLayout, name);

        return new VulkanDevice(api, instance, chosen, device, queue, family, setLayout, pipelineLayout, pipeline, name);
    }

    private static uint? ComputeFamily(Vk api, PhysicalDevice device)
    {
        uint count = 0;
        api.GetPhysicalDeviceQueueFamilyProperties(device, &count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* f = families)
            api.GetPhysicalDeviceQueueFamilyProperties(device, &count, f);

        for (uint i = 0; i < count; i++)
            if ((families[i].QueueFlags & QueueFlags.ComputeBit) != 0)
                return i;

        return null;
    }

    private static DescriptorSetLayout CreateSetLayout(Vk api, Device device)
    {
        var bindings = new DescriptorSetLayoutBinding[BufferCount];
        for (int i = 0; i < BufferCount; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)i,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
        }

        fixed (DescriptorSetLayoutBinding* b = bindings)
        {
            var info = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = BufferCount,
                PBindings = b,
            };
            Check(api.CreateDescriptorSetLayout(device, &info, null, out var layout), "descriptor layout creation");
            return layout;
        }
    }

    private static PipelineLayout CreatePipelineLayout(Vk api, Device device, DescriptorSetLayout setLayout)
    {
        var range = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.ComputeBit,
            Offset = 0,
            Size = (uint)sizeof(VulkanRasterizer.Params),
        };
        var info = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &range,
        };
        Check(api.CreatePipelineLayout(device, &info, null, out var layout), "pipeline layout creation");
        return layout;
    }

    private static Pipeline CreatePipeline(Vk api, Device device, PipelineLayout layout, string deviceName)
    {
        byte[] code = EmbeddedBytes("raster.spv");
        ShaderModule module;
        fixed (byte* c = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)c,
            };
            Check(api.CreateShaderModule(device, &info, null, out module), "shader module creation");
        }

        var entry = SilkMarshal.StringToPtr("main");
        try
        {
            var pipelineInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Layout = layout,
                Stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = module,
                    PName = (byte*)entry,
                },
            };

            var result = api.CreateComputePipelines(device, default, 1, &pipelineInfo, null, out var pipeline);
            if (result != Result.Success)
                throw new InvalidOperationException($"The rasterizer shader did not build on {deviceName} (Vulkan error {result}).");

            api.DestroyShaderModule(device, module, null);
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free(entry);
        }
    }

    /// <summary>Index of a memory type that both the CPU and GPU can use directly, preferring device-local.</summary>
    public uint FindMemoryType(uint allowedTypes)
    {
        const MemoryPropertyFlags host = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

        int fallback = -1;
        for (int i = 0; i < _memory.MemoryTypeCount; i++)
        {
            if ((allowedTypes & (1u << i)) == 0)
                continue;

            var flags = _memory.MemoryTypes[i].PropertyFlags;
            if ((flags & host) != host)
                continue;

            if ((flags & MemoryPropertyFlags.DeviceLocalBit) != 0)
                return (uint)i;

            if (fallback < 0)
                fallback = i;
        }

        if (fallback < 0)
            throw new InvalidOperationException("The Vulkan device has no host-visible memory to share buffers through.");

        return (uint)fallback;
    }

    public static void Check(Result result, string what)
    {
        if (result != Result.Success)
            throw new InvalidOperationException($"Vulkan {what} failed with {result}.");
    }

    private static HashSet<string> InstanceExtensions(Vk api)
    {
        uint count = 0;
        api.EnumerateInstanceExtensionProperties((byte*)null, &count, null);
        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = properties)
            api.EnumerateInstanceExtensionProperties((byte*)null, &count, p);

        return Names(properties);
    }

    private static HashSet<string> DeviceExtensions(Vk api, PhysicalDevice device)
    {
        uint count = 0;
        api.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, null);
        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = properties)
            api.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, p);

        return Names(properties);
    }

    private static HashSet<string> Names(ExtensionProperties[] properties)
    {
        var names = new HashSet<string>();
        fixed (ExtensionProperties* p = properties)
            for (int i = 0; i < properties.Length; i++)
                names.Add(Marshal.PtrToStringAnsi((nint)p[i].ExtensionName) ?? "");

        return names;
    }

    /// <summary>
    /// Opens the Vulkan library. The loader is the right thing where there is one; on a Mac the
    /// MoltenVK library from Homebrew or the Vulkan SDK serves instead.
    /// </summary>
    private static Vk LoadApi()
    {
        foreach (var candidate in LibraryCandidates())
        {
            if (!NativeLibrary.TryLoad(candidate, out var handle))
                continue;

            return new Vk(new LamdaNativeContext(name =>
                NativeLibrary.TryGetExport(handle, name, out var address) ? address : 0));
        }

        throw new InvalidOperationException(
            OperatingSystem.IsMacOS()
                ? "No Vulkan library was found. Install MoltenVK (brew install molten-vk)."
                : "No Vulkan library was found.");
    }

    private static IEnumerable<string> LibraryCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return "vulkan-1.dll";
            yield break;
        }

        if (!OperatingSystem.IsMacOS())
        {
            yield return "libvulkan.so.1";
            yield return "libvulkan.so";
            yield break;
        }

        string? sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (!string.IsNullOrEmpty(sdk))
        {
            yield return Path.Combine(sdk, "lib", "libvulkan.1.dylib");
            yield return Path.Combine(sdk, "lib", "libMoltenVK.dylib");
        }

        yield return "libvulkan.1.dylib";
        foreach (var prefix in new[] { "/opt/homebrew", "/usr/local" })
        {
            yield return $"{prefix}/lib/libvulkan.1.dylib";
            yield return $"{prefix}/lib/libMoltenVK.dylib";
        }
    }

    private static byte[] EmbeddedBytes(string name)
    {
        using var stream = typeof(VulkanDevice).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded file '{name}' is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
