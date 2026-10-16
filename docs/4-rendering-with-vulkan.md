# 4. Rendering with Vulkan

BRUTAL has no renderer. It gives you Vulkan, and you build the renderer your game needs on top. This guide walks through [`Renderer.cs`](../src/Dodge/Renderer.cs), the smallest renderer that is still worth building on. It clears the screen and draws solid rectangles, and it handles everything a real renderer must, including window resizes. The code targets Vulkan 1.3 and uses *dynamic rendering*, which removes render pass and framebuffer objects and makes the setup much shorter.

## How the bindings are shaped

BRUTAL's Vulkan API is generated from the Vulkan spec, so it maps one-to-one onto the C API with a few C# conveniences. A handful of conventions cover almost everything.

- **Wrapper classes** for the top-level objects: `Instance`, `PhysicalDevice`, `Device`, `Queue` and `CommandBuffer`. Each one carries its own function table, so calls are direct, with no global lookup.
- **Extension methods** with the `vk` and `vkCmd` prefixes removed. `vkCreateImageView(device, ...)` becomes `device.CreateImageView(...)`, and `vkCmdDraw(cmd, ...)` becomes `cmd.Draw(...)`.
- **Structs with their type tag pre-filled.** You never set `sType`; `new VkRenderingInfo { ... }` is already correct. Pointer fields such as `ColorAttachments` take raw pointers, which is why rendering code is `unsafe`.
- **Spans instead of count-and-pointer pairs.** `device.WaitForFences([fence], true, timeout)` takes a collection expression. Queries that return arrays come in pairs: `_Count` returns the size, and `_Write` fills a span you supply, often a `stackalloc`.
- **Errors become exceptions.** A failing `VkResult` throws a `VkResultError` that carries the result code, so you do not check return values by hand. Non-error results such as `SuboptimalKHR` are returned normally.

`Brutal.Vulkan.Abstractions` adds helpers on top, such as `VulkanHelpers` for common extensions and validation layers, `FindQueueFamily`, `CreateFence` and `Queue.Submit` overloads that take spans. Use them where they help and drop to the raw API where they do not.

## Setup, step by step

The constructor creates Vulkan objects in dependency order, and `Dispose` destroys them in reverse.

1. **Instance.** GLFW reports the surface extensions the platform needs, and Debug builds add the Khronos validation layer if it is installed.
2. **Surface.** `Glfw.CreateWindowSurface` connects the window to Vulkan.
3. **Physical device.** Dodge picks a GPU that supports Vulkan 1.3 and the swapchain extension, and has a queue family that can both draw and present to the surface. It prefers discrete GPUs.
4. **Device and queue.** Dynamic rendering and `synchronization2` are core in Vulkan 1.3, but you still have to enable them. Dodge does this by chaining `VkPhysicalDeviceVulkan13Features` through `Next`.
5. **Per-frame objects.** Each of the two frames in flight gets a command buffer, an "image available" semaphore and a fence. Each swapchain image gets a "render finished" semaphore, because the spec does not allow reusing it before the presentation engine is done with it.
6. **Swapchain.** Dodge prefers an sRGB format and uses FIFO presentation, which is v-sync and is always available.
7. **Pipeline.** The shaders are compiled from GLSL with `Brutal.ShaderC`. With dynamic rendering, the pipeline names its colour format through `VkPipelineRenderingCreateInfo` instead of a render pass.

## Drawing a frame

A frame has three calls, which keeps the game's draw code simple.

```csharp
if (renderer.BeginFrame(clearColor))
{
    renderer.DrawRect(position, size, color);
    renderer.EndFrame();
}
```

`BeginFrame` waits on the frame's fence so the CPU never gets more than two frames ahead. It then acquires a swapchain image, moves the image into a drawable layout with a pipeline barrier, and begins rendering with a clear. It also sets a viewport that letterboxes the 1280×720 playfield to fit any window shape.

`DrawRect` uses no vertex buffer. The vertex shader builds a quad from `gl_VertexIndex`, and each rectangle's position, size and colour arrive as push constants. One push and one six-vertex draw per rectangle is fine for a few hundred shapes. A larger game would batch thousands into a single buffer.

`EndFrame` ends rendering, moves the image to the present layout, submits, and presents. If the swapchain is out of date, usually after a resize, it is rebuilt. The bindings report "out of date" as a `VkResultError`, so the renderer catches that one result code and lets every other error through.

## Colour

The swapchain uses an sRGB format, so colours written by shaders are treated as linear and gamma-encoded on output. That is correct for lighting maths, but it means a value like `0.5` looks brighter than you might expect. Pick colours in linear space, or convert them from sRGB before passing them in.

## Where to go next

The renderer's public surface is deliberately small, so you can grow it without changing the game. Typical next steps, in roughly the order a game needs them:

- **Vertex and instance buffers** through `Brutal.Vulkan.Vma`, to draw many shapes in one call.
- **Textures** loaded with `Brutal.Stb` or `Brutal.Ktx`, plus descriptor sets and samplers to read them.
- **Precompiled shaders.** Compile to SPIR-V at build time and ship the `.spv` files, so players do not need shaderc at runtime.
- **Debug UI** with `Brutal.ImGui`, whose Vulkan backend is also a good reference for idiomatic BRUTAL rendering code.

Next: [Shipping your game](5-shipping.md).
