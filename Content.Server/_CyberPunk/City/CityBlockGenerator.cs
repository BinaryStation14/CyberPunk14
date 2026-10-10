using System.Linq;
using Content.Server._CyberPunk.Procgen;

namespace Content.Server._CyberPunk.City;

/// <summary>
/// How a built-up district looks: how its street pass is weighted, and what its buildings are made of.
/// </summary>
public sealed class BlockStyle
{
    /// <summary>Percent scales on the street pass's road, sidewalk and lot tiles.</summary>
    public int Roads = 50, Walks = 70, Lots = 200;

    /// <summary>Percent scale on lot cells inside a building, so higher makes bigger buildings.</summary>
    public int Sprawl = 100;

    /// <summary>Percent scale on lot cells with a party wall, so higher packs buildings side by side.</summary>
    public int Terraces = 100;

    public CityFloor Sidewalk = CityFloor.Sidewalk;
    public CityFloor[] Floors = { CityFloor.Steel };
    public CityStructure Wall = CityStructure.Wall;
    public CityStructure Window = CityStructure.Window;

    /// <summary>Share of street-facing walls without a door that get windows.</summary>
    public int WindowPercent = 50;

    /// <summary>Share of wall tiles left as a bare girder.</summary>
    public int DecayPercent;

    internal readonly Lazy<(CityBlockGenerator.StreetTile[] Tiles, WfcRules Rules)> StreetRules;

    public BlockStyle()
    {
        StreetRules = new Lazy<(CityBlockGenerator.StreetTile[], WfcRules)>(() => CityBlockGenerator.BuildStreetRules(this));
    }
}

/// <summary>
/// Fills a built-up district with streets and buildings, after Switchboard's <c>sb_procgen/src/district.rs</c>.
/// </summary>
/// <remarks>
/// The district is a lattice: every <see cref="Cell"/>th row and column of tiles is a boundary line, and the 4x4
/// tiles between lines form a cell. Walls and doors only stand on boundary lines, so neighbouring rooms share one
/// wall.
/// <list type="number">
/// <item>The street pass collapses each cell into a road, sidewalk or building lot. Its sockets keep roads joined
/// to roads, put sidewalk between every road and every lot, and decide which lots join into one building and which
/// meet at a party wall.</item>
/// <item>The room pass collapses each building's cells again, deciding for each shared side whether it is open
/// floor, a wall or a wall with a door. Rooms it leaves cut off get a door, so every room can be reached.</item>
/// </list>
/// </remarks>
public static class CityBlockGenerator
{
    public const int Cell = 5;

    private const int StreetAttempts = 100;
    private const int RoomAttempts = 20;
    private const int MinLotPercent = 20;

    internal enum Plot : byte
    {
        Road,
        Walk,
        Lot,
    }

    internal enum StreetSocket : byte
    {
        /// <summary>Road continuing along its length into more road.</summary>
        Street,
        /// <summary>The middle of a two-lane road, between its two halves.</summary>
        Median,
        /// <summary>A road's edge, meeting a sidewalk.</summary>
        RoadCurb,
        /// <summary>A sidewalk's edge, meeting a road.</summary>
        WalkCurb,
        /// <summary>Sidewalk continuing into sidewalk.</summary>
        Pave,
        /// <summary>A sidewalk meeting a building's outer wall.</summary>
        WalkFace,
        /// <summary>A building's outer wall, meeting a sidewalk.</summary>
        LotFace,
        /// <summary>Lot continuing into the same building.</summary>
        Same,
        /// <summary>A party wall between two buildings.</summary>
        Party,
    }

    internal readonly record struct StreetTile(Plot Plot, StreetSocket[] Sockets, int Weight);

    private enum RoomSocket : byte
    {
        Open,
        Wall,
        Door,
        /// <summary>Outside this building; only walls may face it.</summary>
        Void,
    }

    private enum EdgeKind : byte
    {
        Ground,
        Wall,
        /// <summary>A wall with a door <see cref="Edge.At"/> tiles along it.</summary>
        Door,
        /// <summary>An outer wall with two windows in the middle, between posts.</summary>
        Windows,
    }

    private readonly record struct Edge(EdgeKind Kind, CityFloor Floor = default, int At = 0);

