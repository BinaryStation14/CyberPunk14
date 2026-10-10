using System.Linq;

namespace Content.Server._CyberPunk.Cyberspace;

/// <summary>
/// What a tile of cyberspace is. Only data, node and bus tiles can be walked on.
/// </summary>
public enum CyberFloor : byte
{
    /// <summary>Nothing: the dark between paths.</summary>
    Void,

    /// <summary>Drifting noise, seen but not walked on.</summary>
    Static,

    /// <summary>A path between nodes.</summary>
    Data,

    /// <summary>A node's pad, where a machine sits.</summary>
    Node,

    /// <summary>The backbone's data bus between regions, and its hub.</summary>
    Bus,
}

/// <summary>
/// What a pad in a region is.
/// </summary>
public enum PadKind : byte
{
    Router,
    Switch,
    Host,
}

/// <summary>
/// A rectangle, from its bottom-left corner.
/// </summary>
public readonly record struct CyberRect(int X, int Y, int W, int H)
{
    public bool Contains(int x, int y)
    {
        return x >= X && y >= Y && x < X + W && y < Y + H;
    }
}

/// <summary>
/// The size of a region, in slots: columns of slots two cells apart, rows of hosts at the bottom, rows of
/// switches above them and the router at the top, where it opens onto the bus. Slots are in cells of the
/// region, x to the right and y up.
/// </summary>
public sealed record RegionShape(int Columns, int HostRows, int SwitchRows)
{
    /// <summary>The smallest region: a practice grid, or a network of a few machines.</summary>
    public static readonly RegionShape Smallest = new(5, 2, 1);

    /// <summary>
    /// A region with room for a network's hosts and switches, and a quarter as many hosts again (at least three)
    /// to grow into, roughly square.
    /// </summary>
    public static RegionShape For(int hosts, int switches)
    {
        var need = hosts + Math.Max(3, hosts / 4);
        var columns = Math.Max(Smallest.Columns, (int) Math.Ceiling(Math.Sqrt(need)));
        var hostRows = Math.Max(Smallest.HostRows, (need + columns - 1) / columns);
        var switchRows = Math.Max(Smallest.SwitchRows, (switches + columns - 1) / columns);
        return new RegionShape(columns, hostRows, switchRows);
    }

    public bool Fits(int hosts, int switches)
    {
        return hosts <= Columns * HostRows && switches <= Columns * SwitchRows;
    }

    /// <summary>Width in cells.</summary>
    public int Width => 2 * Columns - 1;

    /// <summary>Height in cells.</summary>
    public int Height => 2 * (HostRows + SwitchRows) + 1;

    /// <summary>Slot columns, in the order they fill: from the middle out.</summary>
    private IEnumerable<int> ColumnOrder()
    {
        var middle = Columns / 2;
        yield return 2 * middle;
        for (var step = 1; step <= middle || middle + step < Columns; step++)
        {
            if (middle - step >= 0)
                yield return 2 * (middle - step);
            if (middle + step < Columns)
                yield return 2 * (middle + step);
        }
    }

    /// <summary>The router's slot: the middle of the top row, by the bus.</summary>
    public (int X, int Y) RouterSlot => (2 * (Columns / 2), Height - 1);

    /// <summary>Switch slots, top row first, in the order they fill.</summary>
    public List<(int X, int Y)> SwitchSlots =>
        Enumerable.Range(0, SwitchRows)
            .SelectMany(row => ColumnOrder().Select(x => (x, Height - 3 - 2 * row)))
            .ToList();

    /// <summary>Host slots, top row first, each row from the middle out.</summary>
    public List<(int X, int Y)> HostSlots =>
        Enumerable.Range(0, HostRows)
            .SelectMany(row => ColumnOrder().Select(x => (x, 2 * (HostRows - 1 - row))))
            .ToList();
}

