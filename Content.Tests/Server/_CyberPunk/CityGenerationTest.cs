using System.Collections.Generic;
using System.Linq;
using Content.Server._CyberPunk.City;
using NUnit.Framework;

namespace Content.Tests.Server._CyberPunk;

[TestFixture]
[TestOf(typeof(CityGenerator))]
public sealed class CityGenerationTest
{
    private static readonly ulong[] Seeds = { 0, 1, 2, 3, 4, 5, 6, 7 };

    [Test]
    public void SameSeedSameCity()
    {
        var a = CityGenerator.Generate(42);
        var b = CityGenerator.Generate(42);
        Assert.That(a.Zones, Is.EqualTo(b.Zones));
        Assert.That(a.Floors, Is.EqualTo(b.Floors));
        Assert.That(a.Structures, Is.EqualTo(b.Structures));
        Assert.That(a.Cables, Is.EqualTo(b.Cables));
        Assert.That(a.Fixtures, Is.EqualTo(b.Fixtures));
        Assert.That(a.Spawn, Is.EqualTo(b.Spawn));
    }

    [Test]
    public void CoastalCityInTheBadlands([ValueSource(nameof(Seeds))] ulong seed)
    {
        var plan = CityGenerator.Generate(seed);
        Assert.That(plan.Zones, Does.Contain(CityZone.Ocean));
        Assert.That(plan.Zones, Does.Contain(CityZone.Corporate));
        Assert.That(plan.Zones.Any(z => z is CityZone.Badlands or CityZone.Scrub));
        Assert.That(plan.Floor(plan.Spawn.X, plan.Spawn.Y), Is.EqualTo(CityFloor.Asphalt));
        Assert.That(plan.Structure(plan.Spawn.X, plan.Spawn.Y), Is.EqualTo(CityStructure.None));
    }

    /// <summary>
    /// Every city has each zone within its share of districts, one each of its landmarks, and a few townships out
    /// in the badlands, inside the fence.
    /// </summary>
    [Test]
    public void EveryZoneAndLandmark([ValueSource(nameof(Seeds))] ulong seed)
    {
        var plan = CityGenerator.Generate(seed);
        var quotas = new Dictionary<CityZone, (int Min, int Max)>
        {
            [CityZone.Corporate] = (3, 4),
            [CityZone.Public] = (2, 3),
            [CityZone.Commercial] = (5, 7),
            [CityZone.HighClass] = (3, 4),
            [CityZone.MediumClass] = (5, 7),
            [CityZone.LowClass] = (5, 7),
            [CityZone.Industrial] = (5, 7),
            [CityZone.Shanty] = (2, 4),
        };
        foreach (var (zone, (min, max)) in quotas)
        {
            Assert.That(plan.Zones.Count(z => z == zone), Is.InRange(min, max), zone.ToString());
        }

        foreach (var landmark in new[]
                 {
                     CityLandmark.Headquarters, CityLandmark.CityHall, CityLandmark.PoliceStation, CityLandmark.Hospital,
                     CityLandmark.TransitStation, CityLandmark.Megabuilding, CityLandmark.SolarPlant,
                 })
        {
            Assert.That(plan.Landmarks.Count(l => l.Kind == landmark), Is.EqualTo(1), landmark.ToString());
        }

        var townships = plan.Landmarks.Where(l => l.Kind == CityLandmark.Township).ToList();
        Assert.That(townships, Has.Count.InRange(2, 4));
        var (near, far) = (CityGenerator.FenceInset, plan.Size - 1 - CityGenerator.FenceInset);
        Assert.That(townships.All(t => t.X > near && t.Y > near && t.X + t.W - 1 < far && t.Y + t.H - 1 < far));
    }

    /// <summary>
    /// Mountains rise in the badlands, a fence runs round the city, and nothing gets past the wall round the edge.
    /// </summary>
    [Test]
    public void FencedAndWalledIn([ValueSource(nameof(Seeds))] ulong seed)
    {
        var plan = CityGenerator.Generate(seed);
        Assert.That(plan.Structures, Does.Contain(CityStructure.Mountain));
        Assert.That(plan.Structures, Does.Contain(CityStructure.Fence));
        var last = plan.Size - 1;
        var edge = Enumerable.Range(0, plan.Size)
            .SelectMany(t => new[] { (t, 0), (t, last), (0, t), (last, t) })
            .Where(tile => plan.Structure(tile.Item1, tile.Item2) != CityStructure.BoundaryWall);
        Assert.That(edge, Is.Empty);
    }

