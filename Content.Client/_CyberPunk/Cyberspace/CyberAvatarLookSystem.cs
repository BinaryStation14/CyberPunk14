using System.Numerics;
using Content.Client.Graphics;
using Content.Shared._CyberPunk.Cyberspace;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._CyberPunk.Cyberspace;

/// <summary>
/// Draws a runner's virtual body, and everything it wears or holds, through the cyber avatar shader.
/// </summary>
public sealed partial class CyberAvatarLookSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string PostShaderId = "cyber-avatar";
    private static readonly ProtoId<ShaderPrototype> Shader = "CyberAvatar";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberAvatarLookComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CyberAvatarLookComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<CyberAvatarLookComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<CyberAvatarLookComponent> ent, ref ComponentStartup args)
    {
        Apply(ent);
    }

    private void OnState(Entity<CyberAvatarLookComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Apply(ent);
    }

    private void OnShutdown(Entity<CyberAvatarLookComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.RemovePostShader((ent, sprite), PostShaderId);
    }

    private void Apply(Entity<CyberAvatarLookComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var tint = ent.Comp.Tint;
        var shader = _proto.Index(Shader).InstanceUnique();
        shader.SetParameter("tint", new Vector3(tint.R, tint.G, tint.B));
        // Keeps avatars from glitching in step with each other.
        shader.SetParameter("seed", ent.Owner.Id % 1000 * 0.137f);

        _sprite.SetPostShader((ent, sprite), new SpriteComponent.PostShaderArgs(PostShaderId, shader)
        {
            Before = ContentPostShaderIds.BeforeOutlines,
        });
    }
}
