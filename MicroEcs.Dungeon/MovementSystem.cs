using MicroEcs;

namespace MicroEcs.Dungeon;

/// <summary>
/// Moves entities by their <see cref="Velocity"/>, refusing the move if the destination tile
/// holds a <see cref="Blocker"/>. Resets <c>Velocity</c> to zero after each tick so movement
/// has to be re-issued every frame (matches the original "intent only when a key is pressed"
/// behaviour from <c>GameCycle.RunAsync</c>).
///
/// The collision check is the ECS analogue of the original <c>Field.InStuck</c>:
/// instead of querying a list inside the Field object, we ask the world for any entity at
/// the candidate position carrying the <see cref="Blocker"/> tag.
/// </summary>
public sealed class MovementSystem : SystemBase
{
    private readonly QueryDescription _movers = new QueryDescription()
        .WithAll<Position, Velocity>()
        .WithNone<BulletTag>();

    private readonly QueryDescription _blockers = new QueryDescription()
        .WithAll<Position, Blocker>();

    public override void OnUpdate(in UpdateContext ctx)
    {
        // Snapshot blocker positions for this frame. We could query inside the inner loop, but
        // this is dramatically less work when there are thousands of walls and only a handful
        // of movers, and it sidesteps the rule against structural changes during iteration.
        var blocked = new HashSet<(int, int)>();
        ctx.World.Query(_blockers).ForEach<Position>((ref Position p) => blocked.Add((p.X, p.Y)));

        ctx.World.Query(_movers).ForEach<Position, Velocity>(
            (ref Position p, ref Velocity v) =>
            {
                if (v.Dx != 0 || v.Dy != 0)
                {
                    int nx = p.X + v.Dx;
                    int ny = p.Y + v.Dy;
                    if (!blocked.Contains((nx, ny)))
                    {
                        p.X = nx;
                        p.Y = ny;
                    }
                }
                v.Dx = 0;
                v.Dy = 0;
            });
    }
}
