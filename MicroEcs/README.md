# MicroEcs

A small, fast, archetype-based **Entity Component System** for **.NET 10 / C# 14**.

Written from scratch in pure C# with no third-party dependencies, no `unsafe` blocks, and no source generators. Designed to be small enough to read in one sitting and fast enough to be useful for real workloads — game logic, simulations, in-memory data processing.

---

## Why ECS?

Object-oriented game code tends toward two failure modes: deep inheritance hierarchies (`Enemy : Character : GameObject : ...`) that become rigid as features accumulate, and pointer-chasing memory layouts that thrash the CPU cache as soon as you have a few thousand active objects.

ECS attacks both problems at once:

- **Entities** are bare integer ids. They have no behaviour and no fields of their own.
- **Components** are pure data structs (`struct Position { float X, Y; }`). No methods, no inheritance.
- **Systems** are stateless functions that query for entities matching a component combination and transform their data.

That separation lets you store all `Position` components contiguously in memory, all `Velocity` components contiguously in memory, and so on. Systems then iterate those arrays linearly — exactly the access pattern modern CPUs are built to make fast.

## Architecture

MicroEcs uses the **archetype + chunks** layout pioneered by Unity DOTS and refined by libraries like Flecs and Arch.

```
World
 ├── Archetype (Position, Velocity)
 │     ├── Chunk #0 — [E0, E1, ..., E255]
 │     │     ├── column<Position>: [P0, P1, ..., P255]
 │     │     └── column<Velocity>: [V0, V1, ..., V255]
 │     └── Chunk #1 — [E256, ...]
 ├── Archetype (Position, Velocity, Health)
 │     └── Chunk #0 — ...
 └── ...
```

- An **archetype** = one unique combination of component types. Every entity belongs to exactly one archetype at a time.
- Each archetype owns a list of fixed-capacity **chunks** (256 entities by default). Inside a chunk, components are stored as **Structure of Arrays**: one column per component type.
- Adding or removing a component **moves** the entity to a different archetype — its data is copied into a new chunk, and the old slot is filled by swapping in the last entity of the old chunk.

Querying for `(Position, Velocity)` walks just the chunks of archetypes whose signature is a superset of `{Position, Velocity}`. Inside each chunk, iteration is linear over packed arrays — exactly what the cache wants.

## Quick start

```csharp
using MicroEcs;

// 1. Define components as structs.
public record struct Position(float X, float Y);
public record struct Velocity(float X, float Y);
public struct PlayerTag : ITag { } // zero-sized, just a filter

// 2. Create a world and some entities.
using var world = new World();
var player = world.Create(new Position(0, 0), new Velocity(1, 0), new PlayerTag());

for (int i = 0; i < 1000; i++)
    world.Create(new Position(i, i), new Velocity(0, 1));

// 3. Build a query.
var query = new QueryDescription()
    .WithAll<Position, Velocity>()
    .WithNone<PlayerTag>();

// 4. Iterate. The delegate gets a `ref` to each component so it can mutate in place.
const float dt = 1f / 60f;
world.Query(query).ForEach<Position, Velocity>((ref Position p, ref Velocity v) =>
{
    p.X += v.X * dt;
    p.Y += v.Y * dt;
});
```

## Systems

Bigger games organize logic into systems and group them:

```csharp
public sealed class MovementSystem : SystemBase
{
    private readonly QueryDescription _q = new QueryDescription().WithAll<Position, Velocity>();

    public override void OnUpdate(in UpdateContext ctx)
    {
        float dt = ctx.DeltaTime;
        ctx.World.Query(_q).ForEach<Position, Velocity>((ref Position p, ref Velocity v) =>
        {
            p.X += v.X * dt;
            p.Y += v.Y * dt;
        });
    }
}

var systems = new SystemGroup("Main");
systems.Add(new MovementSystem());
systems.Add(new CollisionSystem());
systems.Add(new RenderingSystem());

while (running)
    systems.Update(world, deltaTime);
```

## Structural changes during iteration

You can't add, remove, or destroy entities while iterating a query — that would mid-flight reorganise the chunks the query is walking. Use a `CommandBuffer` to record changes for later:

