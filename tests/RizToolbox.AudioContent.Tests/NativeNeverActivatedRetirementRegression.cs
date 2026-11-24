using System.Runtime.CompilerServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Exercises the sealed transaction and real registry/owned-clone contracts with controlled
/// transports. These tests do not claim IL2CPP, Unity timing, a native setter or runtime acceptance.</summary>
internal static class NativeNeverActivatedRetirementRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Never-active native backend rejects unsupported builds before native reads or allocation", NativeAdmission),
        ("Never-active cancellation uses pre-clone observation, delayed five-dead proof and exact registry Close", SuccessfulCancellation),
        ("Never-active destruction waits for a later frame and all five owned Unity objects", FiveDeadAndFrame),
        ("Never-active known pre-retention injection failure retires the still-inactive clone", KnownInjectionRejection),
        ("Never-active callback activity unknown or in flight keeps exact classification and roots", CallbackPending),
        ("Never-active verifier rejects wrong generation controller result scene and result identities", ExactRequest),
        ("Never-active transaction rejects worker and reentrant work before touching native state", ThreadAndReentry),
        ("Never-active process responsibility retains an abandoned transaction through later retirement", Abandoned),
        ("Never-active module withdrawal keeps the host after Close until later-frame bundle cleanup", HostWithdrawal),
        // These failures deliberately retain bounded process responsibility; no test reset/free exists.
        ("Never-active transaction rejects null-info and foreign parameter violations", InactiveViolations),
        ("Never-active Awake original in-flight or observation failure makes retirement sticky unknown", AwakeViolations),
        ("Never-active globals owner and delegate changes cannot be restored into apparent success", GlobalsViolations),
        ("Never-active branch rejects a session committed outside its closed transaction", CommittedSession),
        ("Never-active issuer cannot substitute a fake closed flag for exact registry token and Close", FakeClosed),
        ("Never-active unknown allocation cannot use an empty-controller verifier", UnknownAllocation),
        ("Never-active unknown parameter mutation never destroys or releases its controller", UnknownSetter),
        ("Never-active unknown Destroy and native root free are never retried", UnknownDestroyAndFree),
        ("Never-active observation free failure preserves process responsibility without retry", UnknownObservationFree),
        ("Never-active frame observation failure is sticky and never retried as destruction proof", UnknownFrame)
    };

    private static void NativeAdmission()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        var bundle = new OwnedNativeLocalPlayBundle(native.Api, main);
        var registry = new LocalRunSessionRegistry(main);
        var session = registry.RegisterPrepared(new(new("retirement.test", "native-admission"), 1, "chart"), 29_999_000, bundle);
        var claim = registry.BeginCommit(session);
        var profile = new AdapterBuildProfile("test", "", "test", "test", "test", "test", new Dictionary<string, TypeBinding>());
        var backend = new NativeNeverActivatedRetirementBackend(native.Api, main, profile, IntPtr.Zero, null!, bundle);
        Throws<UnsupportedSignatureException>(() => backend.CreateClosedTransaction(registry, claim));
        Equal(0, native.NewRoots); Equal(0, native.Invocations); Equal(0, native.FieldReads);
        // Clean up only the fixture's unconstructed bundle; this verifier is not used by the implementation.
        registry.BeginReturn(session); var proof = registry.VerifyConsumersEnded(session, new EmptyFixtureVerifier())!;
        Check(registry.Close(session, proof)); Check(bundle.TryCompleteDestroy());
    }

    private static void SuccessfulCancellation()
    {
        using var fixture = new Fixture(); var owner = fixture.Start();
        Equal("globals-baseline,awake-window,instantiate,registry-root,inject", string.Join(",", fixture.Backend.Events));
        Check(!((ILocalRunConsumersEndedVerifier)owner).Verify(fixture.Request(), out _));
        owner.RequestRetirement(); owner.Tick(); Equal(0, fixture.Backend.DestroyRequests);
        fixture.Backend.CurrentFrame++; owner.Tick(); Equal(1, fixture.Backend.DestroyRequests);
        fixture.Backend.SetAllDead(); owner.Tick(); Equal(0, fixture.Backend.DestroyPolls);
        fixture.Backend.CurrentFrame++; owner.Tick(); Equal(1, fixture.Backend.DestroyPolls);
        Check(fixture.Backend.ControllerClosed); Check(fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
        Check(!fixture.Backend.Frees.Contains("registry") && !fixture.Backend.Frees.Contains("observation"));
        owner.Tick(); Equal(1, fixture.Issuances); Equal(1, fixture.Bundle.DestroyRequests);
        Check(fixture.Backend.Frees.Contains("registry") && fixture.Backend.Frees.Contains("observation"));
        Check(!fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
        Equal(LocalRunSessionPhase.Closed, fixture.Registry.Read(fixture.Session).Phase);
        owner.Tick(); Equal(0, fixture.Backend.BundlePolls);
        fixture.Backend.CurrentFrame++; owner.Tick(); Check(!owner.HasPendingWork);
        Equal(NativeNeverActivatedRetirementPhase.Closed, owner.Read().Phase);
        Check(fixture.Registry.Read(fixture.Session).ConsumerEndEvidence!.Contains("Awake=0"));
    }

    private static void FiveDeadAndFrame()
    {
        for (var index = 0; index < 5; index++)
        {
            using var fixture = new Fixture(); var owner = fixture.Start(); owner.RequestRetirement();
            fixture.Backend.CurrentFrame++; owner.Tick();
            fixture.Backend.SetAllDead(); fixture.Backend.Dead[index] = false;
            fixture.Backend.CurrentFrame++; owner.Tick();
            Equal(NativeNeverActivatedRetirementPhase.DestroyRequested, owner.Read().Phase);
            Check(fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
            Equal(0, fixture.Backend.Frees.Count); Equal(0, fixture.Issuances);
            fixture.Backend.Dead[index] = true; fixture.Finish();
            Equal(5, fixture.Backend.Frees.Count(value => value.StartsWith("owned-")));
        }
    }

    private static void KnownInjectionRejection()
    {
        using var fixture = new Fixture(); fixture.Backend.RejectBeforeRetention = true;
        var owner = fixture.Start(); Equal(NativeNeverActivatedRetirementPhase.RetirementRequested, owner.Read().Phase);
        Equal(0L, fixture.Backend.CurrentParameters); Equal(1, fixture.Backend.InjectCalls);
        fixture.Finish(); Check(!owner.HasPendingWork); Equal(1, fixture.Backend.InjectCalls);
    }

    private static void CallbackPending()
    {
        using var fixture = new Fixture(); var owner = fixture.Start(); fixture.ReachConfirmed();
        fixture.ActivityKnown = false; owner.Tick(); Equal(0, fixture.Issuances);
        fixture.ActivityKnown = true; fixture.InFlight = 1; owner.Tick(); Equal(0, fixture.Issuances);
        Check(fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
        Check(!fixture.Backend.Frees.Contains("registry") && !fixture.Backend.Frees.Contains("observation"));
        fixture.InFlight = 0; fixture.Finish(); Check(!owner.HasPendingWork); Equal(1, fixture.Issuances);
    }

    private static void ExactRequest()
    {
        using var fixture = new Fixture(); fixture.BeforeExactVerification = verifier => {
            var request = fixture.Request();
            foreach (var wrong in new[] {
                request with { Identity = request.Identity with { RunGeneration = request.Identity.RunGeneration + 1 } },
                request with { ControllerIdentity = request.ControllerIdentity + 1 },
                request with { ResultSceneIdentity = 55 }, request with { ResultSceneGeneration = 1 },
                request with { ResultIdentity = 66 }
            }) Check(!verifier.Verify(wrong, out _));
        };
        fixture.Start(); fixture.Finish(); Equal(1, fixture.Issuances);
    }

    private static void ThreadAndReentry()
    {
        using var fixture = new Fixture(); var owner = fixture.Start(); var reads = fixture.Backend.Reads;
        Task.Run(() => {
            Throws<InvalidOperationException>(owner.RequestRetirement); Throws<InvalidOperationException>(owner.Tick);
        }).GetAwaiter().GetResult();
        Equal(reads, fixture.Backend.Reads);
        fixture.Backend.OnGlobals = () => { Throws<InvalidOperationException>(owner.Tick); Throws<InvalidOperationException>(owner.RequestRetirement); };
        owner.RequestRetirement(); fixture.Backend.OnGlobals = null; fixture.Finish(); Check(!owner.HasPendingWork);
    }

    private static void Abandoned()
    {
        using var fixture = new Fixture(); var weak = Abandon(fixture);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Check(weak.TryGetTarget(out var owner));
        fixture.Owner = owner; Check(owner!.HasPendingWork); fixture.Finish(); Check(!owner.HasPendingWork);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NativeNeverActivatedRetirement> Abandon(Fixture fixture)
    { var owner = fixture.Start(); owner.RequestRetirement(); fixture.Owner = null; return new(owner); }

    private static void HostWithdrawal()
    {
        using var main = new MainThreadDispatcher(); var transport = new IdleHostTransport();
        var host = LocalRunProtectionHost.Create(main, new SilentLogger(), transport); host.InstallHandler(new IdleHandler());
        var bundle = new BundleLease(); var registry = host.Sessions;
        var session = host.RegisterPrepared(new(new("retirement.test", "host-withdrawal"), 1, "chart"), 39_000_000, bundle);
        var claim = registry.BeginCommit(session); var backend = new Backend(main, bundle, 39_000_000);
        var owner = NativeNeverActivatedRetirement.StartCore(main, registry, claim, backend, host.TryRetireNeverActivated);
        Equal(1, NativeNeverActivatedRetirement.ReadWorkForHost(host).Pending);
        Check(!host.RequestStop());
        backend.CurrentFrame++; LocalRunProtectionHost.TickProcessOwners();
        Equal(NativeNeverActivatedRetirementPhase.RetirementRequested, owner.Read().Phase);
        backend.CurrentFrame++; LocalRunProtectionHost.TickProcessOwners();
        backend.SetAllDead(); backend.CurrentFrame++; LocalRunProtectionHost.TickProcessOwners();
        LocalRunProtectionHost.TickProcessOwners();
        Equal(LocalRunSessionPhase.Closed, registry.Read(session).Phase);
        var afterClose = host.Read();
        Check(!afterClose.Stopped && afterClose.HandlerWorkPending); Equal(5, afterClose.RetainedBindings);
        Equal(NativeNeverActivatedRetirementPhase.BundleDestroyPending, owner.Read().Phase);
        Equal(0, backend.BundlePolls); Equal(0, transport.Detaches);
        backend.CurrentFrame++; LocalRunProtectionHost.TickProcessOwners();
        Check(host.Read().Stopped && !owner.HasPendingWork); Equal(5, transport.Detaches);
        Equal(0, NativeNeverActivatedRetirement.ReadWorkForHost(host).Pending);
    }

    private static void InactiveViolations()
    {
        foreach (var foreignParameters in new[] { false, true })
        {
            using var fixture = new Fixture(); var owner = fixture.Start();
            if (foreignParameters) fixture.Backend.CurrentParameters++;
            else fixture.Backend.LevelInformation = 99;
            owner.RequestRetirement(); Check(owner.CleanupUnknown); Equal(0, fixture.Backend.DestroyRequests);
            Check(fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
        }
    }

    private static void AwakeViolations()
    {
        for (var index = 0; index < 3; index++)
        {
            using var fixture = new Fixture(); var owner = fixture.Start();
            fixture.Backend.Awake = index switch {
                0 => fixture.Backend.Awake with { OriginalCalls = 1 },
                1 => fixture.Backend.Awake with { InFlight = 1 },
                _ => fixture.Backend.Awake with { Failures = new[] { "Incomplete capture" } }
            };
            owner.RequestRetirement(); Check(owner.CleanupUnknown);
            fixture.Backend.Awake = fixture.Backend.ZeroAwake(); owner.RequestRetirement(); owner.Tick();
            Equal(0, fixture.Backend.DestroyRequests); Equal(0, fixture.Issuances);
        }
    }

    private static void GlobalsViolations()
    {
        foreach (var ownerChanged in new[] { false, true })
        {
            using var fixture = new Fixture(); var owner = fixture.Start();
            fixture.Backend.Globals = ownerChanged ? fixture.Backend.Globals with { OwnerIdentity = 123, IsCurrentOwner = false }
                : fixture.Backend.Globals with { DelegateIdentities = new long[] { 1, 2, 3, 4, 5, 99 } };
            owner.RequestRetirement(); Check(owner.CleanupUnknown);
            fixture.Backend.Globals = fixture.Backend.BaselineGlobals; owner.Tick();
            Equal(0, fixture.Backend.DestroyRequests); Equal(0, fixture.Issuances);
        }
    }

    private static void CommittedSession()
    {
        using var fixture = new Fixture(); var owner = fixture.Start(); fixture.Registry.Commit(fixture.Claim);
        owner.RequestRetirement(); Check(owner.CleanupUnknown); Equal(0, fixture.Backend.DestroyRequests);
    }
    private static void FakeClosed()
    {
        using var fixture = new Fixture(); fixture.FakeClosed = true; var owner = fixture.Start(); fixture.ReachConfirmed(); owner.Tick();
        Check(owner.CleanupUnknown); Check(fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
        Check(!fixture.Backend.Frees.Contains("registry") && !fixture.Backend.Frees.Contains("observation"));
    }
    private static void UnknownAllocation()
    {
        using var fixture = new Fixture(); fixture.Backend.FailAllocation = true; var owner = fixture.Start();
        Check(owner.CleanupUnknown); Equal(0L, fixture.Registry.Read(fixture.Session).ControllerIdentity);
        owner.RequestRetirement(); owner.Tick(); Equal(0, fixture.Issuances); Equal(0, fixture.Backend.DestroyRequests);
    }
    private static void UnknownSetter()
    {
        using var fixture = new Fixture(); fixture.Backend.ThrowAfterMutation = true; var owner = fixture.Start();
        Check(owner.CleanupUnknown); Check(fixture.Backend.CurrentParameters != 0);
        owner.RequestRetirement(); owner.Tick(); Equal(1, fixture.Backend.InjectCalls); Equal(0, fixture.Backend.DestroyRequests);
        Equal(0, fixture.Backend.Frees.Count); Equal(0, fixture.Issuances);
    }
    private static void UnknownDestroyAndFree()
    {
        foreach (var free in new[] { false, true })
        {
            using var fixture = new Fixture(); var owner = fixture.Start(); fixture.Backend.ThrowDestroy = !free;
            fixture.Backend.FailOwnedFree = free; owner.RequestRetirement(); fixture.Backend.CurrentFrame++; owner.Tick();
            fixture.Backend.SetAllDead(); fixture.Backend.CurrentFrame++; owner.Tick(); Check(owner.CleanupUnknown);
            var attempts = fixture.Backend.DestroyRequests; var frees = fixture.Backend.Frees.Count;
            fixture.Backend.ThrowDestroy = false; fixture.Backend.FailOwnedFree = false;
            owner.RequestRetirement(); owner.Tick(); Equal(attempts, fixture.Backend.DestroyRequests); Equal(frees, fixture.Backend.Frees.Count);
            Check(fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
            Check(!fixture.Backend.Frees.Contains("registry")); Equal(0, fixture.Issuances);
        }
    }
    private static void UnknownObservationFree()
    {
        using var fixture = new Fixture(); fixture.Backend.FailObservationFree = true; var owner = fixture.Start();
        fixture.ReachConfirmed(); owner.Tick(); Check(owner.CleanupUnknown);
        Equal(LocalRunSessionPhase.Closed, fixture.Registry.Read(fixture.Session).Phase);
        Equal(1, fixture.Backend.ReleaseCalls); var attempts = fixture.Backend.Frees.Count;
        fixture.Backend.FailObservationFree = false; owner.Tick(); Equal(1, fixture.Backend.ReleaseCalls); Equal(attempts, fixture.Backend.Frees.Count);
    }
    private static void UnknownFrame()
    {
        using var fixture = new Fixture(); var owner = fixture.Start(); fixture.Backend.ThrowFrame = true;
        owner.RequestRetirement(); Check(owner.CleanupUnknown); var reads = fixture.Backend.Reads;
        fixture.Backend.ThrowFrame = false; owner.RequestRetirement(); owner.Tick();
        Equal(reads, fixture.Backend.Reads); Equal(0, fixture.Backend.DestroyRequests); Equal(0, fixture.Issuances);
        Check(fixture.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, fixture.Backend.ControllerIdentity));
    }

    private sealed class Fixture : IDisposable
    {
        private static long _nextIdentity = 30_000_000;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunSessionRegistry.CommitClaim Claim;
        internal readonly BundleLease Bundle = new();
        internal readonly Backend Backend;
        internal NativeNeverActivatedRetirement? Owner;
        internal int Issuances, InFlight;
        internal bool ActivityKnown = true, FakeClosed;
        internal Action<ILocalRunConsumersEndedVerifier>? BeforeExactVerification;
        internal Fixture()
        {
            var identity = Interlocked.Add(ref _nextIdentity, 32);
            Registry = new(Main); Session = Registry.RegisterPrepared(new(new("retirement.test", "song"), 1, "chart"), identity, Bundle);
            Claim = Registry.BeginCommit(Session); Backend = new(Main, Bundle, identity);
        }
        internal NativeNeverActivatedRetirement Start()
            => Owner = NativeNeverActivatedRetirement.StartCore(Main, Registry, Claim, Backend, Issue);
        private bool Issue(LocalRunSessionRegistry.SessionHandle session, ILocalRunConsumersEndedVerifier verifier, out bool closed)
        {
            closed = false;
            if (!ActivityKnown || InFlight != 0) return false;
            if (FakeClosed) { closed = true; return true; }
            BeforeExactVerification?.Invoke(verifier);
            var token = Registry.VerifyConsumersEnded(session, verifier);
            if (token is null) return false;
            Issuances++; closed = Registry.Close(session, token); return true;
        }
        internal LocalRunRetirementRequest Request()
        { var value = Registry.Read(Session); return new(value.Identity, value.ControllerIdentity, 0, 0, 0); }
        internal void ReachConfirmed()
        {
            Owner!.RequestRetirement(); Backend.CurrentFrame++; Owner.Tick(); Backend.SetAllDead(); Backend.CurrentFrame++; Owner.Tick();
            Equal(NativeNeverActivatedRetirementPhase.DestructionConfirmed, Owner.Read().Phase);
        }
        internal void Finish()
        {
            Owner!.RequestRetirement(); Backend.SetAllDead();
            for (var index = 0; index < 8 && Owner.HasPendingWork && !Owner.CleanupUnknown; index++) { Backend.CurrentFrame++; Owner.Tick(); }
            Check(!Owner.HasPendingWork);
        }
        public void Dispose() => Main.Dispose();
    }

    private sealed class BundleLease : IDisposable
    { internal int DestroyRequests; public void Dispose() => DestroyRequests++; }
    private sealed class EmptyFixtureVerifier : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Unconstructed native test fixture has no consumers; not production evidence."; return true; } }
    private sealed class IdleHandler : ILocalRunBoundaryHandler
    {
        public void ValidateHost(LocalRunSessionRegistry registry) { }
        public bool HasPendingWork => false;
        public void Handle(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver) { }
        public void Tick() { }
    }
    private sealed class SilentLogger : ILogger
    { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private sealed class IdleHostTransport : ILocalRunBoundaryTransport
    {
        internal int Detaches;
        public ILocalRunBoundaryBinding Bind(LocalBoundaryKind boundary, Func<long, bool> classify, Action<long> handle) => new Slot(this, boundary);
        private sealed class Slot : ILocalRunBoundaryBinding
        {
            private readonly IdleHostTransport _owner; private readonly LocalBoundaryKind _boundary;
            internal Slot(IdleHostTransport owner, LocalBoundaryKind boundary) => (_owner, _boundary) = (owner, boundary);
            public LocalRunBoundaryCounters Read() => new(_boundary, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            public LocalRunBoundaryActivity ReadActivity() => new(0, true);
            public void Dispose() => _owner.Detaches++;
        }
    }

    private sealed class Backend : INativeNeverActivatedRetirementBackend
    {
        private readonly MainThreadDispatcher _main;
        private readonly BundleLease _bundle;
        private readonly InactiveControllerRootRecords _roots;
        private readonly OwnedNativeInactiveController _clone;
        private NativeRuntime.RootHandle? _observation;
        internal readonly List<string> Events = new(), Frees = new();
        internal readonly bool[] Dead = new bool[5];
        internal int CurrentFrame = 10, DestroyRequests, DestroyPolls, BundlePolls, ReleaseCalls, Reads, InjectCalls;
        internal bool RejectBeforeRetention, ThrowAfterMutation, ThrowDestroy, FailOwnedFree, FailObservationFree, FailAllocation, ThrowFrame;
        internal long CurrentParameters, LevelInformation;
        internal ControllerAwakeLedgerSnapshot Awake;
        internal ControllerGlobalsSnapshot Globals;
        internal Action? OnGlobals;
        private readonly long _controller, _parameters;
        private bool _unknown;
        internal Backend(MainThreadDispatcher main, BundleLease bundle, long metadata)
        {
            (_main, _bundle, _controller, _parameters) = (main, bundle, metadata + 1, metadata + 2);
            _roots = new(main);
            _clone = new(main, _roots, () => { Reads++; return new(_controller, _controller + 2, _controller + 3, false, true, false, true, CurrentParameters, LevelInformation); },
                () => { DestroyRequests++; if (ThrowDestroy) throw new IOException("Unknown Destroy result."); },
                () => { DestroyPolls++; return Dead.All(value => value); }, () => Root(90, "independent"));
            Globals = BaselineGlobals; Awake = ZeroAwake();
        }
        public IDisposable BundleResponsibility => _bundle;
        public bool ControllerBound { get; private set; }
        public bool ControllerClosed => _clone.IsClosed;
        public long ControllerIdentity => _controller;
        public long ParametersIdentity => _parameters;
        public bool CleanupUnknown => _unknown || _clone.CleanupUnknown;
        public bool InjectionFailureCanRetire => RejectBeforeRetention && !_unknown;
        public ControllerGlobalsSnapshot BaselineGlobals { get; } = new(500, true, false, new long[] { 1, 2, 3, 4, 5, 6 });
        public int Frame() { Reads++; if (ThrowFrame) throw new IOException("Unknown native frame or boxed-result free."); return CurrentFrame; }
        internal ControllerAwakeLedgerSnapshot ZeroAwake() => new("before-instantiate", _controller, true, true, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            Array.Empty<ControllerAwakeLedgerRow>(), Array.Empty<string>());
        public void CreateClosedTransaction(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
        {
            Events.Add("globals-baseline"); Events.Add("awake-window"); Events.Add("instantiate");
            if (FailAllocation) { _unknown = true; throw new IOException("Native allocation publication is unknown."); }
            for (uint index = 1; index <= 5; index++) { var saved = index; _roots.Allocate(() => Root(saved, "owned-" + saved)); }
            _clone.MarkReady(); _observation = Root(70, "observation");
            var registryRoot = Root(71, "registry"); registry.BindPendingController(claim, registryRoot.Target.ToInt64(), registryRoot);
            Events.Add("registry-root"); ControllerBound = true;
        }
        public void Inject(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
        {
            Events.Add("inject"); InjectCalls++;
            if (RejectBeforeRetention) throw new UnsupportedSignatureException("Known rejection before any retention or setter.");
            CurrentParameters = _parameters;
            if (ThrowAfterMutation) { _unknown = true; throw new IOException("Setter mutation output is unknown."); }
        }
        private NativeRuntime.RootHandle Root(uint handle, string label)
            => new(handle, _ => { Frees.Add(label); if (FailOwnedFree && label == "owned-3" || FailObservationFree && label == "observation") throw new IOException("Unknown native free."); },
                _ => new IntPtr(_controller), _main.VerifyAccess);
        public NativeInactiveControllerObservation ReadInactive() => _clone.Read();
        public ControllerAwakeLedgerSnapshot ReadAwake() { Reads++; return Awake; }
        public ControllerGlobalsSnapshot ReadGlobals() { Reads++; OnGlobals?.Invoke(); return Globals; }
        public void VerifyRetainedIdentitiesAndSource() { Reads++; Equal(_controller, _observation!.Target.ToInt64()); }
        public void RequestControllerDestroy() => _clone.RequestDestroy();
        public bool TryCompleteControllerDestroy() => _clone.TryCompleteDestroy();
        public void ReleaseAfterSessionClosed()
        {
            ReleaseCalls++;
            try { _observation!.Dispose(); }
            catch { _unknown = true; throw; }
        }
        public bool TryCompleteBundleDestroy() { BundlePolls++; Check(_bundle.DestroyRequests == 1); return true; }
        internal void SetAllDead() => Array.Fill(Dead, true);
    }
}
