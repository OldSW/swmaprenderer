using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace SwMapRenderer.Rendering.Gpu;

/// <summary>
/// The Vulkan half of the GPU rasterizer: host-visible buffers, a command buffer, and one
/// dispatch of raster.spv. Buffers are mapped for good and written directly; on the unified
/// memory of an Apple or integrated GPU that is the whole transfer, and elsewhere it is slower
/// than staging through device-local memory, which this does not do.
/// </summary>
internal sealed unsafe class VulkanRasterizer : GpuRasterizerBase, IDisposable
{
    /// <summary>The push constant block of raster.comp.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Params
    {
        public Vector4 Clear;
        public int HueCount, Width, Height, TilesX;
    }

    private struct DeviceBuffer
    {
        public Buffer Handle;
        public DeviceMemory Memory;
        public byte* Mapped;
        public long Capacity;
    }

    private readonly VulkanDevice _gpu;
    private readonly Vk _vk;
    private readonly CommandPool _pool;
    private readonly CommandBuffer _commands;
    private readonly Fence _fence;
    private readonly DescriptorPool _descriptorPool;
    private readonly DescriptorSet _descriptors;

    // Triangles, bin starts, bin lists, sprites, hues, output: the order of raster.comp's bindings.
    private readonly DeviceBuffer[] _buffers = new DeviceBuffer[VulkanDevice.BufferCount];
    private uint[]? _uploadedHues;

