using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._CyberPunk.City;
using Content.Shared.Teleportation.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._CyberPunk;

/// <summary>
/// Raising the city builds its map and leads every city portal to a gateway back on its streets.
/// </summary>
[TestFixture]
public sealed class CityTest : GameTest
{
    [Test]
    public async Task CityPortalLeadsToTheCity()
    {
        var server = Pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var city = entMan.System<CitySystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            mapSys.CreateMap(out var mapId);
            var portal = entMan.SpawnEntity("CityPortal", new MapCoordinates(0, 0, mapId));

            city.Generate(1);
            var plan = CityGenerator.Generate(1);
            Assert.That(city.City, Is.Not.Null);
            var cityMap = city.City!.Value;
            var grid = entMan.GetComponent<MapGridComponent>(cityMap);
            var spawn = mapSys.GetTileRef(cityMap, grid, new Vector2i(plan.Spawn.X, plan.Spawn.Y));
            Assert.That(spawn.Tile.IsEmpty, Is.False);

            var link = entMan.GetComponent<LinkedEntityComponent>(portal);
            Assert.That(link.LinkedEntities, Has.Count.EqualTo(1));
            var back = link.LinkedEntities.Single();
            Assert.That(entMan.GetComponent<TransformComponent>(back).MapUid, Is.EqualTo(cityMap));

            // A new city replaces the old one, and the portal leads to it instead.
            city.Generate(2);
            Assert.That(entMan.Deleted(cityMap));
            Assert.That(entMan.Deleted(portal), Is.False);
            Assert.That(link.LinkedEntities, Has.Count.EqualTo(1));
            Assert.That(entMan.GetComponent<TransformComponent>(link.LinkedEntities.Single()).MapUid, Is.EqualTo(city.City));
        });
    }
}
