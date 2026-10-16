# 1. What BRUTAL is

BRUTAL is a C# framework for making games, or for making your own game engine. It is not an engine itself. There is no editor, no scene graph and no renderer, and the maintainers have said they will not add them. Kitten Space Agency is built directly on BRUTAL, and Ludeon built their Waylith engine on top of it to move RimWorld off Unity.

## What you get

At its core, BRUTAL is an interop layer that gives C# direct access to native APIs and libraries. Around that it provides low-level building blocks: maths and vector types, unmanaged memory, allocation-free strings and collections, a job system, logging and localization. It also ships bindings for the native libraries games usually need, such as Vulkan, GLFW, SDL, Dear ImGui, FMOD, RakNet, Jolt and Box2D.

Each library is a separate NuGet package, so you only reference what you use. Dodge uses four: `Glfw` for the window, `Vulkan` and `Vulkan.Abstractions` for rendering, and `ShaderC` for shader compilation. The packages pull in BRUTAL's core libraries for you.

## What you build yourself

BRUTAL deliberately leaves the architecture to you. You decide how the game loop runs, how game objects are stored, how the renderer is structured, and which third-party packages you bring in. This is the "brutal" part: on day one you write code that an engine would have given you for free.

The payoff is control. Nothing runs that you did not ask for, and when you do something unusual there is no engine to fight. It also means you have to know what you want before you start.

## How a BRUTAL game is shaped

Most BRUTAL games end up with the same layers, even if each team builds them differently. Dodge has one file per layer, which makes the split easy to see.

```
 Game rules and state          Game.cs        (plain C#, no native APIs)
 Your engine systems           Renderer.cs    (your renderer, audio, physics glue...)
 BRUTAL bindings and helpers   NuGet packages (Brutal.Vulkan, Brutal.Glfw, ...)
 Native libraries and drivers  Vulkan, GLFW, shaderc
```

`Program.cs` sits beside these layers and wires them together. Keeping game rules free of native types is the most useful habit to form early, because it lets you test gameplay and replace systems without touching the rest.

## The performance mindset

BRUTAL is built around avoiding allocations and unnecessary overhead. It uses unmanaged memory and unsafe code where they make sense, and its bindings take spans and pointers rather than allocating wrapper objects. Your code should follow the same habits in hot paths. Dodge's frame loop, for example, allocates nothing: hazards live in a fixed array, push constants live on the stack, and the window title is rebuilt only once a second.

Next: [Project setup](2-project-setup.md).