```csharp
var cb = new CommandBuffer(world);

world.Query(deathQuery).ForEachWithEntity<Health>((Entity e, ref Health h) =>
{
    if (h.Value <= 0) cb.Destroy(e);
});

cb.Playback(); // applies every queued change
```

## Tag components

A `struct` with no instance fields is automatically detected as a tag and stored with zero per-entity overhead:

```csharp
public struct Frozen : ITag { }
public struct Selected : ITag { }

world.Add(entity, new Frozen());

// Filter on tags exactly like data components:
var alive = new QueryDescription().WithAll<Health>().WithNone<Frozen>();
```

The `ITag` interface is purely a documentation hint — the framework only cares whether the struct has any instance fields.

## API surface

| Type                | Purpose                                                                     |
| ------------------- | --------------------------------------------------------------------------- |
| `Entity`            | 8-byte handle: 32-bit slot id + 32-bit generation, so stale handles fail safely. |
| `IComponent`, `ITag`| Optional marker interfaces; not required by the framework.                  |
| `World`             | The ECS database. Owns entities, archetypes, chunks.                        |
| `QueryDescription`  | Declarative `WithAll` / `WithAny` / `WithNone` filters.                     |
| `Query`             | Bound query that exposes `ForEach`, `ForEachWithEntity`, `ForEachChunk`.    |
| `Chunk`             | One slab of contiguous entity data; `GetSpan<T>()` for SIMD-style loops.    |
| `Archetype`         | One unique component-set; you usually never touch this directly.            |
| `CommandBuffer`     | Defer structural changes during iteration.                                  |
| `ISystem`, `SystemBase`, `SystemGroup` | Optional system orchestration.                          |

## Design choices

- **Pure managed code, no `unsafe`.** Spans give us the cache-friendly iteration we need without leaving safe C#. If you need ground-floor SIMD, drop into `ForEachChunk` and use `Vector<T>` over the spans.
- **No reflection on the hot path.** Component ids are resolved through a generic-class cache (`TypeCache<T>.Type`), so `ComponentRegistry.Of<T>()` is an inlined static field load.
- **Archetype graph caching.** Adding `Velocity` to a `(Position)` archetype hits a dictionary the first time and a single `Dictionary.TryGetValue` afterward.
- **Swap-back removal.** Keeps every chunk densely packed; the cost is one component-row copy per delete plus one slot-pointer fixup.

## .NET 10 / C# 14 features used

- **Implicit span conversions** (`buffer[..n]` to `Span<T>`) — cleaner inner loops in the chunk iterators.
- **`field` keyword** (where it would shorten property declarations).
- **Primary constructors** on internal command-record types.
- **Collection expressions** (`[]`) for empty initializers.
- **Server GC + Tiered PGO** enabled in the csproj for production-grade JIT output. JIT improvements in .NET 10 — physical promotion of struct parameters, AVX-512 / AVX10.2 / SVE auto-vectorisation, better inlining — apply automatically and disproportionately benefit struct-heavy code like an ECS.

## What's intentionally missing

Things you may want to add for a full game-engine integration but that I left out to keep the core small:

- Multithreaded systems / job scheduling (the data layout is multithread-ready; you just need a scheduler).
- Source generator that derives queries from system field annotations, à la Friflo `Query Generator`.
- Relationships / parent-child entities.
- Serialization.
- Change tracking (per-component version numbers per chunk).

Every one of those is straightforward to bolt on top of the existing primitives.

## Running

```bash
# Build everything
dotnet build -c Release

# Run the basic sample (10k entities, headless)
dotnet run -c Release --project samples/MicroEcs.Sample

# Run the dungeon game — a real interactive console game built on MicroEcs
dotnet run -c Release --project samples/MicroEcs.Dungeon

# Run tests
dotnet test -c Release

# Run benchmarks
dotnet run -c Release --project src/MicroEcs.Benchmarks -- --filter "*"
```

See [`samples/MicroEcs.Dungeon/README.md`](samples/MicroEcs.Dungeon/README.md) for a worked example
of porting a small OOP console game (sh0rtener/dungeon-world) to ECS — line-by-line mapping from
classes to components + systems.

## License

MIT or whatever you prefer — this is starter code; copy and modify freely.
