using Content.IntegrationTests.Fixtures;
using Content.Server._CyberPunk.Machines;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._CyberPunk;

/// <summary>
/// Data cable joins computers to a router, which gives them addresses and publishes their hostnames; packets
/// cross between routed networks on one map, and a machine that comes back to a different address can still
/// be reached by name.
/// </summary>
[TestFixture]
public sealed class MachineNetworkTest : GameTest
{
    private IEntityManager _entMan = default!;
    private WasmMachineSystem _machines = default!;
    private Entity<MapGridComponent> _grid;

    [Test]
    public async Task RoutersAddressAndNameMachines()
    {
        var server = Pair.Server;
        _entMan = server.ResolveDependency<IEntityManager>();
        _machines = _entMan.System<WasmMachineSystem>();
        var mapSys = _entMan.System<SharedMapSystem>();
        var power = _entMan.System<SharedPowerReceiverSystem>();

        EntityUid a = default, b = default, delta = default, router = default, gap = default;
        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            _grid = mapSys.CreateGridEntity(mapId);
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 7; y++)
                {
                    mapSys.SetTile(_grid, new Vector2i(x, y), new Tile(1));
                }
            }

            // A at one end of a cable, B at the other, and a router beside the middle.
            for (var x = 0; x < 5; x++)
            {
                var cable = Place("CableData", x, 0);
                if (x == 3)
                    gap = cable;
            }

            a = Place("ComputerProgrammable", 0, 0);
            b = Place("ComputerProgrammable", 4, 0);
            router = Place("NetworkRouter", 2, 1);

            // A second network on the same map, with its own router.
            Place("CableData", 0, 5);
            Place("CableData", 1, 5);
            var d = Place("ComputerProgrammable", 0, 5);
            var router2 = Place("NetworkRouter", 1, 6);

            foreach (var ent in new[] { a, b, router, d, router2 })
            {
                power.SetNeedsPower(ent, false);
            }

            delta = d;
        });

        foreach (var machine in new[] { a, b, delta })
        {
            await RunUntil(server, machine, "$ ");
        }

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            var d = delta;
            Type(a, "hostname alpha");
            Type(b, "hostname beta");
            Type(d, "hostname delta");
            Type(b, "build examples/echo_server.wire");
            Type(b, "run examples/echo_server.bin");
            Type(d, "build examples/echo_server.wire");
            Type(d, "run examples/echo_server.bin");
            Type(a, "build examples/ping.wire");
        });

        await RunUntil(server, a, "built examples/ping.bin");
        await RunUntil(server, b, "echo server listening on beta");
        await RunUntil(server, a, "", ticks: 10);

        uint firstB = 0;
        await server.WaitAssertion(() =>
        {
            var addrA = _machines.AddressOf(a);
            var addrB = _machines.AddressOf(b);
            Assert.That(addrA, Is.Not.Null);
            Assert.That(addrB, Is.Not.Null);
            Assert.That(addrA!.Value & 0xFFFFFF00, Is.EqualTo(addrB!.Value & 0xFFFFFF00), "one router, one subnet");
            Assert.That(addrA.Value & 0xFF, Is.EqualTo(2));
            Assert.That(addrB.Value & 0xFF, Is.EqualTo(3));
            firstB = addrB.Value;

            Type(a, "hosts");
            Type(a, "run examples/ping.bin beta hello");
        });

        await RunUntil(server, a, "reply from beta");
        await server.WaitAssertion(() =>
        {
            var screen = Screen(a);
            Assert.That(screen, Does.Contain($"beta {Format(firstB)}\n"));
            Assert.That(screen, Does.Contain("delta 10."));

            // Across the backbone to the other router's network, by name.
            Type(a, "run examples/ping.bin delta hi");
        });

        await RunUntil(server, a, "reply from delta");

        // Cut B off: it loses its address, and a newcomer takes it. When B comes back it gets another, and
        // its name still finds it.
        EntityUid c = default;
        await server.WaitAssertion(() =>
        {
            _entMan.DeleteEntity(gap);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(_machines.AddressOf(b), Is.Null);
            c = Place("ComputerProgrammable", 1, 0);
            power.SetNeedsPower(c, false);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(_machines.AddressOf(c), Is.EqualTo(firstB));
            Place("CableData", 3, 0);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var addrB = _machines.AddressOf(b);
            Assert.That(addrB, Is.Not.Null);
            Assert.That(addrB, Is.Not.EqualTo(firstB));
            Type(a, "run examples/ping.bin beta again");
        });

        await RunUntil(server, a, "reply from beta (" + Format(_machines.AddressOf(b)!.Value) + ") in");

        // Without a powered router the network carries nothing: no addresses.
        await server.WaitAssertion(() =>
        {
            power.SetNeedsPower(router, true);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(_machines.AddressOf(a), Is.Null);
            Assert.That(_machines.AddressOf(b), Is.Null);
            Type(a, "ip");
        });

        await RunUntil(server, a, "ip: no address");
    }

    /// <summary>
    /// A vending machine on data cable gets an address, and a computer's requests read it and work it.
    /// </summary>
    [Test]
    public async Task ComputersWorkMachinesWithAUi()
    {
        var server = Pair.Server;
        _entMan = server.ResolveDependency<IEntityManager>();
        _machines = _entMan.System<WasmMachineSystem>();
        var mapSys = _entMan.System<SharedMapSystem>();
        var power = _entMan.System<SharedPowerReceiverSystem>();

        EntityUid computer = default, vending = default;
        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            _grid = mapSys.CreateGridEntity(mapId);
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    mapSys.SetTile(_grid, new Vector2i(x, y), new Tile(1));
                }

                Place("CableData", x, 0);
            }

            computer = Place("ComputerProgrammable", 0, 0);
            vending = Place("VendingMachineCola", 2, 0);
            var router = Place("NetworkRouter", 1, 1);
            foreach (var ent in new[] { computer, vending, router })
            {
                power.SetNeedsPower(ent, false);
            }
        });

        await server.WaitRunTicks(30);

        uint address = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(_machines.AddressOf(vending), Is.Not.Null, "the vending machine has an address");
            address = _machines.AddressOf(vending)!.Value;

            var info = (MachineLiteral.Dict) MachineLiteral.Parse(_machines.DeviceRequest(computer, address, "info")!)!;
            Assert.That(info.TryGet("kind", out var kind) && kind is "VendingMachineCola");
            Assert.That(info.TryGet("calls", out var calls) && ((MachineLiteral.Dict) calls!).TryGet("VendingMachineEject", out _));

            var state = (MachineLiteral.Dict) MachineLiteral.Parse(_machines.DeviceRequest(computer, address, "state")!)!;
            Assert.That(state.TryGet("VendingMachine", out var stock));
            Assert.That(((MachineLiteral.Dict) stock!).TryGet("Inventory", out var inventory));
            var item = (string) ((MachineLiteral.Dict) inventory!)[0].Key!;

            Assert.That(_machines.DeviceRequest(computer, address, $"call vending_machine_eject {{\"type\": \"Regular\", \"id\": \"{item}\"}}"),
                Is.EqualTo("None"));
            Assert.That(_machines.DeviceRequest(computer, address, "call dance {}"), Does.StartWith("!"));
            Assert.That(_machines.DeviceRequest(computer, address, "call vending_machine_eject {\"type\": 7}"), Does.StartWith("!"));
            Assert.That(_machines.DeviceRequest(computer, address, "hello"), Does.StartWith("!"));
            Assert.That(_machines.DeviceRequest(computer, address + 100, "info"), Is.Null);

            power.SetNeedsPower(vending, true);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(_machines.DeviceRequest(computer, address, "state"), Is.EqualTo("!it has no power"));
        });
    }

    private EntityUid Place(string prototype, int x, int y)
    {
        return _entMan.SpawnEntity(prototype, new EntityCoordinates(_grid, x + 0.5f, y + 0.5f));
    }

    private void Type(EntityUid machine, string line)
    {
        _machines.TypeLine((machine, _entMan.GetComponent<WasmMachineComponent>(machine)), line);
    }

    private string Screen(EntityUid machine)
    {
        return _entMan.GetComponent<WasmMachineComponent>(machine).Screen;
    }

    private async Task RunUntil(RobustIntegrationTest.ServerIntegrationInstance server,
        EntityUid machine,
        string text,
        int ticks = 600)
    {
        for (var i = 0; i < ticks; i++)
        {
            await server.WaitRunTicks(1);
            if (text.Length > 0 && Screen(machine).Contains(text))
                return;
        }

        if (text.Length > 0)
            Assert.Fail($"\"{text}\" never appeared. The screen:\n{Screen(machine)}");
    }

    private static string Format(uint address)
    {
        return $"{address >> 24}.{(address >> 16) & 255}.{(address >> 8) & 255}.{address & 255}";
    }
}
