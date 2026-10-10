using System.Linq;
using Content.Server._CyberPunk.Procgen;

namespace Content.Server._CyberPunk.City;

/// <summary>
/// Generates a coastal city with badlands round it. The same seed always gives the same city.
/// </summary>
/// <remarks>
/// <para>
/// The map is a square grid of districts with an avenue between every two of them and round the outside. First
/// a Wave Function Collapse pass picks each district's <see cref="CityZone"/>. Which zones may sit side by side is
/// fixed (no corporate towers next to the badlands), and each district is narrowed beforehand by where it lies:
/// a coastline wanders along one side of the map with the sea beyond it, and the city grows out from a centre
/// near the shore, densest in the middle and thinning to badlands at the edge.
/// </para>
/// <para>
/// Then the ground is painted. The sea and the beaches follow a smoothed, noisy blend of the districts, so the
/// shore doesn't follow district edges. Built-up districts are filled with streets and buildings by
/// <see cref="CityBlockGenerator"/>, styled per zone. Avenues are streets wherever they touch the city, and two
/// highways carry on through the badlands.
/// </para>
/// </remarks>
public static class CityGenerator
{
    public const int DistrictCells = 8;
    public const int DistrictSize = DistrictCells * CityBlockGenerator.Cell + 1;
    /// <summary>
    /// How wide an avenue is. A district and an avenue together are a whole number of cells, so neighbouring
    /// districts can be joined into one block over the avenue between them.
    /// </summary>
    public const int Avenue = 9;

    public const int Pitch = DistrictSize + Avenue;

    private const int ZoneAttempts = 100;

    private const int ZoneCount = (int) CityZone.Solar + 1;

    private static readonly int[] ZoneWeights =
    {
        10, // Ocean
        6, // Coast
        4, // Docks
        10, // Downtown
        8, // Commercial
        8, // Residential
        5, // Industrial
        6, // Slums
        2, // Park
        10, // Badlands
        6, // Scrub
        1, // Solar
    };

    /// <summary>The zones each zone may sit beside, besides itself.</summary>
    private static readonly Dictionary<CityZone, CityZone[]> Touches = new()
    {
        [CityZone.Ocean] = new[] { CityZone.Coast, CityZone.Docks },
        [CityZone.Coast] = new[]
        {
            CityZone.Docks, CityZone.Downtown, CityZone.Commercial, CityZone.Residential, CityZone.Slums,
            CityZone.Park, CityZone.Badlands, CityZone.Scrub,
        },
        [CityZone.Docks] = new[] { CityZone.Downtown, CityZone.Commercial, CityZone.Industrial, CityZone.Slums },
        [CityZone.Downtown] = new[] { CityZone.Commercial, CityZone.Residential, CityZone.Park },
        [CityZone.Commercial] = new[] { CityZone.Residential, CityZone.Industrial, CityZone.Slums, CityZone.Park },
        [CityZone.Residential] = new[] { CityZone.Industrial, CityZone.Slums, CityZone.Park, CityZone.Scrub },
        [CityZone.Industrial] = new[] { CityZone.Slums, CityZone.Badlands, CityZone.Scrub, CityZone.Solar },
        [CityZone.Slums] = new[] { CityZone.Badlands, CityZone.Scrub },
        [CityZone.Park] = Array.Empty<CityZone>(),
        [CityZone.Badlands] = new[] { CityZone.Scrub, CityZone.Solar },
        [CityZone.Scrub] = new[] { CityZone.Solar },
        [CityZone.Solar] = Array.Empty<CityZone>(),
    };

    private static readonly WfcRules ZoneRules = new(ZoneWeights, (a, _, b) => a == b
        || Array.IndexOf(Touches[(CityZone) a], (CityZone) b) >= 0
        || Array.IndexOf(Touches[(CityZone) b], (CityZone) a) >= 0);

