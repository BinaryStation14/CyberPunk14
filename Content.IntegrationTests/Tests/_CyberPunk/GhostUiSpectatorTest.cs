using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.APC;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._CyberPunk;

/// <summary>
/// Ghosts can open any machine's UI to look at it, can't send it anything, and never take a single-user
/// UI away from the living player using it.
/// </summary>
[TestFixture]
public sealed class GhostUiSpectatorTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: GhostUiSpectatorTestMachine
  components:
  - type: ActivatableUI
    key: enum.ApcUiKey.Key
    singleUser: true
  - type: UserInterface
    interfaces:
      enum.ApcUiKey.Key:
        type: ApcBoundUserInterface
";

    [Test]
    public async Task GhostViewsWithoutInteractingOrDisplacing()
    {
        var server = Pair.Server;
        await Pair.CreateTestMap();
        var coords = Pair.TestMap!.GridCoords;

        var entManager = server.ResolveDependency<IEntityManager>();
        var ui = entManager.System<SharedUserInterfaceSystem>();
        var verbs = entManager.System<SharedVerbSystem>();
        var activatable = entManager.System<ActivatableUISystem>();
        var key = ApcUiKey.Key;

        await server.WaitAssertion(() =>
        {
            var machine = entManager.SpawnEntity("GhostUiSpectatorTestMachine", coords);
            var ghost = entManager.SpawnEntity("MobObserver", coords);
            var human = entManager.SpawnEntity("MobHuman", coords);
            var other = entManager.SpawnEntity("MobHuman", coords);
            var aui = entManager.GetComponent<ActivatableUIComponent>(machine);

            Assert.That(activatable.IsSpectator(ghost, machine), Is.True);
            Assert.That(activatable.IsSpectator(human, machine), Is.False);

            // The ghost opens it first and doesn't claim it.
            Activate(verbs, machine, ghost);
            Assert.That(ui.IsUiOpen(machine, key, ghost), Is.True);
            Assert.That(aui.CurrentSingleUser, Is.Null);

            // A living player can still take it.
            Activate(verbs, machine, human);
            Assert.That(ui.IsUiOpen(machine, key, human), Is.True);
            Assert.That(aui.CurrentSingleUser, Is.EqualTo(human));

            // Another living player is still kept out, so single-user machines keep working.
            Activate(verbs, machine, other);
            Assert.That(ui.IsUiOpen(machine, key, other), Is.False);

            // The ghost closes and reopens while the player is using it, without displacing them.
            Activate(verbs, machine, ghost);
            Assert.That(ui.IsUiOpen(machine, key, ghost), Is.False);
            Activate(verbs, machine, ghost);
            Assert.That(ui.IsUiOpen(machine, key, ghost), Is.True);
            Assert.That(ui.IsUiOpen(machine, key, human), Is.True);
            Assert.That(aui.CurrentSingleUser, Is.EqualTo(human));
            Assert.That(activatable.IsUiOpenByNonSpectator(machine, key), Is.True);

            // Every message from the ghost is rejected; the player's goes through.
            Assert.That(MessageAllowed(entManager, machine, ghost, key), Is.False);
            Assert.That(MessageAllowed(entManager, machine, human, key), Is.True);

            // When the player leaves, only the ghost is watching, and it still hasn't taken the machine.
            ui.CloseUi(machine, key, human);
            Assert.That(aui.CurrentSingleUser, Is.Null);
            Assert.That(activatable.IsUiOpenByNonSpectator(machine, key), Is.False);
            Activate(verbs, machine, other);
            Assert.That(ui.IsUiOpen(machine, key, other), Is.True);
            Assert.That(aui.CurrentSingleUser, Is.EqualTo(other));
            Assert.That(ui.IsUiOpen(machine, key, ghost), Is.True);
        });
    }

    private static void Activate(SharedVerbSystem verbs, EntityUid machine, EntityUid user)
    {
        var verb = verbs.GetLocalVerbs(machine, user, typeof(ActivationVerb)).SingleOrDefault();
        Assert.That(verb, Is.Not.Null, $"{user} has no verb to open the machine");
        verb!.Act!.Invoke();
    }

    private static bool MessageAllowed(IEntityManager entManager, EntityUid machine, EntityUid actor, Enum key)
    {
        var attempt = new BoundUserInterfaceMessageAttempt(actor, machine, key, new ApcToggleMainBreakerMessage());
        entManager.EventBus.RaiseLocalEvent(machine, ref attempt);
        return !attempt.Cancelled;
    }
}
