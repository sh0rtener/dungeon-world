using DungeonWorld.Game.Shared;

namespace DungeonWorld.Game;

public class Player
{
    public char Symbol = '⨶';
    public Point Position { get; private set; }
    public int Speed { get; private set; }

    public void Move(Direction direction)
    {
        switch (direction)
        {
            case Direction.Up:
                Position = new(Position.X, Position.Y + Speed);
                break;

            case Direction.Down:
                Position = new(Position.X, Position.Y - Speed);
                break;

            case Direction.Left:
                Position = new(Position.X - Speed, Position.Y);
                break;

            case Direction.Right:
                Position = new(Position.X + Speed, Position.Y);
                break;

        }
        // ... Point -> ..?
    }   
}