    private static readonly Dictionary<CityZone, BlockStyle> Styles = new()
    {
        [CityZone.Downtown] = new BlockStyle
        {
            Sprawl = 250,
            Terraces = 50,
            Floors = new[] { CityFloor.White, CityFloor.Dark, CityFloor.Marble },
            Wall = CityStructure.WallReinforced,
            Window = CityStructure.WindowReinforced,
            WindowPercent = 80,
        },
        [CityZone.Commercial] = new BlockStyle
        {
            Floors = new[] { CityFloor.Steel, CityFloor.White, CityFloor.Wood, CityFloor.Dark },
            Window = CityStructure.WindowTinted,
            WindowPercent = 60,
        },
        [CityZone.Residential] = new BlockStyle
        {
            Sprawl = 50,
            Terraces = 250,
            Floors = new[] { CityFloor.Wood, CityFloor.Steel, CityFloor.Wood },
            Wall = CityStructure.WallBrick,
        },
        [CityZone.Industrial] = new BlockStyle
        {
            Roads = 70,
            Sprawl = 300,
            Sidewalk = CityFloor.Concrete,
            Floors = new[] { CityFloor.Steel, CityFloor.SteelDirty, CityFloor.Plating },
            Wall = CityStructure.WallConcrete,
            WindowPercent = 15,
        },
        [CityZone.Docks] = new BlockStyle
        {
            Roads = 80,
            Sprawl = 400,
            Terraces = 30,
            Sidewalk = CityFloor.Concrete,
            Floors = new[] { CityFloor.Plating, CityFloor.SteelDirty },
            Wall = CityStructure.WallReinforced,
            WindowPercent = 10,
        },
        [CityZone.Slums] = new BlockStyle
        {
            Walks = 150,
            Sprawl = 30,
            Terraces = 300,
            Sidewalk = CityFloor.OldConcrete,
            Floors = new[] { CityFloor.SteelDirty, CityFloor.Plating, CityFloor.OldConcrete },
            Wall = CityStructure.WallRust,
            WindowPercent = 25,
            DecayPercent = 6,
        },
    };

    public static bool IsBuiltUp(CityZone zone)
    {
        return zone is >= CityZone.Docks and <= CityZone.Park;
    }

    /// <summary>The first tile of district <paramref name="d"/> along either axis.</summary>
    public static int Origin(int d)
    {
        return Avenue + d * Pitch;
    }

    /// <summary>How many cells a block <paramref name="districts"/> districts across is, avenues between included.</summary>
    private static int BlockCells(int districts)
    {
        return (districts * Pitch - Avenue - 1) / CityBlockGenerator.Cell;
    }

