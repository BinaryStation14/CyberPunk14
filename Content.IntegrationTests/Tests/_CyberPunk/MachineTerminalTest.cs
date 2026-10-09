using System.Linq;
using Content.Client._CyberPunk.Machines;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._CyberPunk.Machines;
using Content.Server._CyberPunk.Wasm;
using Content.Shared._CyberPunk.Machines;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests._CyberPunk;

/// <summary>
/// A programmable computer's terminal window shows its screen, sends every key to the machine, follows output
/// from anyone else at the machine, and shows the whole screen again when it's reopened.
/// </summary>
public sealed class MachineTerminalTest : InteractionTest
{
    /// <summary>
    /// Presses each key of <paramref name="text"/> at the open terminal, then Enter.
    /// </summary>
    private async Task TypeLine(string text)
    {
        var keys = text.Select(c => (int) c).Append(TerminalKeys.Enter).ToArray();
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalKeysMessage(keys));
    }

    [Test]
    public async Task TerminalShowsScreenAndTakesInput()
    {
        await SpawnTarget("ComputerProgrammable");
        ToggleNeedPower();
        await RunTicks(30);

        // Open the terminal: it shows what the machine printed before it was opened.
        await Interact();
        var window = GetWindow<MachineTerminalWindow>();
        Assert.That(window.Screen.Text, Does.Contain(DefaultOs.Name));

        // A typed line runs on the machine, and its output reaches the window.
        await TypeLine("echo first line");
        Assert.That(window.Screen.Text, Does.Contain("echo first line\nfirst line\n"));

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

        // A program in raw mode gets each key as it's pressed.
        await TypeLine("build keys.wat");
        await TypeLine("run keys.bin");
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalKeysMessage(['a', TerminalKeys.Up]));
        Assert.That(window.Screen.Text, Does.Contain("key 97\n"));
        Assert.That(window.Screen.Text, Does.Contain($"key {TerminalKeys.Up}\n"));

        // Ctrl+Q ends the program, and keys edit a line for the shell again.
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalKeysMessage([TerminalKeys.Ctrl('q')]));
        await TypeLine("echo back");
        Assert.That(window.Screen.Text, Does.EndWith("echo back\nback\n$ "));
    }

    [Test]
    public async Task TerminalShowsAProgramsUi()
    {
        await SpawnTarget("ComputerProgrammable");
        ToggleNeedPower();
        await RunTicks(30);

        var machines = SEntMan.System<WasmMachineSystem>();
        var target = SEntMan.GetEntity(Target!.Value);
        await Server.WaitPost(() =>
        {
            machines.WriteFile((target, SEntMan.GetComponent<WasmMachineComponent>(target)),
                "panel.wire",
                """
                ui.show(ui.column([ui.label("Door control"), ui.button("open", "Open")]))
                def tick():
                    for e in ui.events():
                        print("pressed", e.id)
                        sys.exit()
                """u8.ToArray());
        });

        await Interact();
        var window = GetWindow<MachineTerminalWindow>();
        Assert.That(window.ShowingProgram, Is.False);

        // The program's UI takes the window's place of the screen.
        await TypeLine("build panel.wire");
        await TypeLine("run panel.bin");
        await RunTicks(5);
        Assert.That(window.ShowingProgram, Is.True);
        Assert.That(window.Program.ChildCount, Is.EqualTo(1));

        // Pressing its button reaches the program, and when the program ends the screen is back.
        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalUiEventMessage("open", ProgramUiEventKind.Click, ""));
        await RunTicks(5);
        Assert.That(window.Screen.Text, Does.Contain("pressed open\n"));
        Assert.That(window.ShowingProgram, Is.False);
        Assert.That(window.Program.ChildCount, Is.Zero);
    }

    [Test]
    public async Task TheCalculatorExampleCalculates()
    {
        await SpawnTarget("ComputerProgrammable");
        ToggleNeedPower();
        await RunTicks(30);

        await Interact();
        var window = GetWindow<MachineTerminalWindow>();
        await TypeLine("build examples/calc.wire");
        await TypeLine("run examples/calc.bin");
        await RunTicks(5);
        Assert.That(window.ShowingProgram, Is.True);

        // 12 + 3 = 15, then 7 / 0 is an error that C clears.
        var target = SEntMan.GetEntity(Target!.Value);
        string Display() => SEntMan.GetComponent<WasmMachineComponent>(target).Vm!.Ui!.Children[1].Text;
        foreach (var (button, shows) in new[]
                 {
                     ("num1", "1"), ("num2", "12"), ("add", "12"), ("num3", "3"), ("eq", "15"),
                     ("num7", "7"), ("div", "7"), ("num0", "0"), ("eq", "Error"), ("clear", "0"),
                 })
        {
            await SendBui(MachineTerminalUiKey.Key, new MachineTerminalUiEventMessage(button, ProgramUiEventKind.Click, ""));
            await RunTicks(3);
            Assert.That(Display(), Is.EqualTo(shows), $"after {button}");
        }

        await SendBui(MachineTerminalUiKey.Key, new MachineTerminalUiEventMessage("quit", ProgramUiEventKind.Click, ""));
        await RunTicks(5);
        Assert.That(window.ShowingProgram, Is.False);
        Assert.That(window.Screen.Text, Does.Contain("calc: bye\n"));
    }

    [Test]
    public async Task TerminalKeepsScreenWhenReopened()
    {
        await SpawnTarget("ComputerProgrammable");
        ToggleNeedPower();
        await RunTicks(30);

        // The client opens the terminal itself, as a player clicking on it does.
        await PressKey(EngineKeyFunctions.Use);
        await RunTicks(15);
        await TypeLine("echo kept output");
        await CloseBui(MachineTerminalUiKey.Key);
        Assert.That(IsUiOpen(MachineTerminalUiKey.Key), Is.False);

        await PressKey(EngineKeyFunctions.Use);
        await RunTicks(15);
        var window = GetWindow<MachineTerminalWindow>();
        Assert.That(window.Screen.Text, Does.Contain(DefaultOs.Name));
        Assert.That(window.Screen.Text, Does.Contain("kept output"));
    }
}
