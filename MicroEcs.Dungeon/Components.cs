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

// ----- Combat components -----

/// <summary>Hit points for player and enemies.</summary>
public record struct Health(int Current, int Max);

/// <summary>Seconds until this entity can fire again.</summary>
public record struct ShootCooldown(float TimeRemaining, float MaxCooldown);

/// <summary>Damage dealt on contact. Present on all bullet entities.</summary>
public record struct Bullet(int Damage);

/// <summary>Seconds remaining before this entity auto-destructs.</summary>
public record struct Lifetime(float TimeRemaining);

/// <summary>Player's pending shoot direction for this frame (0,0 = no shot).</summary>
public record struct ShootIntent(int Dx, int Dy);

// ----- Combat tags -----

/// <summary>Marks enemy entities.</summary>
public struct EnemyTag : ITag { }

/// <summary>On every bullet. Lets MovementSystem exclude bullets via WithNone.</summary>
public struct BulletTag : ITag { }

/// <summary>Player-owned bullets — collide with enemies only.</summary>
public struct PlayerBulletTag : ITag { }

/// <summary>Enemy-owned bullets — collide with player only.</summary>
public struct EnemyBulletTag : ITag { }
