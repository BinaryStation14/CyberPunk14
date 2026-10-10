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

    public MachineTerminalScreenMessage(string screen)
    {
        Screen = screen;
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
/// Keys pressed at a machine's terminal, in order, as codes from <see cref="TerminalKeys"/>. The server answers
/// with a <see cref="MachineTerminalKeysHandledMessage"/> carrying the same <see cref="Sequence"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalKeysMessage : BoundUserInterfaceMessage
{
    public readonly int[] Keys;
    public readonly uint Sequence;

    public MachineTerminalKeysMessage(int[] keys, uint sequence = 0)
    {
        Keys = keys;
        Sequence = sequence;
    }
}

/// <summary>
/// Tells whoever sent a <see cref="MachineTerminalKeysMessage"/> that its keys are handled, and their echo is
/// on the screen, so they can stop showing their own.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalKeysHandledMessage : BoundUserInterfaceMessage
{
    public readonly uint Sequence;

    /// <summary>The line being typed after the keys, for the client's own echo to start from.</summary>
    public readonly string Line;

    public MachineTerminalKeysHandledMessage(uint sequence, string line)
    {
        Sequence = sequence;
        Line = line;
    }
}

/// <summary>
/// Whether a machine's terminal echoes what's typed onto the line, so clients can show it before the echo
/// comes back. It doesn't in raw mode, or while the machine isn't running.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalEchoMessage : BoundUserInterfaceMessage
{
    public readonly bool Echo;

    public MachineTerminalEchoMessage(bool echo)
    {
        Echo = echo;
    }
}

/// <summary>
/// The title a machine's programs give its terminal window, or null for the window's own.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalTitleMessage : BoundUserInterfaceMessage
{
    public readonly string? Title;

    public MachineTerminalTitleMessage(string? title)
    {
        Title = title;
    }
}
