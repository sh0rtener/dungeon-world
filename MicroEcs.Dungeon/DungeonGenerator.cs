using MicroEcs;

namespace MicroEcs.Dungeon;

/// <summary>
/// Procedurally fills a <see cref="World"/> with an entity-per-tile dungeon.
///
/// The generator runs in five passes:
///   1. <b>Place rooms.</b> Drop N non-overlapping rectangles. Each one is randomly tagged as
///      either "rectangular" or "cave"; cave rooms are larger because the cellular-automata
///      pass we run on them later eats away at their volume.
///   2. <b>Cellular-automata caves.</b> For every cave-tagged room, fill it with random noise
///      and run the classic 4-5 rule a few times. The result is an organic, irregular cavern
///      instead of a sharp rectangle.
///   3. <b>Connect rooms.</b> L-shaped tunnels from each room's centre to the next, plus a
///      flood-fill that prunes any disconnected cave fragments and forces the room centre to
///      stay reachable.
///   4. <b>Dead-end branches.</b> Walk every corridor cell and occasionally fork off a short
///      perpendicular stub. Stops at the first existing floor tile so a stub never accidentally
///      becomes a shortcut.
///   5. <b>Materialise.</b> Every floor cell becomes a Floor entity, every wall cell adjacent
///      to floor becomes a Blocker entity, and the player + goal are spawned in the first and
///      last room respectively.
/// </summary>
public sealed class DungeonGenerator
{
    // ----- inputs -----
    private readonly Random _rng;
    private readonly int _width;
    private readonly int _height;
    private readonly int _maxRooms;
    private readonly int _minRoomSize;
    private readonly int _maxRoomSize;
    private readonly double _caveProbability;
    private readonly int _caveSimulationSteps;
    private readonly double _caveInitialFillChance;
    private readonly double _branchProbability;
    private readonly int _branchMinLength;
    private readonly int _branchMaxLength;

    /// <summary>
    /// Working grid. <c>true</c> = floor (passable), <c>false</c> = unset (becomes wall or void).
    /// We mutate this throughout generation and only spawn entities at the very end.
    /// </summary>
    private readonly bool[,] _floor;

    /// <summary>
    /// Tracks which floor cells came from a corridor (vs from a room). The branch step uses this
    /// so it only spawns dead-ends off corridors, never out of room walls.
    /// </summary>
    private readonly bool[,] _isCorridor;

    private readonly List<Room> _rooms = new();

    public DungeonGenerator(
        int width = 80,
        int height = 40,
        int maxRooms = 14,
        int minRoomSize = 4,
        int maxRoomSize = 9,
        double caveProbability = 0.4,
        int caveSimulationSteps = 4,
        double caveInitialFillChance = 0.45,
        double branchProbability = 0.12,
        int branchMinLength = 3,
        int branchMaxLength = 7,
        int? seed = null)
    {
        _width = width;
        _height = height;
        _maxRooms = maxRooms;
        _minRoomSize = minRoomSize;
        _maxRoomSize = maxRoomSize;
        _caveProbability = caveProbability;
        _caveSimulationSteps = caveSimulationSteps;
        _caveInitialFillChance = caveInitialFillChance;
        _branchProbability = branchProbability;
        _branchMinLength = branchMinLength;
        _branchMaxLength = branchMaxLength;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _floor = new bool[width, height];
        _isCorridor = new bool[width, height];
    }

    public (int X, int Y) PlayerSpawn { get; private set; }
    public (int X, int Y) GoalSpawn { get; private set; }

    public void Generate(World world)
    {
        PlaceRooms();
        ApplyCellularAutomataToCaves();
        ConnectRooms();
        EnsureCaveCentersReachable();
        PruneDisconnectedFloor();
        AddDeadEndBranches();

        if (_rooms.Count == 0)
            throw new InvalidOperationException("Map generation produced no rooms; try larger dimensions.");

        PlayerSpawn = (_rooms[0].CenterX, _rooms[0].CenterY);
        GoalSpawn = (_rooms[^1].CenterX, _rooms[^1].CenterY);

        SpawnTiles(world);
        SpawnPlayer(world);
        SpawnGoal(world);
    }

