using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._CyberPunk.Cyberspace;
using Content.Server._CyberPunk.Machines;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._CyberPunk;

/// <summary>
/// A routed network gets a region of cyberspace, with a node on a pad for every machine on it, paths between
/// the pads of linked machines and out to the bus, and barriers along the paths. Unplug a machine and its pad
/// goes; plug it back in and it comes back where it was.
/// </summary>
[TestFixture]
public sealed class CyberspaceTest : GameTest
{
    private IEntityManager _entMan = default!;
    private Entity<MapGridComponent> _grid;

    [Test]
    public async Task NetworksGetRegionsOfCyberspace()
    {
        var server = Pair.Server;
        _entMan = server.ResolveDependency<IEntityManager>();
        var machines = _entMan.System<WasmMachineSystem>();
        var cyberspace = _entMan.System<CyberspaceSystem>();
        var mapSys = _entMan.System<SharedMapSystem>();
        var power = _entMan.System<SharedPowerReceiverSystem>();
        var lookup = _entMan.System<EntityLookupSystem>();

        EntityUid a = default, b = default, vending = default, router = default, gap = default;
        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            _grid = mapSys.CreateGridEntity(mapId);
            for (var x = 0; x < 6; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    mapSys.SetTile(_grid, new Vector2i(x, y), new Tile(1));
                }

                var cable = Place("CableData", x, 0);
                if (x == 4)
                    gap = cable;
            }

            a = Place("ComputerProgrammable", 0, 0);
            vending = Place("VendingMachineCola", 2, 0);
            b = Place("ComputerProgrammable", 5, 0);
            router = Place("NetworkRouter", 1, 1);
            foreach (var ent in new[] { a, b, vending, router })
            {
                power.SetNeedsPower(ent, false);
            }
        });

        await server.WaitRunTicks(30);

        Vector2i bTile = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(cyberspace.MapUid, Is.Not.Null, "the first network makes cyberspace");
            Assert.That(cyberspace.RegionOf(router), Is.Not.Null, "the network has a region");

            var routerNode = cyberspace.NodeOf(router);
            Assert.That(routerNode, Is.Not.Null);
            var subnet = (machines.AddressOf(a)!.Value >> 16) & 255;
            Assert.That(Name(routerNode!.Value), Is.EqualTo($"router 10.{subnet}"));

            foreach (var machine in new[] { a, b, vending })
            {
                var node = cyberspace.NodeOf(machine);
                Assert.That(node, Is.Not.Null, $"{machine} has a node");
                Assert.That(Reaches(cyberspace, Tile(routerNode.Value), Tile(node!.Value)), $"{machine}'s pad is joined to the router's");
                Assert.That(cyberspace.FloorAt(Tile(node.Value).X, Tile(node.Value).Y), Is.EqualTo(CyberFloor.Node));
            }

            Assert.That(Name(cyberspace.NodeOf(a)!.Value), Does.StartWith("computer 10."));
            Assert.That(Name(cyberspace.NodeOf(vending)!.Value), Does.StartWith("device 10."));

            // The router's pad opens onto the bus, and the bus leads to the backbone.
            var backbone = _entMan.AllEntities<CyberNodeComponent>()
                .Single(n => n.Comp.Kind == CyberNodeKind.Backbone);
            Assert.That(Reaches(cyberspace, Tile(routerNode.Value), Tile(backbone)));

            // Every tile beside a path that can't be walked on is walled off.
            var mapId = _entMan.GetComponent<TransformComponent>(cyberspace.MapUid!.Value).MapID;
            var rect = cyberspace.Layout!.RegionRect(cyberspace.RegionOf(router)!.Value);
            var checkedOne = false;
            for (var y = rect.Y; y < rect.Y + rect.H; y++)
            {
                for (var x = rect.X; x < rect.X + rect.W; x++)
                {
                    if (CyberLayout.Walkable(cyberspace.FloorAt(x, y)) || !CyberLayout.Walkable(cyberspace.FloorAt(x + 1, y)))
                        continue;

                    var box = Box2.CenteredAround(new Vector2(x + 0.5f, y + 0.5f), new Vector2(0.5f, 0.5f));
                    var hit = lookup.GetEntitiesIntersecting(mapId, box)
                        .Any(e => _entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "CyberspaceBarrier");
                    Assert.That(hit, $"a barrier at {x}, {y}");
                    checkedOne = true;
                }
            }

            Assert.That(checkedOne);

            // Unplug B.
            bTile = Tile(cyberspace.NodeOf(b)!.Value);
            _entMan.DeleteEntity(gap);
        });

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(cyberspace.NodeOf(b), Is.Null, "an unplugged machine has no pad");
            Assert.That(cyberspace.FloorAt(bTile.X, bTile.Y), Is.Not.EqualTo(CyberFloor.Node));
            Place("CableData", 4, 0);
        });

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(cyberspace.NodeOf(b), Is.Not.Null);
            Assert.That(Tile(cyberspace.NodeOf(b)!.Value), Is.EqualTo(bTile), "plugged back in, it's where it was");

            // Without a powered router the region empties.
            power.SetNeedsPower(router, true);
        });

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(cyberspace.NodeOf(router), Is.Null);
            Assert.That(cyberspace.NodeOf(a), Is.Null);
        });
    }

    private EntityUid Place(string prototype, int x, int y)
    {
        return _entMan.SpawnEntity(prototype, new EntityCoordinates(_grid, x + 0.5f, y + 0.5f));
    }

    private string Name(EntityUid uid)
    {
        return _entMan.GetComponent<MetaDataComponent>(uid).EntityName;
    }

    private Vector2i Tile(EntityUid uid)
    {
        var pos = _entMan.GetComponent<TransformComponent>(uid).LocalPosition;
        return new Vector2i((int) MathF.Floor(pos.X), (int) MathF.Floor(pos.Y));
    }

    /// <summary>
    /// Whether one tile of cyberspace can be walked to from another.
    /// </summary>
    private static bool Reaches(CyberspaceSystem cyberspace, Vector2i from, Vector2i to)
    {
        var seen = new HashSet<Vector2i> { from };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(from);
        while (queue.TryDequeue(out var at))
        {
            if (at == to)
                return true;

            foreach (var step in new[] { new Vector2i(0, 1), new Vector2i(1, 0), new Vector2i(0, -1), new Vector2i(-1, 0) })
            {
                var next = at + step;
                if (CyberLayout.Walkable(cyberspace.FloorAt(next.X, next.Y)) && seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        return false;
    }
}
