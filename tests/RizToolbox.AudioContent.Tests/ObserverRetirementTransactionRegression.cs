using System.Reflection;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

internal static class ObserverRetirementTransactionRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Observer retirement withdraws all memberships before actual drain and independent root frees", Sequence),
        ("Observer withdrawal with physical work pending retains every root until a later real drain", PendingDrain),
        ("Observer retirement marks unknown root-free outcome and never retries it", UnknownFree),
        ("Observer retirement rechecks closure and physical activity after each root free", LostDuringFree),
        ("Observer completion receipt rejects copied issuer, foreign owner and different transaction context", ExactReceipt)
    };
    private static void Sequence()
    {
        using var f = new Fixture(); var owner = new Owner(f); var backend = new Backend(f.Main);
        var receipt = owner.Run(backend)!;
        Equal("binding0,binding1,root2,root1", string.Join(",", backend.Events));
        Equal(2, receipt.WithdrawnBindings); Equal(2, receipt.ReleasedRoots); Check(owner.Completed && !owner.Unknown);
        Check(backend.ActivityReads >= 10); receipt.ValidateFor(f.Context, owner);
        Check(ReferenceEquals(receipt, owner.Run(backend))); Equal(4, backend.Events.Count);
    }
    private static void PendingDrain()
    {
        using var f = new Fixture(); var owner = new Owner(f); var backend = new Backend(f.Main) { InFlight = 1 };
        Check(owner.Run(backend) is null); Equal("binding0,binding1", string.Join(",", backend.Events));
        Equal(2, backend.RetirementRootCount); Check(!owner.Completed && !owner.Unknown);
        backend.InFlight = 0; Check(owner.Run(backend) is not null);
        Equal("binding0,binding1,root2,root1", string.Join(",", backend.Events));
    }
    private static void UnknownFree()
    {
        using var f = new Fixture(); var owner = new Owner(f); var backend = new Backend(f.Main) { FailRoot = 2 };
        Throws<IOException>(() => owner.Run(backend)); Check(owner.Unknown && !owner.Completed);
        Equal("binding0,binding1,root2", string.Join(",", backend.Events)); Equal(2, backend.RetirementRootCount);
        Throws<InvalidOperationException>(() => owner.Run(backend)); Equal(3, backend.Events.Count);
    }
    private static void LostDuringFree()
    {
        using var f = new Fixture(); var owner = new Owner(f); var backend = new Backend(f.Main);
        backend.DuringFree = () => backend.InFlight = 1;
        Throws<InvalidOperationException>(() => owner.Run(backend)); Check(owner.Unknown && !owner.Completed);
        Equal("binding0,binding1,root2", string.Join(",", backend.Events)); Equal(1, backend.RetirementRootCount);
    }
    private static void ExactReceipt()
    {
        using var f = new Fixture(); using var other = new Fixture(); var owner = new Owner(f); var backend = new Backend(f.Main);
        var receipt = owner.Run(backend)!;
        Throws<InvalidOperationException>(() => receipt.ValidateFor(other.Context, owner));
        Throws<InvalidOperationException>(() => receipt.ValidateFor(f.Context, new Owner(f)));
        Throws<InvalidOperationException>(() => new NativeLocalObserverRetirementReceipt(owner.Transaction!, new object(), f.Context.Session, 2, 2));
        var copied = new NativeLocalObserverRetirementTransaction(f.Main, owner, f.Context);
        Throws<InvalidOperationException>(() => copied.TryComplete(f.Context, new Backend(f.Main), () => { }, () => { }, _ => { }));
        Check(owner.Completed); Equal(4, backend.Events.Count);
    }
    private sealed class Owner : INativeLocalObserverRetirementOwner
    {
        private readonly Fixture _fixture;
        internal NativeLocalObserverRetirementTransaction? Transaction;
        internal bool Completed, Unknown;
        internal Owner(Fixture fixture) => _fixture = fixture;
        internal NativeLocalObserverRetirementReceipt? Run(Backend backend)
        {
            Transaction ??= new(_fixture.Main, this, _fixture.Context);
            return Transaction.TryComplete(_fixture.Context, backend, () => { if (Unknown) throw new InvalidOperationException(); },
                () => Completed = true, _ => Unknown = true);
        }
        public void RequireRetirementTransaction(NativeLocalObserverRetirementTransaction transaction, NativeLocalActiveObserverRetirementContext context)
        {
            _fixture.Main.VerifyAccess();
            if (!ReferenceEquals(Transaction, transaction) || !ReferenceEquals(context, _fixture.Context) || context.Production || Unknown)
                throw new InvalidOperationException("Controlled receipt must belong to this exact private transaction.");
        }
    }
    private sealed class Backend : INativeLocalObserverRetirementBackend
    {
        private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[2];
        private readonly bool[] _freeAttempted = new bool[2];
        private readonly IDisposable[] _bindings;
        internal readonly List<string> Events = new();
        internal long InFlight;
        internal int ActivityReads, FailRoot;
        internal Action? DuringFree;
        internal Backend(MainThreadDispatcher main)
        {
            _bindings = new IDisposable[] { new Binding(() => Events.Add("binding0")), new Binding(() => Events.Add("binding1")) };
            for (uint index = 1; index <= 2; ++index)
            {
                var id = index;
                _roots[index - 1] = new(id, value => { Events.Add("root" + value); DuringFree?.Invoke();
                    if (value == FailRoot) throw new IOException("Controlled free is unknown."); },
                    _ => throw new Exception("Root targets are never read during frozen observer retirement."), main.VerifyAccess);
            }
        }
        public IDisposable[] RetirementBindings() => _bindings;
        public ExactHookBroker.ManagedBoundaryActivity ReadRetirementActivity() { ++ActivityReads; return new(InFlight, true); }
        public int RetirementRootSlots => _roots.Length;
        public int RetirementRootCount => _roots.Count(root => root is not null);
        public bool HasRetirementRoot(int slot) => _roots[slot] is not null;
        public void FreeRetirementRootOnce(int slot)
        {
            if (_freeAttempted[slot]) throw new InvalidOperationException("Controlled unknown free cannot retry.");
            _freeAttempted[slot] = true; _roots[slot]!.Dispose(); _roots[slot] = null;
        }
        private sealed class Binding : IDisposable
        {
            private readonly Action _withdraw; private bool _attempted;
            internal Binding(Action withdraw) => _withdraw = withdraw;
            public void Dispose() { if (_attempted) throw new Exception("Membership withdrawal cannot repeat."); _attempted = true; _withdraw(); }
        }
    }

    // Establish the real controlled factory/source/registry protocol. The
    // distinct controlled context cannot pass a production owner's type gate.
    private sealed class Fixture : IDisposable
    {
        private static int _next;
        private readonly long _base = 0x5d00_0000_0000L + Interlocked.Increment(ref _next) * 10000L;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly NativeLocalActiveObserverRetirementContext Context;
        internal Fixture()
        {
            var registry = new LocalRunSessionRegistry(Main); var bundle = new Lease();
            var session = registry.RegisterPrepared(new(new("observer.retirement", "song"), 1, "chart"), _base + 1000, bundle);
            var claim = registry.BeginCommit(session); var launch = new LocalRunLaunchCommitState(Main, registry, claim, bundle);
            var roots = new InactiveControllerRootRecords(Main);
            for (uint index = 1; index <= 8; ++index)
            {
                var id = index; roots.Allocate(() => new NativeRuntime.RootHandle(id, _ => { }, _ => new(_base + 100 + id), Main.VerifyAccess));
            }
            var factory = new OwnedNativeInactiveController(Main, roots,
                () => new(_base + 106, _base + 107, _base + 104, false, true, false, true, 0, 0),
                () => { }, () => true, () => throw new Exception("No extra factory root is needed."));
            factory.MarkReady(); registry.BindPendingController(claim, _base + 106, new Lease()); launch.NoteControllerBound(_base + 106);
            var sourceBackend = new SourceBackend(new(_base + 106, _base + 107, _base + 108, _base + 104, _base + 105));
            var sourceAuthority = new SourceAuthority(sourceBackend, launch.Identity);
            var source = NativeLocalActiveDestructionTracker.CaptureActiveFactoryBackend(Main, sourceBackend, sourceAuthority, launch, factory);
            var activation = new NativeInactiveControllerActivation(Main, new ActivationBackend(), new ActivationAuthority(launch.Identity, _base + 106));
            typeof(NativeInactiveControllerActivation).GetField("_attempted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(activation, true);
            typeof(OwnedNativeInactiveController).GetField("_activation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(factory, activation);
            sourceAuthority.Activated = true; source.Tick(); ++sourceBackend.FrameValue;
            sourceBackend.Live = sourceBackend.Live with { ControllerAlive = false, CloneGameObjectAlive = false, CloneTransformAlive = false,
                StagingGameObjectAlive = false, StagingTransformAlive = false };
            Check(source.Tick()); var death = source.FreezeForActiveFactoryRetirement(launch, factory)!;
            registry.BeginReturn(session); Check(registry.Close(session, registry.VerifyConsumersEnded(session, new Ended())!));
            Check(factory.TryCompleteActiveDestroyBackend(launch, death, () => { }));
            Context = NativeLocalActiveObserverRetirementContext.ForControlled(launch, factory, death, () => { });
            Check(source.TryReleaseFrozenObservationRootsAfterCloseBackend(launch, factory, death, () => { }));
        }
        public void Dispose() => Main.Dispose();
    }
    private sealed class SourceBackend : IActiveDestructionBackend
    {
        internal ActiveDestructionLiveness Live;
        internal int FrameValue = 10;
        private int _roots = 5;
        internal SourceBackend(ActiveDestructionIdentity identity) => Live = new(identity, true, true, true, true, true);
        public bool CleanupUnknown => false;
        public int RootRecords => _roots;
        public void Initialize() { }
        public int Frame() => FrameValue;
        public ActiveDestructionLiveness ReadLiveness() => Live;
        public void VerifyStagingCanDestroy() => throw new NotSupportedException();
        public void DestroyStagingOnce() => throw new NotSupportedException();
        public void ReleaseObservationRoots(Func<bool> mayFreeNext)
        { while (_roots != 0) { Check(mayFreeNext()); --_roots; Check(mayFreeNext()); } }
    }
    private sealed class SourceAuthority : IActiveDestructionAuthority
    {
        private readonly SourceBackend _backend;
        internal bool Activated;
        public LocalRunSessionIdentity Identity { get; }
        public long Controller => _backend.Live.Identity.Controller;
        public bool Unknown { get; private set; }
        internal SourceAuthority(SourceBackend backend, LocalRunSessionIdentity identity) => (_backend, Identity) = (backend, identity);
        public void VerifyBeforeCapture() { if (Activated || Unknown) throw new InvalidOperationException(); }
        public void VerifyObservation() { if (!Activated || Unknown) throw new InvalidOperationException(); }
        public ActiveDestructionReadiness ReadReadiness() => new(!Unknown, true, Activated, _backend.FrameValue);
        public void MarkUnknown(string failure) => Unknown = true;
    }
    private sealed class Ended : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled actual factory/registry close only."; return true; } }
    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class ActivationBackend : IInactiveControllerActivationBackend
    {
        public bool CleanupUnknown => false;
        public int RetainedCallArgumentRecords => 0;
        public void RequireSupportedBuild() => throw new NotSupportedException();
        public NativeInactiveControllerActivationGraph ReadGraph() => throw new NotSupportedException();
        public ControllerAwakeLedgerSnapshot ReadAwake(NativeControllerAwakeLedger.CaptureWindow window) => throw new NotSupportedException();
        public void CaptureInstalledGlobals(OwnedNativeControllerGlobals globals) => throw new NotSupportedException();
        public void DisableClone() => throw new NotSupportedException(); public void DetachClone() => throw new NotSupportedException(); public void ActivateClone() => throw new NotSupportedException();
    }
    private sealed class ActivationAuthority : IInactiveControllerActivationAuthority
    {
        public LocalRunSessionIdentity Identity { get; }
        public long Controller { get; }
        public long Information => 0; public bool CleanupUnknown => false; public bool InstalledGlobalsCaptured => false;
        internal ActivationAuthority(LocalRunSessionIdentity identity, long controller) => (Identity, Controller) = (identity, controller);
        public void VerifyPending() => throw new NotSupportedException();
        public NativeInactiveControllerParameterBindingObservation ReadParameters() => throw new NotSupportedException();
        public void NoteActivationAttempt() => throw new NotSupportedException();
        public ControllerAwakeLedgerSnapshot ReadAwake() => throw new NotSupportedException();
        public ControllerGlobalsSnapshot BaselineGlobals() => throw new NotSupportedException(); public ControllerGlobalsSnapshot InstalledGlobals() => throw new NotSupportedException();
        public ControllerGlobalsSnapshot ObserveGlobals() => throw new NotSupportedException(); public void CaptureInstalledGlobals() => throw new NotSupportedException();
        public void MarkUnknown(string failure) => throw new NotSupportedException();
    }
}
