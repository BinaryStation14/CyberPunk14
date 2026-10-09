using System.Linq;
using Content.Server._CyberPunk.Network;
using Content.Server._CyberPunk.Wasm;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.NodeContainer;
using Content.Shared.Power;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._CyberPunk.Machines;

/// <summary>
/// The network between machines, after Switchboard's <c>sb_sim/src/network.rs</c>: data cable joins machines to
/// switches and routers, and everything joined that way is one local network. A network works when a router
/// with power is on it: the router gives each machine an address, joins the network to every other routed
/// network on the same map, and keeps the directory of hostnames the machines publish. Machines on a network
/// with no router have no address and can't send anything.
/// </summary>
/// <remarks>
/// The network is worked out again only when something changes: cable or a machine joins or leaves, a hub
/// gains or loses power, or a program changes its hostname. Packets sent in a tick are delivered after every
/// machine has run, to be read the next tick.
/// </remarks>
public sealed partial class WasmMachineSystem
{
    [Dependency] private NodeContainerSystem _nodes = default!;
    [Dependency] private NodeGroupSystem _nodeGroups = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private static readonly IReadOnlyDictionary<string, uint> NoHosts = new Dictionary<string, uint>();

    private bool _networkDirty = true;

    /// <summary>The machine at each address, and the map whose backbone its network is on.</summary>
    private readonly Dictionary<uint, (EntityUid Machine, MapId Map)> _addresses = new();

    private readonly List<Packet> _sent = new();

    private void InitializeNetwork()
    {
        SubscribeLocalEvent<WasmMachineComponent, NodeGroupsRebuilt>(OnMachineNodesRebuilt);

        SubscribeLocalEvent<NetworkHubComponent, MapInitEvent>(OnHubMapInit);
        SubscribeLocalEvent<NetworkHubComponent, ComponentShutdown>(OnHubShutdown);
        SubscribeLocalEvent<NetworkHubComponent, PowerChangedEvent>(OnHubPowerChanged);
        SubscribeLocalEvent<NetworkHubComponent, NodeGroupsRebuilt>(OnHubNodesRebuilt);

        SubscribeLocalEvent<DataCableComponent, MapInitEvent>(OnCableMapInit);
        SubscribeLocalEvent<DataCableComponent, AnchorStateChangedEvent>(OnCableAnchorChanged);
    }

    /// <summary>
    /// Has the network worked out again before machines next run.
    /// </summary>
    public void RefreshNetwork()
    {
        _networkDirty = true;
    }

    private void OnMachineNodesRebuilt(Entity<WasmMachineComponent> ent, ref NodeGroupsRebuilt args)
    {
        _networkDirty = true;
    }

    private void OnHubNodesRebuilt(Entity<NetworkHubComponent> ent, ref NodeGroupsRebuilt args)
    {
        _networkDirty = true;
    }

    private void OnHubMapInit(Entity<NetworkHubComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Router && (ent.Comp.Subnet <= 0 || SubnetTaken(ent)))
            ent.Comp.Subnet = FreeSubnet();

