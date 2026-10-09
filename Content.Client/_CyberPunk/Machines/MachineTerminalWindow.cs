using Content.Shared._CyberPunk.Machines;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._CyberPunk.Machines;

/// <summary>
/// A machine's terminal. Keys go straight to the machine through the screen, which edits the line itself. When the
/// program in front shows a UI, it covers the screen, and a button switches between the two.
/// </summary>
public sealed class MachineTerminalWindow : DefaultWindow
{
    public readonly TerminalScreen Screen = new();
    public readonly ProgramUiView Program = new();

    private readonly ScrollContainer _programPanel;
    private readonly Button _toggle;
    private bool _hasUi;

    /// <summary>
    /// Whether the program's UI is showing, rather than the screen.
    /// </summary>
    public bool ShowingProgram => _programPanel.Visible;

    public MachineTerminalWindow()
    {
        Title = Loc.GetString("machine-terminal-title");
        Resizable = false;

        _toggle = new Button { Visible = false, HorizontalAlignment = HAlignment.Right, ToggleMode = true };
        _toggle.OnToggled += args => ShowProgram(args.Pressed);

        // A scroll container measures as nothing, so the panel takes the screen's size, and scrolls when the
        // program's UI is bigger.
        _programPanel = new ScrollContainer { Visible = false, HScrollEnabled = true };
        _programPanel.AddChild(new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1b1f22") },
            HorizontalExpand = true,
            VerticalExpand = true,
            Children = { Program },
        });

        var stack = new Control { Children = { Screen, _programPanel } };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        column.AddChild(_toggle);
        column.AddChild(stack);
        Contents.AddChild(column);
        UpdateToggle();
    }

    /// <summary>
    /// Names the window as its programs ask, or gives it back its own name.
    /// </summary>
    public void SetProgramTitle(string? title)
    {
        Title = string.IsNullOrWhiteSpace(title) ? Loc.GetString("machine-terminal-title") : title;
    }

    /// <summary>
    /// Shows the program's new UI, switching to it when there wasn't one, and back to the screen when it's gone.
    /// </summary>
    public void SetUi(ProgramUiNode? root)
    {
        Program.SetUi(root);
        var had = _hasUi;
        _hasUi = root != null;
        _toggle.Visible = _hasUi;
        if (_hasUi != had)
            ShowProgram(_hasUi);
    }

    private void ShowProgram(bool show)
    {
        _programPanel.Visible = show;
        _toggle.Pressed = show;
        UpdateToggle();
        if (!show)
            Screen.GrabKeyboardFocus();
        else if (Screen.HasKeyboardFocus())
            Screen.ReleaseKeyboardFocus();
    }

    private void UpdateToggle()
    {
        _toggle.Text = Loc.GetString(_programPanel.Visible ? "machine-terminal-show-screen" : "machine-terminal-show-program");
    }
}
