using System.Linq;
using System.Numerics;
using Content.Server._CyberPunk.Machines;
using Content.Server._CyberPunk.Wasm;
using Content.Shared.GameTicking;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._CyberPunk.Cyberspace;

/// <summary>
/// Cyberspace, after Switchboard's <c>sb_sim/src/cyberspace.rs</c>: one map, made when the first network comes
/// up, holding a region for each routed network with a pad for every machine on it and a corridor for every
/// link, the bus between the regions and the hub with the backbone in the middle. A region is generated again
/// whenever its network changes: pull out a machine and its pad and corridors are gone.
/// </summary>
/// <remarks>
/// <para>
/// Each network keeps the region it first took, by its router, while the router exists. Machines keep their
/// slots while unplugged, so plugging one back in puts it where it was.
/// </para>
/// <para>
/// Tiles don't stop anyone walking, so the tiles that can't be walked on next to ones that can are walled off
/// by invisible barriers: one entity per <see cref="Chunk"/>-tile chunk, with a box for each run of such tiles.
/// </para>
/// </remarks>
public sealed partial class CyberspaceSystem : EntitySystem
{
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Regions kept spare for networks built during the round.</summary>
    public const int SpareRegions = 4;

    /// <summary>The fewest regions cyberspace has room for.</summary>
    public const int MinRegions = 8;

    /// <summary>The fewest hosts a region has room for.</summary>
    public const int MinHosts = 12;

    /// <summary>Practice regions, sealed off from everything.</summary>
    public const int Sandboxes = 8;

    /// <summary>Width of a chunk of barriers, in tiles.</summary>
    public const int Chunk = 16;

    private const int BarrierLayer = (int) (CollisionGroup.Impassable | CollisionGroup.MidImpassable
                                            | CollisionGroup.HighImpassable | CollisionGroup.LowImpassable
                                            | CollisionGroup.BulletImpassable | CollisionGroup.InteractImpassable);

    private static readonly EntProtoId Barrier = "CyberspaceBarrier";

    private static readonly Dictionary<CyberFloor, string> TileIds = new()
    {
        [CyberFloor.Static] = "CyberStatic",
        [CyberFloor.Data] = "CyberData",
        [CyberFloor.Node] = "CyberNode",
        [CyberFloor.Bus] = "CyberBus",
    };

    private static readonly Dictionary<CyberNodeKind, EntProtoId> NodePrototypes = new()
    {
        [CyberNodeKind.Backbone] = "CyberNodeBackbone",
        [CyberNodeKind.Router] = "CyberNodeRouter",
        [CyberNodeKind.Switch] = "CyberNodeSwitch",
        [CyberNodeKind.Computer] = "CyberNodeComputer",
        [CyberNodeKind.DoorController] = "CyberNodeDoorController",
        [CyberNodeKind.Camera] = "CyberNodeCamera",
        [CyberNodeKind.Device] = "CyberNodeDevice",
        [CyberNodeKind.AccessPoint] = "CyberNodeAccessPoint",
        [CyberNodeKind.Deck] = "CyberNodeDeck",
    };

    private sealed class Region
    {
        /// <summary>The router of the network it belongs to.</summary>
        public EntityUid? Router;

        /// <summary>Each machine's slot, kept while it's away.</summary>
        public readonly Dictionary<EntityUid, (int X, int Y)> Slots = new();

        public RegionGraph? Graph;

        /// <summary>The machine on each pad, in the order of the graph's pads.</summary>
        public List<EntityUid> Pads = new();

        /// <summary>Each machine's node.</summary>
        public Dictionary<EntityUid, EntityUid> Nodes = new();
    }

    private EntityUid? _mapUid;
    private CyberLayout? _layout;
    private CyberFloor[] _tiles = Array.Empty<CyberFloor>();
    private ulong _seed;
    private Region[] _regions = Array.Empty<Region>();
    private readonly Dictionary<(int, int), EntityUid> _barriers = new();
    private readonly HashSet<(int, int)> _dirtyChunks = new();

    /// <summary>The map holding cyberspace, once it's made.</summary>
    public EntityUid? MapUid => _mapUid;

    /// <summary>Its layout, once it's made.</summary>
    public CyberLayout? Layout => _layout;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MachineNetworksRebuiltEvent>(OnNetworksRebuilt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        InitializeRunners();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        if (_mapUid is { } map && !TerminatingOrDeleted(map))
            QueueDel(map);

        Reset();
    }

