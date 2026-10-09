using Robust.Shared.Serialization;

namespace Content.Shared._CyberPunk.Machines;

[Serializable, NetSerializable]
public enum MachineTerminalUiKey : byte
{
    Key,
}

/// <summary>
/// Asks for the whole screen of a machine's terminal, which a window does when it opens.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalRefreshMessage : BoundUserInterfaceMessage;

/// <summary>
/// The whole screen of a machine's terminal, sent to someone who asked for it with
/// <see cref="MachineTerminalRefreshMessage"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalScreenMessage : BoundUserInterfaceMessage
{
    public readonly string Screen;
    public readonly bool Raw;

    public MachineTerminalScreenMessage(string screen, bool raw)
    {
        Screen = screen;
        Raw = raw;
    }
}

/// <summary>
/// New output on a machine's terminal, sent to everyone who has it open, to add to their screen with
/// <see cref="TerminalText.Apply"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalOutputMessage : BoundUserInterfaceMessage
{
    public readonly string Text;

    public MachineTerminalOutputMessage(string text)
    {
        Text = text;
    }
}

/// <summary>
/// A line typed at a machine's terminal.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalLineMessage : BoundUserInterfaceMessage
{
    public readonly string Line;

    public MachineTerminalLineMessage(string line)
    {
        Line = line;
    }
}

/// <summary>
/// A key pressed at a machine's terminal in raw mode, as one of the codes in <see cref="TerminalKeys"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalKeyMessage : BoundUserInterfaceMessage
{
    public readonly int Key;

    public MachineTerminalKeyMessage(int key)
    {
        Key = key;
    }
}
