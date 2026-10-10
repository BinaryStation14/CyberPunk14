using Content.Server.Cloning;
using Content.Shared.Body;
using Content.Shared.Cloning;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._CyberPunk.Cyberspace;

/// <summary>
/// A runner's virtual body takes their shape: the outside of their species' body, coloured and marked like
/// theirs, their voice, and copies of the clothes they wear. Its species is its own, so it doesn't breathe, eat
/// or feel the cold, and it bleeds ghostlight.
/// </summary>
public sealed partial class CyberspaceSystem
{
    [Dependency] private CloningSystem _cloning = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private HumanoidProfileSystem _humanoid = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private OrganRelationSystem _organs = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;

    private static readonly ProtoId<SpeciesPrototype> AvatarSpecies = "CyberAvatar";
    private static readonly ProtoId<CloningSettingsPrototype> AvatarCloning = "CyberAvatar";

    private void TakeShape(EntityUid avatar, EntityUid body)
    {
        var profile = CompOrNull<HumanoidProfileComponent>(body);
        GrowBody(avatar, profile?.Species ?? HumanoidCharacterProfile.DefaultSpecies);
        _visualBody.CopyAppearanceFrom(body, avatar);

        if (profile != null)
        {
            _humanoid.ApplyProfileTo(avatar,
                new HumanoidCharacterProfile()
                    .WithSpecies(AvatarSpecies)
                    .WithSex(profile.Sex)
                    .WithGender(profile.Gender)
                    .WithAge(profile.Age)
                    .WithVoice(profile.Voice));
        }

        var settings = ProtoMan.Index(AvatarCloning);
        _cloning.CloneComponents(body, avatar, settings);
        Dress(avatar, body, settings);
    }

    /// <summary>
    /// Gives the avatar the parts of a species' body that show, hands included, and none of the organs inside.
    /// </summary>
    private void GrowBody(EntityUid avatar, ProtoId<SpeciesPrototype> species)
    {
        if (!ProtoMan.Resolve(species, out var speciesProto)
            || !ProtoMan.Index(speciesProto.DollPrototype).TryComp<InitialBodyComponent>(out var initial, Factory))
            return;

        var grown = new Dictionary<ProtoId<OrganCategoryPrototype>, EntityUid>();
        foreach (var (category, organ) in initial.Organs)
        {
            if (!HasComp<VisualOrganComponent>(organ) && !HasComp<VisualOrganMarkingsComponent>(organ))
                continue;

            if (TrySpawnInContainer(organ, avatar, BodyComponent.ContainerID, out var uid))
                grown[category] = uid.Value;
        }

        if (initial.Relationships is not { } relationships)
            return;

        foreach (var (parent, children) in relationships)
        {
            if (!grown.TryGetValue(parent, out var parentUid))
                continue;

            foreach (var child in children)
            {
                if (grown.TryGetValue(child, out var childUid))
                    _organs.Relate(parentUid, childUid);
            }
        }
    }

    /// <summary>
    /// Dresses the avatar in copies of what the body wears, with nothing in their pockets or bags.
    /// </summary>
    private void Dress(EntityUid avatar, EntityUid body, CloningSettingsPrototype settings)
    {
        if (settings.CopyEquipment is not { } slots)
            return;

        var coords = Transform(avatar).Coordinates;
        var worn = _inventory.GetSlotEnumerator(body, slots);
        while (worn.NextItem(out var item, out var slot))
        {
            if (Prototype(item) is not { } proto
                || !_whitelist.CheckBoth(item, settings.EquipmentBlacklist, settings.EquipmentWhitelist))
                continue;

            var copy = Spawn(proto.ID, coords);
            if (TryComp<StorageComponent>(copy, out var storage))
                _container.CleanContainer(storage.Container);

            if (!_inventory.TryEquip(avatar, copy, slot.Name, silent: true))
                Del(copy);
        }
    }
}
