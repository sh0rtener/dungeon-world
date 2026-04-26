using MicroEcs;
using MicroEcs.Dungeon;

// ============================================================================
// Dungeon World, ECS edition.
//
// The original code (https://github.com/sh0rtener/dungeon-world) had three classes
// doing all the work: Player, Field, and GameCycle. The same game in MicroEcs is:
//
//   - components for "what is a thing" (Position, Renderable, PlayerTag, ...)
//   - systems for "what happens each tick" (Input, Movement, Goal, Render)
//   - a generator that materialises a procedural map as a swarm of entities
//
// The driver below is intentionally tiny; almost all of it is the input thread and
// terminal teardown. Everything game-shaped lives in components and systems.
// ============================================================================

// ---- 1. Build the world and seed it from the procedural generator ----
using var world = new World();

// Pass an int seed for reproducible runs; pass nothing for a fresh dungeon every launch.
var generator = new DungeonGenerator(width: 80, height: 36);
generator.Generate(world);

// ---- 2. Wire up systems ----
var inputQueue = new Queue<ConsoleKey>();
var inputSystem = new InputSystem(inputQueue);
var goalSystem = new GoalSystem();
var healthSystem = new HealthSystem();

var systems = new SystemGroup("Main");
systems.Add(inputSystem);
systems.Add(new PlayerShootSystem());
systems.Add(new EnemyShootSystem());
systems.Add(new BulletSystem());
systems.Add(new MovementSystem());
systems.Add(healthSystem);
systems.Add(goalSystem);
systems.Add(new RenderSystem());

// ---- 3. Console plumbing: a background reader pumps key events into the queue ----
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
Console.CursorVisible = false;
var originalFg = Console.ForegroundColor;

var inputTask = Task.Run(() =>
{
    while (!cts.IsCancellationRequested)
    {
        if (Console.KeyAvailable)
        {
            var k = Console.ReadKey(intercept: true).Key;
            lock (inputQueue)
            {
                if (inputQueue.Count < 16) inputQueue.Enqueue(k);
            }
        }
        else
        {
            Thread.Sleep(5);
        }
    }
}, cts.Token);

// ---- 4. Game loop. Frame-paced, not tick-paced — input drives state changes. ----
const int frameMs = 33;            // ~30 FPS cap; the loop is mostly idle either way
const float dt = frameMs / 1000f;

try
{
    while (!cts.IsCancellationRequested && !goalSystem.Reached && !inputSystem.QuitRequested && !healthSystem.PlayerDied)
    {
        systems.Update(world, dt);
        Thread.Sleep(frameMs);
    }
}
finally
{
    cts.Cancel();
    try { inputTask.Wait(200); } catch { /* the task was cancelled, that's the point */ }

    Console.ResetColor();
    Console.ForegroundColor = originalFg;
    Console.CursorVisible = true;
    Console.SetCursorPosition(0, Math.Max(0, Console.WindowHeight - 1));
    Console.WriteLine();

    if (goalSystem.Reached)
        Console.WriteLine("You reached the exit. Well done.");
    else if (healthSystem.PlayerDied)
        Console.WriteLine("You died.");
    else if (inputSystem.QuitRequested)
        Console.WriteLine("Bye.");
    else
        Console.WriteLine("Interrupted.");
}
