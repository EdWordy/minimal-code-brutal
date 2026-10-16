using System.Runtime.InteropServices;
using Brutal;
using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.Pointers.Extensions;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;

namespace Dodge;

/// <summary>
/// The smallest useful Vulkan renderer: it clears the screen and draws solid rectangles.
/// It uses Vulkan 1.3 dynamic rendering, so there are no render passes or framebuffers.
/// </summary>
public sealed unsafe class Renderer : IDisposable
{
    // Two frames in flight lets the CPU record frame N+1 while the GPU draws frame N.
    private const int FramesInFlight = 2;

    // Matches the push constant block in Shaders/quad.vert.
    [StructLayout(LayoutKind.Sequential)]
    private struct QuadPushConstants
    {
        public float4 Rect;
        public float4 Color;
        public float2 Playfield;
    }

    private struct Frame
    {
        public CommandBuffer CommandBuffer;
        public VkSemaphore ImageAvailable;
        public VkFence InFlight;
    }

    private readonly GlfwWindow _window;
    private readonly float2 _playfield;

    private readonly Instance _instance;
    private readonly VkSurfaceKHR _surface;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly VkCommandPool _commandPool;
    private readonly Frame[] _frames = new Frame[FramesInFlight];
    private readonly VkPipelineLayout _pipelineLayout;

    private VkSwapchainKHR _swapchain;
    private VkFormat _swapchainFormat;
    private VkExtent2D _swapchainExtent;
    private VkImage[] _swapchainImages = [];
    private VkImageView[] _swapchainViews = [];
    private VkSemaphore[] _renderFinished = []; // One per swapchain image, as the spec recommends.
    private VkPipeline _pipeline;

    private int _frameIndex;
    private int _imageIndex;
    private bool _framebufferResized;

    /// <param name="window">A GLFW window created with <c>ClientApi = NoApi</c>.</param>
    /// <param name="playfield">The size of the game's coordinate space. It is letterboxed to fit the window.</param>
    public Renderer(GlfwWindow window, float2 playfield)
    {
        _window = window;
        _playfield = playfield;
        _window.OnFramebufferSize += (_, _) => _framebufferResized = true;

        _instance = CreateInstance();

        if (Glfw.CreateWindowSurface(_instance.VkHandle, window, out var surface) != 0)
            throw new Exception("Failed to create a Vulkan surface for the window.");
        _surface = new VkSurfaceKHR((nint)surface);

        _physicalDevice = PickPhysicalDevice(out var queueFamily);
        _device = CreateDevice(queueFamily);
        _queue = _device.GetQueue(queueFamily, 0);

        _commandPool = _device.CreateCommandPool(
            new VkCommandPoolCreateInfo
            {
                Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = queueFamily,
            },
            null
        );

        for (var i = 0; i < FramesInFlight; i++)
        {
            _frames[i] = new Frame
            {
                CommandBuffer = _device.AllocateCommandBuffer(
                    new VkCommandBufferAllocateInfo { CommandPool = _commandPool, Level = VkCommandBufferLevel.Primary }
                ),
                ImageAvailable = _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null),
                // Start signalled so the first wait in BeginFrame returns immediately.
                InFlight = _device.CreateFence(VkFenceCreateFlags.SignaledBit, null),
            };
        }

