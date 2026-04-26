using MicroEcs;

namespace MicroEcs.Dungeon;

public sealed class EnemyShootSystem : SystemBase
{
    private readonly QueryDescription _playerQ = new QueryDescription()
        .WithAll<PlayerTag, Position>();

    private readonly QueryDescription _enemiesQ = new QueryDescription()
        .WithAll<Position, ShootCooldown, EnemyTag>();

    public override void OnUpdate(in UpdateContext ctx)
    {
        Position playerPos = default;
        bool hasPlayer = false;
        ctx.World.Query(_playerQ).ForEach<Position>((ref Position p) =>
        {
            playerPos = p;
            hasPlayer = true;
        });

        var pendingShots = new List<(Position origin, int dx, int dy)>();
        float dt = ctx.DeltaTime;

        ctx.World.Query(_enemiesQ).ForEach<Position, ShootCooldown>(
            (ref Position pos, ref ShootCooldown cd) =>
            {
                cd.TimeRemaining -= dt;
                if (!hasPlayer || cd.TimeRemaining > 0) return;

                int dist = Math.Abs(playerPos.X - pos.X) + Math.Abs(playerPos.Y - pos.Y);
                if (dist > 20) { cd.TimeRemaining = cd.MaxCooldown; return; }

                // Cardinal direction toward player — pick the larger delta axis.
                int adx = Math.Abs(playerPos.X - pos.X);
                int ady = Math.Abs(playerPos.Y - pos.Y);
                int dx, dy;
                if (adx >= ady)
                {
                    dx = Math.Sign(playerPos.X - pos.X);
                    dy = 0;
                }
                else
                {
                    dx = 0;
                    dy = Math.Sign(playerPos.Y - pos.Y);
                }

                pendingShots.Add((pos, dx, dy));
                cd.TimeRemaining = cd.MaxCooldown;
            });

        foreach (var (origin, dx, dy) in pendingShots)
            BulletSystem.SpawnBullet(ctx.World, origin, dx, dy, isPlayer: false);
    }
}
