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
/// A rectangle of tiles, from its bottom-left corner.
/// </summary>
public readonly record struct CyberRect(int X, int Y, int W, int H)
{
    public bool Contains(int x, int y)
    {
        return x >= X && y >= Y && x < X + W && y < Y + H;
    }
}

/// <summary>
/// The fixed shape of cyberspace, after Switchboard's <c>sb_procgen/src/cyberspace.rs</c>: a lattice of cells
/// <see cref="Cell"/> tiles square, where each network has a region of cells, the same size for all, set out in
/// a grid with the backbone's hub in the middle. Between the regions run the bus's streets, one cell wide, all
/// joined to the hub. Beyond them, in a band of their own with void all round, are the practice regions. The
/// layout never changes once made, so a region keeps its place as its network changes.
/// </summary>
/// <remarks>
/// Inside a region every machine has a pad on a fixed slot: the router at the top, where it opens onto the bus
/// above, switches below it and hosts below them, slots two cells apart so pads never touch. Tile coordinates
/// have x to the right and y up.
/// </remarks>
public sealed class CyberLayout
{
    /// <summary>Width of a cell, in tiles.</summary>
    public const int Cell = 5;

    /// <summary>A region's width in cells: five slot columns two cells apart.</summary>
    public const int RegionWidth = 9;

    /// <summary>Slot columns of a region, in the order they fill (from the middle).</summary>
    private static readonly int[] Columns = { 4, 2, 6, 0, 8 };

    /// <summary>Regions' height in cells.</summary>
    public readonly int RegionHeight;

    /// <summary>Region slots across and up (the hub takes one).</summary>
    public readonly int Cols;

    public readonly int Rows;

    /// <summary>Which slot is the hub.</summary>
    public readonly int Hub;

    /// <summary>How many regions there are for networks.</summary>
    public readonly int Regions;

    /// <summary>How many practice regions follow them, cut off from everything.</summary>
    public readonly int Sandboxes;

    /// <summary>
    /// A layout for <paramref name="regions"/> networks of at most <paramref name="hosts"/> hosts each (more
    /// fit, by row, up to the region's height), with <paramref name="sandboxes"/> practice regions.
    /// </summary>
    public CyberLayout(int regions, int hosts, int sandboxes = 0)
    {
        var hostRows = Math.Max((hosts + Columns.Length - 1) / Columns.Length, 2);
        var slots = regions + 1;
        Cols = (int) Math.Ceiling(Math.Sqrt(slots));
        Rows = (slots + Cols - 1) / Cols;
        RegionHeight = 2 * hostRows + 3;
        Hub = Rows / 2 * Cols + Cols / 2;
        Regions = regions;
        Sandboxes = sandboxes;
    }

    /// <summary>Every region, the networks' and then the practice ones.</summary>
    public int AllRegions => Regions + Sandboxes;

    /// <summary>The networks' part of the level, in cells.</summary>
    private (int W, int H) CityCells => (Cols * (RegionWidth + 1) + 1, Rows * (RegionHeight + 1) + 1);

    private (int X, int Y) SlotOrigin(int slot)
    {
        var (c, r) = (slot % Cols, slot / Cols);
        return (1 + c * (RegionWidth + 1), 1 + r * (RegionHeight + 1));
    }

    private int SlotOf(int region)
    {
        return region >= Hub ? region + 1 : region;
    }

    /// <summary>The level's size in cells: the networks' part, and the practice band above it.</summary>
    public (int W, int H) Cells
    {
        get
        {
            var (w, h) = CityCells;
            if (Sandboxes == 0)
                return (w, h);

            return (Math.Max(w, Sandboxes * (RegionWidth + 2) + 1), h + RegionHeight + 2);
        }
    }

    /// <summary>The level's size in tiles.</summary>
    public (int W, int H) Size => (Cells.W * Cell, Cells.H * Cell);

    /// <summary>A region's tiles (practice regions numbered after the networks').</summary>
    public CyberRect RegionRect(int region)
    {
        var (x, y) = region >= Regions
            ? (1 + (region - Regions) * (RegionWidth + 2), CityCells.H + 1)
            : SlotOrigin(SlotOf(region));

        return new CyberRect(x * Cell, y * Cell, RegionWidth * Cell, RegionHeight * Cell);
    }

    /// <summary>The hub's tiles.</summary>
    public CyberRect HubRect
    {
        get
        {
            var (x, y) = SlotOrigin(Hub);
            return new CyberRect(x * Cell, y * Cell, RegionWidth * Cell, RegionHeight * Cell);
        }
    }

    /// <summary>The tile at the middle of a region's slot.</summary>
    public (int X, int Y) SlotCentre(int region, (int X, int Y) slot)
    {
        var rect = RegionRect(region);
        return (rect.X + slot.X * Cell + Cell / 2, rect.Y + slot.Y * Cell + Cell / 2);
    }

    /// <summary>The router's slot: the middle of the top row, by the bus.</summary>
    public (int X, int Y) RouterSlot => (Columns[0], RegionHeight - 1);

    /// <summary>Switch slots, in the order they fill.</summary>
    public List<(int X, int Y)> SwitchSlots => Columns.Select(x => (x, RegionHeight - 3)).ToList();

    /// <summary>Host slots, top row first, each row from the middle out.</summary>
    public List<(int X, int Y)> HostSlots
    {
        get
        {
            var slots = new List<(int, int)>();
            for (var row = (RegionHeight - 5) / 2; row >= 0; row--)
            {
                foreach (var x in Columns)
                {
                    slots.Add((x, row * 2));
                }
            }

            return slots;
        }
    }

    /// <summary>
    /// The whole level with no network in it: the bus between regions and the hub, everything else void. Row
    /// by row from the bottom.
    /// </summary>
    public CyberFloor[] Base()
    {
        var (w, h) = Size;
        var (cityW, cityH) = CityCells;
        var hub = HubRect;
        var tiles = new CyberFloor[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (cx, cy) = (x / Cell, y / Cell);
                var bus = cx < cityW
                          && cy < cityH
                          && (cx % (RegionWidth + 1) == 0 || cy % (RegionHeight + 1) == 0);

                tiles[y * w + x] = bus || hub.Contains(x, y) ? CyberFloor.Bus : CyberFloor.Void;
            }
        }

        return tiles;
    }

    /// <summary>Whether a tile of cyberspace can be walked on.</summary>
    public static bool Walkable(CyberFloor floor)
    {
        return floor is CyberFloor.Data or CyberFloor.Node or CyberFloor.Bus;
    }
}
