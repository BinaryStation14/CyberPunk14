using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._CyberPunk.Machines;

/// <summary>
/// A machine's terminal. Keys go straight to the machine through the screen, which edits the line itself.
/// </summary>
public sealed class MachineTerminalWindow : DefaultWindow
{
    public readonly TerminalScreen Screen = new();

    public MachineTerminalWindow()
    {
        Title = Loc.GetString("machine-terminal-title");
        Resizable = false;
        Contents.AddChild(Screen);
    }
}