    private static bool StreetSocketsFit(StreetSocket a, StreetSocket b)
    {
        return (a, b) switch
        {
            (StreetSocket.RoadCurb, StreetSocket.WalkCurb) => true,
            (StreetSocket.WalkCurb, StreetSocket.RoadCurb) => true,
            (StreetSocket.WalkFace, StreetSocket.LotFace) => true,
            (StreetSocket.LotFace, StreetSocket.WalkFace) => true,
            (StreetSocket.Street or StreetSocket.Median or StreetSocket.Pave or StreetSocket.Same or StreetSocket.Party, _) => a == b,
            _ => false,
        };
    }

    /// <summary>A weight scaled by a percentage, never scaled down to nothing.</summary>
    private static int Scale(int weight, int percent)
    {
        return weight == 0 ? 0 : Math.Max(1, weight * percent / 100);
    }

    /// <summary>Every combination of <paramref name="choices"/> on the four sides.</summary>
    private static IEnumerable<T[]> EverySide<T>(T[] choices)
    {
        var n = choices.Length;
        for (var code = 0; code < n * n * n * n; code++)
        {
            var sides = new T[4];
            var rest = code;
            for (var side = 0; side < 4; side++)
            {
                sides[side] = choices[rest % n];
                rest /= n;
            }

            yield return sides;
        }
    }

    /// <summary>
    /// The street pass's tiles. Weights favour long straight roads lined with sidewalk and buildings a few cells
    /// big, with the occasional plaza, alley and junction, scaled by the style.
    /// </summary>
    internal static (StreetTile[], WfcRules) BuildStreetRules(BlockStyle style)
    {
        var tiles = new List<StreetTile>();

        // Every road cell is one half of a two-lane road, with its other half across a median side: either a
        // straight stretch, or a quarter of a two-by-two junction or bend.
        foreach (var s in EverySide(new[] { StreetSocket.Street, StreetSocket.Median, StreetSocket.RoadCurb }))
        {
            var medians = Enumerable.Range(0, 4).Where(d => s[d] == StreetSocket.Median).ToArray();
            var weight = 0;
            if (medians.Length == 1)
            {
                var m = medians[0];
                var straight = s[(m + 1) % 4] == StreetSocket.Street && s[(m + 3) % 4] == StreetSocket.Street
                               && s[WfcWave.Opposite(m)] == StreetSocket.RoadCurb;
                weight = straight ? 40 : 0;
            }
            else if (medians.Length == 2 && medians[1] - medians[0] != 2)
            {
                weight = new[] { 2, 1, 1 }[s.Count(x => x == StreetSocket.Street)];
            }

            tiles.Add(new StreetTile(Plot.Road, s, Scale(weight, style.Roads)));
        }

        foreach (var s in EverySide(new[] { StreetSocket.WalkCurb, StreetSocket.Pave, StreetSocket.WalkFace }))
        {
            var curbs = s.Count(x => x == StreetSocket.WalkCurb);
            var faces = s.Count(x => x == StreetSocket.WalkFace);
            var facing = Enumerable.Range(0, 4)
                .Any(d => s[d] == StreetSocket.WalkCurb && s[WfcWave.Opposite(d)] == StreetSocket.WalkFace);
            var weight = (curbs, faces) switch
            {
                // A sidewalk between a road and a building.
                (1, 1) when facing => 60,
                // Wrapping round the corner of a block.
                (1, 2) => 6,
                (0, 1) or (0, 2) or (1, 1) or (2, 0) or (2, 1) or (1, 0) or (0, 0) => 1,
                // Curbs on several sides would start roads everywhere.
                _ => 0,
            };
            tiles.Add(new StreetTile(Plot.Walk, s, Scale(weight, style.Walks)));
        }

        foreach (var s in EverySide(new[] { StreetSocket.LotFace, StreetSocket.Same, StreetSocket.Party }))
        {
            var faces = s.Count(x => x == StreetSocket.LotFace);
            var parties = s.Count(x => x == StreetSocket.Party);
            var weight = (faces, parties) switch
            {
                (0, 0) => Scale(30, style.Sprawl),
                (0, _) => 1,
                (1, 0) => 30,
                (2, 0) => 15,
                (1, 1) or (2, 1) => Scale(6, style.Terraces),
                (3, 0) => 2,
                _ => 1,
            };
            tiles.Add(new StreetTile(Plot.Lot, s, Scale(weight, style.Lots)));
        }

        var array = tiles.ToArray();
        var rules = new WfcRules(array.Select(t => t.Weight).ToArray(),
            (a, direction, b) => StreetSocketsFit(array[a].Sockets[direction], array[b].Sockets[WfcWave.Opposite(direction)]));
        return (array, rules);
    }