    public VulkanRasterizer(VulkanDevice gpu, int width, int height) : base(width, height)
    {
        _gpu = gpu;
        _vk = gpu.Api;

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = gpu.QueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        VulkanDevice.Check(_vk.CreateCommandPool(gpu.Device, &poolInfo, null, out _pool), "command pool creation");

        var allocate = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        CommandBuffer commands;
        VulkanDevice.Check(_vk.AllocateCommandBuffers(gpu.Device, &allocate, &commands), "command buffer allocation");
        _commands = commands;

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        VulkanDevice.Check(_vk.CreateFence(gpu.Device, &fenceInfo, null, out _fence), "fence creation");

        var size = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = VulkanDevice.BufferCount };
        var descriptorPoolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &size,
        };
        VulkanDevice.Check(_vk.CreateDescriptorPool(gpu.Device, &descriptorPoolInfo, null, out _descriptorPool),
            "descriptor pool creation");

        var layout = gpu.DescriptorSetLayout;
        var setInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        DescriptorSet set;
        VulkanDevice.Check(_vk.AllocateDescriptorSets(gpu.Device, &setInfo, &set), "descriptor set allocation");
        _descriptors = set;
    }

    protected override void Dispatch(in Frame frame, Span<byte> rgba)
    {
        long bytes = (long)Width * Height * 4;

        Upload(0, frame.Triangles);
        Upload(1, MemoryMarshal.AsBytes(frame.BinStart));
        Upload(2, MemoryMarshal.AsBytes(frame.BinList));
        Upload(3, MemoryMarshal.AsBytes(frame.Sprites));

        // The hue table is the same for the whole run, so it is copied once, and again only if
        // its buffer was replaced. With nothing drawn the shader still needs a buffer bound.
        if (frame.HueCount == 0)
        {
            Ensure(4, 16);
        }
        else
        {
            bool replaced = Ensure(4, frame.Hues.Length * 4L);
            if (replaced || !ReferenceEquals(frame.Hues, _uploadedHues))
            {
                Upload(4, MemoryMarshal.AsBytes(frame.Hues.AsSpan()));
                _uploadedHues = frame.Hues;
            }
        }

        Ensure(5, bytes);

        // Buffers that were replaced are different objects, and the set has to be told.
        UpdateDescriptors();

        Record(frame);
        Submit();

        new ReadOnlySpan<byte>(_buffers[5].Mapped, (int)bytes).CopyTo(rgba);
    }

    private void Record(in Frame frame)
    {
        VulkanDevice.Check(_vk.ResetCommandBuffer(_commands, 0), "command buffer reset");

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VulkanDevice.Check(_vk.BeginCommandBuffer(_commands, &begin), "command buffer begin");

        _vk.CmdBindPipeline(_commands, PipelineBindPoint.Compute, _gpu.Pipeline);
        var set = _descriptors;
        _vk.CmdBindDescriptorSets(_commands, PipelineBindPoint.Compute, _gpu.PipelineLayout, 0, 1, &set, 0, null);

        var parameters = new Params
        {
            Clear = frame.Clear,
            HueCount = frame.HueCount,
            Width = frame.Width,
            Height = frame.Height,
            TilesX = frame.TilesX,
        };
        _vk.CmdPushConstants(_commands, _gpu.PipelineLayout, ShaderStageFlags.ComputeBit, 0,
            (uint)sizeof(Params), &parameters);

        _vk.CmdDispatch(_commands, (uint)((frame.Width + 7) / 8), (uint)((frame.Height + 7) / 8), 1);

        // The shader's writes have to be visible to the CPU reading the mapped buffer.
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
        };
        _vk.CmdPipelineBarrier(_commands, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.HostBit, 0,
            1, &barrier, 0, null, 0, null);

        VulkanDevice.Check(_vk.EndCommandBuffer(_commands), "command buffer end");
    }

    private void Submit()
    {
        var commands = _commands;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commands,
        };

        lock (_gpu.QueueLock)
            VulkanDevice.Check(_vk.QueueSubmit(_gpu.Queue, 1, &submit, _fence), "queue submission");

        var fence = _fence;
        VulkanDevice.Check(_vk.WaitForFences(_gpu.Device, 1, &fence, true, ulong.MaxValue), "wait for the GPU");
        VulkanDevice.Check(_vk.ResetFences(_gpu.Device, 1, &fence), "fence reset");
    }

    private void UpdateDescriptors()
    {
        var infos = stackalloc DescriptorBufferInfo[VulkanDevice.BufferCount];
        var writes = stackalloc WriteDescriptorSet[VulkanDevice.BufferCount];

        for (int i = 0; i < VulkanDevice.BufferCount; i++)
        {
            infos[i] = new DescriptorBufferInfo { Buffer = _buffers[i].Handle, Offset = 0, Range = Vk.WholeSize };
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptors,
                DstBinding = (uint)i,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &infos[i],
            };
        }

        _vk.UpdateDescriptorSets(_gpu.Device, VulkanDevice.BufferCount, writes, 0, null);
    }

    /// <summary>Copies data into a binding's buffer, growing it first if need be.</summary>
    private void Upload(int binding, ReadOnlySpan<byte> data)
    {
        Ensure(binding, Math.Max(data.Length, 16));
        data.CopyTo(new Span<byte>(_buffers[binding].Mapped, data.Length));
    }

    /// <summary>
    /// Makes a binding's buffer at least this big. Contents are not kept: every frame rewrites
    /// them, apart from the hue table, which the caller re-uploads when this says it was replaced.
    /// Nothing is in flight here, because each dispatch waits for its fence.
    /// </summary>
    private bool Ensure(int binding, long bytes)
    {
        ref var buffer = ref _buffers[binding];
        if (buffer.Handle.Handle != 0 && buffer.Capacity >= bytes)
            return false;

        Release(ref buffer);

        long size = Math.Max(bytes, buffer.Capacity * 3 / 2);
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)size,
            Usage = BufferUsageFlags.StorageBufferBit,
            SharingMode = SharingMode.Exclusive,
        };
        VulkanDevice.Check(_vk.CreateBuffer(_gpu.Device, &info, null, out buffer.Handle), "buffer creation");

        _vk.GetBufferMemoryRequirements(_gpu.Device, buffer.Handle, out var requirements);
        var allocate = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = _gpu.FindMemoryType(requirements.MemoryTypeBits),
        };
        VulkanDevice.Check(_vk.AllocateMemory(_gpu.Device, &allocate, null, out buffer.Memory), "memory allocation");
        VulkanDevice.Check(_vk.BindBufferMemory(_gpu.Device, buffer.Handle, buffer.Memory, 0), "memory binding");

        void* mapped;
        VulkanDevice.Check(_vk.MapMemory(_gpu.Device, buffer.Memory, 0, Vk.WholeSize, 0, &mapped), "memory mapping");
        buffer.Mapped = (byte*)mapped;
        buffer.Capacity = size;
        return true;
    }

    private void Release(ref DeviceBuffer buffer)
    {
        if (buffer.Handle.Handle == 0)
            return;

        _vk.UnmapMemory(_gpu.Device, buffer.Memory);
        _vk.DestroyBuffer(_gpu.Device, buffer.Handle, null);
        _vk.FreeMemory(_gpu.Device, buffer.Memory, null);
        buffer.Handle = default;
        buffer.Memory = default;
        buffer.Mapped = null;
    }

    public void Dispose()
    {
        for (int i = 0; i < _buffers.Length; i++)
            Release(ref _buffers[i]);

        _vk.DestroyDescriptorPool(_gpu.Device, _descriptorPool, null);
        _vk.DestroyFence(_gpu.Device, _fence, null);
        _vk.DestroyCommandPool(_gpu.Device, _pool, null);
    }
}
