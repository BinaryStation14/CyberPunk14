using System.Linq;
using System.Numerics;

namespace Content.Server._CyberPunk.Procgen;

/// <summary>
/// A small deterministic random number generator, SplitMix64 as in Switchboard's <c>sb_procgen/src/rng.rs</c>,
/// so the same seed gives the same map on every platform and version.
/// </summary>
public struct CyberRng
{
    private ulong _state;

    public CyberRng(ulong seed)
    {
        _state = seed;
    }

    /// <summary>A child generator, so one part's randomness doesn't reshuffle another's.</summary>
    public CyberRng Fork(ulong salt)
    {
        return new CyberRng(NextU64() ^ unchecked(salt * 0x9E3779B97F4A7C15));
    }

    public ulong NextU64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A number from <paramref name="min"/> up to and including <paramref name="max"/>.</summary>
    public int Range(int min, int max)
    {
        return min + (int) (NextU64() % (ulong) (max - min + 1));
    }

    /// <summary>True <paramref name="n"/> times in <paramref name="d"/>.</summary>
    public bool Chance(int n, int d)
    {
        return NextU64() % (ulong) d < (ulong) n;
    }

    public T Pick<T>(IReadOnlyList<T> items)
    {
        return items[Range(0, items.Count - 1)];
    }

    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Range(0, i);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}

/// <summary>
/// A set of WFC tile indices, up to <see cref="Max"/>.
/// </summary>
public struct WfcSet : IEquatable<WfcSet>
{
    public const int Max = 256;

    private ulong _a, _b, _c, _d;

    public static WfcSet Single(int tile)
    {
        var set = new WfcSet();
        set.Insert(tile);
        return set;
    }

    private readonly ulong Word(int index)
    {
        return index switch
        {
            0 => _a,
            1 => _b,
            2 => _c,
            _ => _d,
        };
    }

    public void Insert(int tile)
    {
        var bit = 1UL << (tile % 64);
        switch (tile / 64)
        {
            case 0: _a |= bit; break;
            case 1: _b |= bit; break;
            case 2: _c |= bit; break;
            default: _d |= bit; break;
        }
    }

    public readonly bool Contains(int tile)
    {
        return (Word(tile / 64) & (1UL << (tile % 64))) != 0;
    }

    public int Count => BitOperations.PopCount(_a) + BitOperations.PopCount(_b)
                        + BitOperations.PopCount(_c) + BitOperations.PopCount(_d);

    public bool IsEmpty => (_a | _b | _c | _d) == 0;

    public WfcSet Union(WfcSet other)
    {
        return new WfcSet { _a = _a | other._a, _b = _b | other._b, _c = _c | other._c, _d = _d | other._d };
    }

    public WfcSet Intersect(WfcSet other)
    {
        return new WfcSet { _a = _a & other._a, _b = _b & other._b, _c = _c & other._c, _d = _d & other._d };
    }

    /// <summary>The tiles in the set, lowest first.</summary>
    public IEnumerable<int> Tiles()
    {
        for (var tile = 0; tile < Max; tile++)
        {
            if (Contains(tile))
                yield return tile;
        }
    }

    public bool Equals(WfcSet other)
    {
        return _a == other._a && _b == other._b && _c == other._c && _d == other._d;
    }

    public override bool Equals(object? obj)
    {
        return obj is WfcSet other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_a, _b, _c, _d);
    }
}

/// <summary>
/// Which WFC tiles exist, how likely each is, and which may be neighbours.
/// </summary>
public sealed class WfcRules
{
    public readonly int[] Weights;

    /// <summary>Tiles that may sit in each direction (north, east, south, west) from each tile.</summary>
    public readonly WfcSet[][] Allowed;

    /// <summary>
    /// Rules from each tile's weight, where <paramref name="fits"/> says whether tile b may sit in a direction
    /// from tile a.
    /// </summary>
    public WfcRules(int[] weights, Func<int, int, int, bool> fits)
    {
        if (weights.Length > WfcSet.Max)
            throw new ArgumentException("too many tiles");

        Weights = weights;
        Allowed = new WfcSet[4][];
        for (var direction = 0; direction < 4; direction++)
        {
            Allowed[direction] = new WfcSet[weights.Length];
            for (var a = 0; a < weights.Length; a++)
            {
                var set = new WfcSet();
                for (var b = 0; b < weights.Length; b++)
                {
                    if (fits(a, direction, b))
                        set.Insert(b);
                }

                Allowed[direction][a] = set;
            }
        }
    }

