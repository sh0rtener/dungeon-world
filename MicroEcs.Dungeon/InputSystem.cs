using MicroEcs;

namespace MicroEcs.Dungeon;

/// <summary>
/// Drains queued console keys (filled by a background reader) and writes the latest direction
/// onto the player's <see cref="Velocity"/> component.
///
/// The original game put input handling, queue management, and velocity computation all inside
/// <c>GameCycle.RunAsync</c>. Splitting input out lets every other system stay agnostic about
/// where movement intent actually came from — replace this system with an AI controller and the
/// rest of the simulation is unchanged.
/// </summary>
public sealed class InputSystem : SystemBase
{
    private readonly Queue<ConsoleKey> _inputQueue;
    private readonly QueryDescription _playerQuery = new QueryDescription()
        .WithAll<PlayerTag, Velocity>();

    /// <summary>True once the user pressed Q (or Escape) and wants to quit.</summary>
    public bool QuitRequested { get; private set; }

    public InputSystem(Queue<ConsoleKey> inputQueue)
    {
        _inputQueue = inputQueue;
    }

    public override void OnUpdate(in UpdateContext ctx)
    {
        // Only the most recent key per frame matters — older queued moves are discarded so a
        // player who held a key during a slow frame doesn't suddenly leap five tiles.
        ConsoleKey? key = null;
        lock (_inputQueue)
        {
            while (_inputQueue.Count > 0) key = _inputQueue.Dequeue();
        }

        int dx = 0, dy = 0;
        switch (key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.W or ConsoleKey.K: dy = -1; break;
            case ConsoleKey.DownArrow or ConsoleKey.S or ConsoleKey.J: dy = 1; break;
            case ConsoleKey.LeftArrow or ConsoleKey.A or ConsoleKey.H: dx = -1; break;
            case ConsoleKey.RightArrow or ConsoleKey.D or ConsoleKey.L: dx = 1; break;
            case ConsoleKey.Q or ConsoleKey.Escape: QuitRequested = true; break;
        }

        ctx.World.Query(_playerQuery).ForEach<Velocity>((ref Velocity v) =>
        {
            v.Dx = dx;
            v.Dy = dy;
        });
    }
}