    /// <summary>
    /// Generates a block <paramref name="width"/> by <paramref name="height"/> cells into the plan with its
    /// bottom-left tile at (<paramref name="ox"/>, <paramref name="oy"/>). Its edge is all road or sidewalk, so it
    /// joins the avenues round it.
    /// </summary>
    public static void Generate(CityPlan plan, int ox, int oy, int width, int height, BlockStyle style, CyberRng rng)
    {
        var grid = new Grid(width, height);
        var streets = SolveStreets(grid, style, rng.Fork(1));
        if (streets == null)
        {
            // A plaza, rather than no district at all.
            for (var y = 0; y < grid.TilesHigh; y++)
            {
                for (var x = 0; x < grid.TilesWide; x++)
                {
                    plan.Set(ox + x, oy + y, style.Sidewalk);
                }
            }

            return;
        }

        var layout = new Layout(grid, streets, style);
        var buildingRng = rng.Fork(2);
        var index = 0UL;
        foreach (var building in FindBuildings(grid, streets))
        {
            layout.AddBuilding(buildingRng.Fork(index++), building);
        }

        layout.PlaceLamps(rng.Fork(3));
        layout.Rasterize(plan, ox, oy, rng.Fork(4));
    }

    private readonly record struct Grid(int Width, int Height)
    {
        public int Length => Width * Height;
        public int TilesWide => Width * Cell + 1;
        public int TilesHigh => Height * Cell + 1;

        public int Index(int x, int y)
        {
            return y * Width + x;
        }

        public (int X, int Y) Position(int i)
        {
            return (i % Width, i / Width);
        }

        public int? Neighbour(int i, int direction)
        {
            var (x, y) = Position(i);
            var (dx, dy) = WfcWave.Directions[direction];
            var (nx, ny) = (x + dx, y + dy);
            return nx >= 0 && ny >= 0 && nx < Width && ny < Height ? Index(nx, ny) : null;
        }
    }

    /// <summary>
    /// Collapses the street pass. A result is only kept if every road and sidewalk joins up with the district's
    /// edge, every building has a sidewalk to open onto, and there is enough to build on.
    /// </summary>
    private static StreetTile[]? SolveStreets(Grid grid, BlockStyle style, CyberRng rng)
    {
        var (tiles, rules) = style.StreetRules.Value;
        for (var attempt = 0UL; attempt < StreetAttempts; attempt++)
        {
            var wave = new WfcWave(grid.Width, grid.Height, rules.All());
            // Nothing on the edge may need a neighbour beyond it, except roads and sidewalks, which carry on into
            // the avenue, and buildings' outer walls, which front onto the avenue's sidewalk.
            for (var i = 0; i < grid.Length; i++)
            {
                var outward = Enumerable.Range(0, 4).Where(d => grid.Neighbour(i, d) == null).ToArray();
                if (outward.Length == 0)
                    continue;

                var safe = new WfcSet();
                for (var t = 0; t < tiles.Length; t++)
                {
                    if (tiles[t].Weight > 0 && outward.All(d => tiles[t].Sockets[d] is StreetSocket.Street
                            or StreetSocket.RoadCurb or StreetSocket.WalkCurb or StreetSocket.Pave or StreetSocket.LotFace))
                    {
                        safe.Insert(t);
                    }
                }

                var (x, y) = grid.Position(i);
                wave.Set(x, y, safe);
            }

            var attemptRng = rng.Fork(attempt);
            if (wave.Solve(rules, ref attemptRng) is not { } solution)
                continue;

            var cells = solution.Select(t => tiles[t]).ToArray();
            if (StreetsAreUsable(grid, cells))
                return cells;
        }

        return null;
    }

