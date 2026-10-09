namespace Content.Server._CyberPunk.Cyberspace;

/// <summary>
/// What a node in cyberspace stands for.
/// </summary>
public enum CyberNodeKind : byte
{
    /// <summary>The backbone that joins every network, in the middle of the hub.</summary>
    Backbone,
    Router,
    Switch,
    Computer,
    DoorController,
    Camera,

    /// <summary>A machine with a UI of its own on the network, like a vending machine.</summary>
    Device,
}

/// <summary>
/// A node in cyberspace: the pad of a machine on a network, or the backbone.
/// </summary>
[RegisterComponent, Access(typeof(CyberspaceSystem))]
public sealed partial class CyberNodeComponent : Component
{
    [ViewVariables]
    public CyberNodeKind Kind;

    /// <summary>
    /// The machine it stands for; none for the backbone.
    /// </summary>
    [ViewVariables]
    public EntityUid? Machine;

    /// <summary>
    /// The region it's in; none for the backbone.
    /// </summary>
    [ViewVariables]
    public int? Region;
}

/// <summary>
/// The map that holds all of cyberspace.
/// </summary>
[RegisterComponent, Access(typeof(CyberspaceSystem))]
public sealed partial class CyberspaceMapComponent : Component;