        var pushConstants = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit,
            Size = ByteSize.Of<QuadPushConstants>(),
        };
        _pipelineLayout = _device.CreatePipelineLayout([], [pushConstants], null);

        CreateSwapchain();
        _pipeline = CreatePipeline();
    }

    /// <summary>
    /// Waits for a free frame, acquires a swapchain image and starts recording.
    /// Returns false when nothing can be drawn this frame (for example while minimised).
    /// </summary>
    public bool BeginFrame(float4 clearColor)
    {
        if (_window.FramebufferSize.X == 0 || _window.FramebufferSize.Y == 0)
            return false;

        ref var frame = ref _frames[_frameIndex];
        _device.WaitForFences([frame.InFlight], true, nint.MaxValue);

        try
        {
            _device.AcquireNextImageKHR(_swapchain, nint.MaxValue, frame.ImageAvailable, default, out _imageIndex);
        }
        catch (VkResultError e) when (e.Result == VkResult.ErrorOutOfDateKHR)
        {
            RecreateSwapchain();
            return false;
        }

        // Only reset the fence once we know we will submit work that signals it again.
        _device.ResetFences([frame.InFlight]);

        var cmd = frame.CommandBuffer;
        cmd.Reset();
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);

        // The swapchain image starts in an unknown layout; make it writable as a colour attachment.
        TransitionImage(
            cmd,
            _swapchainImages[_imageIndex],
            VkImageLayout.Undefined,
            VkImageLayout.ColorAttachmentOptimal,
            VkPipelineStageFlags2.ColorAttachmentOutputBit,
            VkAccessFlags2.None,
            VkPipelineStageFlags2.ColorAttachmentOutputBit,
            VkAccessFlags2.ColorAttachmentWriteBit
        );

        var colorAttachment = new VkRenderingAttachmentInfo
        {
            ImageView = _swapchainViews[_imageIndex],
            ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearColorValue { Float32 = clearColor },
        };

        cmd.BeginRendering(
            new VkRenderingInfo
            {
                RenderArea = new VkRect2D { Extent = _swapchainExtent },
                LayerCount = 1,
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
            }
        );

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);

        // Fit the playfield inside the window without stretching it.
        var window = new float2(_swapchainExtent.Width, _swapchainExtent.Height);
        var scale = MathF.Min(window.X / _playfield.X, window.Y / _playfield.Y);
        var size = _playfield * scale;
        var offset = (window - size) * 0.5f;

        cmd.SetViewport(0, [new VkViewport { X = offset.X, Y = offset.Y, Width = size.X, Height = size.Y, MaxDepth = 1 }]);
        cmd.SetScissor(0, [new VkRect2D { Extent = _swapchainExtent }]);

        return true;
    }

    /// <summary>Draws a solid rectangle. Coordinates are playfield units with the origin at the top left.</summary>
    public void DrawRect(float2 position, float2 size, float4 color)
    {
        var push = new QuadPushConstants
        {
            Rect = new float4(position.X, position.Y, size.X, size.Y),
            Color = color,
            Playfield = _playfield,
        };

        var cmd = _frames[_frameIndex].CommandBuffer;
        cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.VertexBit, 0, new ReadOnlySpan<byte>(&push, sizeof(QuadPushConstants)));
        cmd.Draw(6, 1, 0, 0);
    }

    /// <summary>Finishes recording, submits the frame and presents it.</summary>
    public void EndFrame()
    {
        ref var frame = ref _frames[_frameIndex];
        var cmd = frame.CommandBuffer;

        cmd.EndRendering();

        // Hand the image over to the presentation engine.
        TransitionImage(
            cmd,
            _swapchainImages[_imageIndex],
            VkImageLayout.ColorAttachmentOptimal,
            VkImageLayout.PresentSrcKHR,
            VkPipelineStageFlags2.ColorAttachmentOutputBit,
            VkAccessFlags2.ColorAttachmentWriteBit,
            VkPipelineStageFlags2.None,
            VkAccessFlags2.None
        );

        cmd.End();

        var renderFinished = _renderFinished[_imageIndex];
        _queue.Submit(
            [frame.ImageAvailable],
            [VkPipelineStageFlags.ColorAttachmentOutputBit],
            [cmd],
            [renderFinished],
            frame.InFlight
        );

        var swapchain = _swapchain;
        var imageIndex = _imageIndex;
        var outOfDate = false;
        try
        {
            var result = _queue.PresentKHR(
                new VkPresentInfoKHR
                {
                    WaitSemaphoreCount = 1,
                    WaitSemaphores = &renderFinished,
                    SwapchainCount = 1,
                    Swapchains = &swapchain,
                    ImageIndices = &imageIndex,
                }
            );
            outOfDate = result == VkResult.SuboptimalKHR;
        }
        catch (VkResultError e) when (e.Result == VkResult.ErrorOutOfDateKHR)
        {
            outOfDate = true;
        }

        if (outOfDate || _framebufferResized)
            RecreateSwapchain();

        _frameIndex = (_frameIndex + 1) % FramesInFlight;
    }

    public void Dispose()
    {
        _device.WaitIdle();

        DestroySwapchain();
        _device.DestroyPipeline(_pipeline, null);
        _device.DestroyPipelineLayout(_pipelineLayout, null);

        foreach (var frame in _frames)
        {
            _device.DestroySemaphore(frame.ImageAvailable, null);
            _device.DestroyFence(frame.InFlight, null);
        }

        _device.DestroyCommandPool(_commandPool, null);
        _device.Dispose();
        _instance.DestroySurfaceKHR(_surface, null);
        _instance.Dispose();
    }

    private static Instance CreateInstance()
    {
        // GLFW knows which surface extensions this platform needs.
        var extensions = new HashSet<string>();
        var glfwExtensions = Glfw.GetRequiredInstanceExtensions(out var count);
        for (var i = 0; i < count; i++)
            extensions.Add(Marshal.PtrToStringUTF8((nint)glfwExtensions[i])!);
        VulkanHelpers.AddPlatformRequiredExtensions(extensions);

        // Validation layers catch API misuse during development. They come with the Vulkan SDK.
        var layers = new HashSet<string>();
#if DEBUG
        if (VulkanHelpers.Validation.IsSupported)
            VulkanHelpers.AddValidationLayers(layers);
#endif

        return Vulkan.CreateInstance(
            new Instance.CreateInfo
            {
                AppInfo = new VkApplicationInfo
                {
                    ApplicationName = "Dodge"u8.AsPointer(),
                    EngineName = "BRUTAL"u8.AsPointer(),
                    ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3,
                },
                Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
                EnabledExtensions = extensions,
                EnabledLayers = layers,
            }
        );
    }

    /// <summary>Picks a Vulkan 1.3 GPU with a queue that can both draw and present, preferring discrete GPUs.</summary>
    private PhysicalDevice PickPhysicalDevice(out int queueFamily)
    {
        PhysicalDevice? best = null;
        queueFamily = -1;

        foreach (var candidate in _instance.PhysicalDevices)
        {
            var properties = candidate.GetProperties();
            if (properties.ApiVersion < (int)VulkanHelpers.Api.VERSION_1_3)
                continue;
            if (!candidate.SupportsExtension(VK.KHR_SWAPCHAIN_EXTENSION_NAME))
                continue;

            var family = candidate.FindQueueFamily(VkQueueFlags.GraphicsBit);
            if (family < 0 || !candidate.GetSurfaceSupportKHR(family, _surface))
                continue;

            if (best == null || properties.DeviceType == VkPhysicalDeviceType.DiscreteGpu)
            {
                best = candidate;
                queueFamily = family;
            }
        }

        return best ?? throw new Exception("No GPU with Vulkan 1.3 and presentation support was found.");
    }

    private Device CreateDevice(int queueFamily)
    {
        var extensions = new HashSet<string> { VK.KHR_SWAPCHAIN_EXTENSION_NAME };
        VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(extensions, _physicalDevice);

        // Dynamic rendering and synchronization2 are core in 1.3 but must still be switched on.
        var features13 = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true, Synchronization2 = true };

        var priority = 1.0f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            QueuePriorities = &priority,
        };

        return _physicalDevice.CreateDevice(
            new Device.CreateInfo
            {
                Extensions = extensions,
                QueueCreateInfos = [queueInfo],
                Next = &features13,
            }
        );
    }

    private void CreateSwapchain()
    {
        var capabilities = _physicalDevice.GetSurfaceCapabilitiesKHR(_surface);

        // Prefer an sRGB format so colours written in linear space display correctly.
        Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[_physicalDevice.GetSurfaceFormatsKHR_Count(_surface)];
        _physicalDevice.GetSurfaceFormatsKHR_Write(_surface, formats);
        var format = formats[0];
        foreach (var candidate in formats)
        {
            if (candidate.Format == VkFormat.B8G8R8A8SRGB && candidate.ColorSpace == VkColorSpaceKHR.SRGBNonlinearKHR)
            {
                format = candidate;
                break;
            }
        }

        // A current extent of -1 means the window lets us choose, so use the framebuffer size.
        var extent = capabilities.CurrentExtent;
        if (extent.Width == -1)
        {
            var fb = _window.FramebufferSize;
            extent = new VkExtent2D
            {
                Width = Math.Clamp(fb.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Height = Math.Clamp(fb.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height),
            };
        }

        var imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0)
            imageCount = Math.Min(imageCount, capabilities.MaxImageCount);

        _swapchain = _device.CreateSwapchainKHR(
            new VkSwapchainCreateInfoKHR
            {
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = format.Format,
                ImageColorSpace = format.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = VkImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = VkSharingMode.Exclusive,
                PreTransform = capabilities.CurrentTransform,
                CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
                PresentMode = VkPresentModeKHR.FifoKHR, // V-sync. Always available.
                Clipped = true,
            },
            null
        );
        _swapchainFormat = format.Format;
        _swapchainExtent = extent;

        _swapchainImages = new VkImage[_device.GetSwapchainImagesKHR_Count(_swapchain)];
        _device.GetSwapchainImagesKHR_Write(_swapchain, _swapchainImages);

        _swapchainViews = new VkImageView[_swapchainImages.Length];
        _renderFinished = new VkSemaphore[_swapchainImages.Length];
        for (var i = 0; i < _swapchainImages.Length; i++)
        {
            _swapchainViews[i] = _device.CreateImageView(
                new VkImageViewCreateInfo
                {
                    Image = _swapchainImages[i],
                    ViewType = VkImageViewType._2D,
                    Format = _swapchainFormat,
                    SubresourceRange = ColorRange,
                },
                null
            );
            _renderFinished[i] = _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
        }
    }

    private void DestroySwapchain()
    {
        foreach (var semaphore in _renderFinished)
            _device.DestroySemaphore(semaphore, null);
        foreach (var view in _swapchainViews)
            _device.DestroyImageView(view, null);
        _device.DestroySwapchainKHR(_swapchain, null);
    }

    private void RecreateSwapchain()
    {
        _framebufferResized = false;

        // A minimised window has a zero-sized framebuffer; wait until it is restored.
        while (_window.FramebufferSize.X == 0 || _window.FramebufferSize.Y == 0)
            Glfw.WaitEvents();

        _device.WaitIdle();
        var oldFormat = _swapchainFormat;
        DestroySwapchain();
        CreateSwapchain();

        // The pipeline is built against the swapchain format, so rebuild it if that changed.
        if (_swapchainFormat != oldFormat)
        {
            _device.DestroyPipeline(_pipeline, null);
            _pipeline = CreatePipeline();
        }
    }

    private VkPipeline CreatePipeline()
    {
        var vertexShader = CompileShader("Shaders/quad.vert", ShaderKind.VertexShader);
        var fragmentShader = CompileShader("Shaders/quad.frag", ShaderKind.FragmentShader);

        var entryPoint = "main"u8.AsPointer();
        Span<VkPipelineShaderStageCreateInfo> stages =
        [
            new() { Stage = VkShaderStageFlags.VertexBit, Module = vertexShader, Name = entryPoint },
            new() { Stage = VkShaderStageFlags.FragmentBit, Module = fragmentShader, Name = entryPoint },
        ];

        // No vertex buffers: the vertex shader builds the quad from gl_VertexIndex.
        var vertexInput = new VkPipelineVertexInputStateCreateInfo();
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { Topology = VkPrimitiveTopology.TriangleList };
        var viewport = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, ScissorCount = 1 };
        var raster = new VkPipelineRasterizationStateCreateInfo
        {
            PolygonMode = VkPolygonMode.Fill,
            CullMode = VkCullModeFlags.None,
            LineWidth = 1.0f,
        };
        var multisample = new VkPipelineMultisampleStateCreateInfo { RasterizationSamples = VkSampleCountFlags._1Bit };
        var blendAttachment = new VkPipelineColorBlendAttachmentState
        {
            ColorWriteMask =
                VkColorComponentFlags.RBit | VkColorComponentFlags.GBit | VkColorComponentFlags.BBit | VkColorComponentFlags.ABit,
        };
        var blend = new VkPipelineColorBlendStateCreateInfo { AttachmentCount = 1, Attachments = &blendAttachment };

        // Viewport and scissor are set per frame, so window resizes do not need a new pipeline.
        Span<VkDynamicState> dynamicStates = [VkDynamicState.Viewport, VkDynamicState.Scissor];
        var dynamicState = new VkPipelineDynamicStateCreateInfo
        {
            DynamicStateCount = dynamicStates.Length,
            DynamicStates = dynamicStates.AsPointer(),
        };

        // With dynamic rendering, the pipeline names its attachment formats instead of a render pass.
        var colorFormat = _swapchainFormat;
        var rendering = new VkPipelineRenderingCreateInfo { ColorAttachmentCount = 1, ColorAttachmentFormats = &colorFormat };

        VkPipeline pipeline;
        _device.CreateGraphicsPipelines(
            default,
            [
                new VkGraphicsPipelineCreateInfo
                {
                    Next = &rendering,
                    StageCount = stages.Length,
                    Stages = stages.AsPointer(),
                    VertexInputState = &vertexInput,
                    InputAssemblyState = &inputAssembly,
                    ViewportState = &viewport,
                    RasterizationState = &raster,
                    MultisampleState = &multisample,
                    ColorBlendState = &blend,
                    DynamicState = &dynamicState,
                    Layout = _pipelineLayout,
                },
            ],
            null,
            new ReadOnlySpan<VkPipeline>(&pipeline, 1)
        );

        // Shader modules are only needed while the pipeline is created.
        _device.DestroyShaderModule(vertexShader, null);
        _device.DestroyShaderModule(fragmentShader, null);
        return pipeline;
    }

    private VkShaderModule CompileShader(string relativePath, ShaderKind kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, relativePath);

        using var compiler = ShaderC.CreateCompiler();
        using var result = compiler.CompileFile(path, kind);
        var spirv = new ReadOnlySpan<byte>(result.GetBytes(), (int)result.GetLength());

        return ShaderModuleExtensions.FromSpirv(_device, spirv, null);
    }

    private static readonly VkImageSubresourceRange ColorRange = new()
    {
        AspectMask = VkImageAspectFlags.ColorBit,
        LevelCount = 1,
        LayerCount = 1,
    };

    private static void TransitionImage(
        CommandBuffer cmd,
        VkImage image,
        VkImageLayout oldLayout,
        VkImageLayout newLayout,
        VkPipelineStageFlags2 srcStage,
        VkAccessFlags2 srcAccess,
        VkPipelineStageFlags2 dstStage,
        VkAccessFlags2 dstAccess
    )
    {
        var barrier = new VkImageMemoryBarrier2
        {
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
            Image = image,
            SubresourceRange = ColorRange,
        };

        cmd.PipelineBarrier2(new VkDependencyInfo { ImageMemoryBarrierCount = 1, ImageMemoryBarriers = &barrier });
    }
}