    private static bool StreetsAreUsable(Grid grid, StreetTile[] cells)
    {
        if (cells.Count(c => c.Plot == Plot.Lot) * 100 / grid.Length < MinLotPercent)
            return false;

        if (!FindBuildings(grid, cells).All(b => b.Any(c => cells[c].Sockets.Contains(StreetSocket.LotFace))))
            return false;

        // Every road and sidewalk can be walked to from the avenues.
        var reached = new bool[grid.Length];
        var stack = new Stack<int>();
        for (var i = 0; i < grid.Length; i++)
        {
            var onEdge = Enumerable.Range(0, 4).Any(d => grid.Neighbour(i, d) == null);
            if (onEdge && cells[i].Plot != Plot.Lot)
            {
                reached[i] = true;
                stack.Push(i);
            }
        }

        Flood(grid, reached, stack, (_, _, n) => cells[n].Plot != Plot.Lot);
        return Enumerable.Range(0, grid.Length).All(i => cells[i].Plot == Plot.Lot || reached[i]);
    }

    private static void Flood(Grid grid, bool[] reached, Stack<int> stack, Func<int, int, int, bool> crosses)
    {
        while (stack.TryPop(out var i))
        {
            for (var d = 0; d < 4; d++)
            {
                if (grid.Neighbour(i, d) is not { } n || reached[n] || !crosses(i, d, n))
                    continue;

                reached[n] = true;
                stack.Push(n);
            }
        }
    }

    /// <summary>Groups lot cells joined by <see cref="StreetSocket.Same"/> sides into buildings, in cell order.</summary>
    private static List<List<int>> FindBuildings(Grid grid, StreetTile[] cells)
    {
        var assigned = new bool[grid.Length];
        var buildings = new List<List<int>>();
        for (var i = 0; i < grid.Length; i++)
        {
            if (cells[i].Plot != Plot.Lot || assigned[i])
                continue;

            var reached = new bool[grid.Length];
            reached[i] = true;
            var stack = new Stack<int>();
            stack.Push(i);
            Flood(grid, reached, stack, (c, d, _) => cells[c].Sockets[d] == StreetSocket.Same);
            var members = Enumerable.Range(0, grid.Length).Where(c => reached[c]).ToList();
            foreach (var c in members)
            {
                assigned[c] = true;
            }

            buildings.Add(members);
        }

        return buildings;
    }

    /// <summary>The street and room passes' results, ready to turn into tiles.</summary>
    private sealed class Layout
    {
        private readonly Grid _grid;
        private readonly StreetTile[] _streets;
        private readonly BlockStyle _style;

        /// <summary>Ground inside each cell.</summary>
        private readonly CityFloor[] _ground;

        /// <summary>Each cell's sides, the same seen from either side.</summary>
        private readonly Edge[][] _edges;

        private readonly List<(int Cell, int X, int Y)> _lamps = new();

        public Layout(Grid grid, StreetTile[] streets, BlockStyle style)
        {
            _grid = grid;
            _streets = streets;
            _style = style;
            _ground = streets.Select(c => c.Plot == Plot.Road ? CityFloor.Asphalt : style.Sidewalk).ToArray();
            _edges = new Edge[grid.Length][];
            for (var i = 0; i < grid.Length; i++)
            {
                _edges[i] = new Edge[4];
                for (var d = 0; d < 4; d++)
                {
                    _edges[i][d] = grid.Neighbour(i, d) is not { } n
                        ? new Edge(streets[i].Plot == Plot.Lot ? EdgeKind.Wall : EdgeKind.Ground, _ground[i])
                        : (streets[i].Plot, streets[n].Plot) switch
                        {
                            (Plot.Road, Plot.Road) => new Edge(EdgeKind.Ground, CityFloor.Asphalt),
                            (Plot.Lot, _) or (_, Plot.Lot) => new Edge(EdgeKind.Wall),
                            _ => new Edge(EdgeKind.Ground, style.Sidewalk),
                        };
                }
            }
        }

