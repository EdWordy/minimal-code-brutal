# 2. Project setup

A BRUTAL game is an ordinary .NET console project with a few NuGet packages. There is no special project template or build tool, so everything you already know about `dotnet` applies.

## Prerequisites

You need the .NET 10 SDK and a GPU with Vulkan 1.3 drivers, on Windows or Linux. Install the Vulkan SDK too, because it provides the validation layers that catch API mistakes during development. Without them, Vulkan errors usually show up as crashes or a black window with no explanation.

## Creating a project

Start from a console project and add the packages you need. The package IDs all start with `Ahwoo.Brutal`, and the full list is on the [Ahwoo NuGet profile](https://www.nuget.org/profiles/Ahwoo).

```sh
dotnet new console -n MyGame
cd MyGame
dotnet add package Ahwoo.Brutal.Glfw
dotnet add package Ahwoo.Brutal.Vulkan
dotnet add package Ahwoo.Brutal.Vulkan.Abstractions
dotnet add package Ahwoo.Brutal.ShaderC
```

Then turn on unsafe code in the `.csproj`. BRUTAL passes pointers to native APIs directly, so you will write `unsafe` blocks as a matter of course, especially in rendering code.

```xml
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

[`src/Dodge/Dodge.csproj`](../src/Dodge/Dodge.csproj) is a complete example. It also copies the shader sources and `NOTICE.txt` to the output folder, because the game reads the shaders at runtime and the licence requires the notice.

## Choosing packages

Pick packages by the job they do and add more as the game grows. For a window and input, use `Glfw` or `Sdl`. For rendering, use `Vulkan` and `Vulkan.Abstractions`, then add `Vulkan.Vma` once you allocate buffers and images. For debug tools, add `ImGui` and `ImPlot`. Physics comes from `Jolt` (3D) or `Box2D` (2D), and audio from `Fmod` or `OpenAl`.

BRUTAL does not restrict you to its own packages. Any NuGet package works alongside it, so use whatever suits your game.

## Native libraries

Packages that wrap a native library ship that library for Windows x64, Linux x64 and macOS arm64. You do not install GLFW or shaderc yourself. When you build, .NET copies them into a `runtimes/<platform>/native/` folder, and a platform-specific publish places them next to the executable.

The Vulkan loader is the exception. BRUTAL loads the system's loader (`vulkan-1.dll` on Windows, `libvulkan.so` on Linux), which comes with the GPU driver. Players therefore need up-to-date drivers, but no extra install.

## Building and running

Build and run with the normal commands. Debug builds of Dodge enable validation layers when the Vulkan SDK is installed, so keep a terminal open and read anything it prints.

```sh
dotnet run --project src/Dodge              # Debug, with validation
dotnet run --project src/Dodge -c Release   # Release, without
```

Next: [Window, input and the game loop](3-game-loop.md).
