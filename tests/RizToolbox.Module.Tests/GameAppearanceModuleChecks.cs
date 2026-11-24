using RizToolbox.Adapter.GameAppearance;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class GameAppearanceModuleChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Game appearance refuses an unknown build before constructing any native provider", UnknownBuild),
        ("Game appearance and shared targets activate with no menu or active gameplay objects", NoUiDependency),
        ("Lifecycle withdrawal expires appearance views while retaining other shared targets", IndependentWithdrawal),
        ("Property degradation keeps the appearance module available when optional line getters are absent", OptionalLineSymbols)
    };
    private static void Check(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    { if (!condition) throw new InvalidOperationException("Game appearance module check failed at line " + line + "."); }
    private static void UnknownBuild()
    {
        using var main = new MainThreadDispatcher(); var shared = new SemanticTargetsModule(main, new Logger()); var calls = 0;
        var module = new GameAppearanceModule(main, shared, (_, _) => { calls++; throw new InvalidOperationException(); }, new string('f', 64));
        Check(module.Probe() is { Ready: false, Permanent: true } && calls == 0);
        Check(!module.Manifest.RequiredCapabilities.Contains(GameCapabilityIds.Resources)); module.Release();
    }
    private static void NoUiDependency()
    {
        using var f = new ModuleFixture(); f.Manager.Tick(0);
        Check(f.Plugin.View is not null && f.Plugin.Targets is not null);
        Check(f.Plugin.View!.GetTargets() is { Success: true, Value.Count: 0 });
        Check(f.Plugin.Targets!.GetTargets().Count == 0 && f.Caps.GetCapability(GameAppearanceCapabilityIds.Appearance).Available);
        Check(!f.Caps.GetCapability(GameCapabilityIds.Resources).Available && f.Creates == 1);
    }
    private static void OptionalLineSymbols()
    {
        var resolver = new Resolver();
        resolver.Missing.UnionWith(new[] { "appearance.line.start-getter", "appearance.line.end-getter", "appearance.line.start-setter" });
        using var f = new ModuleFixture(resolver); f.Manager.Tick(0);
        Check(f.Creates == 1 && f.Plugin.View is not null && f.Caps.GetCapability(GameAppearanceCapabilityIds.Appearance).Available);
        Check(!NativeGameAppearanceBackend.Requirements.Any(symbol => resolver.Missing.Contains(symbol.Id)));
        Check(NativeGameAppearanceBackend.Requirements.Any(symbol => symbol.Id == "appearance.child.pool"));
    }
    private static void IndependentWithdrawal()
    {
        using var f = new ModuleFixture(); f.Manager.Tick(0); var old = f.Plugin.View!;
        var sentinel = f.Shared.Targets.Register("test.non-appearance", () => 17);
        Check(sentinel.Success);
        f.Caps.SetUnavailable(GameLifecycleCapabilityIds.Lifecycle, "Controlled lifecycle provider withdrawn."); f.Manager.Tick(1);
        Check(!f.Caps.GetCapability(GameAppearanceCapabilityIds.Appearance).Available
            && f.Caps.GetCapability(TargetCapabilityIds.Targets).Available
            && f.Shared.Targets.Acquire(sentinel.Value!).Value!.Read().Value == 17);
        var expired = old.GetTargets();
        Check(!expired.Success && expired.Error is { Code: ErrorCode.ScopeExpired, CapabilityId: GameAppearanceCapabilityIds.Appearance });
        f.Caps.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, new EmptyLifecycle()); f.Manager.Tick(2);
        Check(f.Creates == 2 && f.Plugin.Enables == 2 && f.Plugin.View!.GetTargets().Success);
        expired = old.GetTargets();
        Check(!expired.Success && expired.Error is { Code: ErrorCode.ScopeExpired, CapabilityId: GameAppearanceCapabilityIds.Appearance });
    }
    private sealed class ModuleFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Logger Log = new();
        internal readonly CapabilityRegistry Caps = new(new("test", "test", "test", NativeGameAppearanceBackend.Fingerprint, true));
        internal readonly SemanticTargetsModule Shared;
        internal readonly ModuleManager Manager;
        internal readonly PluginHost Host;
        internal readonly Consumer Plugin = new();
        internal int Creates;
        internal ModuleFixture(Resolver? resolver = null)
        {
            Caps.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, new EmptyLifecycle());
            Shared = new(Main, Log); Manager = new(Caps, resolver ?? new Resolver(), Log, "fixed", NativeGameAppearanceBackend.Fingerprint);
            Manager.Add(Shared);
            Manager.Add(new GameAppearanceModule(Main, Shared, (targets, mutations) => {
                Creates++; return new(Main, Log, targets, mutations, new EmptyBackend());
            }, NativeGameAppearanceBackend.Fingerprint));
            Host = new(Caps, Main, new EventHub(Log), Log); Check(Host.Register(Plugin).Success); Host.Enable(Plugin.Manifest.Id);
        }
        public void Dispose() { Host.Dispose(); Manager.Dispose(); Main.Dispose(); }
    }
    private sealed class EmptyBackend : IGameAppearanceBackend
    {
        public IReadOnlyList<GameAppearanceProperty> Properties => Array.Empty<GameAppearanceProperty>();
        public IEnumerable<GameAppearanceCandidate> Discover(GameAppearanceRun run, int maximumScanned)
            => throw new InvalidOperationException("No observed run may start native discovery.");
    }
    private sealed class EmptyLifecycle : IGameLifecycleService
    {
        public Result<GameLifecycleSnapshot> GetSnapshot() => Result<GameLifecycleSnapshot>.Fail(new(ErrorCode.ObservationUnavailable, "No observed run."));
        public Result<IGameLifecycleSubscription> Subscribe(Action<GameLifecycleChange> handler)
            => Result<IGameLifecycleSubscription>.Fail(new(ErrorCode.ObservationUnavailable, "No observed run."));
    }
    private sealed class Consumer : IRizPlugin
    {
        public PluginManifest Manifest => new("test.game-appearance", new(1, 0), new(1, 0),
            RequiredCapabilities: new[] { GameAppearanceCapabilityIds.Appearance, TargetCapabilityIds.Targets });
        internal IGameAppearanceService? View;
        internal ITargetService? Targets;
        internal int Enables;
        public void Enable(IPluginContext context)
        {
            View = context.GetService<IGameAppearanceService>(GameAppearanceCapabilityIds.Appearance).Service;
            Targets = context.GetService<ITargetService>(TargetCapabilityIds.Targets).Service; Enables++;
        }
        public void Disable() { }
    }
}
