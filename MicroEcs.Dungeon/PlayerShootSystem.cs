using MicroEcs;

namespace MicroEcs.Dungeon;

public sealed class PlayerShootSystem : SystemBase
{
    private readonly QueryDescription _playerQ = new QueryDescription()
        .WithAll<PlayerTag, Position, ShootIntent>();

    public override void OnUpdate(in UpdateContext ctx)
    {
        int shootDx = 0, shootDy = 0;
        Position origin = default;
        bool hasPending = false;

        ctx.World.Query(_playerQ).ForEach<Position, ShootIntent>(
            (ref Position pos, ref ShootIntent si) =>
            {
                if (si.Dx != 0 || si.Dy != 0)
                {
                    shootDx = si.Dx;
                    shootDy = si.Dy;
                    origin = pos;
                    hasPending = true;
                }
                si.Dx = 0;
                si.Dy = 0;
            });

        if (hasPending)
            BulletSystem.SpawnBullet(ctx.World, origin, shootDx, shootDy, isPlayer: true);
    }
}
