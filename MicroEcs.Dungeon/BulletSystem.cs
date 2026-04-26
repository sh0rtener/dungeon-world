using MicroEcs;

namespace MicroEcs.Dungeon;

public sealed class BulletSystem : SystemBase
{
    private readonly QueryDescription _wallsQ = new QueryDescription()
        .WithAll<Position, Blocker>()
        .WithNone<EnemyTag>();

    private readonly QueryDescription _enemiesQ = new QueryDescription()
        .WithAll<Position, EnemyTag>();

    private readonly QueryDescription _playerQ = new QueryDescription()
        .WithAll<Position, PlayerTag>();

    private readonly QueryDescription _playerBulletsQ = new QueryDescription()
        .WithAll<Position, Velocity, Lifetime>()
        .WithAll<PlayerBulletTag>();

    private readonly QueryDescription _enemyBulletsQ = new QueryDescription()
        .WithAll<Position, Velocity, Lifetime>()
        .WithAll<EnemyBulletTag>();

    public override void OnUpdate(in UpdateContext ctx)
    {
        var world = ctx.World;

        var walls = new HashSet<(int, int)>();
        world.Query(_wallsQ).ForEach<Position>((ref Position p) => walls.Add((p.X, p.Y)));

        var enemyByPos = new Dictionary<(int, int), Entity>();
        world.Query(_enemiesQ).ForEachWithEntity<Position>(
            (Entity e, ref Position p) => enemyByPos[(p.X, p.Y)] = e);

        Position playerPos = default;
        Entity playerEnt = default;
        bool hasPlayer = false;
        world.Query(_playerQ).ForEachWithEntity<Position>(
            (Entity e, ref Position p) => { playerPos = p; playerEnt = e; hasPlayer = true; });

        var toDestroy = new List<Entity>();
        var damages = new List<(Entity target, int dmg)>();
        float dt = ctx.DeltaTime;

        // ---- Player bullets hit enemies ----
        world.Query(_playerBulletsQ).ForEachChunk(chunk =>
        {
            var entities = chunk.Entities;
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            var lifetimes = chunk.GetSpan<Lifetime>();
            var bullets = chunk.GetSpan<Bullet>();

            for (int i = 0; i < positions.Length; i++)
            {
                ref var lt = ref lifetimes[i];
                lt.TimeRemaining -= dt;
                if (lt.TimeRemaining <= 0) { toDestroy.Add(entities[i]); continue; }

                ref var pos = ref positions[i];
                ref var vel = ref velocities[i];
                int steps = Math.Max(Math.Abs(vel.Dx), Math.Abs(vel.Dy));
                int sdx = Math.Sign(vel.Dx), sdy = Math.Sign(vel.Dy);
                bool hit = false;

                for (int s = 0; s < steps && !hit; s++)
                {
                    int nx = pos.X + sdx, ny = pos.Y + sdy;
                    if (walls.Contains((nx, ny)))
                    {
                        toDestroy.Add(entities[i]);
                        hit = true;
                    }
                    else if (enemyByPos.TryGetValue((nx, ny), out var enemy))
                    {
                        damages.Add((enemy, bullets[i].Damage));
                        toDestroy.Add(entities[i]);
                        hit = true;
                    }
                    else
                    {
                        pos.X = nx;
                        pos.Y = ny;
                    }
                }
            }
        });

        // ---- Enemy bullets hit player ----
        world.Query(_enemyBulletsQ).ForEachChunk(chunk =>
        {
            var entities = chunk.Entities;
            var positions = chunk.GetSpan<Position>();
            var velocities = chunk.GetSpan<Velocity>();
            var lifetimes = chunk.GetSpan<Lifetime>();
            var bullets = chunk.GetSpan<Bullet>();

            for (int i = 0; i < positions.Length; i++)
            {
                ref var lt = ref lifetimes[i];
                lt.TimeRemaining -= dt;
                if (lt.TimeRemaining <= 0) { toDestroy.Add(entities[i]); continue; }

                ref var pos = ref positions[i];
                ref var vel = ref velocities[i];
                int steps = Math.Max(Math.Abs(vel.Dx), Math.Abs(vel.Dy));
                int sdx = Math.Sign(vel.Dx), sdy = Math.Sign(vel.Dy);
                bool hit = false;

                for (int s = 0; s < steps && !hit; s++)
                {
                    int nx = pos.X + sdx, ny = pos.Y + sdy;
                    if (walls.Contains((nx, ny)))
                    {
                        toDestroy.Add(entities[i]);
                        hit = true;
                    }
                    else if (hasPlayer && nx == playerPos.X && ny == playerPos.Y)
                    {
                        damages.Add((playerEnt, bullets[i].Damage));
                        toDestroy.Add(entities[i]);
                        hit = true;
                    }
                    else
                    {
                        pos.X = nx;
                        pos.Y = ny;
                    }
                }
            }
        });

        foreach (var (target, dmg) in damages)
        {
            if (world.IsAlive(target))
                world.GetRef<Health>(target).Current -= dmg;
        }

        foreach (var e in toDestroy)
            if (world.IsAlive(e)) world.Destroy(e);
    }

    public static void SpawnBullet(World world, Position origin, int dx, int dy, bool isPlayer)
    {
        const int speed = 5;
        var e = world.Create(
            origin,
            new Velocity(dx * speed, dy * speed),
            new Renderable('*', isPlayer ? ConsoleColor.Yellow : ConsoleColor.Red),
            new Layer(7),
            new Bullet(isPlayer ? 1 : 2));
        world.Add(e, new Lifetime(0.6f));
        world.Add(e, default(BulletTag));
        if (isPlayer) world.Add(e, default(PlayerBulletTag));
        else world.Add(e, default(EnemyBulletTag));
    }
}
