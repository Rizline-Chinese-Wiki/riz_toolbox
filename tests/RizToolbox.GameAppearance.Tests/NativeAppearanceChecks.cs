using RizToolbox.Adapter;
using RizToolbox.Adapter.GameAppearance;
using RizToolbox.Runtime;
using static Checks;

internal static class NativeAppearanceChecks
{
    internal static (string Name, Action Run)[] Cases => new (string, Action)[] {
        ("Property degradation isolates a missing actual line getter while end and note accessors still execute", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var api = f.AppearanceApi;
            f.AppearanceMethodByName("get_startColor").Name = "missing_startColor";
            NativeAppearanceColorAccessors? start = null;
            Check(!NativeGameAppearanceBackend.TryBindProperty(() => start = Track(f, "start"), out var reason));
            Check(start is null && reason is not null); Equal(0, f.Invocations); Equal(0, f.ActiveRoots);
            NativeAppearanceColorAccessors? end = null, note = null;
            Check(NativeGameAppearanceBackend.TryBindProperty(() => end = Track(f, "end"), out _));
            Check(NativeGameAppearanceBackend.TryBindProperty(() => note = NativeAppearanceColorAccessors.Bind(api,
                f.AppearanceSpriteClass, "UnityEngine", "SpriteRenderer", "get_color",
                f.AppearanceSpriteClass, "UnityEngine", "SpriteRenderer", "set_color"), out _));
            using (var line = api.Root(f.AppearanceLine)) using (var renderer = api.Root(f.AppearanceRenderer))
            using (var sprite = api.Root(f.AppearanceSprite))
            {
                var desired = new Rgba(.1f, .2f, .3f, .4f);
                var cache = api.InspectExactDeclaredReadField(f.AppearanceLineClass, "_endColorPlan", "UnityEngine.Color", false, 0x78);
                end!.WriteColor(api, line, desired); Equal(desired, end.ReadTrack(api, line, cache, renderer));
                note!.WriteColor(api, sprite, desired); Equal(desired, note.ReadRendered(api, sprite));
                Check(f.AllAppearanceReceiversRooted); Equal(0, f.FieldWrites);
            }
            Equal(0, f.ActiveRoots);
            Throws<InvalidOperationException>(() => NativeGameAppearanceBackend.TryBindProperty(
                () => throw new InvalidOperationException("actual runtime is not ready"), out _));
        }),
        ("Actual NativeRuntime track Color getters/setters round-trip rendered values and the exact game cache", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var api = f.AppearanceApi;
            using (var line = api.Root(f.AppearanceLine)) using (var renderer = api.Root(f.AppearanceRenderer))
            {
                var binding = Track(f, "start"); var cache = api.InspectExactDeclaredReadField(f.AppearanceLineClass, "_startColorPlan", "UnityEngine.Color", false, 0x68);
                // Cached and rendered values initially differ. The actual rendered getter must win.
                Equal(f.AppearanceRendered, binding.ReadTrack(api, line, cache, renderer));
                var desired = new Rgba(.9f, .7f, .5f, 1); binding.WriteColor(api, line, desired);
                Equal(desired, binding.ReadTrack(api, line, cache, renderer)); Equal(desired, api.ReadExactField<Rgba>(line, cache));
                Equal(1, f.AppearanceSetters); Equal(2, f.AppearanceGetters); Check(f.AllAppearanceReceiversRooted);
                Equal(0, f.FieldWrites);
            }
            Equal(0, f.ActiveRoots);
        }),
        ("Actual NativeRuntime track end property uses its independent getter/setter and source offset", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var api = f.AppearanceApi;
            using var line = api.Root(f.AppearanceLine); using var renderer = api.Root(f.AppearanceRenderer);
            var binding = Track(f, "end"); var field = api.InspectExactDeclaredReadField(f.AppearanceLineClass, "_endColorPlan", "UnityEngine.Color", false, 0x78);
            var desired = new Rgba(.1f, .2f, .3f, .4f); binding.WriteColor(api, line, desired); Equal(desired, binding.ReadTrack(api, line, field, renderer));
            Equal(new Rgba(.2f, .4f, .6f, 1), f.AppearanceRendered);
            Equal(0, f.FieldWrites);
        }),
        ("Actual NativeRuntime note renderer Color accessors invoke the declared SpriteRenderer property", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var api = f.AppearanceApi;
            using var sprite = api.Root(f.AppearanceSprite);
            var binding = NativeAppearanceColorAccessors.Bind(api, f.AppearanceSpriteClass, "UnityEngine", "SpriteRenderer", "get_color",
                f.AppearanceSpriteClass, "UnityEngine", "SpriteRenderer", "set_color");
            var desired = new Rgba(1, .25f, .5f, .75f); binding.WriteColor(api, sprite, desired);
            Equal(desired, binding.ReadRendered(api, sprite)); Check(f.AllAppearanceReceiversRooted); Equal(0, f.FieldWrites);
        }),
        ("Actual metadata rejects a changed accessor return before native invocation", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.AppearanceMethodByName("get_startColor").Return = f.Single;
            Throws<NotSupportedException>(() => Track(f, "start")); Equal(0, f.Invocations); Equal(0, f.ActiveRoots);
        }),
        ("Actual metadata rejects a changed Color parameter before native invocation", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.AppearanceMethodByName("set_startColor").Parameters = new[] { f.Single };
            Throws<MissingMethodException>(() => Track(f, "start")); Equal(0, f.Invocations); Equal(0, f.ActiveRoots);
        }),
        ("Exact field offset and type checks fail before game cache storage is read", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ChangeAppearanceCacheOffset(0x6C);
            Throws<NotSupportedException>(() => f.AppearanceApi.InspectExactDeclaredReadField(f.AppearanceLineClass, "_startColorPlan", "UnityEngine.Color", false, 0x68));
            Throws<NotSupportedException>(() => f.AppearanceApi.InspectExactDeclaredReadField(f.AppearanceLineClass, "_startColorPlan", "System.Single", false, 0x6C));
            Equal(0, f.FieldReads); Equal(0, f.Invocations);
        }),
        ("Foreign or expired native field owners cannot substitute for a rooted LineInformation", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var api = f.AppearanceApi;
            var field = api.InspectExactDeclaredReadField(f.AppearanceLineClass, "_startColorPlan", "UnityEngine.Color", false, 0x68);
            using var foreign = api.Root(f.AppearanceSprite); Throws<NotSupportedException>(() => api.ReadExactField<Rgba>(foreign, field));
            var expired = api.Root(f.AppearanceLine); expired.Dispose(); Throws<InvalidOperationException>(() => api.ReadExactField<Rgba>(expired, field)); Equal(0, f.FieldReads);
        }),
        ("NativeRuntime rejects a wrong setter receiver before native side effects", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var binding = Track(f, "start");
            using var foreign = f.AppearanceApi.Root(f.AppearanceSprite);
            Throws<NotSupportedException>(() => binding.WriteColor(f.AppearanceApi, foreign, new Rgba(1, 0, 0, 1)));
            Equal(0, f.Invocations); Equal(0, f.FieldWrites);
        }),
        ("Native getter cannot report the requested color merely because the source cache changed", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var api = f.AppearanceApi; var binding = Track(f, "start");
            using var line = api.Root(f.AppearanceLine); using var renderer = api.Root(f.AppearanceRenderer);
            var field = api.InspectExactDeclaredReadField(f.AppearanceLineClass, "_startColorPlan", "UnityEngine.Color", false, 0x68);
            f.AppearanceIgnoreWrites = true; var before = f.AppearanceRendered; var desired = new Rgba(1, 0, 0, 1);
            binding.WriteColor(api, line, desired); Equal(before, binding.ReadTrack(api, line, field, renderer)); Check(before != desired);
        }),
        ("Native accessors reject worker threads before invoking the controlled exports", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); var binding = Track(f, "start"); using var line = f.AppearanceApi.Root(f.AppearanceLine);
            Task.Run(() => Throws<InvalidOperationException>(() => binding.WriteColor(f.AppearanceApi, line, new Rgba(1, 0, 0, 1)))).GetAwaiter().GetResult();
            Equal(0, f.Invocations); Equal(0, f.FieldWrites);
        })
    };
    private static NativeAppearanceColorAccessors Track(FakeNative f, string name)
        => NativeAppearanceColorAccessors.Bind(f.AppearanceApi, f.AppearanceRendererClass, "UnityEngine", "LineRenderer", "get_" + name + "Color",
            f.AppearanceLineClass, "InLevel", "LineInformation", "set_" + name + "Color");
}
