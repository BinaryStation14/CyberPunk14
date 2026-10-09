namespace Content.Server._CyberPunk.Network;

/// <summary>
/// Data cable, which joins networked machines to switches and routers. Laid next to a switch or router, it has
/// the hub look again for cable to join.
/// </summary>
[RegisterComponent]
public sealed partial class DataCableComponent : Component
{
    /// <summary>
    /// Its node in its node container.
    /// </summary>
    [DataField]
    public string Node = "data";
}
