using DungeonWorld.Game.Map;
using DungeonWorld.Game.Shared;

namespace DungeonWorld.Game;

public class GameCycle
{
    public Player Player { get; }
    public Field Field { get; }

    private Task inputListenning;
    private Direction currentDirection = Direction.None;

    public GameCycle(Player player, Field field)
    {
        Player = player;
        Field = field;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        StartListenInput(cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Field.Clean();

            if (currentDirection != Direction.None)
            {
                Player.Move(currentDirection);
            }

            Field.AddPlayer(Player);

            Field.Draw();
        }
    }

    public void StartListenInput(CancellationToken cancellationToken)
    {
        inputListenning = Task.Run(() =>
        {
            try
            {

                while (true) 
                { 
                    cancellationToken.ThrowIfCancellationRequested();
                    currentDirection = Direction.None;

                    var key = Console.ReadKey();

                    currentDirection = key.Key switch
                    {
                        ConsoleKey.UpArrow => Direction.Up,
                        ConsoleKey.DownArrow => Direction.Down,
                        ConsoleKey.LeftArrow => Direction.Left,
                        ConsoleKey.RightArrow => Direction.Right,
                        _ => Direction.None,
                    };

                    Task.Delay(1, cancellationToken);
                }

            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception)
            {

                throw;
            }
        }, cancellationToken);
    }
}