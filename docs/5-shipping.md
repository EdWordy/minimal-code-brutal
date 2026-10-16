# 5. Shipping your game

Shipping a BRUTAL game means producing a self-contained build and meeting the licence's attribution terms. Both are short jobs, but both are easy to forget until release week.

## Publishing a build

Publish once for each platform you support. A self-contained publish bundles the .NET runtime, so players do not need to install .NET.

```sh
dotnet publish src/Dodge -c Release -r win-x64 --self-contained -o publish/win-x64
dotnet publish src/Dodge -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

The output folder contains everything the game needs. That includes BRUTAL's native libraries (GLFW and shaderc for Dodge) next to the executable, the shader sources, and `NOTICE.txt`. Players still need a Vulkan 1.3 driver, which comes with any current GPU driver. Always test a published build on a clean machine, because a missing native library only shows up there.

## Meeting the licence

BRUTAL is free, including for commercial games, with no royalties. In return, the [BRUTAL License](https://github.com/ahwootech/BRUTAL/blob/main/LICENSE.txt) asks any product you distribute to do three things.

1. **Credit BRUTAL.** Put the copyright line, `BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.`, in your credits screen or equivalent (section 2.1).
2. **Show the logo.** Display the BRUTAL logo, available from ahwoo.com/brutal-branding, before gameplay starts. Show it unaltered except for scaling, long enough to read, and no less prominently than other logos (section 2.2).
3. **Ship `NOTICE.txt`.** Include BRUTAL's `NOTICE.txt` alongside every copy of the game (section 2.3).

Dodge handles the third item in its `.csproj`, which copies [`NOTICE.txt`](../src/Dodge/NOTICE.txt) to every build. It does not have a credits screen or a logo splash, because drawing an image needs texture support that would double the renderer's size. Add both before you distribute a game built from it.

If these terms do not suit your project, Ahwoo offers a separate commercial licence. They also waive the logo requirement for non-profit projects and where showing it would work against the application's purpose. Third-party libraries that BRUTAL redistributes keep their own licences, which are listed in the `LICENSES` folder of each package.

## Getting help

The [BRUTAL repository](https://github.com/ahwootech/BRUTAL) is the definitive reference, and reading its source is often the fastest way to answer a question. For everything else, ask on the [Guides and Help forum](https://forums.ahwoo.com/forums/brutal/guides-and-help/) or the [Discord](https://discord.gg/MEuNTYbAh8).
