using Content.Shared.Ghost.Components;

namespace Content.Shared.UserInterface;

/// <summary>
/// Read-only spectating of machine UIs by ghosts.
/// A ghost that can't interact can open any activatable UI that doesn't set
/// <see cref="ActivatableUIComponent.BlockSpectators"/>, even ones that need hands or complex interaction.
/// It never takes a single-user UI, never blocks anyone else from one, and every message it sends
/// is rejected by <see cref="Content.Shared.Interaction.SharedInteractionSystem"/>.
/// </summary>
public sealed partial class ActivatableUISystem
{
    /// <summary>
    /// Whether <paramref name="user"/> is a spectator of <paramref name="target"/>:
    /// a ghost that can't interact with it, so it may only look.
    /// </summary>
    public bool IsSpectator(EntityUid user, EntityUid target)
    {
        return HasComp<GhostComponent>(user) && !_blockerSystem.CanInteract(user, target);
    }

    /// <summary>
    /// Whether anyone other than a spectating ghost has this UI open.
    /// Use this instead of <see cref="SharedUserInterfaceSystem.IsUiOpen(Entity{UserInterfaceComponent?}, Enum)"/>
    /// when the answer decides whether the machine is in use.
    /// </summary>
    public bool IsUiOpenByNonSpectator(EntityUid uid, Enum key)
    {
        foreach (var actor in _uiSystem.GetActors(uid, key))
        {
            if (!IsSpectator(actor, uid))
                return true;
        }

        return false;
    }
}
