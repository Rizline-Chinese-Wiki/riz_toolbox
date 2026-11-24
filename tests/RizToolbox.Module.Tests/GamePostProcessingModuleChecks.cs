using RizToolbox.Adapter;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class GamePostProcessingModuleChecks
{
    private const string Capability = GamePostProcessingCapabilityIds.PostProcessing;
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Game blur waits for demand and a supported scene before native discovery", Demand),
        ("Game blur SDK and semantic alias arbitrate one shared integer parameter", Competition),
        ("Game blur preserves external writes, refuses ambiguity and expires rebuilt targets", Replacement),
        ("Game blur intent waits for a unique target in its scene and follows rebuilt generations", IntentReplacement),
        ("Game blur intent keeps equal-priority order across rebinds and respects external writes", IntentPriority),
        ("Game blur intent ends if the selected component's channel becomes invalid", InvalidatedIntent),
        ("Game blur intent releases an acquisition when its plugin stops inside the setter", IntentScope),
        ("Game blur provider withdrawal retires intents and scope views", IntentProvider)
    };

    private static void Check(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    { if (!condition) throw new InvalidOperationException("Game blur module check failed at line " + line + "."); }

    private static void Demand()
    {
        using var f = new Fixture();
        f.Manager.SetCapabilityDemand(Array.Empty<string>());
        f.Manager.Tick(0, "MainMenu");
        Check(f.Creates == 0 && !f.Caps.GetCapability(Capability).Available);
        f.Manager.SetCapabilityDemand(new[] { Capability });
        f.Manager.Tick(1, "Level");
        Check(f.Creates == 0 && !f.Caps.GetCapability(Capability).Available);
        f.Manager.Tick(2, "MainMenu");
        Check(f.Creates == 1 && f.A.View!.GetDownsampleTarget().Error?.KnownCode == ErrorCode.TargetMissing);
        var old = f.A.View!;
        f.Manager.Tick(3, "Level");
        Check(!f.Caps.GetCapability(Capability).Available && old.GetDownsampleTarget().Error?.KnownCode == ErrorCode.ScopeExpired);
        Check(RizlineAdapter.IsDemandManaged(f.Module));
    }

    private static void Competition()
    {
        using var f = new Fixture(); var source = f.Backend.Replace();
        f.Manager.Tick(0, "MainMenu");
        var target = f.A.View!.GetDownsampleTarget().Value!;
        var match = f.B.Targets!.Resolve<int>(new(Key: GamePostProcessingRoles.MainCameraBlurDownsample));
        Check(match.Target == target.Target && target is { Minimum: 0, Maximum: 2, Scene: "MainMenu" });
        var invalid = f.A.View.OverrideDownsample(target.Target, -1);
        Check(!invalid.Success && source.Value == 0 && source.Writes == 0);
        using var low = f.A.View.OverrideDownsample(target.Target, 1, 10).Value!;
        Check(source.Value == 1);
        source.FailAfterWrite = true;
        var rejected = f.B.View!.OverrideDownsample(target.Target, 2, 20);
        Check(rejected.Error?.KnownCode == ErrorCode.MutationWriteFailed && source.Value == 1);
        using var high = f.B.Mutations!.Override(new MutationKey<int>(match.Target!, GamePostProcessingRoles.DownsampleProperty), 2, 20).Value!;
        Check(source.Value == 2);
        high.Dispose(); Check(source.Value == 1);
        low.Dispose(); Check(source.Value == 0);
        var worker = Task.Run(f.A.View.GetDownsampleTarget).GetAwaiter().GetResult();
        Check(worker.Error?.KnownCode == ErrorCode.WrongThread);
    }

    private static void Replacement()
    {
        using var f = new Fixture(); var source = f.Backend.Replace();
        f.Manager.Tick(0, "MainMenu");
        var target = f.A.View!.GetDownsampleTarget().Value!;
        using var owned = f.A.View.OverrideDownsample(target.Target, 2).Value!;
        source.Value = 1; // Unrelated game write.
        f.Manager.Tick(1, "MainMenu");
        Check(source.Value == 1);
        owned.Dispose(); Check(source.Value == 1);
        f.Backend.Ambiguous = true;
        f.Manager.Tick(2, "MainMenu");
        Check(f.A.View.GetDownsampleTarget().Error?.KnownCode == ErrorCode.AmbiguousTarget && source.Retained == 0);
        var replacement = f.Backend.Replace();
        f.Manager.Tick(3, "MainMenu");
        var next = f.A.View.GetDownsampleTarget().Value!;
        Check(next.Target.Generation != target.Target.Generation && next.Target.Key == target.Target.Key);
        Check(f.A.View.OverrideDownsample(target.Target, 2).Error?.KnownCode == ErrorCode.TargetExpired && replacement.Writes == 0);
        using var lease = f.A.View.OverrideDownsample(next.Target, 2).Value!;
        var rebuilt = f.Backend.Replace();
        f.Manager.Tick(4, "MainMenu");
        var rebuiltTarget = f.A.View.GetDownsampleTarget().Value!;
        Check(rebuiltTarget.Target.Generation != next.Target.Generation && replacement.Retained == 0 && rebuilt.Writes == 0);
        using var released = f.A.View.OverrideDownsample(rebuiltTarget.Target, 2).Value!;
        f.Manager.Tick(5, "Level");
        Check(rebuilt.Value == 0 && rebuilt.Retained == 0 && f.A.View.GetDownsampleTarget().Error?.KnownCode == ErrorCode.ScopeExpired);
    }

    private static TargetQuery Select(string scene = "MainMenu")
        => new(Scene: scene, Role: GamePostProcessingRoles.MainCameraBlurDownsample);

    private static void IntentReplacement()
    {
        using var f = new Fixture();
        f.Manager.Tick(0, "MainMenu");
        var service = f.A.View!;
        Check(service.BindDownsample(Select("Level"), 1).Error?.KnownCode == ErrorCode.InvalidArgument);
        Check(service.BindDownsample(Select() with { IncludeTemplates = true }, 1).Error?.KnownCode == ErrorCode.InvalidArgument);
        Check(service.BindDownsample(Select() with { Key = GamePostProcessingRoles.MainCameraBlurDownsample + "/GameStart" }, 1)
            .Error?.KnownCode == ErrorCode.InvalidArgument);
        Check(service.BindDownsample(Select(), 3).Error?.KnownCode == ErrorCode.InvalidArgument);
        Check(Task.Run(() => service.BindDownsample(Select(), 2)).GetAwaiter().GetResult().Error?.KnownCode == ErrorCode.WrongThread);
        var discoveries = f.Backend.Discoveries;
        using var intent = service.BindDownsample(Select(), 2).Value!;
        var waiting = Task.Run(() => intent.LastSnapshot).GetAwaiter().GetResult();
        Check(waiting is { State: VisualBindingState.Waiting, Target: null } && f.Backend.Discoveries == discoveries);
        Check(Task.Run(intent.Stop).GetAwaiter().GetResult().Error?.KnownCode == ErrorCode.WrongThread);

        var source = f.Backend.Replace();
        f.Backend.Ambiguous = true;
        f.Manager.Tick(1, "MainMenu");
        var ambiguous = intent.LastSnapshot;
        Check(ambiguous is { State: VisualBindingState.Ambiguous, Target: null } && ambiguous.Error?.KnownCode == ErrorCode.AmbiguousTarget);
        Check(source.Writes == 0 && source.Retained == 0);
        f.Backend.Ambiguous = false;
        f.Manager.Tick(2, "MainMenu");
        var first = intent.LastSnapshot;
        Check(first.State == VisualBindingState.Bound && first.Revision > ambiguous.Revision && source.Value == 2);

        var rebuilt = f.Backend.Replace(reuseIdentity: true);
        f.Manager.Tick(3, "MainMenu");
        var second = intent.LastSnapshot;
        Check(rebuilt.Identity == source.Identity && second.Target != first.Target && second.Target?.Key == first.Target!.Key);
        Check(second.State == VisualBindingState.Bound && rebuilt.Value == 2 && source.Retained == 0);

        f.Backend.Ambiguous = true;
        var writes = rebuilt.Writes;
        f.Manager.Tick(4, "MainMenu");
        Check(intent.LastSnapshot is { State: VisualBindingState.Ambiguous, Target: null } && rebuilt.Retained == 0 && rebuilt.Writes == writes);
        var startup = f.Backend.Replace("GameStart");
        f.Backend.Ambiguous = true;
        f.Manager.Tick(5, "GameStart");
        Check(intent.LastSnapshot is { State: VisualBindingState.Waiting, Target: null } && startup.Writes == 0);
        var final = f.Backend.Replace();
        f.Manager.Tick(6, "MainMenu");
        Check(intent.LastSnapshot.State == VisualBindingState.Bound && final.Value == 2 && rebuilt.Retained == 0);
        Check(intent.Stop().Success && final.Value == 0 && intent.LastSnapshot is { State: VisualBindingState.Stopped, Target: null });
    }

    private static void IntentPriority()
    {
        using var f = new Fixture();
        var first = f.Backend.Replace();
        f.Manager.Tick(0, "MainMenu");
        using var removed = f.A.View!.BindDownsample(Select(), 0, 1).Value!;
        using var older = f.A.View.BindDownsample(Select(), 1, 10).Value!;
        Check(removed.Stop().Success);
        using var newer = f.B.View!.BindDownsample(Select(), 2, 10).Value!;
        f.Manager.Tick(1, "MainMenu");
        Check(first.Value == 2 && older.LastSnapshot.State == VisualBindingState.Bound && newer.LastSnapshot.State == VisualBindingState.Bound);
        var second = f.Backend.Replace();
        f.Manager.Tick(2, "MainMenu");
        Check(second.Value == 2 && first.Retained == 0);
        var target = f.A.View.GetDownsampleTarget().Value!.Target;
        using var oneShot = f.A.View.OverrideDownsample(target, 0, 11).Value!;
        Check(second.Value == 0);
        oneShot.Dispose();
        Check(second.Value == 2);
        second.Value = 1;
        var writes = second.Writes;
        f.Manager.Tick(3, "MainMenu");
        Check(second.Value == 1 && second.Writes == writes);
        Check(older.LastSnapshot.Error?.KnownCode == ErrorCode.Conflict && newer.LastSnapshot.Error?.KnownCode == ErrorCode.Conflict);
        var third = f.Backend.Replace();
        f.Manager.Tick(4, "MainMenu");
        Check(third.Writes == 0 && older.LastSnapshot.State == VisualBindingState.Stopped && newer.LastSnapshot.State == VisualBindingState.Stopped);
    }

    private static void InvalidatedIntent()
    {
        using var f = new Fixture();
        var source = f.Backend.Replace();
        f.Manager.Tick(0, "MainMenu");
        using var intent = f.A.View!.BindDownsample(Select(), 2).Value!;
        f.Manager.Tick(1, "MainMenu");
        Check(source.Value == 2 && intent.LastSnapshot.State == VisualBindingState.Bound);
        source.Value = 3;
        f.Manager.Tick(2, "MainMenu");
        Check(intent.LastSnapshot is { State: VisualBindingState.Stopped, Target: null } && source.Value == 3);
        source.Value = 0;
        f.Manager.Tick(3, "MainMenu");
        Check(f.A.View.GetDownsampleTarget().Success && source.Value == 0 && source.Writes == 1);
    }

    private static void IntentScope()
    {
        using var f = new Fixture();
        var source = f.Backend.Replace();
        f.Manager.Tick(0, "MainMenu");
        using var intent = f.A.View!.BindDownsample(Select(), 2).Value!;
        source.AfterWrite = f.DisableA;
        f.Manager.Tick(1, "MainMenu");
        Check(intent.LastSnapshot.State == VisualBindingState.Stopped && source.Writes == 2 && source.Value == 0);
        var rebuilt = f.Backend.Replace();
        f.Manager.Tick(2, "MainMenu");
        Check(rebuilt.Writes == 0 && f.B.View!.GetDownsampleTarget().Success);
    }

    private static void IntentProvider()
    {
        using var f = new Fixture();
        var source = f.Backend.Replace();
        f.Manager.Tick(0, "MainMenu");
        var old = f.A.View!;
        using var intent = old.BindDownsample(Select(), 1).Value!;
        f.Manager.Tick(1, "MainMenu");
        Check(source.Value == 1);
        f.Manager.Tick(2, "Level");
        Check(source.Value == 0 && source.Retained == 0 && intent.LastSnapshot.State == VisualBindingState.Stopped);
        f.Manager.Tick(3, "MainMenu");
        Check(source.Value == 0 && f.A.View!.GetDownsampleTarget().Success);
        Check(old.BindDownsample(Select(), 1).Error?.KnownCode == ErrorCode.ScopeExpired);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MainThreadDispatcher _main = new();
        private readonly Logger _log = new();
        internal readonly CapabilityRegistry Caps = new(new("test", "test", "test", NativeGameBlurBackend.Fingerprint, true));
        internal readonly ModuleManager Manager;
        internal readonly GamePostProcessingModule Module;
        internal readonly Backend Backend = new();
        private readonly PluginHost _host;
        internal readonly Plugin A = new("test.blur.a"), B = new("test.blur.b");
        internal int Creates;

        internal Fixture()
        {
            var shared = new SemanticTargetsModule(_main, _log);
            Manager = new(Caps, new Resolver(), _log, "fixed", NativeGameBlurBackend.Fingerprint);
            Manager.Add(shared);
            Module = new(_main, _log, shared, NativeGameBlurBackend.Fingerprint, () => { Creates++; return Backend; });
            Manager.Add(Module, true);
            _host = new(Caps, _main, new EventHub(_log), _log);
            Check(_host.Register(A).Success && _host.Register(B).Success);
            _host.Enable(A.Manifest.Id); _host.Enable(B.Manifest.Id);
            Manager.SetCapabilityDemand(_host.GetRequiredCapabilityDemand());
        }

        internal void DisableA() => Check(_host.Disable(A.Manifest.Id).Success);
        public void Dispose() { _host.Dispose(); Manager.Dispose(); _main.Dispose(); }
    }

    private sealed class Backend : IGameBlurBackend
    {
        private Source? _current;
        private int _next;
        internal bool Ambiguous;
        internal int Discoveries;
        internal Source Replace(string scene = "MainMenu", bool reuseIdentity = false)
        {
            Ambiguous = false;
            return _current = new(reuseIdentity ? _current!.Identity : new(scene, 10, new(0x123), new(++_next)));
        }
        public IGameBlurTarget? Discover()
        {
            Discoveries++;
            if (Ambiguous) throw new AdapterOperationException(new(ErrorCode.AmbiguousTarget, "Controlled camera has two blur sources.",
                TargetId: GamePostProcessingRoles.MainCameraBlurDownsample + "/" + _current!.Identity.Scene));
            return _current is { } current ? new Target(this, current) : null;
        }

        private sealed class Target : IGameBlurTarget
        {
            private readonly Backend _owner;
            private readonly Source _source;
            private bool _disposed;
            internal Target(Backend owner, Source source) { _owner = owner; _source = source; _source.Retained++; }
            public GameBlurIdentity Identity => _source.Identity;
            public bool IsCurrent() => !_disposed && ReferenceEquals(_owner._current, _source);
            public int Read() { Check(IsCurrent()); return _source.Value; }
            public void Write(int value)
            {
                Check(IsCurrent() && !_owner.Ambiguous); _source.Writes++; _source.Value = value;
                var callback = _source.AfterWrite;
                _source.AfterWrite = null;
                callback?.Invoke();
                if (!_source.FailAfterWrite) return;
                _source.FailAfterWrite = false;
                throw new InvalidOperationException("The controlled blur setter failed after changing its value.");
            }
            public void Dispose() { Check(!_disposed); _disposed = true; _source.Retained--; }
        }
    }

    private sealed class Source
    {
        internal readonly GameBlurIdentity Identity;
        internal int Value, Writes, Retained;
        internal bool FailAfterWrite;
        internal Action? AfterWrite;
        internal Source(GameBlurIdentity identity) => Identity = identity;
    }
    private sealed class Plugin : IRizPlugin
    {
        public PluginManifest Manifest { get; }
        internal IGamePostProcessingService? View;
        internal ITargetService? Targets;
        internal IMutationService? Mutations;
        internal Plugin(string id) => Manifest = new(id, new(1, 0), new(1, 0), RequiredCapabilities: new[] { Capability });
        public void Enable(IPluginContext context)
        {
            View = context.GetService<IGamePostProcessingService>(Capability).Service;
            Targets = context.GetService<ITargetService>(TargetCapabilityIds.Targets).Service;
            Mutations = context.GetService<IMutationService>(TargetCapabilityIds.Mutations).Service;
        }
        public void Disable() { }
    }
    private sealed class Resolver : IModuleSymbolResolver
    { public AdapterSymbolProbe Probe(AdapterSymbolRequirement symbol) => new(symbol.Id, true, "controlled signature", null); }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}
