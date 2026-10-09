using System.Collections.Generic;
using System.Linq;
using Content.Server._CyberPunk.Cyberspace;
using NUnit.Framework;

#nullable enable

namespace Content.Tests.Server._CyberPunk;

/// <summary>
/// The shape of cyberspace and the regions generated from networks, ported from Switchboard's procgen tests.
/// </summary>
[TestFixture]
[TestOf(typeof(CyberRegionGenerator))]
public sealed class CyberspaceGenerationTest
{
    private const int Width = CyberLayout.RegionWidth * CyberLayout.Cell;

    private static CyberLayout Layout()
    {
        return new CyberLayout(8, 12);
    }

    /// <summary>A router, two switches and hosts under each.</summary>
    private static RegionGraph Graph(CyberLayout layout)
    {
        var hosts = layout.HostSlots;
        var switches = layout.SwitchSlots;
        return new RegionGraph
        {
            Pads =
            {
                (layout.RouterSlot, PadKind.Router),
                (switches[0], PadKind.Switch),
                (switches[1], PadKind.Switch),
                (hosts[0], PadKind.Host),
                (hosts[1], PadKind.Host),
                (hosts[5], PadKind.Host),
                (hosts[3], PadKind.Host),
            },
            Links = { (0, 1), (0, 2), (1, 2), (1, 3), (1, 5), (2, 4), (2, 6) },
            Gate = 0,
        };
    }

    private static bool Walkable(CyberFloor[] tiles, int i)
    {
        return CyberLayout.Walkable(tiles[i]);
    }

