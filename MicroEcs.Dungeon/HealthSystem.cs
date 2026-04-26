using MicroEcs;

namespace MicroEcs.Dungeon;

public sealed class HealthSystem : SystemBase
{
    private readonly QueryDescription _q = new QueryDescription().WithAll<Health>();

    public bool PlayerDied { get; private set; }

    public override void OnUpdate(in UpdateContext ctx)
    {
        var world = ctx.World;
        var toDestroy = new List<Entity>();

        world.Query(_q).ForEachWithEntity<Health>((Entity e, ref Health h) =>
        {
            if (h.Current <= 0)
            {
                toDestroy.Add(e);
                if (world.Has<PlayerTag>(e)) PlayerDied = true;
            }
        });

        foreach (var e in toDestroy)
            if (world.IsAlive(e)) world.Destroy(e);
    }
}