    /// <summary>Every tile with a weight above zero.</summary>
    public WfcSet All()
    {
        var set = new WfcSet();
        for (var tile = 0; tile < Weights.Length; tile++)
        {
            if (Weights[tile] > 0)
                set.Insert(tile);
        }

        return set;
    }
}

/// <summary>
/// A seeded, simple-tiled Wave Function Collapse solver, after Switchboard's <c>sb_procgen/src/wfc.rs</c>: it
/// repeatedly collapses the cell with the fewest options left to one tile (picked by weight) and propagates what
/// that rules out to its neighbours.
/// </summary>
public sealed class WfcWave
{
    /// <summary>Directions in the order sockets are listed: north (y up), east, south, west.</summary>
    public static readonly (int X, int Y)[] Directions = { (0, 1), (1, 0), (0, -1), (-1, 0) };

    public static int Opposite(int direction)
    {
        return (direction + 2) % 4;
    }

    private readonly int _width;
    private readonly int _height;
    private readonly WfcSet[] _cells;

    public WfcWave(int width, int height, WfcSet options)
    {
        _width = width;
        _height = height;
        _cells = new WfcSet[width * height];
        Array.Fill(_cells, options);
    }

    /// <summary>Replaces one cell's options before solving.</summary>
    public void Set(int x, int y, WfcSet options)
    {
        _cells[y * _width + x] = options;
    }

    private int? Neighbour(int i, int direction)
    {
        var (dx, dy) = Directions[direction];
        var x = i % _width + dx;
        var y = i / _width + dy;
        return x >= 0 && y >= 0 && x < _width && y < _height ? y * _width + x : null;
    }

    /// <summary>
    /// Removes options that can't fit next to their neighbours, starting from the dirty cells. False on a
    /// contradiction.
    /// </summary>
    private bool Propagate(WfcRules rules, Stack<int> dirty)
    {
        while (dirty.TryPop(out var i))
        {
            for (var direction = 0; direction < 4; direction++)
            {
                if (Neighbour(i, direction) is not { } n)
                    continue;

                var reachable = new WfcSet();
                foreach (var tile in _cells[i].Tiles())
                {
                    reachable = reachable.Union(rules.Allowed[direction][tile]);
                }

                var narrowed = _cells[n].Intersect(reachable);
                if (narrowed.Equals(_cells[n]))
                    continue;

                if (narrowed.IsEmpty)
                    return false;

                _cells[n] = narrowed;
                dirty.Push(n);
            }
        }

        return true;
    }

    /// <summary>
    /// Collapses the whole grid: the chosen tile for each cell, row by row from the bottom, or null if the
    /// solver painted itself into a corner; callers retry with a forked generator.
    /// </summary>
    public int[]? Solve(WfcRules rules, ref CyberRng rng)
    {
        if (_cells.Any(c => c.IsEmpty))
            return null;

        // Switchboard pops its dirty list from the end; a stack filled backwards pops in the same order.
        if (!Propagate(rules, new Stack<int>(Enumerable.Range(0, _cells.Length))))
            return null;

        // A fixed random order breaks ties between equally open cells, so the grid doesn't fill in from one
        // corner.
        var tieBreak = new ulong[_cells.Length];
        for (var i = 0; i < tieBreak.Length; i++)
        {
            tieBreak[i] = rng.NextU64();
        }

        while (true)
        {
            var next = -1;
            for (var i = 0; i < _cells.Length; i++)
            {
                var count = _cells[i].Count;
                if (count <= 1)
                    continue;

                if (next < 0
                    || count < _cells[next].Count
                    || count == _cells[next].Count && tieBreak[i] < tieBreak[next])
                {
                    next = i;
                }
            }

            if (next < 0)
                return _cells.Select(c => c.Tiles().First()).ToArray();

            var options = _cells[next].Tiles().ToList();
            var total = options.Aggregate(0UL, (sum, t) => sum + (ulong) rules.Weights[t]);
            var roll = rng.NextU64() % Math.Max(total, 1);
            var chosen = options[0];
            foreach (var t in options)
            {
                var weight = (ulong) rules.Weights[t];
                if (roll < weight)
                {
                    chosen = t;
                    break;
                }

                roll -= weight;
            }

            _cells[next] = WfcSet.Single(chosen);
            var dirty = new Stack<int>();
            dirty.Push(next);
            if (!Propagate(rules, dirty))
                return null;
        }
    }
}