    // ------------------------------------------------------------------
    // Pass 1 — place rooms
    // ------------------------------------------------------------------

    private void PlaceRooms()
    {
        for (int i = 0; i < _maxRooms; i++)
        {
            // Caves get a size bonus because cellular automata will eat ~30-50% of their volume.
            bool isCave = _rng.NextDouble() < _caveProbability;
            int minSize = isCave ? _minRoomSize + 3 : _minRoomSize;
            int maxSize = isCave ? _maxRoomSize + 5 : _maxRoomSize;

            int w = _rng.Next(minSize, maxSize + 1);
            int h = _rng.Next(minSize, maxSize + 1);
            // Clamp so we don't overflow the map even when the cave bonus pushed maxSize up.
            w = Math.Min(w, _width - 3);
            h = Math.Min(h, _height - 3);

            int x = _rng.Next(1, _width - w - 1);
            int y = _rng.Next(1, _height - h - 1);

            var candidate = new Room(x, y, w, h, isCave);
            if (_rooms.Any(r => r.OverlapsWithMargin(candidate))) continue;

            CarveRectangle(x, y, w, h);
            _rooms.Add(candidate);
        }
    }

    // ------------------------------------------------------------------
    // Pass 2 — cellular automata for cave-tagged rooms
    // ------------------------------------------------------------------

    /// <summary>
    /// For every cave room: scramble its rectangle into random noise, then run several iterations
    /// of the "4-5 rule" — a wall stays a wall if it has ≥4 wall neighbours, a floor becomes a
    /// wall if it has ≥5 wall neighbours. This rule is famously good at smoothing random noise
    /// into organic cavern shapes.
    /// </summary>
    private void ApplyCellularAutomataToCaves()
    {
        foreach (var room in _rooms)
        {
            if (!room.IsCave) continue;

            // 2a. Re-seed the room's interior with random noise.
            // We leave a 1-cell border of guaranteed wall so the simulation can't leak into
            // neighbouring rooms.
            for (int x = room.X + 1; x < room.X + room.W - 1; x++)
            {
                for (int y = room.Y + 1; y < room.Y + room.H - 1; y++)
                {
                    _floor[x, y] = _rng.NextDouble() >= _caveInitialFillChance;
                }
            }
            // The rim must start as wall so the automaton sees a boundary it can grow against.
            for (int x = room.X; x < room.X + room.W; x++)
            {
                _floor[x, room.Y] = false;
                _floor[x, room.Y + room.H - 1] = false;
            }
            for (int y = room.Y; y < room.Y + room.H; y++)
            {
                _floor[room.X, y] = false;
                _floor[room.X + room.W - 1, y] = false;
            }

            // 2b. Iterate. Each step needs a snapshot of the whole room because every cell is
            // computed from its current-step neighbours; if we mutated _floor in-place mid-step
            // we'd be reading half-old, half-new values and the simulation would drift.
            for (int step = 0; step < _caveSimulationSteps; step++)
                RunOneCaveStep(room);
        }
    }

    private void RunOneCaveStep(Room room)
    {
        int w = room.W, h = room.H;
        var next = new bool[w, h];

        for (int lx = 0; lx < w; lx++)
        {
            for (int ly = 0; ly < h; ly++)
            {
                int gx = room.X + lx;
                int gy = room.Y + ly;

                int wallNeighbours = CountWallNeighbours(gx, gy);

                // 4-5 rule:
                //   - if currently a wall, become floor only if surrounded by ≤3 walls (open up)
                //   - if currently floor, become wall if ≥5 walls press in (collapse)
                bool isFloor = _floor[gx, gy];
                if (isFloor)
                    next[lx, ly] = wallNeighbours < 5;
                else
                    next[lx, ly] = wallNeighbours <= 3;

                // Always force the rim to stay wall so caves don't bleed outside their footprint.
                if (lx == 0 || ly == 0 || lx == w - 1 || ly == h - 1)
                    next[lx, ly] = false;
            }
        }

        for (int lx = 0; lx < w; lx++)
            for (int ly = 0; ly < h; ly++)
                _floor[room.X + lx, room.Y + ly] = next[lx, ly];
    }