        SetHubEnabled(ent, _power.IsPowered(ent));
        _networkDirty = true;
    }

    private void OnHubShutdown(Entity<NetworkHubComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Leases.Clear();
        _networkDirty = true;
    }

    private void OnHubPowerChanged(Entity<NetworkHubComponent> ent, ref PowerChangedEvent args)
    {
        SetHubEnabled(ent, args.Powered);
        _networkDirty = true;
    }

    private void SetHubEnabled(Entity<NetworkHubComponent> ent, bool enabled)
    {
        if (!_nodes.TryGetNode(ent.Owner, ent.Comp.Node, out DataHubNode? node) || node.Enabled == enabled)
            return;

        node.Enabled = enabled;
        _nodeGroups.QueueReflood(node);
    }

    private bool SubnetTaken(Entity<NetworkHubComponent> ent)
    {
        var query = EntityQueryEnumerator<NetworkHubComponent>();
        while (query.MoveNext(out var uid, out var hub))
        {
            if (uid != ent.Owner && hub.Router && hub.Subnet == ent.Comp.Subnet)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The lowest subnet no router has.
    /// </summary>
    private int FreeSubnet()
    {
        var taken = new HashSet<int>();
        var query = EntityQueryEnumerator<NetworkHubComponent>();
        while (query.MoveNext(out var hub))
        {
            if (hub.Router)
                taken.Add(hub.Subnet);
        }

        var subnet = 1;
        while (taken.Contains(subnet))
        {
            subnet++;
        }

        return subnet;
    }

    /// <summary>
    /// The first three parts of the addresses on a subnet: 10.(1 + (n - 1) / 254).(1 + (n - 1) % 254).
    /// </summary>
    public static uint SubnetBase(int subnet)
    {
        var n = (uint) Math.Max(subnet - 1, 0);
        return 10u << 24 | ((1 + n / 254) & 255) << 16 | (1 + n % 254) << 8;
    }

    private void OnCableMapInit(Entity<DataCableComponent> ent, ref MapInitEvent args)
    {
        RefloodHubsNear(ent);
    }

    private void OnCableAnchorChanged(Entity<DataCableComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            RefloodHubsNear(ent);
    }

    /// <summary>
    /// Has the switches and routers on a cable's tile and the four beside it look for cable again, as cable
    /// doesn't look for them.
    /// </summary>
    private void RefloodHubsNear(EntityUid cable)
    {
        var xform = Transform(cable);
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tile = _map.TileIndicesFor((gridUid, grid), xform.Coordinates);
        foreach (var (_, node) in NodeHelpers.GetCardinalNeighborNodes(GetEntityQuery<NodeContainerComponent>(),
                     (gridUid, grid),
                     tile,
                     _map))
        {
            if (node is DataHubNode)
                _nodeGroups.QueueReflood(node);
        }
    }

    /// <summary>
    /// The network a node is on, if it can connect at all.
    /// </summary>
    private object? NetworkOf(EntityUid uid, string nodeName)
    {
        if (!_nodes.TryGetNode(uid, nodeName, out Node? node))
            return null;

        if (node is DataHubNode { Enabled: false })
            return null;

        return node.NodeGroup;
    }

    /// <summary>
    /// Works out every machine's address, who it can reach, its neighbours and the hostnames it can look up,
    /// and tells each machine.
    /// </summary>
    private void RebuildNetwork()
    {
        _networkDirty = false;
        _addresses.Clear();

        // Each network's router: the first one on it, if there are several.
        var routers = new List<Entity<NetworkHubComponent>>();
        var hubs = EntityQueryEnumerator<NetworkHubComponent>();
        while (hubs.MoveNext(out var uid, out var hub))
        {
            if (hub.Router)
                routers.Add((uid, hub));
        }

        routers.Sort((a, b) => a.Owner.CompareTo(b.Owner));
        var served = new Dictionary<object, Entity<NetworkHubComponent>>();
        foreach (var router in routers)
        {
            if (NetworkOf(router, router.Comp.Node) is not { } network
                || router.Comp.Subnet <= 0
                || !served.TryAdd(network, router))
            {
                router.Comp.Leases.Clear();
            }
        }

        // The machines on each served network, and those on none.
        var members = new Dictionary<object, List<Entity<WasmMachineComponent>>>();
        var unconnected = new List<Entity<WasmMachineComponent>>();
        var machines = EntityQueryEnumerator<WasmMachineComponent>();
        while (machines.MoveNext(out var uid, out var machine))
        {
            if (machine.Vm == null)
                continue;

            if (NetworkOf(uid, machine.DataNode) is { } network && served.ContainsKey(network))
            {
                if (!members.TryGetValue(network, out var list))
                    members[network] = list = new List<Entity<WasmMachineComponent>>();

                list.Add((uid, machine));
            }
            else
            {
                unconnected.Add((uid, machine));
            }
        }

        // Addresses: a machine keeps the one it has while it stays, and a newcomer gets the lowest free one.
        var lans = new List<(MapId Map, List<(uint Address, Entity<WasmMachineComponent> Machine)> Hosts)>();
        foreach (var (network, router) in served)
        {
            var list = members.GetValueOrDefault(network) ?? new List<Entity<WasmMachineComponent>>();
            list.Sort((a, b) => a.Owner.CompareTo(b.Owner));

            var leases = router.Comp.Leases;
            var here = list.Select(m => m.Owner).ToHashSet();
            foreach (var gone in leases.Keys.Where(m => !here.Contains(m)).ToList())
            {
                leases.Remove(gone);
            }

            var used = leases.Values.ToHashSet();
            var map = Transform(router).MapID;
            var hosts = new List<(uint, Entity<WasmMachineComponent>)>();
            foreach (var machine in list)
            {
                if (!leases.TryGetValue(machine, out var host))
                {
                    host = 2;
                    while (host < 255 && used.Contains(host))
                    {
                        host++;
                    }

                    // The network is full.
                    if (host == 255)
                    {
                        unconnected.Add(machine);
                        continue;
                    }

                    leases[machine] = host;
                    used.Add(host);
                }

                var address = SubnetBase(router.Comp.Subnet) | host;
                _addresses[address] = (machine, map);
                hosts.Add((address, machine));
            }

            lans.Add((map, hosts));
        }

        // Every routed network on a map reaches every other, and its routers share one directory of hostnames.
        var reachable = new Dictionary<MapId, HashSet<uint>>();
        var directories = new Dictionary<MapId, Dictionary<string, uint>>();
        foreach (var (address, (machine, map)) in _addresses.OrderBy(a => a.Key))
        {
            if (!reachable.TryGetValue(map, out var set))
            {
                reachable[map] = set = new HashSet<uint>();
                directories[map] = new Dictionary<string, uint>();
            }

            set.Add(address);

            // Two machines with one name: the lower address has it.
            var name = Comp<WasmMachineComponent>(machine).Hostname;
            if (name.Length > 0)
                directories[map].TryAdd(name, address);
        }

        foreach (var (map, hosts) in lans)
        {
            foreach (var (address, machine) in hosts)
            {
                var neighbours = hosts.Select(h => h.Address).Where(a => a != address).OrderBy(a => a).ToList();
                machine.Comp.Vm!.SetNetwork(address, reachable[map], neighbours, directories[map]);
            }
        }

        foreach (var machine in unconnected)
        {
            machine.Comp.Vm!.SetNetwork(null, null, Array.Empty<uint>(), NoHosts);
        }
    }

    /// <summary>
    /// The address a machine has, if it's on a working network.
    /// </summary>
    public uint? AddressOf(EntityUid machine)
    {
        foreach (var (address, (uid, _)) in _addresses)
        {
            if (uid == machine)
                return address;
        }

        return null;
    }

    /// <summary>
    /// Delivers the packets sent this tick, to be read next tick: each goes to the machine with its address, if
    /// that machine is on the sender's backbone.
    /// </summary>
    private void DeliverPackets()
    {
        foreach (var packet in _sent)
        {
            if (!_addresses.TryGetValue(packet.From, out var from)
                || !_addresses.TryGetValue(packet.To, out var to)
                || from.Map != to.Map
                || !TryComp<WasmMachineComponent>(to.Machine, out var machine))
            {
                continue;
            }

            machine.Vm?.Deliver(packet);
        }

        _sent.Clear();
    }
}