    /// <summary>
    /// From where people arrive, every door in the city can be walked to, so every building and every room in it
    /// can be reached.
    /// </summary>
    [Test]
    public void EveryDoorCanBeReached([ValueSource(nameof(Seeds))] ulong seed)
    {
        var plan = CityGenerator.Generate(seed);
        bool Open(int x, int y)
        {
            return plan.Floor(x, y) != CityFloor.Ocean
                   && plan.Structure(x, y) is CityStructure.None or CityStructure.Door;
        }

        var reached = new bool[plan.Size * plan.Size];
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(plan.Spawn);
        reached[plan.Spawn.Y * plan.Size + plan.Spawn.X] = true;
        while (queue.TryDequeue(out var tile))
        {
            foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var (x, y) = (tile.X + dx, tile.Y + dy);
                if (!plan.Contains(x, y) || reached[y * plan.Size + x] || !Open(x, y))
                    continue;

                reached[y * plan.Size + x] = true;
                queue.Enqueue((x, y));
            }
        }

        var doors = Enumerable.Range(0, plan.Size * plan.Size)
            .Where(i => plan.Structures[i] == CityStructure.Door)
            .ToList();
        Assert.That(doors, Is.Not.Empty);
        Assert.That(doors.Where(i => !reached[i]).Select(i => (i % plan.Size, i / plan.Size)), Is.Empty);
    }

    /// <summary>
    /// The solar panels charge the plant's SMES bank and nothing else. The bank feeds every substation, directly
    /// or through a block's own SMES, and a substation feeds every APC, with low-voltage cable on from it.
    /// </summary>
    [Test]
    public void WiredFromThePlant([ValueSource(nameof(Seeds))] ulong seed)
    {
        var plan = CityGenerator.Generate(seed);
        var size = plan.Size;
        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        var terminals = plan.Fixtures.Where(f => f.Kind == CityFixture.Terminal)
            .ToDictionary(f => f.Y * size + f.X, f => f.Direction);

        // The tiles joined by one kind of cable, never across a terminal and the SMES it faces.
        HashSet<int> Network(int start, CityCable cable)
        {
            var seen = new HashSet<int> { start };
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.TryPop(out var i))
            {
                for (var d = 0; d < 4; d++)
                {
                    var (x, y) = (i % size + directions[d].Item1, i / size + directions[d].Item2);
                    var next = y * size + x;
                    if (!plan.Contains(x, y) || (plan.Cables[next] & cable) == 0 || seen.Contains(next)
                        || terminals.TryGetValue(i, out var a) && a == d
                        || terminals.TryGetValue(next, out var b) && b == (d + 2) % 4)
                    {
                        continue;
                    }

                    seen.Add(next);
                    stack.Push(next);
                }
            }

            return seen;
        }

        int Faced(KeyValuePair<int, int> terminal)
        {
            var (dx, dy) = directions[terminal.Value];
            return terminal.Key + dy * size + dx;
        }

        var tiles = Enumerable.Range(0, size * size).ToList();
        var panels = tiles.Where(i => plan.Structures[i] == CityStructure.SolarPanel).ToList();
        Assert.That(panels, Is.Not.Empty);
        var input = Network(panels[0], CityCable.High);
        Assert.That(panels.All(input.Contains));
        Assert.That(input.Any(i => plan.Structures[i] is CityStructure.Smes or CityStructure.Substation), Is.False);

        var bank = terminals.Where(t => input.Contains(t.Key)).Select(Faced).ToList();
        Assert.That(bank, Is.Not.Empty);
        var grid = Network(bank[0], CityCable.High);
        Assert.That(bank.All(grid.Contains));

        var substations = tiles.Where(i => plan.Structures[i] == CityStructure.Substation).ToList();
        Assert.That(substations, Is.Not.Empty);
        foreach (var substation in substations)
        {
            var fed = grid.Contains(substation)
                      || terminals.Any(t => grid.Contains(t.Key) && Network(substation, CityCable.High).Contains(Faced(t)));
            Assert.That(fed, $"substation at {substation % size}, {substation / size}");
        }

        var apcs = plan.Fixtures.Where(f => f.Kind == CityFixture.Apc).ToList();
        Assert.That(apcs, Is.Not.Empty);
        foreach (var (_, x, y, _) in apcs)
        {
            var i = y * size + x;
            Assert.That(plan.Cable(x, y) & CityCable.Low, Is.EqualTo(CityCable.Low));
            Assert.That(Network(i, CityCable.Medium).Any(substations.Contains), $"APC at {x}, {y}");
        }
    }
}