    private void Reset()
    {
        _mapUid = null;
        _layout = null;
        _tiles = Array.Empty<CyberFloor>();
        _regions = Array.Empty<Region>();
        _barriers.Clear();
        _dirtyChunks.Clear();
        _spurs.Clear();
        Array.Clear(_sandboxes);
    }

    /// <summary>
    /// The tile of cyberspace at a position, void outside it.
    /// </summary>
    public CyberFloor FloorAt(int x, int y)
    {
        if (_layout == null)
            return CyberFloor.Void;

        var (w, h) = _layout.Size;
        return x < 0 || y < 0 || x >= w || y >= h ? CyberFloor.Void : _tiles[y * w + x];
    }

    /// <summary>
    /// The region a network has, by its router, if it has one.
    /// </summary>
    public int? RegionOf(EntityUid router)
    {
        for (var r = 0; r < _regions.Length; r++)
        {
            if (_regions[r].Router == router)
                return r;
        }

        return null;
    }

    /// <summary>
    /// The node of a machine in cyberspace, if it has a pad.
    /// </summary>
    public EntityUid? NodeOf(EntityUid machine)
    {
        foreach (var region in _regions)
        {
            if (region.Nodes.TryGetValue(machine, out var node))
                return node;
        }

        return null;
    }

    private void OnNetworksRebuilt(ref MachineNetworksRebuiltEvent ev)
    {
        if (_mapUid is { } existing && TerminatingOrDeleted(existing))
            Reset();

        if (_mapUid == null)
        {
            if (ev.Networks.Count == 0)
                return;

            Create(ev.Networks);
        }

        Rebuild(ev.Networks);
        NameDecks();
    }

    /// <summary>
    /// Makes cyberspace if there's none yet, as for a practice grid before any network is up.
    /// </summary>
    private void EnsureCreated()
    {
        if (_mapUid is { } existing && TerminatingOrDeleted(existing))
            Reset();

        if (_mapUid == null)
            Create(new List<MachineNetwork>());
    }

