using MicroEcs;

namespace MicroEcs.Dungeon;

public sealed class InputSystem : SystemBase
{
    private readonly Queue<ConsoleKey> _inputQueue;
    private readonly QueryDescription _playerMoveQuery = new QueryDescription()
        .WithAll<PlayerTag, Velocity>();
    private readonly QueryDescription _playerShootQuery = new QueryDescription()
        .WithAll<PlayerTag, ShootIntent>();

    private int _lastDx, _lastDy;

    public bool QuitRequested { get; private set; }

    public InputSystem(Queue<ConsoleKey> inputQueue)
    {
        _inputQueue = inputQueue;
    }

    public override void OnUpdate(in UpdateContext ctx)
    {
        ConsoleKey? key = null;
        lock (_inputQueue)
        {
            while (_inputQueue.Count > 0) key = _inputQueue.Dequeue();
        }

        int dx = 0, dy = 0;
        bool shoot = false;
        switch (key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.W or ConsoleKey.K: dy = -1; break;
            case ConsoleKey.DownArrow or ConsoleKey.S or ConsoleKey.J: dy = 1; break;
            case ConsoleKey.LeftArrow or ConsoleKey.A or ConsoleKey.H: dx = -1; break;
            case ConsoleKey.RightArrow or ConsoleKey.D or ConsoleKey.L: dx = 1; break;
            case ConsoleKey.Spacebar: shoot = true; break;
            case ConsoleKey.Q or ConsoleKey.Escape: QuitRequested = true; break;
        }

        if (dx != 0 || dy != 0)
        {
            _lastDx = dx;
            _lastDy = dy;
        }

        ctx.World.Query(_playerMoveQuery).ForEach<Velocity>((ref Velocity v) =>
        {
            v.Dx = dx;
            v.Dy = dy;
        });

        int shootDx = shoot ? _lastDx : 0;
        int shootDy = shoot ? _lastDy : 0;
        ctx.World.Query(_playerShootQuery).ForEach<ShootIntent>((ref ShootIntent si) =>
        {
            si.Dx = shootDx;
            si.Dy = shootDy;
        });
    }
}