        private void SetEdge(int cell, int direction, Edge edge)
        {
            _edges[cell][direction] = edge;
            if (_grid.Neighbour(cell, direction) is { } n)
                _edges[n][WfcWave.Opposite(direction)] = edge;
        }

        /// <summary>Runs the room pass for one building and records its rooms, doors and floors.</summary>
        public void AddBuilding(CyberRng rng, List<int> cells)
        {
            var sides = SolveRooms(rng.Fork(0), cells);

            // Join cells into rooms across open sides, then make sure every room can be reached: wherever a wall
            // separates two parts of the building that don't connect yet, it gets a door.
            var local = cells.Select((c, a) => (c, a)).ToDictionary(p => p.c, p => p.a);
            var rooms = new UnionFind(cells.Count);
            var reach = new UnionFind(cells.Count);
            var walls = new List<(int A, int D, int B)>();
            for (var a = 0; a < cells.Count; a++)
            {
                for (var d = 0; d < 4; d++)
                {
                    if (_streets[cells[a]].Sockets[d] != StreetSocket.Same
                        || _grid.Neighbour(cells[a], d) is not { } n
                        || !local.TryGetValue(n, out var b)
                        || a > b)
                    {
                        continue;
                    }

                    switch (sides[a][d])
                    {
                        case RoomSocket.Open:
                            rooms.Join(a, b);
                            reach.Join(a, b);
                            break;
                        case RoomSocket.Door:
                            reach.Join(a, b);
                            break;
                        default:
                            walls.Add((a, d, b));
                            break;
                    }
                }
            }

            rng.Shuffle(walls);
            foreach (var (a, d, b) in walls)
            {
                if (!reach.Join(a, b))
                    continue;

                sides[a][d] = RoomSocket.Door;
                sides[b][WfcWave.Opposite(d)] = RoomSocket.Door;
            }

            // One main floor for the building, with an accent floor in some rooms.
            var main = rng.Pick(_style.Floors);
            var accent = rng.Pick(_style.Floors);
            var roomFloors = new CityFloor?[cells.Count];
            for (var a = 0; a < cells.Count; a++)
            {
                var root = rooms.Find(a);
                roomFloors[root] ??= rng.Chance(1, 3) ? accent : main;
            }

            for (var a = 0; a < cells.Count; a++)
            {
                var cell = cells[a];
                var floor = roomFloors[rooms.Find(a)]!.Value;
                _ground[cell] = floor;
                for (var d = 0; d < 4; d++)
                {
                    // Each inner side is shared, so it's only set from one cell.
                    var alreadySet = _grid.Neighbour(cell, d) is { } n && local.TryGetValue(n, out var b) && b < a;
                    if (_streets[cell].Sockets[d] != StreetSocket.Same || alreadySet)
                        continue;

                    SetEdge(cell, d, sides[a][d] switch
                    {
                        RoomSocket.Open => new Edge(EdgeKind.Ground, floor),
                        RoomSocket.Door => new Edge(EdgeKind.Door, At: rng.Range(1, 2)),
                        _ => new Edge(EdgeKind.Wall),
                    });
                }
            }

            PlaceEntrances(rng.Fork(1), cells);
            PlaceWindows(rng.Fork(2), cells);
        }

        private List<(int Cell, int Direction)> Faces(List<int> cells)
        {
            return cells
                .SelectMany(c => Enumerable.Range(0, 4).Select(d => (c, d)))
                .Where(f => _streets[f.c].Sockets[f.d] == StreetSocket.LotFace && _edges[f.c][f.d].Kind == EdgeKind.Wall)
                .ToList();
        }

        /// <summary>Puts a door in one, sometimes two, of the walls facing the sidewalk.</summary>
        private void PlaceEntrances(CyberRng rng, List<int> cells)
        {
            var faces = Faces(cells);
            var count = faces.Count > 2 && rng.Chance(1, 3) ? 2 : 1;
            rng.Shuffle(faces);
            foreach (var (cell, direction) in faces.Take(count))
            {
                SetEdge(cell, direction, new Edge(EdgeKind.Door, At: rng.Range(1, 2)));
            }
        }

        private void PlaceWindows(CyberRng rng, List<int> cells)
        {
            foreach (var (cell, direction) in Faces(cells))
            {
                if (rng.Range(0, 99) < _style.WindowPercent)
                    SetEdge(cell, direction, new Edge(EdgeKind.Windows));
            }
        }

