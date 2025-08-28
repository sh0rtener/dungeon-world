using DungeonWorld.Game.Shared;

namespace DungeonWorld.Game;

public class Player
{
    public char Symbol = '@';
    public Point Position { get; private set; }
    public int Speed { get; private set; } = 1;

    public void Move(Direction direction)
    {
        Position = TryMove(direction);
        // ... Point -> ..?
    }

    public Point TryMove(Direction direction)
    {
        switch (direction)
        {
            case Direction.Up:
                return new(Position.X, Position.Y - Speed);

            case Direction.Down:
                return new(Position.X, Position.Y + Speed);

            case Direction.Left:
                return new(Position.X - Speed, Position.Y);
            case Direction.Right:
                return new(Position.X + Speed, Position.Y);
            case Direction.None:
                return new(Position.X, Position.Y);
            default:
                return new(Position.X, Position.Y);
        }
    }
}