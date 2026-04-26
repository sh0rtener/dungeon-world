using MicroEcs;

namespace MicroEcs.Dungeon;

/// <summary>
/// Draws every <see cref="Renderable"/> entity to the console, centred on the player.
///
/// Two improvements over the original <c>Field.Draw</c>:
///   1. <c>Console.Clear</c> is replaced by a back-buffer diff. We keep last frame's screen
///      around and only repaint cells that actually changed, which kills the flicker that
///      <c>Console.Clear</c> caused on every move.
///   2. Layering: a <see cref="Layer"/> component decides paint order, so floors stay under
///      walls stay under the player without us having to manage three separate lists.
/// </summary>
public sealed class RenderSystem : SystemBase
{
    private readonly QueryDescription _playerQuery = new QueryDescription().WithAll<PlayerTag, Position>();
    private readonly QueryDescription _playerHealthQ = new QueryDescription().WithAll<PlayerTag, Health>();
    private readonly QueryDescription _withLayer = new QueryDescription().WithAll<Position, Renderable, Layer>();
    private readonly QueryDescription _goalQuery = new QueryDescription().WithAll<Goal, Position>();

    private char[,]? _backChars;
    private ConsoleColor[,]? _backColors;
    private int _backW, _backH;

    public override void OnUpdate(in UpdateContext ctx)
    {
        int width = Math.Max(20, Console.WindowWidth);
        int height = Math.Max(10, Console.WindowHeight - 1);
        // Reserve the bottom row for HUD text.
        int playArea = height - 1;

        EnsureBackBuffer(width, playArea);

        // ---- 1. Find the camera anchor (the player) ----
        Position cam = default;
        bool foundPlayer = false;
        ctx.World.Query(_playerQuery).ForEach<Position>((ref Position p) =>
        {
            cam = p;
            foundPlayer = true;
        });
        if (!foundPlayer) return;

        int camOffsetX = width / 2;
        int camOffsetY = playArea / 2;

        // ---- 2. Build this frame: collect everything visible, sort by layer ----
        var visible = new List<(int sx, int sy, Renderable r, int layer)>(capacity: 1024);

        // Cache layer per entity by querying once and reading three components in lockstep.
        ctx.World.Query(_withLayer).ForEach<Position, Renderable, Layer>(
            (ref Position p, ref Renderable r, ref Layer l) =>
            {
                int sx = p.X - cam.X + camOffsetX;
                int sy = p.Y - cam.Y + camOffsetY;
                if (sx < 0 || sy < 0 || sx >= width || sy >= playArea) return;
                visible.Add((sx, sy, r, l.Order));
            });

        // Stable sort by layer so higher layers overwrite lower ones during the buffer fill.
        visible.Sort((a, b) => a.layer.CompareTo(b.layer));

        // ---- 3. Fill a fresh frame buffer ----
        var frameChars = new char[width, playArea];
        var frameColors = new ConsoleColor[width, playArea];
        for (int y = 0; y < playArea; y++)
            for (int x = 0; x < width; x++)
            {
                frameChars[x, y] = ' ';
                frameColors[x, y] = ConsoleColor.Black;
            }

        foreach (var (sx, sy, r, _) in visible)
        {
            frameChars[sx, sy] = r.Symbol;
            frameColors[sx, sy] = r.Color;
        }

        // ---- 4. Diff against the back buffer; only repaint changed cells ----
        var prevColor = (ConsoleColor)(-1);
        for (int y = 0; y < playArea; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (frameChars[x, y] == _backChars![x, y] &&
                    frameColors[x, y] == _backColors![x, y])
                    continue;

                Console.SetCursorPosition(x, y);
                if (frameColors[x, y] != prevColor)
                {
                    Console.ForegroundColor = frameColors[x, y];
                    prevColor = frameColors[x, y];
                }
                Console.Write(frameChars[x, y]);

                _backChars[x, y] = frameChars[x, y];
                _backColors[x, y] = frameColors[x, y];
            }
        }

        // ---- 5. HUD line ----
        int goalDistance = ComputeGoalDistance(ctx.World, cam);
        Health? playerHp = null;
        ctx.World.Query(_playerHealthQ).ForEach<Health>((ref Health h) => playerHp = h);
        string hpStr = playerHp.HasValue ? $"HP={playerHp.Value.Current}/{playerHp.Value.Max}" : "HP=?";

        Console.SetCursorPosition(0, playArea);
        Console.ForegroundColor = ConsoleColor.Cyan;
        string hud =
            $" {hpStr}  pos=({cam.X,3},{cam.Y,3})  to-goal={goalDistance,3}  entities={ctx.World.EntityCount,5}  [wasd/hjkl] move  [Space] shoot  [Q] quit ";
        if (hud.Length > width) hud = hud[..width];
        else hud = hud.PadRight(width);
        Console.Write(hud);
        Console.ResetColor();
    }

    private void EnsureBackBuffer(int w, int h)
    {
        if (_backChars is not null && _backW == w && _backH == h) return;

        _backChars = new char[w, h];
        _backColors = new ConsoleColor[w, h];
        // Initialise to a sentinel that will never match the real frame, forcing a full repaint.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                _backChars[x, y] = '\0';
                _backColors[x, y] = (ConsoleColor)(-1);
            }
        _backW = w; _backH = h;

        // Clear the screen exactly once when the buffer is created or resized — afterwards we
        // repaint cell-by-cell.
        try { Console.Clear(); } catch { /* some terminals reject Clear; ignore */ }
    }

    private int ComputeGoalDistance(World world, Position playerPos)
    {
        Position? goal = null;
        world.Query(_goalQuery).ForEach<Position>((ref Position p) => goal = p);
        if (goal is null) return -1;
        return Math.Abs(goal.Value.X - playerPos.X) + Math.Abs(goal.Value.Y - playerPos.Y);
    }
}
