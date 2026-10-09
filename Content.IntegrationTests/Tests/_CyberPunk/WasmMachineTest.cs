using Content.IntegrationTests.Fixtures;
using Content.Server._CyberPunk.Machines;
using Content.Server._CyberPunk.Wasm;
using Content.Server._CyberPunk.Wire;
using Content.Shared.Coordinates;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._CyberPunk;

/// <summary>
/// A programmable computer boots its OS when it gets power, runs what's typed at it, and stops when the
/// power goes.
/// </summary>
[TestFixture]
public sealed class WasmMachineTest : GameTest
{
    [Test]
    public async Task BootsWithPowerAndStopsWithout()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSys = entManager.System<SharedMapSystem>();
        var power = entManager.System<SharedPowerReceiverSystem>();
        var machines = entManager.System<WasmMachineSystem>();

        EntityUid computer = default;
        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            var grid = mapSys.CreateGridEntity(mapId);
            mapSys.SetTile(grid, Vector2i.Zero, new Tile(1));

            // There's no APC, so it starts without power.
            computer = entManager.SpawnEntity("ComputerProgrammable", grid.Owner.ToCoordinates());
            var machine = entManager.GetComponent<WasmMachineComponent>(computer);
            Assert.That(machine.Vm, Is.Not.Null);
            Assert.That(machine.Vm!.State, Is.EqualTo(VmState.Off));

            power.SetNeedsPower(computer, false);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var machine = entManager.GetComponent<WasmMachineComponent>(computer);
            Assert.That(machine.Vm!.State, Is.EqualTo(VmState.Running));
            Assert.That(machine.Screen, Does.Contain(DefaultOs.Name));

            machines.TypeLine((computer, machine), "run hello.wat");
            machines.TypeLine((computer, machine), "build hello.wat");
            machines.TypeLine((computer, machine), "run hello.bin");
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var machine = entManager.GetComponent<WasmMachineComponent>(computer);
            Assert.That(machine.Screen, Does.Contain("Hello from a program!"));

            // A new Wire program builds and runs on the machine itself.
            machines.TypeLine((computer, machine), "new greet");
            machines.TypeLine((computer, machine), "build greet.wire");
            machines.TypeLine((computer, machine), "run greet.bin");
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var machine = entManager.GetComponent<WasmMachineComponent>(computer);
            Assert.That(machine.Screen, Does.Contain("Hello from a new program!"));

            power.SetNeedsPower(computer, true);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var machine = entManager.GetComponent<WasmMachineComponent>(computer);
            Assert.That(machine.Vm!.State, Is.EqualTo(VmState.Off));
            Assert.That(machine.Screen, Does.Contain("[power lost]"));

            entManager.DeleteEntity(computer);
        });
    }

    /// <summary>
    /// Every Wire program a machine starts with builds, so the examples players are pointed at work.
    /// </summary>
    [Test]
    public async Task SeededWireProgramsBuild()
    {
        var server = Pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || Pair.IsTestPrototype(proto) ||
                        !proto.TryComp<WasmMachineComponent>(out var machine, factory))
                        continue;

                    foreach (var (name, source) in machine.Files)
                    {
                        if (!name.EndsWith(".wire"))
                            continue;

                        Assert.DoesNotThrow(() => WireCompiler.Compile(source), $"{proto.ID}'s {name} doesn't build");
                    }
                }
            });
        });
    }
}
