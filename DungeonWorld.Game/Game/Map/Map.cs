using DungeonWorld.Game.Shared;

namespace DungeonWorld.Game.Map;

public class Field
{
    private List<(char s, Point p)> decoration = new List<(char s, Point p)>()
    {
        ('*', new(0, 0)),
    };
    private Player player;

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
        Console.SetCursorPosition(player.Position.Y - topOffset, player.Position.X - leftOffset);
        Console.Write(player.Symbol);
    }
}