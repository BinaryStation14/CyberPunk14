using System.Linq;
using Content.Server._CyberPunk.Procgen;

namespace Content.Server._CyberPunk.City;

public static partial class CityGenerator
{
    /// <summary>What a landmark building is made of.</summary>
    private readonly record struct Look(CityStructure Wall, CityStructure Window, CityFloor[] Floors, int MaxRoom, int WindowEvery = 3);

    private static readonly Look TowerLook = new(CityStructure.WallReinforced, CityStructure.WindowReinforced,
        new[] { CityFloor.Marble, CityFloor.Dark, CityFloor.White }, 9, 2);

    private static readonly Look MegabuildingLook = new(CityStructure.WallConcrete, CityStructure.Window,
        new[] { CityFloor.SteelDirty, CityFloor.OldConcrete }, 6, 4);

    private static readonly (CityLandmark Kind, Look Look)[] CivicBuildings =
    {
        (CityLandmark.CityHall, new Look(CityStructure.Wall, CityStructure.Window, new[] { CityFloor.Marble, CityFloor.Wood }, 8)),
        (CityLandmark.PoliceStation, new Look(CityStructure.WallReinforced, CityStructure.WindowReinforced, new[] { CityFloor.Dark, CityFloor.Steel }, 6)),
        (CityLandmark.Hospital, new Look(CityStructure.Wall, CityStructure.Window, new[] { CityFloor.White }, 6, 2)),
        (CityLandmark.TransitStation, new Look(CityStructure.WallConcrete, CityStructure.Window, new[] { CityFloor.Steel, CityFloor.Concrete }, 13, 2)),
    };

    private static readonly Look SubstationLook = new(CityStructure.WallConcrete, CityStructure.Window,
        new[] { CityFloor.Steel }, 9, 4);

    private static readonly Look[] ShackLooks =
    {
        new(CityStructure.WallWood, CityStructure.Window, new[] { CityFloor.Wood, CityFloor.OldConcrete }, 6, 4),
        new(CityStructure.WallRust, CityStructure.Window, new[] { CityFloor.Plating, CityFloor.SteelDirty }, 6, 5),
    };

    private static readonly Look MotelLook = new(CityStructure.WallBrick, CityStructure.Window,
        new[] { CityFloor.Wood, CityFloor.Dark }, 4);

