using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._CyberPunk.Cyberspace;

/// <summary>
/// A runner has stood at a locked machine's node long enough to breach it.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class CyberBreachDoAfterEvent : SimpleDoAfterEvent;
