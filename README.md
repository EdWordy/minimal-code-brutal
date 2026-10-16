# minimal-code-brutal

A minimal, complete game built on the [BRUTAL](https://github.com/ahwootech/BRUTAL) C# framework, plus short guides for anyone starting their own BRUTAL game.

The game is **Dodge**: you move a square around and avoid falling blocks for as long as you can. It is deliberately small, about 800 lines of C# across three files, and most of that is the Vulkan renderer. It is still a real game rather than a stub. It covers the pieces every BRUTAL game has to provide itself: a window, input, a fixed-timestep game loop, a Vulkan renderer with shaders, and a clean shutdown. Everything you will see is the code you would write on day one of your own project.

## Quick start

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a GPU with Vulkan 1.3 drivers, on Windows or Linux. Install the [Vulkan SDK](https://vulkan.lunarg.com/) as well if you want validation layers in Debug builds, which you do.

```sh
git clone https://github.com/EdWordy/minimal-code-brutal.git
cd minimal-code-brutal
dotnet run --project src/Dodge
```

Move with **WASD** or the **arrow keys**, press **R** to restart after a hit, and press **Esc** to quit. Your survival time is shown in the window title and as the yellow bar along the top.

## What is in here

| Path | What it is |
| --- | --- |
| `src/Dodge/Program.cs` | Entry point: creates the window and runs the fixed-timestep loop |
| `src/Dodge/Game.cs` | Game state and rules, with no knowledge of windows or Vulkan |
| `src/Dodge/Renderer.cs` | A small Vulkan 1.3 renderer that draws solid rectangles |
| `src/Dodge/Shaders/` | GLSL shaders, compiled to SPIR-V at startup |
| `src/Dodge/NOTICE.txt` | The BRUTAL notice that the licence requires every product to ship |
| `docs/` | The guides below |

## Guides

Read the guides in order. Each one builds on the last, starting with the big picture and ending with the details of shipping.

1. [What BRUTAL is](docs/1-what-is-brutal.md): the framework's philosophy, what it gives you, and what you must build yourself.
2. [Project setup](docs/2-project-setup.md): creating a project, choosing packages, and how native libraries get to your game.
3. [Window, input and the game loop](docs/3-game-loop.md): GLFW, a fixed timestep, and keeping game code independent of the platform.
4. [Rendering with Vulkan](docs/4-rendering-with-vulkan.md): a walkthrough of `Renderer.cs` and the conventions of BRUTAL's Vulkan bindings.
5. [Shipping your game](docs/5-shipping.md): publishing builds and meeting the licence's attribution terms.

## Licence

This repository's own code is under the MIT licence in [LICENSE](LICENSE). BRUTAL itself is under the BRUTAL License v1.0, which is free for commercial use but asks for attribution. A game you distribute must credit BRUTAL, show its logo before gameplay, and ship `NOTICE.txt`. [Shipping your game](docs/5-shipping.md) explains what that means in practice.
