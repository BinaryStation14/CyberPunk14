using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.GameTicking;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._CyberPunk.City;

/// <summary>
/// The generated city: one map, made from a <see cref="CityPlan"/> the first time a player steps through a
/// <see cref="CityPortalComponent"/>, with a gateway back by where they arrive. The sea is walled off by
/// invisible barriers. The tiles are laid at once; the walls, rocks and everything else go up over the following
/// ticks, nearest the arrival point first, so raising the city doesn't stall the server.
/// </summary>
public sealed partial class CitySystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private LinkedEntitySystem _link = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private static readonly EntProtoId ReturnPortal = "CityReturnPortal";
    private static readonly EntProtoId Barrier = "CityBarrier";
    private static readonly EntProtoId FenceStraight = "FenceMetalStraight";
    private static readonly EntProtoId FenceCorner = "FenceMetalCorner";

    /// <summary>How many of the city's entities are spawned each tick until it's built.</summary>
    private const int SpawnsPerTick = 2000;

    private static readonly Dictionary<CityFloor, string> TileIds = new()
    {
        [CityFloor.Ocean] = "CityOcean",
        [CityFloor.Sand] = "FloorDesert",
        [CityFloor.Desert] = "FloorDesertAstroSand",
        [CityFloor.LowDesert] = "FloorAsteroidSand",
        [CityFloor.Dirt] = "FloorDirt",
        [CityFloor.Grass] = "FloorGrass",
        [CityFloor.WildGrass] = "FloorPlanetGrass",
        [CityFloor.Asphalt] = "FloorDarkMono",
        [CityFloor.Sidewalk] = "FloorConcrete",
        [CityFloor.OldConcrete] = "FloorConcreteSmooth",
        [CityFloor.Concrete] = "FloorConcreteMono",
        [CityFloor.Steel] = "FloorSteel",
        [CityFloor.SteelDirty] = "FloorTechMaintDark",
        [CityFloor.Dark] = "FloorDark",
        [CityFloor.White] = "FloorWhite",
        [CityFloor.Wood] = "FloorWood",
        [CityFloor.Marble] = "FloorWhiteMarble",
        [CityFloor.Plating] = "Plating",
    };

    private static readonly Dictionary<CityStructure, EntProtoId> StructureIds = new()
    {
        [CityStructure.Wall] = "WallSolid",
        [CityStructure.WallReinforced] = "WallReinforced",
        [CityStructure.WallRust] = "WallSolidRust",
        [CityStructure.WallBrick] = "WallBrick",
        [CityStructure.WallConcrete] = "WallConcrete",
        [CityStructure.WallWood] = "WallWood",
        [CityStructure.Girder] = "Girder",
        [CityStructure.Window] = "Window",
        [CityStructure.WindowReinforced] = "ReinforcedWindow",
        [CityStructure.WindowTinted] = "TintedWindow",
        [CityStructure.Door] = "WoodDoor",
        [CityStructure.Tree] = "FloraTree",
        [CityStructure.Rock] = "FloraRockSolid",
        [CityStructure.SolarPanel] = "SolarPanel",
        [CityStructure.Lamp] = "LightPostSmall",
        [CityStructure.Mountain] = "WallRockSand",
        [CityStructure.BoundaryWall] = "WallPlastitaniumIndestructible",
    };

    private EntityUid? _city;
    private EntityUid? _returnPortal;

    /// <summary>What's left to spawn in the city, in order.</summary>
    private readonly Queue<(EntProtoId Proto, Vector2i Tile, Angle Rotation)> _pending = new();

    /// <summary>Whether the city still has entities waiting to be spawned.</summary>
    public bool Building => _pending.Count > 0;

    /// <summary>The map holding the city, once it's made.</summary>
    public EntityUid? City => _city;

    public override void Initialize()
    {
        base.Initialize();
        // Before the portal system, so the first trip through finds the city there to go to.
        SubscribeLocalEvent<CityPortalComponent, StartCollideEvent>(OnPortalCollide,
            before: new[] { typeof(SharedPortalSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _city = null;
        _returnPortal = null;
        _pending.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_pending.Count == 0)
            return;

        if (_city is not { } city || TerminatingOrDeleted(city))
        {
            _pending.Clear();
            return;
        }

        for (var i = 0; i < SpawnsPerTick && _pending.TryDequeue(out var next); i++)
        {
            var coords = new EntityCoordinates(city, next.Tile.X + 0.5f, next.Tile.Y + 0.5f);
            SpawnAttachedTo(next.Proto, coords, rotation: next.Rotation);
        }

        if (_pending.Count == 0)
            Log.Info("Finished building the city.");
    }

    private void OnPortalCollide(Entity<CityPortalComponent> ent, ref StartCollideEvent args)
    {
        // Only a player raises the city, so things knocked into the portal don't.
        if (!HasComp<ActorComponent>(args.OtherEntity))
            return;

        if (TryComp<LinkedEntityComponent>(ent, out var link) && link.LinkedEntities.Count > 0)
            return;

        if (_city is not { } city || TerminatingOrDeleted(city))
            Generate();
        else if (_returnPortal is { } portal)
            _link.TryLink(ent, portal);
    }

    /// <summary>
    /// Throws away the city, if there is one, and makes a new one from the seed, or a random one. Every city portal
    /// leads to it.
    /// </summary>
    /// <returns>The seed it was made from.</returns>
    public ulong Generate(ulong? seed = null)
    {
        seed ??= (ulong) (uint) _random.Next() << 32 | (uint) _random.Next();
        if (_city is { } old && !TerminatingOrDeleted(old))
            Del(old);

        var plan = CityGenerator.Generate(seed.Value);
        var tileIds = new Dictionary<CityFloor, int>();
        foreach (var (floor, id) in TileIds)
        {
            tileIds[floor] = _tileDefs[id].TileId;
        }

        var mapUid = _map.CreateMap(out _);
        _city = mapUid;
        _meta.SetEntityName(mapUid, "city");
        var grid = EnsureComp<MapGridComponent>(mapUid);

        var gravity = EnsureComp<GravityComponent>(mapUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(mapUid, gravity);

        // Dusk, under a violet sky.
        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = Color.FromHex("#B8A8E0");
        Dirty(mapUid, light);

        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = Atmospherics.OxygenMolesStandard;
        moles[(int) Gas.Nitrogen] = Atmospherics.NitrogenMolesStandard;
        _atmos.SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));

        var tiles = new List<(Vector2i, Tile)>(plan.Size * plan.Size);

        for (var y = 0; y < plan.Size; y++)
        {
            for (var x = 0; x < plan.Size; x++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(tileIds[plan.Floor(x, y)])));
            }
        }

        _map.SetTiles(mapUid, grid, tiles);

        var spawns = new List<(EntProtoId Proto, Vector2i Tile, Angle Rotation)>();
        for (var y = 0; y < plan.Size; y++)
        {
            for (var x = 0; x < plan.Size; x++)
            {
                var tile = new Vector2i(x, y);
                if (StructureIds.TryGetValue(plan.Structure(x, y), out var structure))
                    spawns.Add((structure, tile, Angle.Zero));
                else if (plan.Structure(x, y) == CityStructure.Fence)
                {
                    var (fence, rotation) = Fence(plan, x, y);
                    spawns.Add((fence, tile, rotation));
                }

                if (Walled(plan, x, y))
                    spawns.Add((Barrier, tile, Angle.Zero));
            }
        }

        _pending.Clear();
        var arrival = new Vector2i(plan.Spawn.X, plan.Spawn.Y);
        foreach (var spawn in spawns.OrderBy(s => (s.Tile - arrival).LengthSquared))
        {
            _pending.Enqueue(spawn);
        }

        var portal = Spawn(ReturnPortal, new EntityCoordinates(mapUid, plan.Spawn.X + 0.5f, plan.Spawn.Y + 0.5f));
        _returnPortal = portal;
        var portals = EntityQueryEnumerator<CityPortalComponent>();
        while (portals.MoveNext(out var uid, out _))
        {
            _link.TryLink(uid, portal);
        }

        Log.Info($"Generated the city from seed {seed}: {plan.Size}x{plan.Size} tiles.");
        return seed.Value;
    }

    /// <summary>
    /// Which piece of fence goes on a tile, turned to join up with the fence either side of it. A straight piece
    /// runs north to south unturned, and a corner joins north and west.
    /// </summary>
    private static (EntProtoId, Angle) Fence(CityPlan plan, int x, int y)
    {
        bool Joins(int dx, int dy)
        {
            return plan.Contains(x + dx, y + dy) && plan.Structure(x + dx, y + dy) == CityStructure.Fence;
        }

        var (north, south, east, west) = (Joins(0, 1), Joins(0, -1), Joins(1, 0), Joins(-1, 0));
        if ((north ^ south) && (east ^ west))
        {
            var turns = (north, west) switch
            {
                (true, true) => 0,
                (false, true) => 1,
                (false, false) => 2,
                _ => 3,
            };
            return (FenceCorner, Angle.FromDegrees(90 * turns));
        }

        return (FenceStraight, (east || west) && !(north || south) ? Angle.FromDegrees(90) : Angle.Zero);
    }

    /// <summary>
    /// Whether a tile gets a barrier: sea next to anything that isn't, so nobody wades out to sea.
    /// </summary>
    private static bool Walled(CityPlan plan, int x, int y)
    {
        if (plan.Structure(x, y) != CityStructure.None)
            return false;

        return plan.Floor(x, y) == CityFloor.Ocean
               && (plan.Floor(x - 1, y) != CityFloor.Ocean || plan.Floor(x + 1, y) != CityFloor.Ocean
                   || plan.Floor(x, y - 1) != CityFloor.Ocean || plan.Floor(x, y + 1) != CityFloor.Ocean);
    }
}
