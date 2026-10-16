using Brutal.GlfwApi;
using Brutal.Numerics;
using Dodge;

// Dodge: move with WASD or the arrow keys, avoid the red blocks, press R to restart, Esc to quit.

if (!Glfw.Init())
    throw new Exception("Failed to initialize GLFW");

// We render with Vulkan, so ask GLFW not to create an OpenGL context.
Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
Glfw.WindowHint(GlfwWindowHint.Resizable, 1);

var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "Dodge - BRUTAL", Size = new int2(1280, 720) });

var renderer = new Renderer(window, Game.Playfield);
var game = new Game();

// Fixed timestep: the simulation always advances in 1/60 s steps, however fast frames are drawn.
// This keeps gameplay deterministic and independent of the monitor's refresh rate.
const double step = 1.0 / 60.0;
var accumulator = 0.0;
var previous = Glfw.Time;
var lastTitleSecond = -1;

while (!window.ShouldClose)
{
    Glfw.PollEvents();

    if (window.GetKey(GlfwKey.Escape) == GlfwKeyAction.Press)
        window.ShouldClose = true;

    var now = Glfw.Time;
    accumulator += Math.Min(now - previous, 0.25); // Clamp so a long stall cannot cause a spiral of updates.
    previous = now;

    while (accumulator >= step)
    {
        game.Update((float)step, ReadInput(window));
        accumulator -= step;
    }

    if (renderer.BeginFrame(new float4(0, 0, 0, 1)))
    {
        game.Draw(renderer);
        renderer.EndFrame();
    }

    // Report the score in the title once per second, so we do not build a string every frame.
    var second = (int)game.Time;
    if (second != lastTitleSecond)
    {
        lastTitleSecond = second;
        window.Title = game.IsGameOver ? $"Dodge - survived {second}s - press R" : $"Dodge - {second}s";
    }
}

renderer.Dispose();
window.Dispose();
Glfw.Terminate();

static GameInput ReadInput(GlfwWindow window)
{
    bool Down(GlfwKey key) => window.GetKey(key) != GlfwKeyAction.Release;

    var move = float2.Zero;
    if (Down(GlfwKey.A) || Down(GlfwKey.Left)) move.X -= 1;
    if (Down(GlfwKey.D) || Down(GlfwKey.Right)) move.X += 1;
    if (Down(GlfwKey.W) || Down(GlfwKey.Up)) move.Y -= 1;
    if (Down(GlfwKey.S) || Down(GlfwKey.Down)) move.Y += 1;

    if (move != float2.Zero)
        move = float2.Normalize(move);

    return new GameInput(move, Down(GlfwKey.R));
}
