using System.Reflection;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Controlled fake handles and backend only. These tests exercise actual
/// registry Close and the factory's eight-root release, not a production native
/// consumers-ended verifier, Unity destruction timing or whole-run retirement.</summary>
internal static class ActiveControllerFactoryRetirementRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Active factory retirement requires exact registry Close before any original root release", BeforeClose),
        ("Active factory closes its eight original roots before independent parameter binding release", AfterCloseAndParameters),
        ("Active factory death seal rejects constructed copies and another launch or owner", SealIdentity),
        ("Frozen destruction source performs no later Unity or root-target reads", FrozenSources),
        ("Active factory seal waits for all five later-frame deaths and native drain", PendingDeath),
        ("Controlled active retirement cannot sign a production factory or production seal", ProductionIsolation),
        ("Active factory wrong-thread retirement spends no native or free operation", WrongThread),
        // Intentionally retain unknown owners. No reset/force-free test backdoor.
        ("Active factory stops the original free pass when adopted authority changes during a free", AuthorityLostDuringFree),
        ("Active factory independently attempts known roots once after one free fails", IndependentFreeFailure),
        ("Original factory identity drift before freeze cannot mint an independent death seal", OriginalRootDrift),
        ("Frozen active source faults reject original factory frees before the first attempt", FrozenSourceFault)
    };

    private static void BeforeClose()
    {
        using var f = new Fixture(); var seal = f.ConfirmAndFreeze(); var authorityChecks = 0;
        Throws<InvalidOperationException>(() => f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => authorityChecks++));
        Equal(0, authorityChecks); Equal(0, f.Frees.Count); Equal(8, f.Owner.RootRecords); Check(!f.Owner.IsClosed);
        f.CloseRegistry(); Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => authorityChecks++));
        Check(authorityChecks >= 17); Check(f.Owner.IsClosed);
    }
    private static void AfterCloseAndParameters()
    {
        using var f = new Fixture(bindParameters: true); var seal = f.ConfirmAndFreeze(); f.CloseRegistry();
        Throws<InvalidOperationException>(() => f.Binding!.Dispose()); Equal(0, f.ParameterFrees.Count);
        var targetReads = f.TargetReads; var nativeReads = f.Backend.Reads;
        Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
        Equal("8,7,6,5,4,3,2,1", string.Join(",", f.Frees)); Check(f.Owner.IsClosed && !f.Owner.CleanupUnknown);
        Equal(0, f.Owner.RootRecords); Equal(targetReads, f.TargetReads); Equal(nativeReads, f.Backend.Reads);
        f.Binding!.Dispose(); f.Binding.Dispose(); Check(f.Binding.IsClosed); Equal(0, f.Binding.RootRecords);
        Equal("parameters,controller", string.Join(",", f.ParameterFrees));
        Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => throw new Exception("Completed free must not repeat its authority scope.")));
        Equal(8, f.Frees.Count); Equal(5, f.Source.Read().Roots); Check(f.Source.FactoryRetirementFrozen);
        // The source's five independent observation roots deliberately remain
        // retained. Factory closure does not claim the complete run has retired.
    }
    private static void SealIdentity()
    {
        using var f = new Fixture(); using var other = new Fixture(); var seal = f.ConfirmAndFreeze();
        Throws<InvalidOperationException>(() => new NativeLocalActiveDestructionTracker.FactoryRetirementSeal(f.Source, new object(),
            seal.Session, seal.Identity, seal.CutoutFrame, seal.CloneFrame, seal.ConfirmedFrame, seal.Production));
        Throws<InvalidOperationException>(() => seal.ValidateFor(other.Launch, f.Owner));
        Throws<InvalidOperationException>(() => seal.ValidateFor(f.Launch, other.Owner));
        Throws<InvalidOperationException>(() => f.Source.FreezeForActiveFactoryRetirement(other.Launch, f.Owner));
        Equal(0, f.Frees.Count); Check(ReferenceEquals(seal, f.Source.FreezeForActiveFactoryRetirement(f.Launch, f.Owner)));
        f.CloseRegistry(); Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
    }
    private static void FrozenSources()
    {
        using var f = new Fixture(); var seal = f.ConfirmAndFreeze(); var reads = f.Backend.Reads; var targets = f.TargetReads;
        Throws<InvalidOperationException>(() => f.Source.Tick());
        Throws<InvalidOperationException>(() => f.Source.RequestStagingDestroyOnce());
        Throws<InvalidOperationException>(() => f.Source.ReleaseObservationRootsAfterConfirmed());
        seal.ValidateFor(f.Launch, f.Owner); f.CloseRegistry(); seal.ValidateFor(f.Launch, f.Owner);
        Equal(reads, f.Backend.Reads); Equal(targets, f.TargetReads); Equal(0, f.Backend.Releases);
        Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
        Equal(reads, f.Backend.Reads); Equal(targets, f.TargetReads);
    }
    private static void PendingDeath()
    {
        using var f = new Fixture(); f.Activate();
        Check(f.Source.FreezeForActiveFactoryRetirement(f.Launch, f.Owner) is null);
        f.Authority.Natural = true; Check(!f.Source.Tick());
        f.Backend.Live = f.Backend.Live with { ControllerAlive = false, CloneGameObjectAlive = false, CloneTransformAlive = false };
        Check(!f.Source.Tick()); Check(f.Source.FreezeForActiveFactoryRetirement(f.Launch, f.Owner) is null);
        f.Backend.FrameValue++; Check(f.Source.Tick()); Check(f.Source.FreezeForActiveFactoryRetirement(f.Launch, f.Owner) is null);
        f.Backend.Live = f.Backend.Live with { StagingGameObjectAlive = false, StagingTransformAlive = false };
        f.Backend.FrameValue++; Check(f.Source.Tick()); f.Authority.Drained = false;
        var reads = f.Backend.Reads; Check(f.Source.FreezeForActiveFactoryRetirement(f.Launch, f.Owner) is null); Equal(reads, f.Backend.Reads);
        f.Authority.Drained = true; var seal = f.Source.FreezeForActiveFactoryRetirement(f.Launch, f.Owner)!;
        Check(seal.ConfirmedFrame > seal.CutoutFrame && seal.CloneFrame > seal.CutoutFrame);
        f.CloseRegistry(); Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
    }
    private static void ProductionIsolation()
    {
        using var f = new Fixture(); var seal = f.ConfirmAndFreeze();
        var field = typeof(OwnedNativeInactiveController).GetField("_activationBackend", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // This marker is never invoked. It proves a controlled receipt cannot
        // be promoted merely by presenting an owner with a production backend.
        field.SetValue(f.Owner, RuntimeHelpers.GetUninitializedObject(typeof(NativeInactiveControllerActivationBackend)));
        try
        {
            Throws<InvalidOperationException>(() => seal.ValidateFor(f.Launch, f.Owner));
            Throws<InvalidOperationException>(() => f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
        }
        finally { field.SetValue(f.Owner, null); }
        Throws<InvalidOperationException>(() => new NativeLocalActiveDestructionTracker.FactoryRetirementSeal(f.Source, new object(),
            seal.Session, seal.Identity, seal.CutoutFrame, seal.CloneFrame, seal.ConfirmedFrame, true));
        f.CloseRegistry(); Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
    }
    private static void WrongThread()
    {
        using var f = new Fixture(); var seal = f.ConfirmAndFreeze(); f.CloseRegistry(); var checks = 0;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => seal.ValidateFor(f.Launch, f.Owner));
            Throws<InvalidOperationException>(() => f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => checks++));
        }).GetAwaiter().GetResult();
        Equal(0, checks); Equal(0, f.Frees.Count); Check(f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => checks++));
    }
    private static void AuthorityLostDuringFree()
    {
        using var f = new Fixture(); var seal = f.ConfirmAndFreeze(); f.CloseRegistry(); var known = true;
        f.DuringFree = id => { if (id == 8) known = false; };
        void Verify() { if (!known) throw new InvalidOperationException("Controlled source pump/consumer authority changed."); }
        Throws<AggregateException>(() => f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, Verify));
        Equal("8", string.Join(",", f.Frees)); Equal(7, f.Owner.RootRecords); Check(f.Owner.CleanupUnknown && !f.Owner.IsClosed);
        Check(!f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, Verify)); Equal(1, f.Frees.Count);
        f.PreserveUnknown = true;
    }
    private static void IndependentFreeFailure()
    {
        using var f = new Fixture(); var seal = f.ConfirmAndFreeze(); f.CloseRegistry(); f.FailFree = 6;
        Throws<AggregateException>(() => f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
        Equal("8,7,6,5,4,3,2,1", string.Join(",", f.Frees)); Equal(1, f.Owner.RootRecords);
        Check(f.Owner.CleanupUnknown && !f.Owner.IsClosed); Check(!f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
        Throws<InvalidOperationException>(() => f.Roots.Dispose()); Equal(8, f.Frees.Count); f.PreserveUnknown = true;
    }
    private static void FrozenSourceFault()
    {
        using var f = new Fixture(); var seal = f.ConfirmAndFreeze(); f.CloseRegistry(); f.Backend.Unknown = true;
        Throws<InvalidOperationException>(() => seal.ValidateFor(f.Launch, f.Owner));
        Throws<InvalidOperationException>(() => f.Owner.TryCompleteActiveDestroyBackend(f.Launch, seal, () => { }));
        Equal(0, f.Frees.Count); Equal(8, f.Owner.RootRecords); f.PreserveUnknown = true;
    }
    private static void OriginalRootDrift()
    {
        using var f = new Fixture(); f.Activate(); f.Authority.Natural = true; f.Source.Tick(); f.Backend.FrameValue++;
        f.Backend.Live = f.Backend.Live with { ControllerAlive = false, CloneGameObjectAlive = false, CloneTransformAlive = false,
            StagingGameObjectAlive = false, StagingTransformAlive = false };
        Check(f.Source.Tick()); f.RootTargets[6] = f.Base+9999;
        Throws<NativeLocalActiveDestructionException>(() => f.Source.FreezeForActiveFactoryRetirement(f.Launch, f.Owner));
        Check(f.Source.Unknown && !f.Source.FactoryRetirementFrozen); Equal(0, f.Frees.Count); Equal(8, f.Owner.RootRecords);
        f.PreserveUnknown = true;
    }

    private sealed class Fixture : IDisposable
    {
        private static int _next;
        internal readonly long Base = 0x5a00_0000_0000L+Interlocked.Increment(ref _next)*10000L;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly InactiveControllerRootRecords Roots;
        internal readonly OwnedNativeInactiveController Owner;
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunLaunchCommitState Launch;
        internal readonly Backend Backend = new();
        internal readonly Authority Authority;
        internal readonly NativeLocalActiveDestructionTracker Source;
        internal readonly List<uint> Frees = new();
        internal readonly List<string> ParameterFrees = new();
        internal readonly Dictionary<uint, long> RootTargets = new();
        internal NativeInactiveControllerParameterBinding? Binding;
        internal Action<uint>? DuringFree;
        internal uint FailFree;
        internal int TargetReads;
        internal bool PreserveUnknown;
        private bool _activated, _registryClosed;
        internal Fixture(bool bindParameters = false)
        {
            Registry = new(Main); var bundle = new Lease();
            Session = Registry.RegisterPrepared(new(new("active.factory", "song"), 1, "chart"), Base+1000, bundle);
            var claim = Registry.BeginCommit(Session); Launch = new(Main, Registry, claim, bundle);
            Roots = new(Main);
            for (uint index = 1; index <= 8; ++index)
            {
                var id = index;
                Roots.Allocate(() => new NativeRuntime.RootHandle(id, value => {
                    Frees.Add(value); DuringFree?.Invoke(value); if (value == FailFree) throw new IOException("Controlled unknown root free.");
                }, _ => { TargetReads++; return new IntPtr(RootTargets.TryGetValue(id, out var changed) ? changed : Base+100+id); }, Main.VerifyAccess));
            }
            Owner = new(Main, Roots, () => new(Base+106, Base+107, Base+104, false, true, false, true, _parameters, 0),
                () => { }, () => true, () => throw new Exception("No new controller root is needed."));
            Owner.MarkReady(); Registry.BindPendingController(claim, Base+106, new Lease()); Launch.NoteControllerBound(Base+106);
            if (bindParameters)
            {
                Binding = NativeInactiveControllerParameterBinding.BindBackend(Main, new BindingBackend(this, bundle), Registry, claim);
                Launch.AttachParameterBinding(Binding);
            }
            Backend.Live = new(new(Base+106, Base+107, Base+108, Base+104, Base+105), true, true, true, true, true);
            Authority = new(Backend, Launch.Identity);
            Source = NativeLocalActiveDestructionTracker.CaptureActiveFactoryBackend(Main, Backend, Authority, Launch, Owner);
        }
        private long _parameters;
        internal void Activate()
        {
            if (_activated) return;
            var activation = new NativeInactiveControllerActivation(Main, new ActivationBackend(), new ActivationAuthority(Launch.Identity, Base+106));
            typeof(NativeInactiveControllerActivation).GetField("_attempted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(activation, true);
            typeof(OwnedNativeInactiveController).GetField("_activation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Owner, activation);
            _activated = true; Authority.Activated = true;
        }
        internal NativeLocalActiveDestructionTracker.FactoryRetirementSeal ConfirmAndFreeze()
        {
            Activate(); Authority.Natural = true;
            if (!Source.Read().FiveObjectsDestroyed)
            {
                Source.Tick(); Backend.FrameValue++; Backend.Live = Backend.Live with {
                    ControllerAlive = false, CloneGameObjectAlive = false, CloneTransformAlive = false,
                    StagingGameObjectAlive = false, StagingTransformAlive = false };
                Source.Tick();
            }
            return Source.FreezeForActiveFactoryRetirement(Launch, Owner)!;
        }
        internal void CloseRegistry()
        {
            if (_registryClosed) return; Registry.BeginReturn(Session);
            var token = Registry.VerifyConsumersEnded(Session, new Ended())!;
            Check(Registry.Close(Session, token)); _registryClosed = true;
        }
        public void Dispose()
        {
            try
            {
                if (!PreserveUnknown && !Owner.CleanupUnknown)
                {
                    var seal = ConfirmAndFreeze(); CloseRegistry();
                    Check(Owner.TryCompleteActiveDestroyBackend(Launch, seal, () => { })); Binding?.Dispose();
                }
            }
            finally { Main.Dispose(); }
        }
        private sealed class BindingBackend : IInactiveControllerParameterBindingBackend
        {
            private readonly Fixture _fixture;
            public IDisposable BundleResponsibility { get; }
            public IDisposable ControllerResponsibility => _fixture.Owner;
            public bool ControllerClosed => _fixture.Owner.IsClosed;
            public bool ControllerCleanupUnknown => _fixture.Owner.CleanupUnknown;
            public bool CallCleanupUnknown => false;
            public int RetainedCallArgumentRecords => 0;
            private LocalNativeParametersObservation Inputs => new(_fixture.Base+1000, _fixture.Base+1001, _fixture.Base+1002,
                _fixture.Base+1003, _fixture.Base+1004, 1, 0);
            internal BindingBackend(Fixture fixture, IDisposable bundle) => (_fixture, BundleResponsibility) = (fixture, bundle);
            public void RequireSupportedBuild() => _fixture.Main.VerifyAccess();
            public InactiveControllerParameterInputs ReadBundle() => new(_fixture.Base+1005, Inputs);
            public NativeInactiveControllerObservation ReadController() => _fixture.Owner.Read();
            public void ValidateNativeMetadata(IntPtr controller, IntPtr parameters, LocalNativeParametersObservation expected) { }
            public NativeRuntime.RootHandle RetainController() => new(90, _ => _fixture.ParameterFrees.Add("controller"), _ => new(_fixture.Base+106), _fixture.Main.VerifyAccess);
            public NativeRuntime.RootHandle RetainParameters(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
                => new(91, _ => _fixture.ParameterFrees.Add("parameters"), _ => new(_fixture.Base+1005), _fixture.Main.VerifyAccess);
            public LocalNativeParametersObservation ReadParameters(IntPtr parameters) => Inputs;
            public void SetParameters(IntPtr controller, IntPtr parameters) => _fixture._parameters = parameters.ToInt64();
        }
    }
    private sealed class Backend : IActiveDestructionBackend
    {
        internal ActiveDestructionLiveness Live = new(new(106, 107, 108, 104, 105), true, true, true, true, true);
        internal int FrameValue = 10, Reads, Releases;
        internal bool Unknown;
        public bool CleanupUnknown => Unknown;
        public int RootRecords => 5;
        public void Initialize() { }
        public int Frame() { Reads++; return FrameValue; }
        public ActiveDestructionLiveness ReadLiveness() { Reads++; return Live; }
        public void VerifyStagingCanDestroy() => throw new Exception("No staging destruction is requested in this controlled case.");
        public void DestroyStagingOnce() => throw new Exception("No Unity mutation is requested in this controlled case.");
        public void ReleaseObservationRoots(Func<bool> mayFreeNext) { Releases++; throw new Exception("Five independent observation roots must remain retained in this batch."); }
    }
    private sealed class Authority : IActiveDestructionAuthority
    {
        private readonly Backend _backend;
        internal bool Activated, Natural, Drained = true;
        public LocalRunSessionIdentity Identity { get; }
        public long Controller => _backend.Live.Identity.Controller;
        public bool Unknown { get; private set; }
        internal Authority(Backend backend, LocalRunSessionIdentity identity) => (_backend, Identity) = (backend, identity);
        public void VerifyBeforeCapture() { if (Activated || Unknown) throw new InvalidOperationException("Controlled capture must precede activation."); }
        public void VerifyObservation() { if (!Activated || Unknown) throw new InvalidOperationException("Controlled source is not active and known."); }
        public ActiveDestructionReadiness ReadReadiness() => new(!Unknown, Drained, Natural, Drained ? _backend.FrameValue : -1);
        public void MarkUnknown(string failure) => Unknown = true;
    }
    private sealed class Ended : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled fake-consumer closure only; no production verifier claimed."; return true; } }
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