    /// <summary>
    /// Makes the cyberspace map, with room for the networks there are now and a few more.
    /// </summary>
    private void Create(List<MachineNetwork> networks)
    {
        var regions = Math.Max(networks.Count + SpareRegions, MinRegions);
        var hosts = Math.Max(networks.Select(n => n.Hosts.Count).DefaultIfEmpty(0).Max() + 3, MinHosts);
        var layout = new CyberLayout(regions, hosts, Sandboxes);
        _layout = layout;
        _seed = (ulong) (uint) _random.Next() << 32 | (uint) _random.Next();
        _regions = Enumerable.Range(0, layout.AllRegions).Select(_ => new Region()).ToArray();

        var mapUid = _map.CreateMap(out _);
        _mapUid = mapUid;
        _meta.SetEntityName(mapUid, "cyberspace");
        EnsureComp<CyberspaceMapComponent>(mapUid);
        EnsureComp<MapGridComponent>(mapUid);

        var gravity = EnsureComp<GravityComponent>(mapUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(mapUid, gravity);

        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = Color.FromHex("#C8D2FF");
        Dirty(mapUid, light);

        var (w, h) = layout.Size;
        _tiles = new CyberFloor[w * h];
        Paint(new CyberRect(0, 0, w, h), layout.Base());

        var hub = layout.HubRect;
        var backbone = Spawn(NodePrototypes[CyberNodeKind.Backbone],
            new EntityCoordinates(mapUid, hub.X + hub.W / 2 + 0.5f, hub.Y + hub.H / 2 + 0.5f));
        var node = EnsureComp<CyberNodeComponent>(backbone);
        node.Kind = CyberNodeKind.Backbone;
        _meta.SetEntityName(backbone, "city backbone");
    }

    /// <summary>
    /// Generates again every region whose network changed.
    /// </summary>
    private void Rebuild(List<MachineNetwork> networks)
    {
        if (_layout is not { } layout)
            return;

        // A network whose router is gone gives its region back.
        foreach (var region in _regions)
        {
            if (region.Router is { } router && TerminatingOrDeleted(router))
            {
                region.Router = null;
                region.Slots.Clear();
            }
        }

        // A network new to cyberspace takes the first free region.
        var byRegion = new MachineNetwork?[layout.Regions];
        foreach (var network in networks)
        {
            var r = RegionOf(network.Router);
            if (r == null)
            {
                r = Array.FindIndex(_regions, 0, layout.Regions, region => region.Router == null);
                if (r < 0)
                    continue;

                _regions[r.Value].Router = network.Router;
            }

            byRegion[r.Value] = network;
        }

        for (var r = 0; r < layout.Regions; r++)
        {
            RebuildRegion(layout, r, byRegion[r]);
        }

        FlushBarriers();
    }

    private void RebuildRegion(CyberLayout layout, int r, MachineNetwork? network)
    {
        var region = _regions[r];
        var pads = new List<(EntityUid Machine, (int X, int Y) Slot, PadKind Kind)>();
        var links = network?.Links ?? new List<(EntityUid A, EntityUid B)>();
        if (network != null)
        {
            var switches = network.Hubs.Where(h => h != network.Router).ToList();
            var hosts = network.Hosts.Select(h => h.Machine).ToList();
            var present = new HashSet<EntityUid>(switches.Concat(hosts)) { network.Router };
            var kept = region.Slots.Where(s => !present.Contains(s.Key)).Select(s => s.Value).ToHashSet();
            var held = new HashSet<(int, int)>();

            // Pads on slots: the router at the top, switches, then hosts near their switch. Machines keep
            // their slots.
            held.Add(layout.RouterSlot);
            pads.Add((network.Router, layout.RouterSlot, PadKind.Router));

            var switchSlots = layout.SwitchSlots;
            foreach (var hub in switches)
            {
                var slot = KeptSlot(region, hub, switchSlots, held) ?? FreeSlot(switchSlots, held, kept, null);
                if (slot is not { } s)
                    continue;

                held.Add(s);
                pads.Add((hub, s, PadKind.Switch));
            }

            var hostSlots = layout.HostSlots;
            foreach (var host in hosts)
            {
                var hardware = FirstHardware(host, links, network.Hubs);
                (int, int)? near = null;
                foreach (var pad in pads)
                {
                    if (pad.Machine == hardware)
                        near = pad.Slot;
                }

                var slot = KeptSlot(region, host, hostSlots, held) ?? FreeSlot(hostSlots, held, kept, near);
                if (slot is not { } s)
                    continue;

                held.Add(s);
                pads.Add((host, s, PadKind.Host));
            }

            foreach (var (machine, slot, _) in pads)
            {
                region.Slots[machine] = slot;
            }
        }

        var index = new Dictionary<EntityUid, int>();
        for (var i = 0; i < pads.Count; i++)
        {
            index[pads[i].Machine] = i;
        }

        var padLinks = new SortedSet<(int, int)>();
        foreach (var (a, b) in links)
        {
            if (index.TryGetValue(a, out var ia) && index.TryGetValue(b, out var ib))
                padLinks.Add((Math.Min(ia, ib), Math.Max(ia, ib)));
        }

        var graph = new RegionGraph
        {
            Pads = pads.Select(p => (p.Slot, p.Kind)).ToList(),
            Links = padLinks.ToList(),
            Gate = pads.FindIndex(p => p.Kind == PadKind.Router) is var gate and >= 0 ? gate : null,
        };

        var machines = pads.Select(p => p.Machine).ToList();
        if (region.Graph is not { } old || !old.Same(graph) || !region.Pads.SequenceEqual(machines))
        {
            region.Graph = graph;
            region.Pads = machines;
            var seed = new CyberRng(_seed ^ unchecked((ulong) r * 0x9E3779B97F4A7C15)).NextU64();
            Paint(layout.RegionRect(r), CyberRegionGenerator.Generate(seed, layout, graph));
            SyncNodes(layout, r, network, pads);
            RestampSpurs(r);
            return;
        }

        SyncNodes(layout, r, network, pads);
    }

    /// <summary>
    /// The slot a machine had, if it's still one of these and nothing here holds it.
    /// </summary>
    private static (int, int)? KeptSlot(Region region, EntityUid machine, List<(int X, int Y)> options,
        HashSet<(int, int)> held)
    {
        return region.Slots.TryGetValue(machine, out var slot) && options.Contains(slot) && !held.Contains(slot)
            ? slot
            : null;
    }

    /// <summary>
    /// A free slot nearest <paramref name="near"/>: not held by anything here, nor kept for anything away
    /// unless every slot is.
    /// </summary>
    private static (int, int)? FreeSlot(List<(int X, int Y)> options, HashSet<(int, int)> held,
        HashSet<(int, int)> kept, (int X, int Y)? near)
    {
        int Distance((int X, int Y) s) => near is { } n ? Math.Abs(n.X - s.X) + Math.Abs(n.Y - s.Y) : 0;

        (int, int)? Pick(bool skipKept)
        {
            (int, int)? best = null;
            var bestDistance = int.MaxValue;
            foreach (var s in options)
            {
                if (held.Contains(s) || skipKept && kept.Contains(s))
                    continue;

                var d = Distance(s);
                if (d < bestDistance)
                {
                    best = s;
                    bestDistance = d;
                }
            }

            return best;
        }

        return Pick(true) ?? Pick(false);
    }

    /// <summary>
    /// The switch or router a host is plugged into first: the lowest one.
    /// </summary>
    private static EntityUid? FirstHardware(EntityUid host, List<(EntityUid A, EntityUid B)> links,
        List<EntityUid> hubs)
    {
        EntityUid? first = null;
        foreach (var (a, b) in links)
        {
            var other = a == host ? b : b == host ? a : EntityUid.Invalid;
            if (!hubs.Contains(other))
                continue;

            if (first == null || other.CompareTo(first.Value) < 0)
                first = other;
        }

        return first;
    }

    /// <summary>
    /// Puts a node on every pad of a region, and takes away those of machines no longer there.
    /// </summary>
    private void SyncNodes(CyberLayout layout, int r, MachineNetwork? network,
        List<(EntityUid Machine, (int X, int Y) Slot, PadKind Kind)> pads)
    {
        var region = _regions[r];
        var addresses = network?.Hosts.ToDictionary(h => h.Machine, h => h.Address)
                        ?? new Dictionary<EntityUid, uint>();

        foreach (var (machine, node) in region.Nodes)
        {
            if (!pads.Any(p => p.Machine == machine))
                QueueDel(node);
        }

        // Routers and switches go by the lowest address on their network: "router 10.4".
        var lowest = addresses.Count > 0 ? addresses.Values.Min() : (uint?) null;
        var nodes = new Dictionary<EntityUid, EntityUid>();
        foreach (var (machine, slot, pad) in pads)
        {
            var kind = pad switch
            {
                PadKind.Router => CyberNodeKind.Router,
                PadKind.Switch => CyberNodeKind.Switch,
                _ => HostKind(machine),
            };

            var label = (kind, addresses.TryGetValue(machine, out var address), lowest) switch
            {
                (CyberNodeKind.Router or CyberNodeKind.Switch, _, { } low) => $"{KindName(kind)} 10.{(low >> 16) & 255}",
                (_, true, _) => $"{KindName(kind)} {MachineIo.FormatAddress(address)}",
                _ => KindName(kind),
            };

            var (x, y) = layout.SlotCentre(r, slot);
            var at = new EntityCoordinates(_mapUid!.Value, x + 0.5f, y + 0.5f);

            // A node that changes kind is made again; one that stays is moved and renamed.
            if (region.Nodes.TryGetValue(machine, out var node)
                && !TerminatingOrDeleted(node)
                && TryComp<CyberNodeComponent>(node, out var comp)
                && comp.Kind == kind)
            {
                if (Transform(node).Coordinates != at)
                    _transform.SetCoordinates(node, at);
            }
            else
            {
                if (region.Nodes.TryGetValue(machine, out var stale))
                    QueueDel(stale);

                node = Spawn(NodePrototypes[kind], at);
                comp = EnsureComp<CyberNodeComponent>(node);
                comp.Kind = kind;
                comp.Machine = machine;
                comp.Region = r;
            }

            if (MetaData(node).EntityName != label)
                _meta.SetEntityName(node, label);

            nodes[machine] = node;
        }

        region.Nodes = nodes;
    }

    private CyberNodeKind HostKind(EntityUid machine)
    {
        if (HasComp<AccessPointComponent>(machine))
            return CyberNodeKind.AccessPoint;

        if (!TryComp<WasmMachineComponent>(machine, out var comp))
            return CyberNodeKind.Device;

        return comp.Kind switch
        {
            DeviceKind.DoorController => CyberNodeKind.DoorController,
            DeviceKind.Camera => CyberNodeKind.Camera,
            _ => CyberNodeKind.Computer,
        };
    }

    private static string KindName(CyberNodeKind kind)
    {
        return kind switch
        {
            CyberNodeKind.Backbone => "the city backbone",
            CyberNodeKind.Router => "router",
            CyberNodeKind.Switch => "switch",
            CyberNodeKind.Computer => "computer",
            CyberNodeKind.DoorController => "door controller",
            CyberNodeKind.Camera => "camera",
            CyberNodeKind.AccessPoint => "access point",
            CyberNodeKind.Deck => "deck",
            _ => "device",
        };
    }

    /// <summary>
    /// Rewrites a rectangle of cyberspace's tiles, on the map too, and has the barriers around it laid again.
    /// </summary>
    private void Paint(CyberRect rect, CyberFloor[] tiles)
    {
        if (_layout is not { } layout || _mapUid is not { } mapUid || !TryComp<MapGridComponent>(mapUid, out var grid))
            return;

        var w = layout.Size.W;
        // Tiles cleared go in before tiles laid: the explosion system's edge map counts a batch that does both at
        // once twice over.
        var cleared = new List<(Vector2i, Tile)>();
        var laid = new List<(Vector2i, Tile)>();
        for (var y = 0; y < rect.H; y++)
        {
            for (var x = 0; x < rect.W; x++)
            {
                var floor = tiles[y * rect.W + x];
                var i = (rect.Y + y) * w + rect.X + x;
                if (_tiles[i] == floor)
                    continue;

                _tiles[i] = floor;

                var tile = TileIds.TryGetValue(floor, out var id) ? new Tile(_tileDefs[id].TileId) : Tile.Empty;
                (tile.IsEmpty ? cleared : laid).Add((new Vector2i(rect.X + x, rect.Y + y), tile));
            }
        }

        if (cleared.Count > 0)
            _map.SetTiles(mapUid, grid, cleared);
        if (laid.Count > 0)
            _map.SetTiles(mapUid, grid, laid);

        // The barriers on the tiles just round it may change too.
        for (var cy = Math.Max(rect.Y - 1, 0) / Chunk; cy <= (rect.Y + rect.H) / Chunk; cy++)
        {
            for (var cx = Math.Max(rect.X - 1, 0) / Chunk; cx <= (rect.X + rect.W) / Chunk; cx++)
            {
                _dirtyChunks.Add((cx, cy));
            }
        }
    }

    /// <summary>
    /// Lays the barriers again in every chunk whose tiles changed: one entity per chunk, with a box over each
    /// run, along a row, of tiles that can't be walked on beside tiles that can.
    /// </summary>
    private void FlushBarriers()
    {
        if (_layout is not { } layout || _mapUid is not { } mapUid)
            return;

        var (w, h) = layout.Size;
        foreach (var chunk in _dirtyChunks)
        {
            if (_barriers.Remove(chunk, out var old))
                QueueDel(old);

            var (cx, cy) = chunk;
            var origin = new Vector2(cx * Chunk + Chunk / 2f, cy * Chunk + Chunk / 2f);
            var runs = new List<(int Y, int From, int To)>();
            for (var y = cy * Chunk; y < Math.Min((cy + 1) * Chunk, h); y++)
            {
                int? start = null;
                for (var x = cx * Chunk; x <= Math.Min((cx + 1) * Chunk, w); x++)
                {
                    var blocks = x < Math.Min((cx + 1) * Chunk, w) && NeedsBarrier(x, y);
                    if (blocks && start == null)
                        start = x;
                    else if (!blocks && start is { } from)
                    {
                        runs.Add((y, from, x));
                        start = null;
                    }
                }
            }

            if (runs.Count == 0)
                continue;

            var barrier = Spawn(Barrier, new EntityCoordinates(mapUid, origin));
            for (var i = 0; i < runs.Count; i++)
            {
                var (y, from, to) = runs[i];
                var shape = new PolygonShape();
                var centre = new Vector2((from + to) / 2f, y + 0.5f) - origin;
                shape.SetAsBox((to - from) / 2f, 0.5f, centre, 0f);
                _fixtures.TryCreateFixture(barrier, shape, $"barrier{i}", hard: true, collisionLayer: BarrierLayer,
                    updates: false);
            }

            _fixtures.FixtureUpdate(barrier);
            // It's spawned without fixtures, so it can't collide until it has them.
            _physics.SetCanCollide(barrier, true);
            _barriers[chunk] = barrier;
        }

        _dirtyChunks.Clear();
    }

    /// <summary>
    /// Whether a tile needs a barrier: it can't be walked on, and one of the eight round it can.
    /// </summary>
    private bool NeedsBarrier(int x, int y)
    {
        if (CyberLayout.Walkable(FloorAt(x, y)))
            return false;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && CyberLayout.Walkable(FloorAt(x + dx, y + dy)))
                    return true;
            }
        }

        return false;
    }
}
