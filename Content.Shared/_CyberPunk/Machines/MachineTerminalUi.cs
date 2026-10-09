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
/// Keys pressed at a machine's terminal, in order, as codes from <see cref="TerminalKeys"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class MachineTerminalKeysMessage : BoundUserInterfaceMessage
{
    public readonly int[] Keys;

    public MachineTerminalKeysMessage(int[] keys)
    {
        Keys = keys;
    }
}
