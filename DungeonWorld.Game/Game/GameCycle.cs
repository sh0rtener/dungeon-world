using DungeonWorld.Game.Map;
using DungeonWorld.Game.Shared;

namespace DungeonWorld.Game;

public class GameCycle
{
    public Player Player { get; }
    public Field Field { get; }

    private Task inputListenning;
    private Direction currentDirection = Direction.None;
    private Queue<ConsoleKeyInfo> inputQueue = new Queue<ConsoleKeyInfo>();

    public GameCycle(Player player, Field field)
    {
        Player = player;
        Field = field;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        Console.CursorVisible = false;
        StartListenInput(cancellationToken);

        Field.Clean();
        Field.Draw();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (inputQueue.Any())
            {
                var key = inputQueue.Dequeue();

                currentDirection = key.Key switch
                {
                    ConsoleKey.UpArrow => Direction.Up,
                    ConsoleKey.DownArrow => Direction.Down,
                    ConsoleKey.LeftArrow => Direction.Left,
                    ConsoleKey.RightArrow => Direction.Right,
                    _ => Direction.None,
                };
            }
            else
            {
                currentDirection = Direction.None;
            }

            if (currentDirection != Direction.None)
            {
                if (Field.InStuck(Player.TryMove(currentDirection)))
                    continue;

                Player.Move(currentDirection);
                Field.Clean();
                Field.Draw();
            }
        }
    }

    public void StartListenInput(CancellationToken cancellationToken)
    {
        inputListenning = Task.Run(
            () =>
            {
                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var key = Console.ReadKey();
                        if (inputQueue.Count < 10)
                        {
                            inputQueue.Enqueue(key);
                        }

                        Task.Delay(1, cancellationToken);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    throw;
                }
            },
            cancellationToken
        );
    }
}
