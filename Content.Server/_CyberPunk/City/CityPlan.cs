namespace Content.Server._CyberPunk.City;

/// <summary>
/// What a district of the city is.
/// </summary>
public enum CityZone : byte
{
    Ocean,
    /// <summary>Where land meets the sea: beach, shallows and dunes.</summary>
    Coast,
    /// <summary>The port: warehouses on the waterfront.</summary>
    Docks,
    /// <summary>Corporate towers in the middle of the city.</summary>
    Downtown,
    Commercial,
    Residential,
    Industrial,
    Slums,
    Park,
    Badlands,
    /// <summary>Dry dirt and brush between the city and the badlands.</summary>
    Scrub,
    /// <summary>A solar farm out past the city.</summary>
    Solar,
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
    Girder,
    Window,
    WindowReinforced,
    WindowTinted,
    Door,
    Tree,
    Rock,
    SolarPanel,
    Lamp,
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

    /// <summary>Each district's zone, row by row from the bottom.</summary>
    public readonly CityZone[] Zones;

    /// <summary>A street tile in the middle of the city where people arrive.</summary>
    public (int X, int Y) Spawn;

    public CityPlan(int districts)
    {
        Districts = districts;
        Size = districts * CityGenerator.Pitch + CityGenerator.Avenue;
        Floors = new CityFloor[Size * Size];
        Structures = new CityStructure[Size * Size];
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

    public CityZone Zone(int dx, int dy)
    {
        return Zones[dy * Districts + dx];
    }
}
