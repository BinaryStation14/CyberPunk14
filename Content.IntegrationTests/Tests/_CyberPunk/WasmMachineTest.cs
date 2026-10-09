using Content.IntegrationTests.Fixtures;
using Content.Server._CyberPunk.Machines;
using Content.Server._CyberPunk.Wasm;
using Content.Shared.Coordinates;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

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
            Assert.That(machine.Screen, Does.Contain(StubOs.Name));

            machines.TypeLine((computer, machine), "run hello.wat");
            machines.TypeLine((computer, machine), "build hello.wat");
            machines.TypeLine((computer, machine), "run hello.wasm");
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var machine = entManager.GetComponent<WasmMachineComponent>(computer);
            Assert.That(machine.Screen, Does.Contain("Hello from a program!"));

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
}