        /// <summary>Street lamps on some of the sidewalk cells along a road.</summary>
        public void PlaceLamps(CyberRng rng)
        {
            for (var i = 0; i < _grid.Length; i++)
            {
                if (_streets[i].Plot != Plot.Walk || !rng.Chance(1, 4))
                    continue;

                var curb = Array.IndexOf(_streets[i].Sockets, StreetSocket.WalkCurb);
                if (curb < 0)
                    continue;

                // On the inside tile nearest the road, halfway along.
                var (dx, dy) = WfcWave.Directions[curb];
                _lamps.Add((i, dx == 0 ? 1 : dx > 0 ? 3 : 0, dy == 0 ? 1 : dy > 0 ? 3 : 0));
            }
        }

        public void Rasterize(CityPlan plan, int ox, int oy, CyberRng rng)
        {
            for (var y = 0; y < _grid.TilesHigh; y++)
            {
                for (var x = 0; x < _grid.TilesWide; x++)
                {
                    var (floor, structure) = Tile(x, y);
                    if (structure == CityStructure.Wall)
                        structure = rng.Range(0, 99) < _style.DecayPercent ? CityStructure.Girder : _style.Wall;
                    else if (structure == CityStructure.Window)
                        structure = _style.Window;

                    plan.Set(ox + x, oy + y, floor, structure);
                }
            }

            foreach (var (cell, x, y) in _lamps)
            {
                var (cx, cy) = _grid.Position(cell);
                plan.SetStructure(ox + cx * Cell + 1 + x, oy + cy * Cell + 1 + y, CityStructure.Lamp);
            }
        }

        private (CityFloor, CityStructure) Tile(int x, int y)
        {
            var (lx, ly) = (x % Cell == 0, y % Cell == 0);
            var (cx, cy) = (x / Cell, y / Cell);
            if (!lx && !ly)
                return (_ground[_grid.Index(cx, cy)], CityStructure.None);

            if (lx && !ly)
            {
                // The last line runs along the high side of the last cell.
                var west = cx < _grid.Width;
                return EdgeTile(_grid.Index(Math.Min(cx, _grid.Width - 1), cy), west ? 3 : 1, y % Cell - 1);
            }

            if (!lx && ly)
            {
                var south = cy < _grid.Height;
                return EdgeTile(_grid.Index(cx, Math.Min(cy, _grid.Height - 1)), south ? 2 : 0, x % Cell - 1);
            }

            return CornerTile(cx, cy);
        }

        /// <summary>A tile on a boundary line, <paramref name="along"/> tiles from the line's start.</summary>
        private (CityFloor, CityStructure) EdgeTile(int cell, int direction, int along)
        {
            var floor = _ground[cell];
            var edge = _edges[cell][direction];
            return edge.Kind switch
            {
                EdgeKind.Ground => (edge.Floor, CityStructure.None),
                EdgeKind.Door when edge.At == along => (floor, CityStructure.Door),
                EdgeKind.Windows when along is 1 or 2 => (floor, CityStructure.Window),
                _ => (floor, CityStructure.Wall),
            };
        }

        /// <summary>
        /// A tile where boundary lines cross: a wall post if any line meeting it is a wall, otherwise ground,
        /// preferring sidewalk.
        /// </summary>
        private (CityFloor, CityStructure) CornerTile(int lx, int ly)
        {
            // The up to four cells round the corner, with the sides of each that run into it.
            var around = new[]
            {
                (lx - 1, ly - 1, 0, 1),
                (lx, ly - 1, 0, 3),
                (lx - 1, ly, 2, 1),
                (lx, ly, 2, 3),
            };
            var grounds = new List<CityFloor>();
            foreach (var (cx, cy, d1, d2) in around)
            {
                if (cx < 0 || cy < 0 || cx >= _grid.Width || cy >= _grid.Height)
                    continue;

                var cell = _grid.Index(cx, cy);
                foreach (var d in new[] { d1, d2 })
                {
                    var edge = _edges[cell][d];
                    if (edge.Kind != EdgeKind.Ground)
                        return (_ground[cell], CityStructure.Wall);

                    grounds.Add(edge.Floor);
                }
            }

            var floor = grounds.Contains(_style.Sidewalk) ? _style.Sidewalk : grounds.FirstOrDefault(CityFloor.Asphalt);
            return (floor, CityStructure.None);
        }

