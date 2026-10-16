using Brutal.Numerics;

namespace Dodge;

/// <summary>A snapshot of the input the game cares about, read once per update.</summary>
public readonly record struct GameInput(float2 Move, bool Restart);

/// <summary>
/// All game state and rules. It knows nothing about windows or Vulkan, which keeps it easy to test
/// and lets you swap the renderer without touching gameplay.
/// </summary>
public sealed class Game
{
    public static readonly float2 Playfield = new(1280, 720);

    private const float PlayerSpeed = 520f;
    private const int MaxHazards = 64;

    private static readonly float2 PlayerSize = new(36, 36);
    private static readonly float4 BackgroundColor = new(0.02f, 0.02f, 0.03f, 1);
    private static readonly float4 PlayerColor = new(0.95f, 0.95f, 0.95f, 1);
    private static readonly float4 HazardColor = new(0.9f, 0.15f, 0.1f, 1);
    private static readonly float4 TimerColor = new(0.9f, 0.7f, 0.1f, 1);

    private struct Hazard
    {
        public bool Active;
        public float2 Position;
        public float2 Size;
        public float Speed;
    }

    // A fixed pool: spawning and despawning never allocates during play.
    private readonly Hazard[] _hazards = new Hazard[MaxHazards];
    private readonly Random _random = new();

    private float2 _player;
    private float _spawnTimer;

    /// <summary>Seconds survived in the current run.</summary>
    public float Time { get; private set; }

    public bool IsGameOver { get; private set; }

    public Game() => Reset();

    public void Reset()
    {
        Array.Clear(_hazards);
        _player = new float2((Playfield.X - PlayerSize.X) * 0.5f, Playfield.Y - PlayerSize.Y - 24);
        _spawnTimer = 0;
        Time = 0;
        IsGameOver = false;
    }

    /// <summary>Advances the simulation by a fixed step.</summary>
    public void Update(float dt, in GameInput input)
    {
        if (IsGameOver)
        {
            if (input.Restart)
                Reset();
            return;
        }

        Time += dt;

        _player += input.Move * PlayerSpeed * dt;
        _player = float2.Clamp(_player, float2.Zero, Playfield - PlayerSize);

        // Spawn faster as the run goes on.
        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            Spawn();
            _spawnTimer = MathF.Max(0.08f, 0.6f - Time * 0.015f);
        }

        for (var i = 0; i < _hazards.Length; i++)
        {
            ref var hazard = ref _hazards[i];
            if (!hazard.Active)
                continue;

            hazard.Position.Y += hazard.Speed * dt;

            if (hazard.Position.Y > Playfield.Y)
                hazard.Active = false;
            else if (Overlaps(_player, PlayerSize, hazard.Position, hazard.Size))
                IsGameOver = true;
        }
    }

    public void Draw(Renderer renderer)
    {
        renderer.DrawRect(float2.Zero, Playfield, BackgroundColor);

        foreach (ref readonly var hazard in _hazards.AsSpan())
        {
            if (hazard.Active)
                renderer.DrawRect(hazard.Position, hazard.Size, HazardColor);
        }

        // No text rendering in a minimal example: the bar along the top grows with survival time.
        var timerWidth = MathF.Min(Time / 60f, 1f) * Playfield.X;
        renderer.DrawRect(float2.Zero, new float2(timerWidth, 8), TimerColor);

        renderer.DrawRect(_player, PlayerSize, IsGameOver ? HazardColor : PlayerColor);
    }

    private void Spawn()
    {
        for (var i = 0; i < _hazards.Length; i++)
        {
            ref var hazard = ref _hazards[i];
            if (hazard.Active)
                continue;

            var size = 24 + _random.NextSingle() * 56;
            hazard = new Hazard
            {
                Active = true,
                Size = new float2(size, size),
                Position = new float2(_random.NextSingle() * (Playfield.X - size), -size),
                Speed = 220 + _random.NextSingle() * 200 + Time * 6,
            };
            return;
        }
    }

    private static bool Overlaps(float2 aPos, float2 aSize, float2 bPos, float2 bSize) =>
        aPos.X < bPos.X + bSize.X && bPos.X < aPos.X + aSize.X && aPos.Y < bPos.Y + bSize.Y && bPos.Y < aPos.Y + aSize.Y;
}
