using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._CyberPunk.Machines;

/// <summary>
/// A machine's terminal: its screen, and a line to type commands on. In raw mode the line goes away and keys
/// go straight to the program through the screen.
/// </summary>
public sealed class MachineTerminalWindow : DefaultWindow
{
    public readonly TerminalScreen Screen;
    public readonly HistoryLineEdit Input;

    /// <summary>
    /// A line typed and sent with Enter.
    /// </summary>
    public event Action<string>? OnLine;

    public MachineTerminalWindow()
    {
        Title = Loc.GetString("machine-terminal-title");
        Resizable = false;

        Screen = new TerminalScreen();
        Input = new HistoryLineEdit
        {
            PlaceHolder = Loc.GetString("machine-terminal-placeholder"),
            HorizontalExpand = true,
        };

        Input.OnTextEntered += args =>
        {
            OnLine?.Invoke(args.Text);
            Input.Clear();
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };
        box.AddChild(Screen);
        box.AddChild(Input);
        Contents.AddChild(box);
    }

    /// <summary>
    /// Shows or hides the line to type on as the terminal goes in and out of raw mode, and gives the keyboard to
    /// whichever takes input now.
    /// </summary>
    public void UpdateMode()
    {
        if (Input.Visible != Screen.Raw)
            return;

        Input.Visible = !Screen.Raw;
        if (Screen.Raw)
            Screen.GrabKeyboardFocus();
        else
            Input.GrabKeyboardFocus();
    }
}
