using DungeonWorld.Game.Shared;

namespace DungeonWorld.Game.Map;

public class Field
{
    private List<(char s, Point p)> decoration =
    [
        ('*', new(0, 0)),
    ];

    private Player player;

    public Field(Player player)
    {
        this.player = player;
    }

    public void Clean()
    {
        Console.Clear();
    }

    public void AddPlayer(Player player)
    {
        this.player = player;
    }

    public void Draw()
    {
        var height = Console.WindowHeight;
        var width = Console.WindowWidth;
        var verticalCenter = height / 2 - (height % 2 == 0 ? 1 : 0);
        var horizontalCenter = width / 2 - (width % 2 == 0 ? 1 : 0);
        var playerOnScreen = new Point(horizontalCenter, verticalCenter);
        var playerAbsolute = player.Position;

        var leftTop = playerAbsolute - playerOnScreen;
        var rightBottom = playerAbsolute + playerOnScreen;

        foreach ( var (s, p) in decoration )
        {
            var point = GetPointOnScreen(p);
            if (point.HasValue)
            {
                Console.SetCursorPosition(point.Value.X, point.Value.Y);
                Console.Write(s);
            }
        }

        Console.SetCursorPosition(playerOnScreen.X, playerOnScreen.Y);
        Console.Write(player.Symbol);

        Point? GetPointOnScreen(Point absolute)
        {
            if (absolute.X < leftTop.X || absolute.Y < leftTop.Y)
            {
                return null;
            }

            if (absolute.X > rightBottom.X || absolute.Y > rightBottom.Y)
            {
                return null;
            }

            return absolute - leftTop;
        }
    }

    
}