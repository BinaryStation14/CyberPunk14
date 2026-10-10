using Robust.Shared.GameStates;

namespace Content.Shared._CyberPunk.Cyberspace;

/// <summary>
/// How a runner's virtual body shows in cyberspace: it, and everything it wears or holds, drawn in shades of
/// its runner's colour, glitching now and then.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CyberAvatarLookComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Tint = Color.White;
}