    /// <summary>
    /// Gives each built-up zone at least its share of districts, and no more than its most. Districts are taken
    /// from zones with some to spare, or from open land beside the city, preferring those whose ring and side of
    /// the city suit the zone and whose neighbours it may sit beside.
    /// </summary>
    private static void MeetQuotas(CityPlan plan, Shape shape)
    {
        var n = plan.Districts;
        var counts = new int[(int) CityZone.Solar + 1];
        foreach (var zone in plan.Zones)
        {
            counts[(int) zone]++;
        }

        IEnumerable<CityZone> Around(int x, int y)
        {
            foreach (var (dx, dy) in WfcWave.Directions)
            {
                if (x + dx >= 0 && y + dy >= 0 && x + dx < n && y + dy < n)
                    yield return plan.Zone(x + dx, y + dy);
            }
        }

        // How well a zone would sit in a district: rings and sides that suit it first, then neighbours it may touch.
        int Score(int x, int y, CityZone zone)
        {
            var fit = Around(x, y).Sum(z => CanTouch(zone, z) ? 1 : -4);
            return (shape.Allowed(x, y).Contains(zone) ? 100 : 0) + fit * 10 - (int) (shape.Distance(x, y) * 2);
        }

        void Convert(CityZone zone, Func<CityZone, bool> from)
        {
            var best = (X: -1, Y: -1);
            var bestScore = int.MinValue;
            for (var y = 1; y < n - 1; y++)
            {
                for (var x = 1; x < n - 1; x++)
                {
                    var current = plan.Zone(x, y);
                    if ((x, y) == shape.Centre || current == zone || !from(current))
                        continue;

                    // Open land only joins the city where it borders it, so the streets still reach it.
                    if (!IsBuiltUp(current) && !Around(x, y).Any(IsBuiltUp))
                        continue;

                    var score = Score(x, y, zone);
                    if (score <= bestScore)
                        continue;

                    best = (x, y);
                    bestScore = score;
                }
            }

            if (best.X < 0)
                return;

            counts[(int) plan.Zone(best.X, best.Y)]--;
            counts[(int) zone]++;
            plan.Zones[best.Y * n + best.X] = zone;
        }

        bool Spare(CityZone zone)
        {
            return zone is CityZone.Badlands or CityZone.Scrub or CityZone.Coast
                   || Quotas.TryGetValue(zone, out var quota) && counts[(int) zone] > quota.Min;
        }

        foreach (var (zone, (min, _)) in Quotas)
        {
            for (var tries = 0; tries < min && counts[(int) zone] < min; tries++)
            {
                Convert(zone, Spare);
            }
        }

        foreach (var (zone, (_, max)) in Quotas)
        {
            for (var tries = 0; tries < n * n && counts[(int) zone] > max; tries++)
            {
                // The surplus district that suits some other zone best becomes that zone. With every zone full,
                // the city is too big, and its outermost surplus district is left as scrub.
                var target = Quotas.Where(q => q.Key != zone && counts[(int) q.Key] < q.Value.Max).Select(q => q.Key).ToList();
                if (target.Count == 0)
                {
                    var outer = Enumerable.Range(0, n * n)
                        .Where(i => plan.Zones[i] == zone && i != shape.Centre.Y * n + shape.Centre.X)
                        .MaxBy(i => shape.Distance(i % n, i / n));
                    plan.Zones[outer] = CityZone.Scrub;
                    counts[(int) zone]--;
                    counts[(int) CityZone.Scrub]++;
                    continue;
                }

                var before = counts[(int) zone];
                var best = target.MaxBy(t => Enumerable.Range(0, n * n)
                    .Where(i => plan.Zones[i] == zone && i != shape.Centre.Y * n + shape.Centre.X)
                    .Select(i => Score(i % n, i / n, t))
                    .DefaultIfEmpty(int.MinValue)
                    .Max());
                Convert(best, z => z == zone);
                if (counts[(int) zone] == before)
                    break;
            }
        }
    }

    /// <summary>
    /// Picks where the city's one solar plant goes: open land inside the border, beside a highway if it can be,
    /// as a square of four districts or as big as fits. Failing that, the outermost district of the city gives way.
    /// </summary>
    /// <returns>The plant's districts: the bottom-left one and its size.</returns>
    private static (int X, int Y, int W, int H) PlaceSolarPlant(CityPlan plan, Shape shape)
    {
        var n = plan.Districts;
        var (cx, cy) = shape.Centre;
        foreach (var (w, h) in new[] { (2, 2), (2, 1), (1, 2), (1, 1) })
        {
            var best = (X: -1, Y: -1);
            var bestScore = float.MinValue;
            for (var y = 1; y + h <= n - 1; y++)
            {
                for (var x = 1; x + w <= n - 1; x++)
                {
                    var open = true;
                    for (var dy = 0; dy < h; dy++)
                    {
                        for (var dx = 0; dx < w; dx++)
                        {
                            open &= plan.Zone(x + dx, y + dy) is CityZone.Badlands or CityZone.Scrub;
                        }
                    }

                    // A highway may run alongside but not through it.
                    if (!open || w == 2 && x + 1 == cx || h == 2 && y + 1 == cy)
                        continue;

                    var byHighway = x == cx || x + w == cx || y == cy || y + h == cy;
                    var score = (byHighway ? 100 : 0) - shape.Distance(x, y);
                    if (score <= bestScore)
                        continue;

                    best = (x, y);
                    bestScore = score;
                }
            }

            if (best.X < 0)
                continue;

            for (var dy = 0; dy < h; dy++)
            {
                for (var dx = 0; dx < w; dx++)
                {
                    plan.Zones[(best.Y + dy) * n + best.X + dx] = CityZone.Solar;
                }
            }

            return (best.X, best.Y, w, h);
        }

        var outer = Enumerable.Range(0, n * n)
            .Where(i => i % n > 0 && i / n > 0 && i % n < n - 1 && i / n < n - 1 && plan.Zones[i] != CityZone.Ocean
                        && i != cy * n + cx)
            .MaxBy(i => shape.Distance(i % n, i / n));
        plan.Zones[outer] = CityZone.Solar;
        return (outer % n, outer / n, 1, 1);
    }

