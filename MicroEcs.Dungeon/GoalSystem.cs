using MicroEcs;

namespace MicroEcs.Dungeon;

/// <summary>
/// Closes the loop on the original task ("get from A to B"): when the player's position
/// matches a tile carrying the <see cref="Goal"/> tag, flip <see cref="Reached"/> and let the
/// driver wind down the game.
/// </summary>
public sealed class GoalSystem : SystemBase
{
    private readonly QueryDescription _player = new QueryDescription().WithAll<PlayerTag, Position>();
    private readonly QueryDescription _goal = new QueryDescription().WithAll<Goal, Position>();

    public bool Reached { get; private set; }

    public override void OnUpdate(in UpdateContext ctx)
    {
        Position? playerPos = null;
        ctx.World.Query(_player).ForEach<Position>((ref Position p) => playerPos = p);

        if (playerPos is null) return;

        ctx.World.Query(_goal).ForEach<Position>((ref Position g) =>
        {
            if (g.X == playerPos.Value.X && g.Y == playerPos.Value.Y)
                Reached = true;
        });
    }
}
