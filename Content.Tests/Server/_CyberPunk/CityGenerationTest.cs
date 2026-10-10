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
        Assert.That(a.Spawn, Is.EqualTo(b.Spawn));
    }

    [Test]
    public void CoastalCityInTheBadlands([ValueSource(nameof(Seeds))] ulong seed)
    {
        var plan = CityGenerator.Generate(seed);
        Assert.That(plan.Zones, Does.Contain(CityZone.Ocean));
        Assert.That(plan.Zones, Does.Contain(CityZone.Downtown));
        Assert.That(plan.Zones.Any(z => z is CityZone.Badlands or CityZone.Scrub or CityZone.Solar));
        Assert.That(plan.Floor(plan.Spawn.X, plan.Spawn.Y), Is.EqualTo(CityFloor.Asphalt));
        Assert.That(plan.Structure(plan.Spawn.X, plan.Spawn.Y), Is.EqualTo(CityStructure.None));
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
}