        /// <summary>
        /// Collapses one building's cells into open, wall and door sides. Sides that face outside the building are
        /// always walls; if the pass keeps failing, every inner side starts as a wall and the door fix-up makes it
        /// a building of single-cell rooms.
        /// </summary>
        private RoomSocket[][] SolveRooms(CyberRng rng, List<int> cells)
        {
            var (tiles, rules) = RoomRules.Value;
            var voidTile = tiles.Length - 1;
            var minX = cells.Min(c => _grid.Position(c).X);
            var minY = cells.Min(c => _grid.Position(c).Y);
            var w = cells.Max(c => _grid.Position(c).X) - minX + 1;
            var h = cells.Max(c => _grid.Position(c).Y) - minY + 1;

            for (var attempt = 0UL; attempt < RoomAttempts; attempt++)
            {
                var wave = new WfcWave(w, h, WfcSet.Single(voidTile));
                foreach (var cell in cells)
                {
                    var options = new WfcSet();
                    for (var t = 0; t < voidTile; t++)
                    {
                        if (Enumerable.Range(0, 4).All(d => _streets[cell].Sockets[d] == StreetSocket.Same
                                                            || tiles[t].Sockets[d] == RoomSocket.Wall))
                        {
                            options.Insert(t);
                        }
                    }

                    var (x, y) = _grid.Position(cell);
                    wave.Set(x - minX, y - minY, options);
                }

                var attemptRng = rng.Fork(attempt);
                if (wave.Solve(rules, ref attemptRng) is not { } solution)
                    continue;

                return cells.Select(cell =>
                {
                    var (x, y) = _grid.Position(cell);
                    return (RoomSocket[]) tiles[solution[(y - minY) * w + x - minX]].Sockets.Clone();
                }).ToArray();
            }

            return cells.Select(_ => new[] { RoomSocket.Wall, RoomSocket.Wall, RoomSocket.Wall, RoomSocket.Wall }).ToArray();
        }
    }

    /// <summary>
    /// The room pass's tiles: every mix of open, wall and door sides, plus the void outside the building. Weights
    /// favour rooms of two to four cells with a door or two each.
    /// </summary>
    private static readonly Lazy<((RoomSocket[] Sockets, int Weight)[], WfcRules)> RoomRules = new(() =>
    {
        var tiles = EverySide(new[] { RoomSocket.Open, RoomSocket.Wall, RoomSocket.Door })
            .Select(s =>
            {
                var open = new[] { 2, 10, 10, 4, 1 }[s.Count(x => x == RoomSocket.Open)];
                var doors = new[] { 6, 3, 1, 0, 0 }[s.Count(x => x == RoomSocket.Door)];
                return (Sockets: s, Weight: open * doors);
            })
            .Append((Sockets: new[] { RoomSocket.Void, RoomSocket.Void, RoomSocket.Void, RoomSocket.Void }, Weight: 1))
            .ToArray();

        static bool Fits(RoomSocket a, RoomSocket b)
        {
            return a == b || (a, b) is (RoomSocket.Void, RoomSocket.Wall) or (RoomSocket.Wall, RoomSocket.Void);
        }

        var rules = new WfcRules(tiles.Select(t => t.Weight).ToArray(),
            (a, direction, b) => Fits(tiles[a].Sockets[direction], tiles[b].Sockets[WfcWave.Opposite(direction)]));
        return (tiles, rules);
    });

    private sealed class UnionFind
    {
        private readonly int[] _parent;

        public UnionFind(int n)
        {
            _parent = Enumerable.Range(0, n).ToArray();
        }

        public int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }

            return i;
        }

        /// <summary>Joins the sets holding a and b. False if they were already one set.</summary>
        public bool Join(int a, int b)
        {
            var (ra, rb) = (Find(a), Find(b));
            if (ra == rb)
                return false;

            _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            return true;
        }
    }
}
