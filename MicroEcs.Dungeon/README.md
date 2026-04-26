# Dungeon World — ECS edition

A port of [sh0rtener/dungeon-world](https://github.com/sh0rtener/dungeon-world) to MicroEcs.

The original is a small console game where you walk an `@` symbol around a hand-drawn map of `*` walls, with a camera that follows the player. This rewrite keeps the gameplay feel identical, fills in the two pieces the original `TASK.md` promised but never implemented (procedural generation and a goal to reach), and uses every piece of MicroEcs along the way.

## Run it

```bash
dotnet run -c Release --project samples/MicroEcs.Dungeon
```

Controls: arrow keys / WASD / HJKL to move. `Q` or `Esc` to quit. Reach the green `>` to win.

## What the original looked like

```
Player                 -- a class with Symbol, Position, Speed, Move(), TryMove()
LevelMapValue          -- a class with Symbol, Position, End
Field                  -- holds Player + List<LevelMapValue>; does Draw and InStuck checks
GameCycle              -- holds Player + Field; reads input; runs the main loop
FieldMapGeneratorExt   -- a static helper that hand-codes ~50 wall positions
Direction, Point       -- small value types
```

`GameCycle.RunAsync` was the heart of the game: it owned input handling, movement intent, collision checks, and the redraw call all in one method.

## What the ECS version looks like

The same gameplay shows up as **components** (data) and **systems** (logic), with no class holding both:

| Original concept         | ECS analogue                                                         |
| ------------------------ | -------------------------------------------------------------------- |
| `Player.Position`        | `Position` component on the player entity                            |
| `Player.Symbol`          | `Renderable { Symbol = '@' }` component                              |
| `Player` class identity  | `PlayerTag` (zero-sized tag component)                               |
| `Player.TryMove`         | `Velocity` component, applied by `MovementSystem`                    |
| `LevelMapValue` (a wall) | An entity with `Position + Renderable + Blocker + Layer`             |
| `Field.InStuck`          | A query for `Position + Blocker` inside `MovementSystem`             |
| `Field.Draw`             | `RenderSystem`, with a query for `Position + Renderable + Layer`     |
| `GameCycle` input loop   | `InputSystem` reading from a thread-safe queue                       |
| Hard-coded walls list    | `DungeonGenerator` — rooms-and-corridors, with guaranteed reachability |
| (didn't exist)           | `Goal` tag + `GoalSystem` — closes "get from A to B" from TASK.md      |

## Where the gameplay logic lives now

```
samples/MicroEcs.Dungeon/
  Components.cs        -- Position, Velocity, Renderable, Layer + tags
  DungeonGenerator.cs  -- 5-pass procedural generator (see "How the map is built" below)
  InputSystem.cs       -- key queue → Velocity on the player
  MovementSystem.cs    -- Velocity → Position, blocked by Blocker tiles
  GoalSystem.cs        -- player on Goal tile? mark game won
  RenderSystem.cs      -- draw every Position+Renderable, camera-centred, with layers
  Program.cs           -- wires the above up; ~50 lines of plumbing
```

Notice what's _not_ there: there is no `Field`, no `Player` class, no `LevelMapValue`. Walls and the player are the same kind of thing — entities — and tags differentiate them at query time.

## How the map is built

`DungeonGenerator` runs five passes on a plain `bool[,]` grid before spawning any entities:

1. **Place rooms.** Drop up to N non-overlapping rectangles. Each one is randomly tagged as either "rectangular" or "cave". Cave rooms are deliberately ~3-5 tiles bigger because step 2 will eat into them.

2. **Cellular automata for caves.** For every cave-tagged room, fill its interior with random noise (~45% wall) and run the classic *4-5 rule* four times: a wall stays a wall if ≥4 of its 8 neighbours are walls; a floor becomes a wall if ≥5 neighbours are walls. Random noise smooths into organic, irregular caverns over a few iterations. The room's outer rim is force-locked to wall so the cellular dynamics can't bleed into adjacent rooms.

3. **Connect rooms with L-shaped corridors.** Same as before — every room links to the previous one, guaranteeing connectivity. We also flag every cell carved by a corridor in a parallel `_isCorridor` grid, which step 4 needs.

4. **Dead-end branches off corridors.** Walk every corridor cell. With ~12% probability each, fork off a short stub (3-7 tiles) in a direction *perpendicular* to the corridor's local axis. The stub stops the moment it would touch existing floor, which guarantees that "dead end" stays a dead end and never accidentally becomes a shortcut between rooms.

5. **Repair + materialise.**
   - `EnsureCaveCentersReachable` clears a 3×3 around each cave's centre, in case CA collapsed it into solid rock.
   - `PruneDisconnectedFloor` BFS-floods from the player spawn and turns any disconnected pocket of floor back into wall — CA loves to leave little orphan caverns the player could never reach.
   - Finally, every floor cell becomes a `Floor` entity, every wall cell adjacent to floor becomes a `Blocker` entity, and the player + goal entities are spawned in the first and last room.

The result: each playthrough mixes sharp rectangular halls (the planned rooms), winding caverns (CA caves), and twisty side passages that lead nowhere (the branches). All connectivity is guaranteed by construction.

## Improvements we got "for free" by going ECS

A few things that would have been ugly to bolt onto the original architecture fall out naturally here:

- **Procedural maps with three different cave types in the same dungeon.** The generator just spawns entities — adding a new cell-shape pass (cellular automata) or a new feature (dead-end stubs) doesn't require teaching `Field` about anything. The whole pipeline is "set bits in a 2D array, then iterate the array once and create entities."
- **A real goal.** Adding `Goal` is one tag + one query + four lines of system code. The TASK.md "from A to B" finally exists.
- **Layered rendering.** Floor under walls under player, controlled by an integer `Layer` component. Adding decorations or a fog-of-war layer is just another `Layer` value.
- **Flicker-free rendering.** The original called `Console.Clear()` every frame, which flashes the screen on most terminals. The new `RenderSystem` keeps a back-buffer and repaints only the cells that actually changed.
- **Easy to extend.** Want enemies that wander? Spawn an entity with `Position + Velocity + Renderable + EnemyTag` and add an `AISystem` that writes to its Velocity. Nothing else has to know about enemies.

## Running the rest of MicroEcs

```bash
dotnet run -c Release --project samples/MicroEcs.Sample          # the original 10k-entity demo
dotnet test                                                      # unit tests
dotnet run -c Release --project src/MicroEcs.Benchmarks -- --filter "*"
```