    /// <summary>
    /// A building filling the rectangle from (<paramref name="x0"/>, <paramref name="y0"/>), walls included. Its
    /// inside is split again and again into rooms no bigger than the look allows, with a door in every split, so
    /// every room can be reached; then it gets windows and a door in the middle of each side named in
    /// <paramref name="entrances"/>.
    /// </summary>
    private static void PaintBuilding(CityPlan plan, int x0, int y0, int w, int h, Look look, CyberRng rng, params int[] entrances)
    {
        var (x1, y1) = (x0 + w - 1, y0 + h - 1);
        var main = rng.Pick(look.Floors);
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var edge = x == x0 || y == y0 || x == x1 || y == y1;
                plan.Set(x, y, main, edge ? look.Wall : CityStructure.None);
            }
        }

        Split(plan, x0 + 1, y0 + 1, x1 - 1, y1 - 1, look, main, rng.Fork(1));

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var corner = (x == x0 || x == x1) && (y == y0 || y == y1);
                var side = x == x0 || y == y0 || x == x1 || y == y1;
                if (!side || corner || (x - x0 + y - y0) % look.WindowEvery != 1)
                    continue;

                var (ix, iy) = (Math.Clamp(x, x0 + 1, x1 - 1), Math.Clamp(y, y0 + 1, y1 - 1));
                if (plan.Structure(ix, iy) == CityStructure.None)
                    plan.SetStructure(x, y, look.Window);
            }
        }

        foreach (var direction in entrances)
        {
            var (dx, dy) = WfcWave.Directions[direction];
            // The tiles along that side, corners left out, nearest the middle first; the door goes on the first
            // with open floor inside it.
            var side = dx != 0
                ? Enumerable.Range(y0 + 1, h - 2).Select(y => (X: dx > 0 ? x1 : x0, Y: y)).OrderBy(t => Math.Abs(t.Y - (y0 + y1) / 2))
                : Enumerable.Range(x0 + 1, w - 2).Select(x => (X: x, Y: dy > 0 ? y1 : y0)).OrderBy(t => Math.Abs(t.X - (x0 + x1) / 2));
            foreach (var (sx, sy) in side)
            {
                if (plan.Structure(sx - dx, sy - dy) != CityStructure.None)
                    continue;

                plan.SetStructure(sx, sy, CityStructure.Door);
                break;
            }
        }
    }

    /// <summary>
    /// Splits the inside of a building, from (<paramref name="x0"/>, <paramref name="y0"/>) to
    /// (<paramref name="x1"/>, <paramref name="y1"/>), into rooms with a wall and a door. A wall never ends at a
    /// door, so no door opens onto a wall.
    /// </summary>
    private static void Split(CityPlan plan, int x0, int y0, int x1, int y1, Look look, CityFloor main, CyberRng rng)
    {
        var (w, h) = (x1 - x0 + 1, y1 - y0 + 1);
        if (w <= look.MaxRoom && h <= look.MaxRoom || w < 7 && h < 7)
        {
            var floor = rng.Chance(1, 3) ? rng.Pick(look.Floors) : main;
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    plan.Set(x, y, floor);
                }
            }

            return;
        }

        var vertical = w > h || w == h && rng.Chance(1, 2);
        if (vertical ? w < 7 : h < 7)
            vertical = !vertical;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (vertical)
            {
                var s = rng.Range(x0 + 3, x1 - 3);
                if (plan.Structure(s, y0 - 1) == CityStructure.Door || plan.Structure(s, y1 + 1) == CityStructure.Door)
                    continue;

                var door = rng.Range(y0, y1);
                for (var y = y0; y <= y1; y++)
                {
                    plan.SetStructure(s, y, y == door ? CityStructure.Door : look.Wall);
                }

                Split(plan, x0, y0, s - 1, y1, look, main, rng.Fork(1));
                Split(plan, s + 1, y0, x1, y1, look, main, rng.Fork(2));
                return;
            }
            else
            {
                var s = rng.Range(y0 + 3, y1 - 3);
                if (plan.Structure(x0 - 1, s) == CityStructure.Door || plan.Structure(x1 + 1, s) == CityStructure.Door)
                    continue;

                var door = rng.Range(x0, x1);
                for (var x = x0; x <= x1; x++)
                {
                    plan.SetStructure(x, s, x == door ? CityStructure.Door : look.Wall);
                }

                Split(plan, x0, y0, x1, s - 1, look, main, rng.Fork(1));
                Split(plan, x0, s + 1, x1, y1, look, main, rng.Fork(2));
                return;
            }
        }
    }

    /// <summary>The corporate headquarters: one tower over the whole centre district, in a plaza with lamps.</summary>
    private static void PaintHeadquarters(CityPlan plan, int ox, int oy, CyberRng rng)
    {
        const int plaza = 5;
        for (var y = 0; y < DistrictSize; y++)
        {
            for (var x = 0; x < DistrictSize; x++)
            {
                plan.Set(ox + x, oy + y, CityFloor.Sidewalk);
            }
        }

        var size = DistrictSize - 2 * plaza;
        PaintBuilding(plan, ox + plaza, oy + plaza, size, size, TowerLook, rng.Fork(1), 0, 1, 2, 3);
        foreach (var (lx, ly) in new[] { (2, 2), (2, DistrictSize - 3), (DistrictSize - 3, 2), (DistrictSize - 3, DistrictSize - 3) })
        {
            plan.SetStructure(ox + lx, oy + ly, CityStructure.Lamp);
        }

        plan.Landmarks.Add((CityLandmark.Headquarters, ox + plaza, oy + plaza, size, size));
    }

    /// <summary>
    /// The civic centre: city hall, the police station, the hospital and the transit station round a cross of
    /// plazas, each with its doors onto them.
    /// </summary>
    private static void PaintCivicCentre(CityPlan plan, int ox, int oy, CyberRng rng)
    {
        const int size = (DistrictSize - 11) / 2;
        const int far = DistrictSize - 2 - size;
        for (var y = 0; y < DistrictSize; y++)
        {
            for (var x = 0; x < DistrictSize; x++)
            {
                plan.Set(ox + x, oy + y, CityFloor.Sidewalk);
            }
        }

        var buildings = CivicBuildings.ToList();
        rng.Shuffle(buildings);
        // Each corner, with the sides that face the plazas.
        var corners = new[] { (2, 2, 0, 1), (far, 2, 0, 3), (2, far, 2, 1), (far, far, 2, 3) };
        for (var i = 0; i < corners.Length; i++)
        {
            var (x, y, a, b) = corners[i];
            var (kind, look) = buildings[i];
            PaintBuilding(plan, ox + x, oy + y, size, size, look, rng.Fork((ulong) i + 1), a, b);
            plan.Landmarks.Add((kind, ox + x, oy + y, size, size));
        }

        const int mid = DistrictSize / 2;
        foreach (var (lx, ly) in new[] { (mid - 2, mid - 2), (mid + 2, mid + 2), (mid - 2, mid + 2), (mid + 2, mid - 2) })
        {
            plan.SetStructure(ox + lx, oy + ly, CityStructure.Lamp);
        }
    }

    /// <summary>A megabuilding: one huge block of flats over the whole district, a maze of small rooms.</summary>
    private static void PaintMegabuilding(CityPlan plan, int ox, int oy, CyberRng rng)
    {
        const int yard = 3;
        for (var y = 0; y < DistrictSize; y++)
        {
            for (var x = 0; x < DistrictSize; x++)
            {
                plan.Set(ox + x, oy + y, CityFloor.OldConcrete);
            }
        }

        var size = DistrictSize - 2 * yard;
        PaintBuilding(plan, ox + yard, oy + yard, size, size, MegabuildingLook, rng.Fork(1), 0, 1, 2, 3);
        plan.Landmarks.Add((CityLandmark.Megabuilding, ox + yard, oy + yard, size, size));
    }

    /// <summary>Gardens round the houses: sidewalk away from the road is lawn.</summary>
    private static void PaintLawns(CityPlan plan, int ox, int oy, int w, int h)
    {
        for (var y = oy + 1; y < oy + h - 1; y++)
        {
            for (var x = ox + 1; x < ox + w - 1; x++)
            {
                if (plan.Floor(x, y) != CityFloor.Sidewalk || plan.Structure(x, y) != CityStructure.None)
                    continue;

                var byRoad = false;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        byRoad |= plan.Floor(x + dx, y + dy) == CityFloor.Asphalt;
                    }
                }

                if (!byRoad)
                    plan.Set(x, y, CityFloor.Grass);
            }
        }
    }

    /// <summary>
    /// The solar plant over its districts: rows of panels on concrete either side of a cross of service roads,
    /// a substation by the crossroads, and a fence round it all with a gate where each road leaves.
    /// </summary>
    private static void PaintSolarPlant(CityPlan plan, (int X, int Y, int W, int H) districts)
    {
        var (ox, oy) = (Origin(districts.X), Origin(districts.Y));
        var (w, h) = (districts.W * Pitch - Avenue, districts.H * Pitch - Avenue);
        var (mx, my) = (w / 2, h / 2);
        const int station = 11;
        var (sx, sy) = (mx + 3, my + 3);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (tx, ty) = (ox + x, oy + y);
                var ring = x == 1 || y == 1 || x == w - 2 || y == h - 2;
                var inside = x > 1 && y > 1 && x < w - 2 && y < h - 2;
                var road = Math.Abs(x - mx) <= 1 || Math.Abs(y - my) <= 1;
                var nearStation = x >= sx - 2 && y >= sy - 2 && x <= sx + station + 1 && y <= sy + station - 1;
                if (ring && !road)
                    plan.Set(tx, ty, plan.Floor(tx, ty), CityStructure.Fence);
                else if (road && (inside || ring))
                    plan.Set(tx, ty, CityFloor.Asphalt);
                else if (inside && !nearStation && x > 3 && y > 3 && x < w - 4 && y < h - 4 && y % 3 == 0)
                    plan.Set(tx, ty, CityFloor.Concrete, CityStructure.SolarPanel);
                else
                    plan.Set(tx, ty, plan.Floor(tx, ty));
            }
        }

        PaintBuilding(plan, ox + sx, oy + sy, station, station - 2, SubstationLook, new CyberRng((ulong) (ox * 31 + oy)), 2, 3);
        plan.SetStructure(ox + mx - 2, oy + my - 2, CityStructure.Lamp);
        plan.SetStructure(ox + mx + 2, oy + my - 2, CityStructure.Lamp);
        plan.Landmarks.Add((CityLandmark.SolarPlant, ox, oy, w, h));
    }

    /// <summary>
    /// Sparse townships: a few shacks and a motel either side of a road out in the badlands, inside the fence.
    /// Highways are taken first, then any avenue between two districts of open land, which gets a dirt track.
    /// </summary>
    private static void PaintTownships(CityPlan plan, (Road[,] Vertical, Road[,] Horizontal) roads, CyberRng rng)
    {
        var n = plan.Districts;
        bool Open((int X, int Y) d)
        {
            return plan.Zone(d.X, d.Y) is CityZone.Badlands or CityZone.Scrub;
        }

        // Every avenue between two districts of open land that isn't on the edge of the map: its line, the
        // district along it, which way it runs and whether it's a highway.
        var sites = new List<(int Line, int Along, bool Vertical, bool Highway)>();
        for (var line = 1; line < n; line++)
        {
            for (var along = 1; along < n - 1; along++)
            {
                if (Open((line - 1, along)) && Open((line, along)))
                    sites.Add((line, along, true, roads.Vertical[line, along] == Road.Highway));
                if (Open((along, line - 1)) && Open((along, line)))
                    sites.Add((line, along, false, roads.Horizontal[along, line] == Road.Highway));
            }
        }

        rng.Shuffle(sites);
        var chosen = new List<(int Line, int Along, bool Vertical, bool Highway)>();
        foreach (var site in sites.OrderByDescending(s => s.Highway))
        {
            if (chosen.Count == 4)
                break;

            // Spread out: at least a district between any two.
            var area = TownshipArea(site);
            if (chosen.Any(c => Overlap(TownshipArea(c), area, DistrictSize)))
                continue;

            chosen.Add(site);
        }

        for (var i = 0; i < chosen.Count; i++)
        {
            PaintTownship(plan, chosen[i], rng.Fork((ulong) i + 1));
        }
    }

    private const int TownshipReach = 14;

    /// <summary>The tiles a township covers: its bottom-left tile and size.</summary>
    private static (int X, int Y, int W, int H) TownshipArea((int Line, int Along, bool Vertical, bool Highway) site)
    {
        var (across, along) = (site.Line * Pitch - TownshipReach, Origin(site.Along));
        var depth = Avenue + 2 * TownshipReach;
        return site.Vertical ? (across, along, depth, DistrictSize) : (along, across, DistrictSize, depth);
    }

    private static bool Overlap((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b, int margin)
    {
        return a.X < b.X + b.W + margin && b.X < a.X + a.W + margin && a.Y < b.Y + b.H + margin && b.Y < a.Y + a.H + margin;
    }

    private static void PaintTownship(CityPlan plan, (int Line, int Along, bool Vertical, bool Highway) site, CyberRng rng)
    {
        const int reach = TownshipReach;
        var (line, along, vertical, highway) = site;
        // a runs along the road from the start of the district, b across it from the road's first tile.
        var (start, road) = (Origin(along), line * Pitch);
        void Put(int a, int b, CityFloor? floor, CityStructure structure)
        {
            var (x, y) = vertical ? (road + b, start + a) : (start + a, road + b);
            plan.Set(x, y, floor ?? plan.Floor(x, y), structure);
        }

        (int X, int Y) At(int a, int b)
        {
            return vertical ? (road + b, start + a) : (start + a, road + b);
        }

        for (var a = 0; a < DistrictSize; a++)
        {
            for (var b = -reach; b < Avenue + reach; b++)
            {
                var lane = b is >= 2 and < Avenue - 2;
                if (lane && !highway)
                    Put(a, b, CityFloor.Dirt, CityStructure.None);
                else if (!lane)
                    Put(a, b, null, CityStructure.None);
            }
        }

        // Shacks down both sides of the road, doors onto it, with the motel first on one side.
        var motelSide = rng.Chance(1, 2) ? -1 : 1;
        var index = 0UL;
        foreach (var side in new[] { -1, 1 })
        {
            var a = 2;
            var first = true;
            while (true)
            {
                var motel = first && side == motelSide;
                var (length, depth) = motel ? (13, 7) : (rng.Range(5, 7), rng.Range(5, 6));
                if (a + length > DistrictSize - 2)
                    break;

                if (motel || !rng.Chance(1, 4))
                {
                    var b = side < 0 ? -1 - depth : Avenue + 1;
                    var (x0, y0) = At(a, b);
                    var (w, h) = vertical ? (depth, length) : (length, depth);
                    // The door goes on the side facing the road.
                    var facing = vertical ? (side < 0 ? 1 : 3) : (side < 0 ? 0 : 2);
                    var look = motel ? MotelLook : rng.Pick(ShackLooks);
                    PaintBuilding(plan, x0, y0, w, h, look, rng.Fork(++index), facing);
                    if (motel)
                    {
                        // A motel: a door off the road into every room.
                        for (var t = 2; t < length - 2; t += 4)
                        {
                            var (dx, dy) = At(a + t, side < 0 ? -2 : Avenue + 1);
                            var (ix, iy) = vertical ? (dx + side, dy) : (dx, dy + side);
                            if (plan.Structure(dx, dy) != CityStructure.Door && plan.Structure(ix, iy) == CityStructure.None)
                                plan.SetStructure(dx, dy, CityStructure.Door);
                        }
                    }
                }

                a += length + rng.Range(2, 4);
                first = false;
            }
        }

        // Lamps on the road's verges, clear of the doors.
        Put(10, 0, null, CityStructure.Lamp);
        Put(DistrictSize - 10, Avenue - 1, null, CityStructure.Lamp);

        var (tx, ty, tw, th) = TownshipArea(site);
        plan.Landmarks.Add((CityLandmark.Township, tx, ty, tw, th));
        var district = vertical ? (line - 1, along) : (along, line - 1);
        plan.Zones[district.Item2 * plan.Districts + district.Item1] = CityZone.Township;
    }
}
