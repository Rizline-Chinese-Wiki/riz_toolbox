using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.GameAppearance;

internal sealed partial class NativeGameAppearanceBackend
{
    private static GameAppearanceProperty RingProperty { get; } = new(GameAppearanceRoles.TrackRingColor, "color",
        "LevelInformation.lineControl.lineGroups[] -> active LinesGroupInformation.ringRendererPlan -> poolChild.targetComponent (exact UnityEngine.SpriteRenderer).color; written through LinesGroupInformation.set_color",
        "Fixed dump and existing complete 176-byte setter evidence: LinesGroupInformation.set_color@3FB990 writes _startColorPlan at +88 and forwards to SpriteRenderer.set_color@155C010 while its ring plan is active. SpriteRenderer.get_color@155BF30 reads the actual rendered property. The cache and renderer must agree before admission and on every read; successful writes and restoration verify both. Current run, group membership, active flags and pool ownership are rechecked on every access. Real visible acceptance remains unverified.",
        ExternalWritePolicy.AdoptAndReapply);

    private void BindRings(IntPtr module)
    {
        var group = _types["group"]; var plan = _api.NestedClass(group, "RingRenderer"); _types.Add("ringPlan", plan);
        var sprite = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "SpriteRenderer"); _types.Add("ringSprite", sprite);
        Add("lines.info", _types["lineControl"], "levelInformation", "InLevel.LevelInformation", 0x20);
        Add("group.active", group, "groupActive", "System.Boolean", 0x28);
        Add("group.ringActive", group, "ringActive", "System.Boolean", 0x29);
        Add("group.ringPlan", group, "ringRendererPlan", "InLevel.LinesGroupInformation.RingRenderer", 0x80);
        Add("group.ringColor", group, "_startColorPlan", "UnityEngine.Color", 0x88);
        Add("ringPlan.active", plan, "active", "System.Boolean", 0x28);
        Add("ringPlan.child", plan, "poolChild", "InLevel.EasyPoolChild", 0x18);
        Add("ringPlan.pool", plan, "pool", "InLevel.EasyPool", 0x10);
        var accessors = NativeAppearanceColorAccessors.Bind(_api, sprite, "UnityEngine", "SpriteRenderer", "get_color",
            group, "InLevel", "LinesGroupInformation", "set_color");
        var method = _api.InspectDeclaredMethod(group, "set_color", new[] { "UnityEngine.Color" }, "System.Void", module);
        if (method.Entry?.Rva != 0x3FB990) throw new UnsupportedSignatureException("Game ring color setter differs from the pinned MethodInfo RVA.");
        VerifyBody(module, 0x3FB990, "4b07ecf2e6d210f7702cd2952f0f842d0ff33aaf50bc35c0a1bc4178e10c3c6e");
        _ringColor = accessors;
    }

    private IEnumerable<GameAppearanceCandidate> Rings(NativeGraph graph, GameAppearanceRun run, ScanBudget budget)
    {
        var color = _ringColor; if (color is null) yield break;
        // Ring-only links must not make independently supported note targets depend
        // on an unrelated line-control graph.
        using var source = graph.Fork();
        var control = source.Reference(1, "info.lines"); source.ExpectReference(control, "lines.info", 1);
        var groups = source.Reference(control, "lines.groups"); var seen = new HashSet<IntPtr>();
        foreach (var pointer in ReadArray(source.Pointer(groups), GameAppearanceService.MaximumScanned, budget))
        {
            if (!seen.Add(pointer)) continue;
            using var current = source.Fork(); var group = current.Member(groups, pointer);
            if (!current.Read<bool>(group, "group.active") || !current.Read<bool>(group, "group.ringActive")) continue;
            current.Expect(group, "group.active", true); current.Expect(group, "group.ringActive", true);
            var plan = current.Reference(group, "group.ringPlan");
            if (!current.Read<bool>(plan, "ringPlan.active")) continue;
            current.Expect(plan, "ringPlan.active", true);
            var child = current.Reference(plan, "ringPlan.child"); var pool = current.Reference(plan, "ringPlan.pool");
            current.ExpectReference(child, "child.pool", pool); var renderer = current.Reference(child, "child.target");
            if (_api.ObjectClass(current.Pointer(renderer)) != _types["ringSprite"]
                || !color.TryReadCoupled(_api, current.Handle(group), _fields["group.ringColor"], current.Handle(renderer), out _)) continue;
            var id = InstanceId(current.Pointer(group)); var renderId = InstanceId(current.Pointer(renderer));
            var owned = current.Fork();
            try
            {
                bool AliveRing() => Alive(owned.Pointer(group)) && Alive(owned.Pointer(renderer));
                void RequireCurrent()
                {
                    if (!owned.Current() || !AliveRing())
                        throw new TargetExpiredException("Concrete ring run/object/pool ownership has changed.");
                }
                var captured = new NativeBinding(owned, AliveRing,
                    () => Active(owned.Pointer(group)) && Active(owned.Pointer(renderer)),
                    () => color.ReadCoupled(_api, owned.Handle(group), _fields["group.ringColor"], owned.Handle(renderer)),
                    value => color.WriteCoupled(_api, owned.Handle(group), _fields["group.ringColor"], owned.Handle(renderer), value, RequireCurrent));
                // The actual SpriteRenderer.color identity also collides with note
                // properties, so a shared renderer cannot acquire two coordinators.
                yield return Candidate(run, RingProperty, $"track/group/{id}/ring-color", $"sprite:{renderId}:color",
                    new[] { $"game.track/group/{id}/ring-color", $"game.appearance/track/group/{id}/ring-color" }, captured,
                    $"Live LinesGroupInformation {id} / pooled SpriteRenderer {renderId} judge ring color");
            }
            finally { owned.DisposeUnlessRetained(); }
        }
    }
}
