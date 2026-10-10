using Content.Shared._CyberPunk.Machines;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._CyberPunk.Machines;

/// <summary>
/// Opens a machine's terminal. It asks the server for the whole screen when it opens, and the server sends each
/// new piece of output after that, and the program's UI and window title whenever they change, so everyone at the machine sees the
/// same thing.
/// </summary>
[UsedImplicitly]
public sealed class MachineTerminalBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MachineTerminalWindow? _window;

    public MachineTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MachineTerminalWindow>();
        _window.Screen.OnKeys += (keys, sequence) => SendMessage(new MachineTerminalKeysMessage(keys, sequence));
        _window.Program.OnEvent += (id, kind, value) => SendMessage(new MachineTerminalUiEventMessage(id, kind, value));
        _window.Screen.GrabKeyboardFocus();

        SendMessage(new MachineTerminalRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (_window == null)
            return;

        switch (message)
        {
            case MachineTerminalScreenMessage screen:
                _window.Screen.SetScreen(screen.Screen);
                break;
            case MachineTerminalOutputMessage output:
                _window.Screen.AddOutput(output.Text);
                break;
            case MachineTerminalUiMessage ui:
                _window.SetUi(ui.Root);
                break;
            case MachineTerminalTitleMessage title:
                _window.SetProgramTitle(title.Title);
                break;
            case MachineTerminalEchoMessage echo:
                _window.Screen.SetEcho(echo.Echo);
                break;
            case MachineTerminalKeysHandledMessage handled:
                _window.Screen.KeysHandled(handled.Sequence, handled.Line);
                break;
        }
    }
}
