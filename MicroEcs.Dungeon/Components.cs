using MicroEcs;

namespace MicroEcs.Dungeon;

// ----------------------------------------------------------------------------
// Components — pure data, no behaviour.
//
// Compared to the original Dungeon World code:
//   - The `Point` struct collapses into `Position` (and `Velocity` for movement intent).
//   - The `Player` class loses everything except its symbol and identity, both of which
//     become components — `Renderable` for the symbol, `PlayerTag` for "this is the player".
//   - `LevelMapValue` (decorationMap entries) becomes Position + Renderable + Blocker.
//   - `Direction` enum stays useful but only for input mapping; on entities, intent is a Velocity.
// ----------------------------------------------------------------------------

/// <summary>World-space integer coordinates of an entity.</summary>
public record struct Position(int X, int Y);

/// <summary>Per-tick movement intent. The MovementSystem consumes this and resets it to zero.</summary>
public record struct Velocity(int Dx, int Dy);

/// <summary>What character to draw for this entity, and an optional foreground colour.</summary>
public record struct Renderable(char Symbol, ConsoleColor Color = ConsoleColor.Gray);

/// <summary>Z-order: higher draws on top. Player sits above floor sits above nothing.</summary>
public record struct Layer(int Order);

// ----- Tags (zero-sized; tracked by the ECS but cost no per-entity bytes) -----

/// <summary>Identifies the player. Exactly one entity should carry this tag.</summary>
public struct PlayerTag : ITag { }

/// <summary>Anything blocking movement (walls, doors, etc.).</summary>
public struct Blocker : ITag { }

/// <summary>The exit tile. Stepping on it ends the game with a win.</summary>
public struct Goal : ITag { }

/// <summary>Floor / open ground — purely cosmetic, drawn under everything.</summary>
public struct Floor : ITag { }
