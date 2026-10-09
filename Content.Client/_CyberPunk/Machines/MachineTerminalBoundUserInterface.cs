using Content.Shared._CyberPunk.Machines;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._CyberPunk.Machines;

/// <summary>
/// Opens a machine's terminal. It asks the server for the whole screen when it opens, and the server sends each
/// new piece of output after that, so everyone at the machine sees the same thing.
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
        _window.Screen.OnKeys += keys => SendMessage(new MachineTerminalKeysMessage(keys));
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
        }
    }
}
