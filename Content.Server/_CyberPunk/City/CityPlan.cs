namespace Content.Server._CyberPunk.City;

/// <summary>
/// What a district of the city is.
/// </summary>
public enum CityZone : byte
{
    Ocean,
    /// <summary>Where land meets the sea: beach, shallows and dunes.</summary>
    Coast,
    /// <summary>Corporate towers in the middle of the city.</summary>
    Corporate,
    /// <summary>Shops, markets, bars and clubs.</summary>
    Commercial,
    /// <summary>City hall, the police, the hospital, the transit station and parks.</summary>
    Public,
    HighClass,
    MediumClass,
    LowClass,
    /// <summary>Factories and warehouses, and the port where they meet the sea.</summary>
    Industrial,
    /// <summary>Shacks and ruins at the city's fringe.</summary>
    Shanty,
    Badlands,
    /// <summary>Dry dirt and brush between the city and the badlands.</summary>
    Scrub,
    /// <summary>A sparse township along a road out in the badlands.</summary>
    Township,
    /// <summary>The city's solar power plant.</summary>
    Solar,
}

/// <summary>The buildings and places every city has.</summary>
public enum CityLandmark : byte
{
    Headquarters,
    CityHall,
    PoliceStation,
    Hospital,
    TransitStation,
    Megabuilding,
    SolarPlant,
    Township,
}

public enum CityFloor : byte
{
    Ocean,
    Sand,
    Desert,
    LowDesert,
    Dirt,
    Grass,
    WildGrass,
    Asphalt,
    Sidewalk,
    OldConcrete,
    Concrete,
    Steel,
    SteelDirty,
    Dark,
    White,
    Wood,
    Marble,
    Plating,
}

public enum CityStructure : byte
{
    None,
    Wall,
    WallReinforced,
    WallRust,
    WallBrick,
    WallConcrete,
    WallWood,
    Girder,
    Window,
    WindowReinforced,
    WindowTinted,
    Door,
    Tree,
    Rock,
    SolarPanel,
    Lamp,
    /// <summary>Solid rock: part of a mountain.</summary>
    Mountain,
    /// <summary>The wall round the edge of the map that nobody gets past.</summary>
    BoundaryWall,
    Fence,
    /// <summary>A battery bank storing high-voltage power.</summary>
    Smes,
    /// <summary>Steps high-voltage power down to medium voltage for a block.</summary>
    Substation,
}

/// <summary>The cables under a tile, by voltage.</summary>
[Flags]
public enum CityCable : byte
{
    None = 0,
    High = 1,
    Medium = 2,
    Low = 4,
}

/// <summary>Power fittings that face a way: a wall-mounted APC, or a cable terminal feeding an SMES.</summary>
public enum CityFixture : byte
{
    Apc,
    Terminal,
}

/// <summary>
/// A generated city, as tiles: x to the right and y up, with (0, 0) the bottom-left tile. Districts sit in a
/// square grid with an avenue between every two of them and round the outside (see <see cref="CityGenerator"/>).
/// </summary>
public sealed class CityPlan
{
    public readonly int Districts;
    public readonly int Size;
    public readonly CityFloor[] Floors;
    public readonly CityStructure[] Structures;
    public readonly CityCable[] Cables;

    /// <summary>Each district's zone, row by row from the bottom.</summary>
    public readonly CityZone[] Zones;

    /// <summary>A street tile in the middle of the city where people arrive.</summary>
    public (int X, int Y) Spawn;

    /// <summary>Where each landmark stands, as its bottom-left tile and size.</summary>
    public readonly List<(CityLandmark Kind, int X, int Y, int W, int H)> Landmarks = new();

    /// <summary>Power fittings, each with the direction it faces as an index into the WFC directions.</summary>
    public readonly List<(CityFixture Kind, int X, int Y, int Direction)> Fixtures = new();

    public CityPlan(int districts)
    {
        Districts = districts;
        Size = districts * CityGenerator.Pitch + CityGenerator.Avenue;
        Floors = new CityFloor[Size * Size];
        Structures = new CityStructure[Size * Size];
        Cables = new CityCable[Size * Size];
        Zones = new CityZone[districts * districts];
    }

    public bool Contains(int x, int y)
    {
        return x >= 0 && y >= 0 && x < Size && y < Size;
    }

    public CityFloor Floor(int x, int y)
    {
        return Floors[y * Size + x];
    }

    public CityStructure Structure(int x, int y)
    {
        return Structures[y * Size + x];
    }

    public void Set(int x, int y, CityFloor floor, CityStructure structure = CityStructure.None)
    {
        Floors[y * Size + x] = floor;
        Structures[y * Size + x] = structure;
    }

    public void SetStructure(int x, int y, CityStructure structure)
    {
        Structures[y * Size + x] = structure;
    }

    public CityCable Cable(int x, int y)
    {
        return Cables[y * Size + x];
    }

    public void AddCable(int x, int y, CityCable cable)
    {
        Cables[y * Size + x] |= cable;
    }

    public CityZone Zone(int dx, int dy)
    {
        return Zones[dy * Districts + dx];
    }
}
