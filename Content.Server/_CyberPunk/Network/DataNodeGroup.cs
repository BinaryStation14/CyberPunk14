using Content.Server.NodeContainer.NodeGroups;
using Content.Shared.NodeContainer.NodeGroups;

namespace Content.Server._CyberPunk.Network;

/// <summary>
/// Machines, switches and routers joined by data cable: one local network, if a router serves it.
/// </summary>
[NodeGroup(NodeGroupID.Data)]
public sealed class DataNodeGroup : BaseNodeGroup;
