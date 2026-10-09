using Robust.Shared.Serialization;

namespace Content.Shared._CyberPunk.Machines;

/// <summary>
/// The kinds of widget a program's UI is built from.
/// </summary>
[Serializable, NetSerializable]
public enum ProgramUiKind : byte
{
    /// <summary>Its children, top to bottom.</summary>
    Column,

    /// <summary>Its children, left to right.</summary>
    Row,

    Label,

    /// <summary>Sends <see cref="ProgramUiEventKind.Click"/>.</summary>
    Button,

    /// <summary>A line of text to type; sends <see cref="ProgramUiEventKind.Submit"/> on Enter.</summary>
    Input,

    /// <summary>Items to pick from; sends <see cref="ProgramUiEventKind.Select"/>.</summary>
    List,

    /// <summary>A bar filled <see cref="ProgramUiNode.Value"/> of <see cref="ProgramUiNode.Max"/>.</summary>
    Progress,

    /// <summary>A drawing (<see cref="ProgramUiNode.Ops"/>); sends <see cref="ProgramUiEventKind.Click"/>.</summary>
    Canvas,
}

/// <summary>
/// One widget of a program's UI, and what's inside it. Programs describe it as text (see the server's
/// <c>ProgramUiParser</c>), and the terminal window draws it with the game's own controls.
/// </summary>
[Serializable, NetSerializable]
public sealed class ProgramUiNode
{
    public ProgramUiKind Kind;

    /// <summary>The name events from it carry; empty for widgets that send none.</summary>
    public string Id = "";

    /// <summary>A label's, button's or input's text.</summary>
    public string Text = "";

    /// <summary>A list's items.</summary>
    public string[] Items = [];

    /// <summary>A progress bar's value and maximum.</summary>
    public int Value;

    public int Max;

    /// <summary>A canvas's size, in pixels.</summary>
    public int Width;

    public int Height;

    /// <summary>A column's or row's widgets.</summary>
    public ProgramUiNode[] Children = [];

    /// <summary>What a canvas draws, in order.</summary>
    public CanvasOp[] Ops = [];
}

[Serializable, NetSerializable]
public enum CanvasOpKind : byte
{
    /// <summary>A filled rectangle: X, Y, A (width), B (height).</summary>
    Rect,

    /// <summary>A line from X, Y to A, B.</summary>
    Line,

    /// <summary>Text at X, Y.</summary>
    Text,
}

/// <summary>
/// One thing a canvas draws.
/// </summary>
[Serializable, NetSerializable]
public sealed class CanvasOp
{
    public CanvasOpKind Kind;
    public int X;
    public int Y;
    public int A;
    public int B;
    public string Text = "";
    public Color Color;
}

/// <summary>
/// What someone did to a widget.
/// </summary>
[Serializable, NetSerializable]
public enum ProgramUiEventKind : byte
{
    /// <summary>A button was pressed (no value), or a canvas clicked (the value is "X Y").</summary>
    Click,

    /// <summary>Enter was pressed in an input; the value is its text.</summary>
    Submit,

    /// <summary>A list item was picked; the value is its index.</summary>
    Select,
}

/// <summary>
/// The UI of the program in front of a machine's terminal, or null for none, sent to everyone with the
/// terminal open whenever it changes.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalUiMessage : BoundUserInterfaceMessage
{
    public readonly ProgramUiNode? Root;

    public MachineTerminalUiMessage(ProgramUiNode? root)
    {
        Root = root;
    }
}

/// <summary>
/// Someone used a widget of the program's UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalUiEventMessage : BoundUserInterfaceMessage
{
    public readonly string Id;
    public readonly ProgramUiEventKind Kind;
    public readonly string Value;

    public MachineTerminalUiEventMessage(string id, ProgramUiEventKind kind, string value)
    {
        Id = id;
        Kind = kind;
        Value = value;
    }
}