    /// <summary>
    /// Every district, as blocks: (x, y) of its bottom-left district and its size in districts. Built-up districts
    /// are sometimes joined with neighbours of the same zone into one block of two or four, so the avenues don't
    /// make a regular grid. The city centre stays on its own, so the junction at its corner is always a street.
    /// </summary>
    private static List<(int X, int Y, int W, int H)> Blocks(CityPlan plan, Shape shape, CyberRng rng)
    {
        var n = plan.Districts;
        var taken = new bool[n * n];
        var blocks = new List<(int, int, int, int)>();
        var sizes = new List<(int W, int H)> { (2, 2), (2, 1), (1, 2) };
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                if (taken[y * n + x])
                    continue;

                var zone = plan.Zone(x, y);
                bool Joins(int jx, int jy)
                {
                    return jx < n && jy < n && !taken[jy * n + jx] && plan.Zone(jx, jy) == zone && (jx, jy) != shape.Centre;
                }

                var (w, h) = (1, 1);
                if (Styles.ContainsKey(zone) && (x, y) != shape.Centre && rng.Chance(1, 2))
                {
                    rng.Shuffle(sizes);
                    foreach (var (sw, sh) in sizes)
                    {
                        var fits = true;
                        for (var by = y; by < y + sh; by++)
                        {
                            for (var bx = x; bx < x + sw; bx++)
                            {
                                fits &= Joins(bx, by);
                            }
                        }

                        if (fits)
                        {
                            (w, h) = (sw, sh);
                            break;
                        }
                    }
                }

                for (var by = y; by < y + h; by++)
                {
                    for (var bx = x; bx < x + w; bx++)
                    {
                        taken[by * n + bx] = true;
                    }
                }

                blocks.Add((x, y, w, h));
            }
        }

        return blocks;
    }

    public static CityPlan Generate(ulong seed, int districts = 8)
    {
        if (districts < 4)
            throw new ArgumentOutOfRangeException(nameof(districts), "a city needs at least 4 districts a side");

        var rng = new CyberRng(seed);
        var plan = new CityPlan(districts);
        var shape = new Shape(districts, rng.Fork(1));
        SolveZones(plan, shape, rng.Fork(2));
        ClearStrandedDistricts(plan, shape);

        var terrain = new Terrain(plan, rng.Fork(3).NextU64());
        terrain.Paint();
        PaintAvenues(plan, shape);

        var districtRng = rng.Fork(4);
        foreach (var (dx, dy, w, h) in Blocks(plan, shape, rng.Fork(5)))
        {
            var zoneRng = districtRng.Fork((ulong) (dy * districts + dx));
            var (ox, oy) = (Origin(dx), Origin(dy));
            var zone = plan.Zone(dx, dy);
            if (Styles.TryGetValue(zone, out var style))
            {
                CityBlockGenerator.Generate(plan, ox, oy, BlockCells(w), BlockCells(h), style, zoneRng);
                continue;
            }

            switch (zone)
            {
                case CityZone.Park:
                    PaintPark(plan, ox, oy, zoneRng);
                    break;
                case CityZone.Solar:
                    PaintSolarFarm(plan, ox, oy);
                    break;
                case CityZone.Badlands:
                    if (zoneRng.Chance(1, 2))
                        PaintRuin(plan, ox, oy, zoneRng);
                    break;
            }
        }

        var (cx, cy) = shape.Centre;
        plan.Spawn = (cx * Pitch + Avenue / 2, cy * Pitch + Avenue / 2);
        return plan;
    }

    /// <summary>
    /// Where the sea and the city lie: the coast runs along one side of the map, and the city grows out from a
    /// centre one district in from the shore.
    /// </summary>
    private sealed class Shape
    {
        public readonly (int X, int Y) Centre;
        private readonly int _districts;
        private readonly int _side;
        private readonly int[] _shore;
        private readonly float _radius;
        private readonly float[] _jitter;

        public Shape(int districts, CyberRng rng)
        {
            _districts = districts;
            _side = rng.Range(0, 3);

            // How many districts of sea there are at each point along the coast.
            _shore = new int[districts];
            var (shallowest, deepest) = (Math.Max(1, districts / 4), districts / 3 + 1);
            _shore[0] = rng.Range(shallowest, deepest);
            for (var v = 1; v < districts; v++)
            {
                _shore[v] = Math.Clamp(_shore[v - 1] + rng.Range(-1, 1), shallowest, deepest);
            }

            var cv = rng.Range(districts / 2 - 1, districts / 2);
            Centre = FromCoast(_shore[cv] + 1, cv);
            _radius = districts * 0.27f + rng.Range(0, 6) / 10f;
            _jitter = new float[districts * districts];
            for (var i = 0; i < _jitter.Length; i++)
            {
                _jitter[i] = rng.Range(-5, 5) / 10f;
            }
        }

        /// <summary>
        /// A district's position as (u, v): u counts inland from the sea's edge of the map, v runs along the coast.
        /// </summary>
        private (int U, int V) ToCoast(int x, int y)
        {
            var last = _districts - 1;
            return _side switch
            {
                0 => (x, y),
                1 => (last - x, y),
                2 => (y, x),
                _ => (last - y, x),
            };
        }

        private (int X, int Y) FromCoast(int u, int v)
        {
            var last = _districts - 1;
            return _side switch
            {
                0 => (u, v),
                1 => (last - u, v),
                2 => (v, u),
                _ => (v, last - u),
            };
        }

        /// <summary>
        /// The zones a district may be before the zone pass.
        /// </summary>
        public CityZone[] Allowed(int x, int y)
        {
            if ((x, y) == Centre)
                return new[] { CityZone.Downtown };

            var (u, v) = ToCoast(x, y);
            var shore = _shore[v];
            if (u < shore - 1)
                return new[] { CityZone.Ocean };

            if (u == shore - 1)
                return new[] { CityZone.Ocean, CityZone.Coast, CityZone.Docks };

            var (cu, cv) = ToCoast(Centre.X, Centre.Y);
            var r = MathF.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv)) + _jitter[y * _districts + x];
            var zones = new List<CityZone>();
            if (r <= _radius * 0.5f)
                zones.AddRange(new[] { CityZone.Downtown, CityZone.Commercial, CityZone.Park });
            else if (r <= _radius * 0.8f)
                zones.AddRange(new[] { CityZone.Commercial, CityZone.Residential, CityZone.Industrial, CityZone.Slums, CityZone.Park });
            else if (r <= _radius)
                zones.AddRange(new[] { CityZone.Residential, CityZone.Industrial, CityZone.Slums, CityZone.Commercial });
            else if (r <= _radius + 1.2f)
                zones.AddRange(new[] { CityZone.Slums, CityZone.Industrial, CityZone.Residential, CityZone.Scrub, CityZone.Badlands });
            else
                zones.AddRange(new[] { CityZone.Badlands, CityZone.Scrub, CityZone.Solar });

            if (u == shore)
            {
                zones.Add(CityZone.Coast);
                if (r <= _radius)
                    zones.Add(CityZone.Docks);
            }

            return zones.ToArray();
        }

        /// <summary>A district's zone if the pass can't be solved: just sea, shore and badlands.</summary>
        public CityZone Fallback(int x, int y)
        {
            var (u, v) = ToCoast(x, y);
            return u < _shore[v] - 1 ? CityZone.Ocean : u == _shore[v] - 1 ? CityZone.Coast : CityZone.Badlands;
        }
    }

    private static void SolveZones(CityPlan plan, Shape shape, CyberRng rng)
    {
        var n = plan.Districts;
        for (var attempt = 0UL; attempt < ZoneAttempts; attempt++)
        {
            var wave = new WfcWave(n, n, ZoneRules.All());
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var options = new WfcSet();
                    foreach (var zone in shape.Allowed(x, y))
                    {
                        options.Insert((int) zone);
                    }

                    wave.Set(x, y, options);
                }
            }

            var attemptRng = rng.Fork(attempt);
            if (wave.Solve(ZoneRules, ref attemptRng) is not { } solution)
                continue;

            for (var i = 0; i < solution.Length; i++)
            {
                plan.Zones[i] = (CityZone) solution[i];
            }

            return;
        }

        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                plan.Zones[y * n + x] = shape.Fallback(x, y);
            }
        }
    }

    /// <summary>
    /// Built-up districts that the city's streets don't reach, such as one on its own out at sea, are left as
    /// open land instead.
    /// </summary>
    private static void ClearStrandedDistricts(CityPlan plan, Shape shape)
    {
        var n = plan.Districts;
        var reached = new bool[n * n];
        var stack = new Stack<(int X, int Y)>();
        stack.Push(shape.Centre);
        reached[shape.Centre.Y * n + shape.Centre.X] = true;
        while (stack.TryPop(out var d))
        {
            foreach (var (dx, dy) in WfcWave.Directions)
            {
                var (x, y) = (d.X + dx, d.Y + dy);
                if (x < 0 || y < 0 || x >= n || y >= n || reached[y * n + x] || !IsBuiltUp(plan.Zone(x, y)))
                    continue;

                reached[y * n + x] = true;
                stack.Push((x, y));
            }
        }

        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                if (reached[y * n + x] || !IsBuiltUp(plan.Zone(x, y)))
                    continue;

                var bySea = WfcWave.Directions.Any(d => x + d.X >= 0 && y + d.Y >= 0 && x + d.X < n && y + d.Y < n
                                                        && plan.Zone(x + d.X, y + d.Y) == CityZone.Ocean);
                plan.Zones[y * n + x] = bySea ? CityZone.Coast : CityZone.Scrub;
            }
        }
    }

    /// <summary>
    /// The ground everywhere before anything is built: sea, beaches and the land of each zone.
    /// </summary>
    private sealed class Terrain
    {
        private readonly CityPlan _plan;
        private readonly ulong _seed;

        public Terrain(CityPlan plan, ulong seed)
        {
            _plan = plan;
            _seed = seed;
        }

        private static float SeaLevel(CityZone zone)
        {
            return zone switch
            {
                CityZone.Ocean => 1f,
                CityZone.Coast => 0.5f,
                _ => 0f,
            };
        }

        /// <summary>The district a tile is nearest the middle of, along one axis.</summary>
        private int Nearest(int t)
        {
            return Math.Clamp((int) MathF.Round(DistrictPosition(t)), 0, _plan.Districts - 1);
        }

        /// <summary>Where a tile is in districts, with each district's middle on a whole number.</summary>
        private static float DistrictPosition(int t)
        {
            return (t - Avenue - DistrictSize / 2f) / Pitch;
        }

        /// <summary>How much sea a tile is, blended smoothly between the middles of the districts round it.</summary>
        private float Sea(int x, int y)
        {
            var last = _plan.Districts - 1;
            var fx = Math.Clamp(DistrictPosition(x), 0, last);
            var fy = Math.Clamp(DistrictPosition(y), 0, last);
            var (x0, y0) = ((int) fx, (int) fy);
            var (x1, y1) = (Math.Min(x0 + 1, last), Math.Min(y0 + 1, last));
            var (tx, ty) = (fx - x0, fy - y0);
            float At(int dx, int dy) => SeaLevel(_plan.Zone(dx, dy));
            var bottom = At(x0, y0) + (At(x1, y0) - At(x0, y0)) * tx;
            var top = At(x0, y1) + (At(x1, y1) - At(x0, y1)) * tx;
            return bottom + (top - bottom) * ty
                   + (Noise.At(_seed, x / 30f, y / 30f) - 0.5f) * 0.7f
                   + (Noise.At(_seed ^ 0x5EA, x / 8f, y / 8f) - 0.5f) * 0.3f;
        }

        public void Paint()
        {
            for (var y = 0; y < _plan.Size; y++)
            {
                for (var x = 0; x < _plan.Size; x++)
                {
                    var zone = _plan.Zone(Nearest(x), Nearest(y));
                    var sea = Sea(x, y);
                    if (sea > 0.55f)
                        _plan.Set(x, y, CityFloor.Ocean);
                    else if (sea > 0.4f)
                        _plan.Set(x, y, CityFloor.Sand);
                    else
                        _plan.Set(x, y, Land(zone, x, y), Scatter(zone, x, y));
                }
            }
        }

        private CityFloor Land(CityZone zone, int x, int y)
        {
            var n = Noise.At(_seed ^ 0x1A4D, x / 6f, y / 6f);
            return zone switch
            {
                CityZone.Ocean or CityZone.Coast => n < 0.6f ? CityFloor.Sand : CityFloor.Desert,
                CityZone.Scrub => n < 0.35f ? CityFloor.Dirt : n < 0.7f ? CityFloor.WildGrass : CityFloor.Desert,
                CityZone.Badlands or CityZone.Solar => n < 0.3f ? CityFloor.LowDesert : n < 0.75f ? CityFloor.Desert : CityFloor.Sand,
                _ => CityFloor.Dirt,
            };
        }

        /// <summary>Rocks and the odd tree, scattered over open land.</summary>
        private CityStructure Scatter(CityZone zone, int x, int y)
        {
            var roll = Noise.Hash(_seed ^ 0x5CA7, x, y) % 1000;
            return zone switch
            {
                CityZone.Badlands when roll < 10 => CityStructure.Rock,
                CityZone.Scrub when roll < 5 => CityStructure.Rock,
                CityZone.Scrub when roll < 8 => CityStructure.Tree,
                CityZone.Coast when roll < 3 => CityStructure.Rock,
                _ => CityStructure.None,
            };
        }
    }

    private enum Road : byte
    {
        None,
        Street,
        Highway,
    }

    /// <summary>
    /// Avenues are streets with sidewalks wherever they touch a built-up district. Out in the badlands, the two
    /// running along the city centre's south and west edges carry on as highways; the rest are left as land.
    /// </summary>
    private static void PaintAvenues(CityPlan plan, Shape shape)
    {
        var n = plan.Districts;
        var (cx, cy) = shape.Centre;

        Road Segment(bool highway, (int X, int Y) a, (int X, int Y) b)
        {
            CityZone? Zone((int X, int Y) d)
            {
                return d.X >= 0 && d.Y >= 0 && d.X < n && d.Y < n ? plan.Zone(d.X, d.Y) : null;
            }

            var (za, zb) = (Zone(a), Zone(b));
            if (za is { } ra && IsBuiltUp(ra) || zb is { } rb && IsBuiltUp(rb))
                return Road.Street;

            return highway && za != CityZone.Ocean && zb != CityZone.Ocean ? Road.Highway : Road.None;
        }

        // Segments of the avenues running north to south, by line and district, then those running east to west.
        var vertical = new Road[n + 1, n];
        var horizontal = new Road[n, n + 1];
        for (var i = 0; i <= n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                vertical[i, j] = Segment(i == cx, (i - 1, j), (i, j));
                horizontal[j, i] = Segment(i == cy, (j, i - 1), (j, i));
            }
        }

        for (var i = 0; i <= n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                PaintSegment(plan, vertical[i, j], i * Pitch, Origin(j), true);
                PaintSegment(plan, horizontal[j, i], Origin(j), i * Pitch, false);
            }
        }

        for (var i = 0; i <= n; i++)
        {
            for (var j = 0; j <= n; j++)
            {
                var around = new[]
                {
                    j > 0 ? vertical[i, j - 1] : Road.None,
                    j < n ? vertical[i, j] : Road.None,
                    i > 0 ? horizontal[i - 1, j] : Road.None,
                    i < n ? horizontal[i, j] : Road.None,
                };
                PaintJunction(plan, i * Pitch, j * Pitch, around);
            }
        }
    }

    /// <summary>
    /// One avenue between two junctions, with (<paramref name="x"/>, <paramref name="y"/>) its bottom-left tile.
    /// </summary>
    private static void PaintSegment(CityPlan plan, Road road, int x, int y, bool vertical)
    {
        if (road == Road.None)
            return;

        for (var along = 0; along < DistrictSize; along++)
        {
            for (var across = 0; across < Avenue; across++)
            {
                var (tx, ty) = vertical ? (x + across, y + along) : (x + along, y + across);
                var lanes = across is >= 2 and < Avenue - 2;
                // Highways stop at the shore.
                if (road == Road.Highway && plan.Floor(tx, ty) == CityFloor.Ocean)
                    continue;

                if (lanes)
                    plan.Set(tx, ty, CityFloor.Asphalt);
                else if (road == Road.Street)
                    plan.Set(tx, ty, CityFloor.Sidewalk);
            }

            // Lamps every 12 tiles, alternating sides of the street.
            if (road == Road.Street && along % 12 == 6)
            {
                var across = along % 24 == 6 ? 1 : Avenue - 2;
                var (lx, ly) = vertical ? (x + across, y + along) : (x + along, y + across);
                plan.SetStructure(lx, ly, CityStructure.Lamp);
            }
        }
    }

    /// <summary>Where avenues cross: open road, with sidewalk on the corners between streets.</summary>
    private static void PaintJunction(CityPlan plan, int x, int y, Road[] around)
    {
        if (Array.TrueForAll(around, r => r == Road.None))
            return;

        var street = Array.IndexOf(around, Road.Street) >= 0;
        for (var dy = 0; dy < Avenue; dy++)
        {
            for (var dx = 0; dx < Avenue; dx++)
            {
                var laneX = dx is >= 2 and < Avenue - 2;
                var laneY = dy is >= 2 and < Avenue - 2;
                if (street)
                    plan.Set(x + dx, y + dy, laneX || laneY ? CityFloor.Asphalt : CityFloor.Sidewalk);
                else if ((laneX || laneY) && plan.Floor(x + dx, y + dy) != CityFloor.Ocean)
                    plan.Set(x + dx, y + dy, CityFloor.Asphalt);
            }
        }
    }

    /// <summary>Lawns and trees inside a ring of paths, with a cross through the middle and sometimes a pond.</summary>
    private static void PaintPark(CityPlan plan, int ox, int oy, CyberRng rng)
    {
        const int mid = DistrictSize / 2;

        static bool Path(int t)
        {
            return t <= 1 || t >= DistrictSize - 2 || Math.Abs(t - mid) <= 1;
        }

        var pond = rng.Chance(2, 3);
        var (px, py) = (rng.Pick(new[] { mid / 2, mid + mid / 2 }), rng.Pick(new[] { mid / 2, mid + mid / 2 }));
        var radius = rng.Range(4, 6);
        for (var y = 0; y < DistrictSize; y++)
        {
            for (var x = 0; x < DistrictSize; x++)
            {
                var (dx, dy) = (x - px, y - py);
                if (Path(x) || Path(y))
                    plan.Set(ox + x, oy + y, CityFloor.Sidewalk);
                else if (pond && dx * dx + dy * dy <= radius * radius)
                    plan.Set(ox + x, oy + y, CityFloor.Ocean);
                else if (pond && dx * dx + dy * dy <= (radius + 1) * (radius + 1))
                    plan.Set(ox + x, oy + y, CityFloor.Sand);
                else
                {
                    var nearPath = Path(x - 1) || Path(x + 1) || Path(y - 1) || Path(y + 1);
                    plan.Set(ox + x, oy + y, CityFloor.Grass, !nearPath && rng.Chance(1, 9) ? CityStructure.Tree : CityStructure.None);
                }
            }
        }

        foreach (var t in new[] { mid / 2, mid + 2, mid + mid / 2 })
        {
            plan.SetStructure(ox + t, oy + mid + 2, CityStructure.Lamp);
            plan.SetStructure(ox + mid + 2, oy + t, CityStructure.Lamp);
        }
    }

    /// <summary>Rows of panels on concrete, either side of a service road.</summary>
    private static void PaintSolarFarm(CityPlan plan, int ox, int oy)
    {
        const int mid = DistrictSize / 2;
        for (var y = 3; y < DistrictSize - 3; y++)
        {
            for (var x = 3; x < DistrictSize - 3; x++)
            {
                if (Math.Abs(x - mid) <= 1)
                    plan.Set(ox + x, oy + y, CityFloor.Asphalt);
                else if (y % 3 == 0)
                    plan.Set(ox + x, oy + y, CityFloor.Concrete, CityStructure.SolarPanel);
            }
        }
    }

    /// <summary>A roofless shack falling apart in the badlands.</summary>
    private static void PaintRuin(CityPlan plan, int ox, int oy, CyberRng rng)
    {
        var (w, h) = (rng.Range(5, 9), rng.Range(5, 9));
        var (x0, y0) = (ox + rng.Range(2, DistrictSize - w - 2), oy + rng.Range(2, DistrictSize - h - 2));
        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                var edge = x == x0 || y == y0 || x == x0 + w - 1 || y == y0 + h - 1;
                var structure = !edge ? CityStructure.None : rng.Range(0, 9) switch
                {
                    < 5 => CityStructure.WallRust,
                    < 8 => CityStructure.Girder,
                    _ => CityStructure.None,
                };
                plan.Set(x, y, rng.Chance(1, 3) ? CityFloor.Plating : CityFloor.OldConcrete, structure);
            }
        }
    }
}

/// <summary>
/// Seeded value noise: smooth random values between 0 and 1 that change gently from tile to tile.
/// </summary>
public static class Noise
{
    public static ulong Hash(ulong seed, int x, int y)
    {
        return new CyberRng(seed ^ unchecked((ulong) x * 0x9E3779B97F4A7C15 ^ (ulong) y * 0xC2B2AE3D27D4EB4F)).NextU64();
    }

    public static float At(ulong seed, float x, float y)
    {
        var (x0, y0) = ((int) MathF.Floor(x), (int) MathF.Floor(y));
        var (tx, ty) = (Smooth(x - x0), Smooth(y - y0));
        float Corner(int cx, int cy) => (Hash(seed, cx, cy) >> 40) / (float) (1 << 24);
        var bottom = Corner(x0, y0) + (Corner(x0 + 1, y0) - Corner(x0, y0)) * tx;
        var top = Corner(x0, y0 + 1) + (Corner(x0 + 1, y0 + 1) - Corner(x0, y0 + 1)) * tx;
        return bottom + (top - bottom) * ty;
    }

    private static float Smooth(float t)
    {
        return t * t * (3 - 2 * t);
    }
}