/// <summary>
/// Where things are in cyberspace. The backbone's hub sits in the middle, and the bus runs out of it east and
/// west as one long street. Each network's region hangs off the street, above or below it, as big as its
/// network needs, with its router opening onto the street. A region that outgrows its place moves to a new one
/// and leaves a gap that a later region can take. The practice regions sit in a row of their own far away, with
/// void all round.
/// </summary>
/// <remarks>
/// Positions here are in cells, <see cref="Cell"/> tiles square; tiles are cells times <see cref="Cell"/>. The
/// street is the row of cells at y 0. A region above the street is generated with its router at the top as
/// usual and then flipped, so its router faces down onto the street.
/// </remarks>
public sealed class CyberLayout
{
    /// <summary>Width of a cell, in tiles.</summary>
    public const int Cell = 5;

    /// <summary>Half the hub's width and height, in cells, beside its middle cell.</summary>
    public const int HubHalf = 4;

    /// <summary>Cells of void between regions side by side.</summary>
    public const int Gap = 1;

    /// <summary>The row of cells the practice regions sit on, far from everything else.</summary>
    public const int PracticeRow = -1000;

    /// <summary>The regions placed along each side of the street: east above, east below, west above, west below.</summary>
    private readonly List<CyberRect>[] _lanes = { new(), new(), new(), new() };

    /// <summary>The street's ends, in cells, beyond the hub.</summary>
    public int StreetWest { get; private set; } = -HubHalf;

    public int StreetEast { get; private set; } = HubHalf;

    /// <summary>The hub's cells.</summary>
    public static CyberRect HubCells => new(-HubHalf, -HubHalf, 2 * HubHalf + 1, 2 * HubHalf + 1);

    /// <summary>A rectangle of cells, as tiles.</summary>
    public static CyberRect Tiles(CyberRect cells)
    {
        return new CyberRect(cells.X * Cell, cells.Y * Cell, cells.W * Cell, cells.H * Cell);
    }

    /// <summary>Whether a region's cells are above the street, so it's flipped.</summary>
    public static bool Above(CyberRect cells)
    {
        return cells.Y > 0;
    }

    /// <summary>
    /// Finds a place for a region of a shape: the free stretch of street nearest the hub, on either side,
    /// that it fits along. Returns its cells.
    /// </summary>
    public CyberRect Place(RegionShape shape)
    {
        var (w, h) = (shape.Width, shape.Height);
        CyberRect? best = null;
        var bestDistance = int.MaxValue;
        for (var lane = 0; lane < _lanes.Length; lane++)
        {
            var east = lane < 2;
            var above = lane % 2 == 0;

            // Distances from the hub's edge, along the street, taken up by the regions already here.
            var taken = _lanes[lane]
                .Select(r => east ? (From: r.X - HubHalf - 1, To: r.X + r.W - HubHalf - 1) : (From: -HubHalf - (r.X + r.W), To: -HubHalf - r.X))
                .OrderBy(t => t.From)
                .ToList();

            var at = Gap;
            foreach (var (from, to) in taken)
            {
                if (from - Gap >= at + w)
                    break;

                at = Math.Max(at, to + Gap);
            }

            if (at >= bestDistance)
                continue;

            bestDistance = at;
            var x = east ? HubHalf + 1 + at : -HubHalf - at - w;
            best = new CyberRect(x, above ? 1 : -h, w, h);
        }

        var placed = best!.Value;
        _lanes[LaneOf(placed)].Add(placed);
        StreetWest = Math.Min(StreetWest, placed.X - Gap);
        StreetEast = Math.Max(StreetEast, placed.X + placed.W - 1 + Gap);
        return placed;
    }

    /// <summary>
    /// Gives a region's place back.
    /// </summary>
    public void Free(CyberRect cells)
    {
        _lanes[LaneOf(cells)].Remove(cells);
    }

    private static int LaneOf(CyberRect cells)
    {
        return (cells.X > 0 ? 0 : 2) + (Above(cells) ? 0 : 1);
    }

    /// <summary>The cells of a practice region.</summary>
    public static CyberRect PracticeCells(int slot)
    {
        var shape = RegionShape.Smallest;
        return new CyberRect(slot * (shape.Width + 2 * Gap), PracticeRow, shape.Width, shape.Height);
    }

    /// <summary>Whether a tile of cyberspace can be walked on.</summary>
    public static bool Walkable(CyberFloor floor)
    {
        return floor is CyberFloor.Data or CyberFloor.Node or CyberFloor.Bus;
    }
}