    /// <summary>Which tiles the tile <paramref name="start"/> reaches.</summary>
    private static bool[] Flood(CyberFloor[] tiles, int width, int start)
    {
        var height = tiles.Length / width;
        var seen = new bool[tiles.Length];
        var queue = new Queue<int>();
        queue.Enqueue(start);
        seen[start] = true;
        while (queue.TryDequeue(out var i))
        {
            var (x, y) = (i % width, i / width);
            foreach (var (dx, dy) in CyberRegionGenerator.Directions)
            {
                var (nx, ny) = (x + dx, y + dy);
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;

                var n = ny * width + nx;
                if (!seen[n] && Walkable(tiles, n))
                {
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }

    private static int Centre((int X, int Y) slot)
    {
        var (x, y) = (slot.X * CyberLayout.Cell + CyberLayout.Cell / 2, slot.Y * CyberLayout.Cell + CyberLayout.Cell / 2);
        return y * Width + x;
    }

    [Test]
    public void LinkedPadsAreJoinedAndUnlinkedOnesAreNot()
    {
        var layout = Layout();
        var graph = Graph(layout);

        // An unplugged host: a pad with no links.
        graph.Pads.Add((layout.HostSlots[4], PadKind.Host));
        var tiles = CyberRegionGenerator.Generate(7, layout, graph);
        var reached = Flood(tiles, Width, Centre(graph.Pads[0].Slot));
        for (var i = 0; i < graph.Pads.Count; i++)
        {
            var slot = graph.Pads[i].Slot;
            Assert.That(reached[Centre(slot)], Is.EqualTo(i != 7), $"pad {i}");
            Assert.That(tiles[Centre(slot)], Is.EqualTo(CyberFloor.Node));
        }

        // The gate opens the router's pad onto the top edge.
        var top = (layout.RegionHeight * CyberLayout.Cell - 1) * Width + layout.RouterSlot.X * CyberLayout.Cell + 2;
        Assert.That(Walkable(tiles, top));

        // Nothing else reaches the edge.
        var height = layout.RegionHeight * CyberLayout.Cell;
        var edge = Enumerable.Range(0, Width)
            .Concat(Enumerable.Range(0, height).Select(y => y * Width))
            .Concat(Enumerable.Range(0, height).Select(y => y * Width + Width - 1));

        Assert.That(edge.All(i => !Walkable(tiles, i)));
    }

    [Test]
    public void SameGraphSameTilesAndStylesVary()
    {
        var layout = Layout();
        var graph = Graph(layout);
        var first = CyberRegionGenerator.Generate(3, layout, graph);
        var second = CyberRegionGenerator.Generate(3, layout, graph);
        Assert.That(second, Is.EqualTo(first));

        var sizes = new HashSet<int>();
        for (var seed = 0UL; seed < 20; seed++)
        {
            var tiles = CyberRegionGenerator.Generate(seed, layout, graph);
            Assert.That(tiles.Any(CyberLayout.Walkable));
            sizes.Add(tiles.Count(t => t == CyberFloor.Data));
        }

        Assert.That(sizes, Has.Count.GreaterThan(1), "the WFC pass should pick different styles");
    }

    [Test]
    public void RemovingALinkRemovesItsCorridor()
    {
        var layout = Layout();
        var graph = Graph(layout);
        var cut = Graph(layout);
        cut.Links.Remove((2, 6));
        var tiles = CyberRegionGenerator.Generate(1, layout, cut);
        var reached = Flood(tiles, Width, Centre(graph.Pads[0].Slot));
        Assert.That(reached[Centre(graph.Pads[6].Slot)], Is.False);
        Assert.That(reached[Centre(graph.Pads[4].Slot)]);
    }

    [Test]
    public void RegionsAndTheHubTileTheLevelApart()
    {
        var layout = new CyberLayout(20, 20);
        var (w, h) = layout.Size;
        var tiles = layout.Base();
        Assert.That(tiles, Has.Length.EqualTo(w * h));
        var hub = layout.HubRect;
        for (var r = 0; r < layout.Regions; r++)
        {
            var rect = layout.RegionRect(r);
            Assert.That(rect.X + rect.W < w && rect.Y + rect.H < h);
            Assert.That(rect.X == hub.X && rect.Y == hub.Y, Is.False);

            // Above each region's router is the bus.
            var (x, _) = layout.SlotCentre(r, layout.RouterSlot);
            Assert.That(tiles[(rect.Y + rect.H) * w + x], Is.EqualTo(CyberFloor.Bus));

            // Inside, nothing yet.
            Assert.That(Walkable(tiles, (rect.Y + 2) * w + rect.X + 2), Is.False);
        }

        // The bus all joins up, with the hub.
        var reached = Flood(tiles, w, (hub.Y + 2) * w + hub.X + 2);
        for (var i = 0; i < tiles.Length; i++)
        {
            Assert.That(!Walkable(tiles, i) || reached[i], $"tile {i % w}, {i / w}");
        }

        Assert.That(layout.HostSlots, Has.Count.GreaterThanOrEqualTo(20));
    }

    [Test]
    public void PracticeRegionsAreCutOff()
    {
        var layout = new CyberLayout(6, 10, 3);
        var (w, h) = layout.Size;
        var tiles = layout.Base();
        var hub = layout.HubRect;
        var reached = Flood(tiles, w, (hub.Y + 2) * w + hub.X + 2);
        for (var s = layout.Regions; s < layout.AllRegions; s++)
        {
            var rect = layout.RegionRect(s);
            Assert.That(rect.X + rect.W < w && rect.Y + rect.H < h, $"{rect}");
            for (var r = 0; r < layout.AllRegions; r++)
            {
                if (r == s)
                    continue;

                var other = layout.RegionRect(r);
                var apart = rect.X + rect.W <= other.X
                            || other.X + other.W <= rect.X
                            || rect.Y + rect.H <= other.Y
                            || other.Y + other.H <= rect.Y;

                Assert.That(apart, $"{s} overlaps {r}");
            }

            // Nothing walkable around it, so nothing reaches it.
            for (var x = rect.X - 1; x <= rect.X + rect.W; x++)
            {
                foreach (var y in new[] { rect.Y - 1, rect.Y + rect.H })
                {
                    var i = y * w + x;
                    Assert.That(Walkable(tiles, i), Is.False);
                    Assert.That(reached[i], Is.False);
                }
            }
        }
    }

    /// <summary>
    /// The solver keeps to its sockets: tiles that only fit themselves fill the grid with one of them.
    /// </summary>
    [Test]
    public void WfcNeighboursRespectSockets()
    {
        var rules = new WfcRules(new[] { 1, 1 }, (a, _, b) => a == b);
        for (var seed = 0UL; seed < 20; seed++)
        {
            var rng = new CyberRng(seed);
            var grid = new WfcWave(6, 6, rules.All()).Solve(rules, ref rng);
            Assert.That(grid, Is.Not.Null);
            Assert.That(grid!.All(t => t == grid[0]));
        }
    }

    [Test]
    public void WfcImpossibleCellsFail()
    {
        var rules = new WfcRules(new[] { 1, 1 }, (a, _, b) => a == b);
        var wave = new WfcWave(2, 1, rules.All());
        wave.Set(0, 0, WfcSet.Single(0));
        wave.Set(1, 0, WfcSet.Single(1));
        var rng = new CyberRng(0);
        Assert.That(wave.Solve(rules, ref rng), Is.Null);
    }
}
