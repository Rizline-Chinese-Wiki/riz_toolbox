using RizToolbox.Adapter;
using RizToolbox.Adapter.GameAppearance;
using RizToolbox.Core;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Checks;

internal static class NativeRingColorChecks
{
    internal static (string Name, Action Run)[] Cases => new (string, Action)[] {
        ("Actual ring group setter modifies and restores the cache and rendered SpriteRenderer color", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceRing(); var api = f.AppearanceApi;
            using var group = api.Root(f.AppearanceGroup); using var renderer = api.Root(f.AppearanceRingRenderer);
            var accessor = Ring(f); var cache = Cache(f); var before = accessor.ReadCoupled(api, group, cache, renderer);
            var desired = new Rgba(.9f, .2f, .1f, .7f); var noteBefore = f.AppearanceSpriteColor;
            accessor.WriteCoupled(api, group, cache, renderer, desired, () => RequireRoots(f));
            Equal(desired, api.ReadExactField<Rgba>(group, cache)); Equal(desired, accessor.ReadRendered(api, renderer));
            accessor.WriteCoupled(api, group, cache, renderer, before, () => RequireRoots(f));
            Equal(before, api.ReadExactField<Rgba>(group, cache)); Equal(before, accessor.ReadRendered(api, renderer));
            Equal(noteBefore, f.AppearanceSpriteColor); Equal(2, f.AppearanceSetters);
            Check(f.AllAppearanceReceiversRooted); Equal(0, f.FieldWrites);
        }),
        ("Ring colors with different cache and rendered baselines are refused before writing", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceRing(); var api = f.AppearanceApi;
            using var group = api.Root(f.AppearanceGroup); using var renderer = api.Root(f.AppearanceRingRenderer);
            var accessor = Ring(f); var cache = Cache(f); f.AppearanceRingCached = new(.8f, .7f, .6f, 1);
            var cached = f.AppearanceRingCached; var rendered = f.AppearanceRingRendered;
            Check(!accessor.TryReadCoupled(api, group, cache, renderer, out _));
            Throws<InvalidOperationException>(() => accessor.WriteCoupled(api, group, cache, renderer, new(1, 0, 0, 1), () => RequireRoots(f)));
            Equal(cached, f.AppearanceRingCached); Equal(rendered, f.AppearanceRingRendered); Equal(0, f.AppearanceSetters);
        }),
        ("Ring setter readback detects either missing side effect and restores the complete prior value", () => {
            foreach (var ignoreCache in new[] { false, true })
            {
                using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceRing(); var api = f.AppearanceApi;
                using var group = api.Root(f.AppearanceGroup); using var renderer = api.Root(f.AppearanceRingRenderer);
                var accessor = Ring(f); var cache = Cache(f); var before = f.AppearanceRingCached;
                f.AppearanceRingIgnoreCacheWrites = ignoreCache; f.AppearanceRingIgnoreRendererWrites = !ignoreCache;
                Throws<InvalidOperationException>(() => accessor.WriteCoupled(api, group, cache, renderer, new(1, 0, 0, 1), () => RequireRoots(f)));
                Equal(before, api.ReadExactField<Rgba>(group, cache)); Equal(before, accessor.ReadRendered(api, renderer));
                Equal(2, f.AppearanceSetters); Equal(0, f.FieldWrites);
            }
        }),
        ("Ring write and rollback stop when their current run and pool guard fails", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceRing(); var api = f.AppearanceApi;
            using var group = api.Root(f.AppearanceGroup); using var renderer = api.Root(f.AppearanceRingRenderer);
            var accessor = Ring(f); var cache = Cache(f); var before = f.AppearanceRingRendered;
            Throws<TargetExpiredException>(() => accessor.WriteCoupled(api, group, cache, renderer, new(1, 0, 0, 1),
                () => throw new TargetExpiredException("Controlled run/pool replacement.")));
            Equal(before, f.AppearanceRingCached); Equal(before, f.AppearanceRingRendered); Equal(0, f.AppearanceSetters);
            var checks = 0;
            Throws<TargetExpiredException>(() => accessor.WriteCoupled(api, group, cache, renderer, new(1, 0, 0, 1), () => {
                RequireRoots(f); if (++checks >= 3) throw new TargetExpiredException("Controlled replacement after the setter.");
            }));
            Equal(1, f.AppearanceSetters); Equal(new Rgba(1, 0, 0, 1), f.AppearanceRingCached);
            Equal(f.AppearanceRingCached, f.AppearanceRingRendered); Equal(0, f.FieldWrites);
        }),
        ("Ring shared mutation channel adopts coherent game changes and restores both external baseline values", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceRing(); var api = f.AppearanceApi;
            using var group = api.Root(f.AppearanceGroup); using var renderer = api.Root(f.AppearanceRingRenderer);
            using var main = new MainThreadDispatcher(); var log = new Logger();
            using var targets = new SemanticTargetRegistry(main, log); using var mutations = new MutationCoordinator(targets, main, log);
            var accessor = Ring(f); var cache = Cache(f);
            var target = NeedOp(targets.Register("controlled.ring.color", () => Resource(accessor.ReadCoupled(api, group, cache, renderer))));
            using var channel = NeedOp(mutations.Register(target, "color",
                value => accessor.WriteCoupled(api, group, cache, renderer, Native(value), () => RequireRoots(f))));
            var lease = NeedOp(channel.Override("controlled.plugin", Red));
            var external = new Rgba(.1f, .8f, .3f, .5f); f.AppearanceRingCached = f.AppearanceRingRendered = external;
            Check(channel.Refresh().Success); Equal(Native(Red), f.AppearanceRingCached); Equal(Native(Red), f.AppearanceRingRendered);
            lease.Dispose(); Equal(external, f.AppearanceRingCached); Equal(external, f.AppearanceRingRendered); Equal(0, f.FieldWrites);
        }),
        ("Ring one-sided external changes invalidate leases without overwriting either observed value", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceRing(); var api = f.AppearanceApi;
            using var group = api.Root(f.AppearanceGroup); using var renderer = api.Root(f.AppearanceRingRenderer);
            using var main = new MainThreadDispatcher(); var log = new Logger();
            using var targets = new SemanticTargetRegistry(main, log); using var mutations = new MutationCoordinator(targets, main, log);
            var accessor = Ring(f); var cache = Cache(f);
            var target = NeedOp(targets.Register("controlled.ring.color", () => Resource(accessor.ReadCoupled(api, group, cache, renderer))));
            using var channel = NeedOp(mutations.Register(target, "color",
                value => accessor.WriteCoupled(api, group, cache, renderer, Native(value), () => RequireRoots(f))));
            var lease = NeedOp(channel.Override("controlled.plugin", Red));
            var external = new Rgba(.1f, .8f, .3f, .5f); f.AppearanceRingCached = external; var writes = f.AppearanceSetters;
            Check(!channel.Refresh().Success && !lease.IsActive); channel.Dispose(); lease.Dispose();
            Equal(external, f.AppearanceRingCached); Equal(Native(Red), f.AppearanceRingRendered); Equal(writes, f.AppearanceSetters);
        })
    };
    private static NativeAppearanceColorAccessors Ring(FakeNative f)
        => NativeAppearanceColorAccessors.Bind(f.AppearanceApi, f.AppearanceSpriteClass, "UnityEngine", "SpriteRenderer", "get_color",
            f.AppearanceGroupClass, "InLevel", "LinesGroupInformation", "set_color");
    private static NativeExactReadField Cache(FakeNative f) => f.AppearanceApi.InspectExactDeclaredReadField(f.AppearanceGroupClass,
        "_startColorPlan", "UnityEngine.Color", false, 0x88);
    private static void RequireRoots(FakeNative f) { Check(f.HasRootFor(f.AppearanceGroup)); Check(f.HasRootFor(f.AppearanceRingRenderer)); }
    private static ResourceColor Resource(Rgba value) => new(value.R, value.G, value.B, value.Alpha);
    private static Rgba Native(ResourceColor value) => new(value.R, value.G, value.B, value.A);
}
