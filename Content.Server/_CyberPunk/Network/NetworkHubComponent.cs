using Content.Server._CyberPunk.Machines;

namespace Content.Server._CyberPunk.Network;

/// <summary>
/// A network switch or router. Both join the data cable around them into one network while they have power. A
/// router also gives the machines on its network their addresses, joins its network to every other routed
/// network on the map, and keeps the directory of their hostnames.
/// </summary>
[RegisterComponent, Access(typeof(WasmMachineSystem))]
public sealed partial class NetworkHubComponent : Component
{
    /// <summary>
    /// Whether it's a router rather than a switch.
    /// </summary>
    [DataField]
    public bool Router;

    /// <summary>
    /// Its <see cref="DataHubNode"/> in its node container.
    /// </summary>
    [DataField]
    public string Node = "data";

    /// <summary>
    /// A router's subnet, from 1, which decides its machines' addresses: subnet n hands out
    /// 10.(1 + (n - 1) / 254).(1 + (n - 1) % 254).H. Given at map init when it has none.
    /// </summary>
    [DataField]
    public int Subnet;

    /// <summary>
    /// The last part of the address each machine on a router's network has, by machine. A machine that leaves
    /// the network gives its address back, and may come back to a different one.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, byte> Leases = new();
}
