using Content.Client._CyberPunk.Machines;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._CyberPunk.Machines;
using Content.Server._CyberPunk.Wasm;
using Content.Shared._CyberPunk.Machines;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._CyberPunk;

/// <summary>
/// A programmable computer's terminal window shows its screen, sends what's typed, follows output from anyone
/// else at the machine, and passes keys straight to a program in raw mode.
/// </summary>
public sealed class MachineTerminalTest : InteractionTest
{
    [Test]
    public async Task TerminalShowsScreenAndTakesInput()
    {
        await SpawnTarget("ComputerProgrammable");
        ToggleNeedPower();
        await RunTicks(30);

        // Open the terminal: it shows what the machine printed before it was opened.
        await Interact();
        var window = GetWindow<MachineTerminalWindow>();
        Assert.That(window.Screen.Text, Does.Contain(StubOs.Name));
        Assert.That(window.Input.Visible, Is.True);

        // A typed line runs on the machine, and its output reaches the window.
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalLineMessage("echo first line"));
        await RunTicks(15);
        Assert.That(window.Screen.Text, Does.Contain("first line"));

        // Someone else at the machine types too: their output shows up here as well, and the window's copy of
        // the screen matches the machine's.
        var machines = SEntMan.System<WasmMachineSystem>();
        var target = SEntMan.GetEntity(Target!.Value);
        await Server.WaitPost(() =>
        {
            var other = SEntMan.SpawnEntity("InteractionTestMob", SEntMan.GetCoordinates(PlayerCoords));
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(target, MachineTerminalUiKey.Key, other);
            machines.TypeLine((target, SEntMan.GetComponent<WasmMachineComponent>(target)), "echo from someone else");
        });
        await RunTicks(15);
        Assert.That(window.Screen.Text, Does.Contain("from someone else"));
        Assert.That(window.Screen.Text, Is.EqualTo(SEntMan.GetComponent<WasmMachineComponent>(target).Screen));

        // A program in raw mode gets each key as it's pressed, and the window swaps its input line for keys.
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalLineMessage("build keys.wat"));
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalLineMessage("run keys.wasm"));
        await RunTicks(15);
        Assert.That(window.Screen.Raw, Is.True);
        Assert.That(window.Input.Visible, Is.False);

        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalKeyMessage('a'));
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalKeyMessage(TerminalKeys.Up));
        await RunTicks(15);
        Assert.That(window.Screen.Text, Does.Contain("key 97\n"));
        Assert.That(window.Screen.Text, Does.Contain($"key {TerminalKeys.Up}\n"));

        // Ctrl+Q ends the program, and the terminal goes back to lines.
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalKeyMessage(TerminalKeys.Ctrl('q')));
        await RunTicks(15);
        Assert.That(window.Screen.Raw, Is.False);
        Assert.That(window.Input.Visible, Is.True);
    }
}
