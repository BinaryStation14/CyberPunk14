using System.Numerics;
using Content.Server.Cloning;
using Content.Shared._CyberPunk.Cyberspace;
using Content.Shared.Access.Components;
using Content.Shared.Body;
using Content.Shared.Cloning;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Station.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._CyberPunk.Cyberspace;

/// <summary>
/// A runner's virtual body takes their shape: the outside of their species' body, coloured and marked like
/// theirs, and their voice. It wears a runner's uniform and a proxy of whatever ID their real body wears, and
/// shows in a colour of their own. Its species is its own, so it doesn't breathe, eat or feel the cold, and it
/// bleeds ghostlight.
/// </summary>
public sealed partial class CyberspaceSystem
{
    [Dependency] private CloningSystem _cloning = default!;
    [Dependency] private HumanoidProfileSystem _humanoid = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private OrganRelationSystem _organs = default!;
    [Dependency] private StationSpawningSystem _spawning = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;

    private static readonly ProtoId<SpeciesPrototype> AvatarSpecies = "CyberAvatar";
    private static readonly ProtoId<CloningSettingsPrototype> AvatarCloning = "CyberAvatar";
    private static readonly ProtoId<StartingGearPrototype> AvatarGear = "CyberAvatar";

    private void InitializeAvatars()
    {
        SubscribeLocalEvent<CyberProxyIdComponent, GetAdditionalAccessEvent>(OnProxyIdAccess);
        SubscribeLocalEvent<CyberProxyIdComponent, ExaminedEvent>(OnProxyIdExamined);
    }

    private void TakeShape(EntityUid avatar, Entity<NetrunnerComponent> body)
    {
        var profile = CompOrNull<HumanoidProfileComponent>(body);
        GrowBody(avatar, profile?.Species ?? HumanoidCharacterProfile.DefaultSpecies);
        _visualBody.CopyAppearanceFrom(body.Owner, avatar);

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

        _cloning.CloneComponents(body, avatar, ProtoMan.Index(AvatarCloning));
        _spawning.EquipStartingGear(avatar, AvatarGear);

        body.Comp.Tint ??= Color.FromHsv(new Vector4(_random.NextFloat(), 0.75f, 1f, 1f));
        var look = EnsureComp<CyberAvatarLookComponent>(avatar);
        look.Tint = body.Comp.Tint.Value;
        Dirty(avatar, look);
    }

    private void OnProxyIdAccess(Entity<CyberProxyIdComponent> ent, ref GetAdditionalAccessEvent args)
    {
        if (ReflectedId(ent) is { } id)
            args.Entities.Add(id);
    }

    private void OnProxyIdExamined(Entity<CyberProxyIdComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ReflectedId(ent) is { } id
            ? Loc.GetString("cyberspace-proxy-id-reflects", ("id", id))
            : Loc.GetString("cyberspace-proxy-id-blank"));
    }

    /// <summary>
    /// The ID worn by the real body of the runner whose avatar wears this proxy.
    /// </summary>
    private EntityUid? ReflectedId(EntityUid proxy)
    {
        if (!_container.TryGetContainingContainer(proxy, out var container)
            || !TryComp<CyberAvatarComponent>(container.Owner, out var avatar)
            || !_inventory.TryGetSlotEntity(avatar.Body, "id", out var id))
        {
            return null;
        }

        return id;
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
}
