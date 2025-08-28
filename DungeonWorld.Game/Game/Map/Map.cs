using DungeonWorld.Game.Shared;

namespace DungeonWorld.Game.Map;

public class LevelMapValue
{
    public char Symbol { get; set; }
    public Point Position { get; set; }

    public LevelMapValue(char symbol, Point point)
    {
        Symbol = symbol;
        Position = point;
    }
}

public class Field
{
    private List<LevelMapValue> decorationMap = [new('*', new(2, 2))];

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

    public bool InStuck(Point playerAbsolute) =>
        decorationMap.Select(x => x.Position).Contains(playerAbsolute);

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

        foreach (var decoration in decorationMap)
        {
            var point = GetPointOnScreen(decoration.Position);
            if (point.HasValue)
            {
                Console.SetCursorPosition(point.Value.X, point.Value.Y);
                Console.Write(decoration.Symbol);
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
