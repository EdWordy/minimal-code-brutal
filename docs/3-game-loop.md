# 3. Window, input and the game loop

Every BRUTAL game owns its main loop. No engine calls your `Update` method, so you decide when to poll input, how often to simulate, and when to draw. This guide follows [`Program.cs`](../src/Dodge/Program.cs) and [`Game.cs`](../src/Dodge/Game.cs).

## Creating the window

`Brutal.Glfw` wraps GLFW in a thin C# API. You initialise the library, set window hints, and create a `GlfwWindow`. Because Dodge renders with Vulkan, it tells GLFW not to create an OpenGL context by setting `ClientApi` to `0`, which is GLFW's "no API" value.

```csharp
if (!Glfw.Init())
    throw new Exception("Failed to initialize GLFW");

Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "Dodge", Size = new int2(1280, 720) });
```

The window exposes its state as properties, such as `ShouldClose`, `FramebufferSize` and `Title`. It raises C# events such as `OnKey` and `OnFramebufferSize`. Call `Glfw.PollEvents()` once per frame, or none of these update.

## Reading input

GLFW supports two styles of input. Callbacks like `window.OnKey` report each press and release as it happens, which suits text entry and one-off actions. Polling with `window.GetKey(key)` asks whether a key is down right now, which suits continuous movement.

Dodge polls. `ReadInput` turns the keyboard into a small `GameInput` struct once per simulation step. The game sees only that struct and never sees GLFW, so you could drive it from a gamepad, a replay file or a test without changing `Game.cs`.

## The fixed timestep

Dodge simulates in fixed steps of 1/60 of a second, however fast it draws. Each frame adds the elapsed real time to an accumulator, then runs as many fixed updates as fit. The game behaves the same on a 60 Hz laptop and a 240 Hz monitor, and physics and collisions stay stable.

```csharp
accumulator += Math.Min(now - previous, 0.25);
while (accumulator >= step)
{
    game.Update((float)step, ReadInput(window));
    accumulator -= step;
}
```

The clamp to 0.25 seconds matters. Without it, a long stall such as dragging the window could queue hundreds of updates, and the game would freeze while it catches up. A natural next step is to interpolate rendering between the last two states using `accumulator / step`, which removes the small stutter a fixed step can cause.

## Keeping game code clean

`Game` holds all state and rules and depends only on `Brutal.Numerics`. It updates from `GameInput` and draws through a small `DrawRect` call on the renderer. This keeps the native, unsafe and platform-specific code in one place and the gameplay code in another.

The same file shows the allocation habits from guide 1. Hazards live in a fixed-size array of structs and are reused rather than created and destroyed. That means play never triggers the garbage collector, which is the main source of hitches in C# games.

## Shutting down

Native resources are not managed by the garbage collector, so release them yourself and in reverse order of creation. Dodge disposes the renderer, then the window, then terminates GLFW. The renderer waits for the GPU to go idle before it destroys anything.

Next: [Rendering with Vulkan](4-rendering-with-vulkan.md).