    private int CountWallNeighbours(int x, int y)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                // Out-of-bounds counts as wall — keeps caves clamped against map edges.
                if (!InBounds(nx, ny) || !_floor[nx, ny]) count++;
            }
        }
        return count;
    }

    // ------------------------------------------------------------------
    // Pass 3 — connect rooms with L-shaped corridors
    // ------------------------------------------------------------------

    private void ConnectRooms()
    {
        for (int i = 1; i < _rooms.Count; i++)
        {
            int px = _rooms[i - 1].CenterX, py = _rooms[i - 1].CenterY;
            int cx = _rooms[i].CenterX, cy = _rooms[i].CenterY;

            if (_rng.Next(2) == 0)
            {
                CarveHorizontalCorridor(px, cx, py);
                CarveVerticalCorridor(py, cy, cx);
            }
            else
            {
                CarveVerticalCorridor(py, cy, px);
                CarveHorizontalCorridor(px, cx, cy);
            }
        }
    }

    /// <summary>
    /// After CA, a cave's centre might have collapsed into wall — which would leave the corridor
    /// terminating in solid rock. Force a 3×3 cleared area at every cave centre so connections
    /// always land on floor.
    /// </summary>
    private void EnsureCaveCentersReachable()
    {
        foreach (var room in _rooms)
        {
            if (!room.IsCave) continue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = room.CenterX + dx, y = room.CenterY + dy;
                    if (InBounds(x, y)) _floor[x, y] = true;
                }
        }
    }

    /// <summary>
    /// Cellular automata loves to leave little disconnected pockets of floor — pretty to look at,
    /// awful to play because the goal might land on one. BFS-flood from the player spawn and turn
    /// every floor cell that flood didn't reach back into wall.
    /// </summary>
    private void PruneDisconnectedFloor()
    {
        if (_rooms.Count == 0) return;

        var (sx, sy) = (_rooms[0].CenterX, _rooms[0].CenterY);
        if (!_floor[sx, sy]) return; // very unlucky generation; bail without pruning

        var reachable = new bool[_width, _height];
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((sx, sy));
        reachable[sx, sy] = true;

        // 4-connectivity (no diagonals) — matches how the player actually moves.
        ReadOnlySpan<(int dx, int dy)> dirs = stackalloc (int, int)[]
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (!InBounds(nx, ny)) continue;
                if (reachable[nx, ny]) continue;
                if (!_floor[nx, ny]) continue;
                reachable[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                if (_floor[x, y] && !reachable[x, y])
                    _floor[x, y] = false;
    }

    // ------------------------------------------------------------------
    // Pass 4 — dead-end branches off corridors
    // ------------------------------------------------------------------

    /// <summary>
    /// Walk every cell flagged as corridor in <see cref="_isCorridor"/>. With a small probability,
    /// pick a perpendicular direction and dig a stub a few tiles long. The stub stops at any
    /// existing floor cell so a "dead-end" can't accidentally become a shortcut between rooms.
    /// </summary>
    private void AddDeadEndBranches()
    {
        // Snapshot corridor cells up front so newly carved branches don't seed yet more branches
        // (which would devolve into a snowflake of tunnels).
        var corridorCells = new List<(int x, int y)>();
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                if (_isCorridor[x, y]) corridorCells.Add((x, y));

        foreach (var (x, y) in corridorCells)
        {
            if (_rng.NextDouble() >= _branchProbability) continue;

            // Pick a perpendicular direction relative to the corridor's local axis.
            // Detection: if neighbours along X are floor, the corridor runs horizontally,
            // so the branch should go vertically (and vice versa). If both axes have floor
            // neighbours, we're at an L-bend — pick any axis.
            bool horizontal = IsFloorAt(x - 1, y) || IsFloorAt(x + 1, y);
            bool vertical = IsFloorAt(x, y - 1) || IsFloorAt(x, y + 1);

            int bdx, bdy;
            if (horizontal && !vertical) (bdx, bdy) = _rng.Next(2) == 0 ? (0, -1) : (0, 1);
            else if (vertical && !horizontal) (bdx, bdy) = _rng.Next(2) == 0 ? (-1, 0) : (1, 0);
            else
            {
                // Bend or isolated cell — random cardinal direction.
                ReadOnlySpan<(int, int)> all = stackalloc (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
                (bdx, bdy) = all[_rng.Next(4)];
            }

            int length = _rng.Next(_branchMinLength, _branchMaxLength + 1);
            int cx = x, cy = y;
            for (int step = 0; step < length; step++)
            {
                int nx = cx + bdx, ny = cy + bdy;
                if (!InBounds(nx, ny)) break;

                // Stop one cell short of the map edge so the branch always has a wall around it.
                if (nx <= 0 || ny <= 0 || nx >= _width - 1 || ny >= _height - 1) break;

                // Guard against making shortcuts: if the next cell is already floor, abort.
                // That would turn the "dead end" into a second path between rooms.
                if (_floor[nx, ny]) break;

                _floor[nx, ny] = true;
                cx = nx;
                cy = ny;
            }
        }
    }

    // ------------------------------------------------------------------
    // Carving primitives
    // ------------------------------------------------------------------

    private void CarveRectangle(int x, int y, int w, int h)
    {
        for (int i = x; i < x + w; i++)
            for (int j = y; j < y + h; j++)
                _floor[i, j] = true;
    }

    private void CarveHorizontalCorridor(int x1, int x2, int y)
    {
        int from = Math.Min(x1, x2);
        int to = Math.Max(x1, x2);
        for (int x = from; x <= to; x++)
        {
            if (!InBounds(x, y)) continue;
            // Only flag it as corridor where it passes through what was previously empty space —
            // running through an existing room shouldn't turn that room's floor into a stub source.
            if (!_floor[x, y]) _isCorridor[x, y] = true;
            _floor[x, y] = true;
        }
    }

    private void CarveVerticalCorridor(int y1, int y2, int x)
    {
        int from = Math.Min(y1, y2);
        int to = Math.Max(y1, y2);
        for (int y = from; y <= to; y++)
        {
            if (!InBounds(x, y)) continue;
            if (!_floor[x, y]) _isCorridor[x, y] = true;
            _floor[x, y] = true;
        }
    }

    // ------------------------------------------------------------------
    // Pass 5 — entity materialisation
    // ------------------------------------------------------------------

    private void SpawnTiles(World world)
    {
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                if (_floor[x, y])
                {
                    world.Create(
                        new Position(x, y),
                        new Renderable('.', ConsoleColor.DarkGray),
                        new Layer(0),
                        new Floor());
                }
                else if (HasFloorNeighbour(x, y))
                {
                    world.Create(
                        new Position(x, y),
                        new Renderable('#', ConsoleColor.DarkYellow),
                        new Layer(1),
                        new Blocker());
                }
            }
        }
    }

    private void SpawnPlayer(World world)
    {
        world.Create(
            new Position(PlayerSpawn.X, PlayerSpawn.Y),
            new Velocity(0, 0),
            new Renderable('@', ConsoleColor.White),
            new Layer(10),
            new PlayerTag());
    }

    private void SpawnGoal(World world)
    {
        world.Create(
            new Position(GoalSpawn.X, GoalSpawn.Y),
            new Renderable('>', ConsoleColor.Green),
            new Layer(5),
            new Goal());
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private bool InBounds(int x, int y) => x >= 0 && x < _width && y >= 0 && y < _height;

    private bool IsFloorAt(int x, int y) => InBounds(x, y) && _floor[x, y];

    private bool HasFloorNeighbour(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (InBounds(nx, ny) && _floor[nx, ny]) return true;
            }
        return false;
    }

    // ------------------------------------------------------------------
    // Internal record for room metadata
    // ------------------------------------------------------------------

    private readonly record struct Room(int X, int Y, int W, int H, bool IsCave)
    {
        public int CenterX => X + W / 2;
        public int CenterY => Y + H / 2;

        /// <summary>Overlap test with a 1-tile margin, so adjacent rooms always share a wall.</summary>
        public bool OverlapsWithMargin(Room other) =>
            X - 1 < other.X + other.W &&
            X + W > other.X - 1 &&
            Y - 1 < other.Y + other.H &&
            Y + H > other.Y - 1;
    }
}
