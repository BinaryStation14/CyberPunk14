using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server._CyberPunk.Network;

/// <summary>
/// A switch's or router's ports: it joins the data cable on its own tile and the four beside it, and the
/// switches and routers racked next to it.
/// </summary>
/// <remarks>
/// Data cable doesn't look for hubs itself, so a cable laid next to one has the hub look again
/// (<see cref="DataCableComponent"/>).
/// </remarks>
[DataDefinition]
public sealed partial class DataHubNode : Node
{
    /// <summary>
    /// Whether the hub works: it has power. While it doesn't, it joins nothing.
    /// </summary>
    /// <remarks>
    /// If you change this, you must call <see cref="NodeGroupSystem.QueueReflood"/> on it.
    /// </remarks>
    [ViewVariables]
    public bool Enabled = true;

    public override bool Connectable(IEntityManager entMan, TransformComponent? xform = null)
    {
        return Enabled && base.Connectable(entMan, xform);
    }

    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var mapSystem = entMan.System<SharedMapSystem>();
        var tile = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);

        foreach (var (dir, node) in NodeHelpers.GetCardinalNeighborNodes(nodeQuery, gridEnt, tile, mapSystem))
        {
            if (node is CableNode || node is DataHubNode && dir != Direction.Invalid)
                yield return node;
        }
    }
}
