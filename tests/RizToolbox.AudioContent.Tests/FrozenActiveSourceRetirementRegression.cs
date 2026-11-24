using System.Reflection;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

// Real tracker/factory/registry transactions over controlled independent native
// handles. No production source, Unity timing or game cancellation is claimed.
internal static class FrozenActiveSourceRetirementRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Frozen source keeps five independent roots until exact registry and original factory retirement", RequiresAllOwners),
        ("Frozen source frees each independent root once without native liveness or root-target reads", SinglePass),
        ("Frozen source root-free exception retains Unknown and never retries the attempt", FailedFree),
        ("Frozen source rechecks exact authority after each independent free", LostAuthority),
        ("Frozen source cannot consume another launch or factory death seal", ForeignSeal),
        ("Frozen source rejects wrong-thread release before any free", WrongThread),
        ("A caller-created engine source seal cannot manufacture a private issuance interval", ForgedSourceSeal)
    };

    private static void RequiresAllOwners()
    {
        using var f = new Fixture(); var seal = f.Freeze();
        Throws<InvalidOperationException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, () => { }));
        Equal(0, f.Backend.Frees.Count); Equal(5, f.Source.Read().Roots);
        f.CloseRegistry();
        Throws<InvalidOperationException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, () => { }));
        Equal(0, f.Backend.Frees.Count);
        Check(f.Factory.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
        Check(f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, () => { }));
        Check(f.Source.FrozenObservationRootsReleased); Equal(0, f.Source.Read().Roots);
    }
    private static void SinglePass()
    {
        using var f = new Fixture(); var seal = f.CloseFactory(); var checks = 0;
        var reads = f.Backend.NativeReads; var targets = f.TargetReads;
        Check(f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, () => ++checks));
        Equal("5,4,3,2,1", string.Join(",", f.Backend.Frees)); Check(checks >= 12);
        Equal(reads, f.Backend.NativeReads); Equal(targets, f.TargetReads);
        seal.ValidateFor(f.Launch, f.Factory);
        Check(f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal,
            () => throw new Exception("Completed release cannot repeat its authority interval.")));
        Equal(5, f.Backend.Frees.Count); Check(!f.Source.Unknown && f.Source.Read().Closed);
    }
    private static void FailedFree()
    {
        using var f = new Fixture(); var seal = f.CloseFactory(); f.Backend.FailFree = 4;
        Throws<NativeLocalActiveDestructionException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, () => { }));
        Equal("5,4", string.Join(",", f.Backend.Frees)); Equal(4, f.Source.Read().Roots);
        Check(f.Source.Unknown && !f.Source.FrozenObservationRootsReleased);
        Throws<InvalidOperationException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, () => { }));
        Equal(2, f.Backend.Frees.Count); f.PreserveUnknown = true;
    }
    private static void LostAuthority()
    {
        using var f = new Fixture(); var seal = f.CloseFactory(); var known = true;
        f.Backend.DuringFree = _ => known = false;
        void Verify() { if (!known) throw new InvalidOperationException("Exact external generation changed during root free."); }
        Throws<NativeLocalActiveDestructionException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, Verify));
        Equal("5", string.Join(",", f.Backend.Frees)); Equal(4, f.Source.Read().Roots);
        Check(f.Source.Unknown); f.PreserveUnknown = true;
    }
    private static void ForeignSeal()
    {
        using var f = new Fixture(); using var other = new Fixture(); var seal = f.CloseFactory(); other.CloseFactory();
        Throws<InvalidOperationException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(other.Launch, f.Factory, seal, () => { }));
        Throws<InvalidOperationException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, other.Factory, seal, () => { }));
        Equal(0, f.Backend.Frees.Count); Equal(0, other.Backend.Frees.Count);
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var seal = f.CloseFactory();
        Task.Run(() => Throws<InvalidOperationException>(() => f.Source.TryReleaseFrozenObservationRootsAfterCloseBackend(f.Launch, f.Factory, seal, () => { })))
            .GetAwaiter().GetResult();
        Equal(0, f.Backend.Frees.Count);
    }
    private static void ForgedSourceSeal()
    {
        using var f = new Fixture(); var death = f.Freeze();
        var engine = (NativeEngineCoroutineRuntimeCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(NativeEngineCoroutineRuntimeCoordinator));
        Throws<InvalidOperationException>(() => new NativeLocalActiveSourceClosureSeal(engine, new object(), f.Launch, f.Factory, death));
        Equal(0, f.Backend.Frees.Count); Equal(5, f.Source.Read().Roots);
    }

    private sealed class Fixture : IDisposable
    {
        private static int _next;
        internal readonly long Base = 0x5c00_0000_0000L + Interlocked.Increment(ref _next) * 10000L;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunLaunchCommitState Launch;
        internal readonly OwnedNativeInactiveController Factory;
        internal readonly Backend Backend;
        internal readonly Authority Authority;
        internal readonly NativeLocalActiveDestructionTracker Source;
        internal int TargetReads;
        internal bool PreserveUnknown;
        private bool _registryClosed;
        private NativeLocalActiveDestructionTracker.FactoryRetirementSeal? _seal;
        internal Fixture()
        {
            Registry = new(Main); var bundle = new Lease();
            Session = Registry.RegisterPrepared(new(new("frozen.source", "song"), 1, "chart"), Base + 1000, bundle);
            var claim = Registry.BeginCommit(Session); Launch = new(Main, Registry, claim, bundle);
            var roots = new InactiveControllerRootRecords(Main);
            for (uint index = 1; index <= 8; ++index)
            {
                var id = index;
                roots.Allocate(() => new NativeRuntime.RootHandle(id, _ => { }, _ => { ++TargetReads; return new(Base + 100 + id); }, Main.VerifyAccess));
            }
            Factory = new(Main, roots, () => new(Base + 106, Base + 107, Base + 104, false, true, false, true, 0, 0),
                () => { }, () => true, () => throw new Exception("No extra factory root is needed."));
            Factory.MarkReady(); Registry.BindPendingController(claim, Base + 106, new Lease()); Launch.NoteControllerBound(Base + 106);
            Backend = new(Main, new(Base + 106, Base + 107, Base + 108, Base + 104, Base + 105), () => ++TargetReads);
            Authority = new(Backend, Launch.Identity);
            Source = NativeLocalActiveDestructionTracker.CaptureActiveFactoryBackend(Main, Backend, Authority, Launch, Factory);
        }
        internal NativeLocalActiveDestructionTracker.FactoryRetirementSeal Freeze()
        {
            if (_seal is not null) return _seal;
            var activation = new NativeInactiveControllerActivation(Main, new ActivationBackend(), new ActivationAuthority(Launch.Identity, Base + 106));
            typeof(NativeInactiveControllerActivation).GetField("_attempted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(activation, true);
            typeof(OwnedNativeInactiveController).GetField("_activation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Factory, activation);
            Authority.Activated = Authority.Natural = true;
            Source.Tick(); ++Backend.FrameValue;
            Backend.Live = Backend.Live with { ControllerAlive = false, CloneGameObjectAlive = false, CloneTransformAlive = false,
                StagingGameObjectAlive = false, StagingTransformAlive = false };
            Check(Source.Tick());
            return _seal = Source.FreezeForActiveFactoryRetirement(Launch, Factory)!;
        }
        internal void CloseRegistry()
        {
            if (_registryClosed) return;
            Registry.BeginReturn(Session); var token = Registry.VerifyConsumersEnded(Session, new Ended())!;
            Check(Registry.Close(Session, token)); _registryClosed = true;
        }
        internal NativeLocalActiveDestructionTracker.FactoryRetirementSeal CloseFactory()
        {
            var seal = Freeze(); CloseRegistry(); Check(Factory.TryCompleteActiveDestroyBackend(Launch, seal, () => { })); return seal;
        }
        public void Dispose()
        {
            try
            {
                if (!PreserveUnknown && !Source.Unknown)
                {
                    var seal = CloseFactory();
                    Check(Source.TryReleaseFrozenObservationRootsAfterCloseBackend(Launch, Factory, seal, () => { }));
                }
            }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Backend : IActiveDestructionBackend
    {
        internal ActiveDestructionLiveness Live;
        internal int FrameValue = 10, NativeReads;
        internal uint FailFree;
        internal Action<uint>? DuringFree;
        internal readonly List<uint> Frees = new();
        private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[5];
        public bool CleanupUnknown { get; private set; }
        public int RootRecords => _roots.Count(root => root is not null);
        internal Backend(MainThreadDispatcher main, ActiveDestructionIdentity identity, Action targetRead)
        {
            Live = new(identity, true, true, true, true, true);
            for (uint index = 1; index <= 5; ++index)
            {
                var id = index;
                _roots[index - 1] = new(id, value => { Frees.Add(value); DuringFree?.Invoke(value);
                    if (value == FailFree) throw new IOException("Controlled independent root-free outcome is unknown."); },
                    _ => { targetRead(); throw new Exception("Frozen retirement cannot read a root target."); }, main.VerifyAccess);
            }
        }
        public void Initialize() { }
        public int Frame() { ++NativeReads; return FrameValue; }
        public ActiveDestructionLiveness ReadLiveness() { ++NativeReads; return Live; }
        public void VerifyStagingCanDestroy() => throw new Exception("No staging mutation is requested.");
        public void DestroyStagingOnce() => throw new Exception("No staging mutation is requested.");
        public void ReleaseObservationRoots(Func<bool> mayFreeNext)
        {
            try
            {
                for (var index = _roots.Length - 1; index >= 0; --index)
                {
                    if (!mayFreeNext()) throw new InvalidOperationException("Controlled source authority changed.");
                    _roots[index]!.Dispose(); _roots[index] = null;
                    if (!mayFreeNext()) throw new InvalidOperationException("Controlled source authority changed after free.");
                }
            }
            catch { CleanupUnknown = true; throw; }
        }
    }
    private sealed class Authority : IActiveDestructionAuthority
    {
        private readonly Backend _backend;
        internal bool Activated, Natural;
        public LocalRunSessionIdentity Identity { get; }
        public long Controller => _backend.Live.Identity.Controller;
        public bool Unknown { get; private set; }
        internal Authority(Backend backend, LocalRunSessionIdentity identity) => (_backend, Identity) = (backend, identity);
        public void VerifyBeforeCapture() { if (Activated || Unknown) throw new InvalidOperationException("Controlled source must capture before activation."); }
        public void VerifyObservation() { if (!Activated || Unknown) throw new InvalidOperationException("Controlled source is not known and active."); }
        public ActiveDestructionReadiness ReadReadiness() => new(!Unknown, true, Natural, _backend.FrameValue);
        public void MarkUnknown(string failure) => Unknown = true;
    }
    private sealed class Ended : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled exact-owner registry closure; no production verifier claimed."; return true; } }
    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class ActivationBackend : IInactiveControllerActivationBackend
    {
        public bool CleanupUnknown => false;
        public int RetainedCallArgumentRecords => 0;
        public void RequireSupportedBuild() => throw new NotSupportedException();
        public NativeInactiveControllerActivationGraph ReadGraph() => throw new NotSupportedException();
        public ControllerAwakeLedgerSnapshot ReadAwake(NativeControllerAwakeLedger.CaptureWindow window) => throw new NotSupportedException();
        public void CaptureInstalledGlobals(OwnedNativeControllerGlobals globals) => throw new NotSupportedException();
        public void DisableClone() => throw new NotSupportedException();
        public void DetachClone() => throw new NotSupportedException();
        public void ActivateClone() => throw new NotSupportedException();
    }
    private sealed class ActivationAuthority : IInactiveControllerActivationAuthority
    {
        public LocalRunSessionIdentity Identity { get; }
        public long Controller { get; }
        public long Information => 0;
        public bool CleanupUnknown => false;
        public bool InstalledGlobalsCaptured => false;
        internal ActivationAuthority(LocalRunSessionIdentity identity, long controller) => (Identity, Controller) = (identity, controller);
        public void VerifyPending() => throw new NotSupportedException();
        public NativeInactiveControllerParameterBindingObservation ReadParameters() => throw new NotSupportedException();
        public void NoteActivationAttempt() => throw new NotSupportedException();
        public ControllerAwakeLedgerSnapshot ReadAwake() => throw new NotSupportedException();
        public ControllerGlobalsSnapshot BaselineGlobals() => throw new NotSupportedException();
        public ControllerGlobalsSnapshot InstalledGlobals() => throw new NotSupportedException();
        public ControllerGlobalsSnapshot ObserveGlobals() => throw new NotSupportedException();
        public void CaptureInstalledGlobals() => throw new NotSupportedException();
        public void MarkUnknown(string failure) => throw new NotSupportedException();
    }
}
