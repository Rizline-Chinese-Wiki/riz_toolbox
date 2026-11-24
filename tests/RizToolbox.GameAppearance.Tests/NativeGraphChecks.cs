using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.GameAppearance;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Checks;

internal static class NativeGraphChecks
{
    internal static (string Name, Action Run)[] Cases => new (string, Action)[] {
        ("Actual native graph roots controller/information/gameObject/scene before matching and invocation", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
            using (var g = new GraphFixture(f))
            {
                Equal(-12, f.AppearanceOwnSceneHandle); Equal(7, f.AppearanceActiveSceneHandle);
                Check(f.AppearanceOwnSceneName != g.Run.Scene && f.AppearanceActiveSceneName == g.Run.Scene);
                Check(g.Current()); Equal(g.Initial, g.Read()); g.Write(Red); Equal(Red, g.Read());
                Check(g.MatcherCalls > 0 && g.AllMatcherArgumentsRooted);
                Check(f.AllAppearanceReceiversRooted && f.AllAppearanceSceneValuesRooted);
                Equal(0, f.FieldWrites);
            }
            Equal(0, f.ActiveRoots);
        }),
        ("Actual native graph refuses reads/writes when the observed run changes between discovery ticks", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
            using (var g = new GraphFixture(f))
            {
                g.Write(Red); var before = f.AppearanceSetters; g.ExpectedRunId = "replacement-run";
                Check(!g.Current()); Throws<TargetExpiredException>(() => g.Read());
                Throws<TargetExpiredException>(() => g.Write(Blue)); Equal(before, f.AppearanceSetters);
                Equal(Red, g.ActualColor);
            }
            Equal(0, f.ActiveRoots);
        }),
        ("Forked native graphs retain the original run ID and scene generation", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
            using (var g = new GraphFixture(f)) using (var fork = g.Fork())
            {
                Check(fork.Current()); Equal(g.Run, fork.Run);
                g.ExpectedSceneGeneration++; Check(!fork.Current());
                Throws<TargetExpiredException>(() => fork.Write(Blue)); Equal(0, f.AppearanceSetters);
                Equal(g.Run.RunId, g.LastMatchedRun); Equal(g.Run.SceneGeneration, g.LastMatchedScene);
            }
            Equal(0, f.ActiveRoots);
        }),
        ("Actual native graph expires a replaced information reference even while the sampler identity is unchanged", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
            using (var g = new GraphFixture(f))
            {
                Check(g.Current()); f.SetAppearanceInformation(f.AppearanceLine);
                Check(!g.Current()); Throws<TargetExpiredException>(() => g.Write(Red)); Equal(0, f.AppearanceSetters);
            }
            Equal(0, f.ActiveRoots);
        }),
        ("Actual native graph expires an inactive controller or unloaded/invalid scene before writing", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
            using (var g = new GraphFixture(f))
            {
                f.AppearanceControllerActive = false; Check(!g.Current()); Throws<TargetExpiredException>(() => g.Write(Red));
                f.AppearanceControllerActive = true; f.AppearanceSceneLoaded = false; Check(!g.Current());
                Throws<TargetExpiredException>(() => g.Read());
                f.AppearanceSceneLoaded = true; f.AppearanceSceneValid = false; Check(!g.Current());
                Throws<TargetExpiredException>(() => g.Write(Blue)); Equal(0, f.AppearanceSetters);
            }
            Equal(0, f.ActiveRoots);
        }),
        ("Actual appearance CaptureRun discovers a persistent controller in another loaded scene", () => {
            using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
            using (var g = new GraphFixture(f))
            {
                Check(g.CapturedCurrent());
                Check(g.Current()); g.Write(Red); Equal(Red, g.Read());
                Check(f.AllAppearanceReceiversRooted && f.AllAppearanceSceneValuesRooted && f.AllArrayReadsRooted);
            }
            Equal(0, f.ActiveRoots);
        }),
        ("Appearance rejects invalid or unloaded object and active scenes independently", () => {
            var changes = new Action<FakeNative>[] {
                f => f.AppearanceSceneValid = false, f => f.AppearanceSceneLoaded = false,
                f => f.AppearanceActiveSceneValid = false, f => f.AppearanceActiveSceneLoaded = false };
            for (var i = 0; i < changes.Length; ++i)
            {
                using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
                using (var g = new GraphFixture(f))
                {
                    changes[i](f); Check(!g.Current()); Throws<TargetExpiredException>(() => g.Write(Red));
                    if (i < 2) Throws<InvalidOperationException>(() => g.Capture());
                    else Throws<TargetExpiredException>(() => g.Capture());
                    Equal(0, f.AppearanceSetters);
                }
                Equal(0, f.ActiveRoots);
            }
        }),
        ("Appearance requires the active scene and enabled controller, regardless of object scene name", () => {
            var changes = new Action<FakeNative>[] {
                f => { f.AppearanceOwnSceneName = "Play"; f.AppearanceActiveSceneName = "Menu"; },
                f => f.AppearanceControllerEnabled = false,
                f => f.AppearanceControllerActive = false };
            for (var i = 0; i < changes.Length; ++i)
            {
                using var f = new FakeNative(); f.ConfigureAppearance(); f.ConfigureAppearanceGraph();
                using (var g = new GraphFixture(f))
                {
                    changes[i](f); Check(!g.Current()); Throws<TargetExpiredException>(() => g.Read());
                    if (i == 0) Throws<TargetExpiredException>(() => g.Capture());
                    else Throws<InvalidOperationException>(() => g.Capture());
                    Equal(0, f.AppearanceSetters);
                }
                Equal(0, f.ActiveRoots);
            }
        })
    };

    // Reflection stays inside this controlled test assembly. It reaches the actual
    // private graph/binding implementations without adding a production constructor
    // capable of bypassing loaded-PE verification, and uses NativeRuntime exports for
    // all root, metadata, field, scene and accessor work.
    private sealed class GraphFixture : IDisposable
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type Graph = typeof(NativeGameAppearanceBackend).GetNestedType("NativeGraph", BindingFlags.NonPublic)!;
        private static readonly Type Binding = typeof(NativeGameAppearanceBackend).GetNestedType("NativeBinding", BindingFlags.NonPublic)!;
        private readonly object _graph;
        private readonly NativeGameAppearanceBackend _owner;
        private readonly FakeNative _native;
        internal Func<bool> Current { get; }
        internal Func<ResourceColor> Read { get; }
        internal Action<ResourceColor> Write { get; }
        internal ResourceColor Initial { get; }
        internal ResourceColor ActualColor => new(_native.AppearanceRendered.R, _native.AppearanceRendered.G, _native.AppearanceRendered.B, _native.AppearanceRendered.Alpha);
        internal GameAppearanceRun Run => (GameAppearanceRun)Graph.GetField("Run", Private)!.GetValue(_graph)!;
        internal string ExpectedRunId = "run";
        internal long ExpectedSceneGeneration = 10;
        internal string? LastMatchedRun;
        internal long LastMatchedScene;
        internal int MatcherCalls;
        internal bool AllMatcherArgumentsRooted = true;

        internal GraphFixture(FakeNative native)
        {
            _native = native; Initial = ActualColor;
            var owner = (NativeGameAppearanceBackend)RuntimeHelpers.GetUninitializedObject(typeof(NativeGameAppearanceBackend));
            _owner = owner;
            var fields = new Dictionary<string, NativeExactReadField> {
                ["controller.info"] = native.AppearanceApi.InspectExactDeclaredReadField(native.AppearanceLineClass,
                    "LevelInformation", "InLevel.LineInformation", false, 0x28),
                ["info.chart"] = native.AppearanceApi.InspectExactDeclaredReadField(native.AppearanceLineClass,
                    "chart", "InLevel.LineInformation", false, 0x88),
                ["info.progress"] = native.AppearanceApi.InspectExactDeclaredReadField(native.AppearanceLineClass,
                    "progressControl", "InLevel.LineInformation", false, 0x28),
                ["info.loaded"] = native.AppearanceApi.InspectExactDeclaredReadField(native.AppearanceLineClass,
                    "loadOver", "System.Boolean", false, 0xBE),
                ["info.over"] = native.AppearanceApi.InspectExactDeclaredReadField(native.AppearanceLineClass,
                    "levelOver", "System.Boolean", false, 0xC0),
                ["info.crashed"] = native.AppearanceApi.InspectExactDeclaredReadField(native.AppearanceLineClass,
                    "crashed", "System.Boolean", false, 0xC1),
                ["progress.ready"] = native.AppearanceApi.InspectExactDeclaredReadField(native.AppearanceLineClass,
                    "timeReady", "System.Boolean", false, 0x65)
            };
            Set(owner, "_api", native.AppearanceApi); Set(owner, "_fields", fields);
            Set(owner, "_scene", native.AppearanceSceneClass); Set(owner, "_sceneManager", native.AppearanceSceneManagerClass);
            Set(owner, "_resources", native.AppearanceResourcesClass); Set(owner, "_controller", native.AppearanceLineClass);
            Set(owner, "_unityObject", native.AppearanceUnityObjectClass);
            Set(owner, "_matchesRun", (Func<long, long, string, long, bool>)((information, controller, run, scene) => {
                ++MatcherCalls; LastMatchedRun = run; LastMatchedScene = scene;
                AllMatcherArgumentsRooted &= native.HasRootFor(new(information)) && native.HasRootFor(new(controller));
                return information == native.AppearanceInformation.ToInt64() && controller == native.AppearanceLine.ToInt64()
                    && run == ExpectedRunId && scene == ExpectedSceneGeneration;
            }));
            _graph = Activator.CreateInstance(Graph, Private, null, new object[] { owner }, null)!;
            Method<Func<IntPtr, int>>(_graph, "Object")(native.AppearanceLine);
            Method<Func<int, string, int>>(_graph, "Reference")(0, "controller.info");
            Method<Func<IntPtr, int>>(_graph, "Object")(native.AppearanceRenderer);
            Set(_graph, "Scene", "Play"); Set(_graph, "Run", new GameAppearanceRun("run", "chart", "Play", 10));
            Current = Method<Func<bool>>(_graph, "Current"); (Read, Write) = Bind();
        }
        private GraphFixture(GraphFixture original, object graph)
        {
            _native = original._native; _owner = original._owner; _graph = graph; Initial = original.Initial;
            Current = Method<Func<bool>>(_graph, "Current"); (Read, Write) = Bind();
        }
        internal GraphFixture Fork() => new(this, Method<Func<object>>(_graph, "Fork")());
        internal object Capture()
        {
            try { return typeof(NativeGameAppearanceBackend).GetMethod("CaptureRun", Private)!.Invoke(_owner, new object[] { Run })!; }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        internal bool CapturedCurrent()
        {
            using var captured = (IDisposable)Capture();
            return Method<Func<bool>>(captured, "Current")();
        }
        private (Func<ResourceColor>, Action<ResourceColor>) Bind()
        {
            var api = _native.AppearanceApi;
            var handles = Method<Func<int, NativeRuntime.RootHandle>>(_graph, "Handle");
            var cache = api.InspectExactDeclaredReadField(_native.AppearanceLineClass, "_startColorPlan", "UnityEngine.Color", false, 0x68);
            var accessor = NativeAppearanceColorAccessors.Bind(api, _native.AppearanceRendererClass, "UnityEngine", "LineRenderer", "get_startColor",
                _native.AppearanceLineClass, "InLevel", "LineInformation", "set_startColor");
            var binding = Activator.CreateInstance(Binding, Private, null, new object[] { _graph, (Func<bool>)(() => true), (Func<bool>)(() => true),
                (Func<Rgba>)(() => accessor.ReadTrack(api, handles(0), cache, handles(2))),
                (Action<Rgba>)(color => accessor.WriteColor(api, handles(0), color)) }, null)!;
            return (Method<Func<ResourceColor>>(binding, "Read"), Method<Action<ResourceColor>>(binding, "Write"));
        }
        private static T Method<T>(object instance, string name) where T : Delegate
            => instance.GetType().GetMethod(name, Private)!.CreateDelegate<T>(instance);
        private static void Set(object instance, string name, object value)
            => instance.GetType().GetField(name, Private)!.SetValue(instance, value);
        public void Dispose() => ((IDisposable)_graph).Dispose();
    }
}
