using System.Reflection;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

/// <summary>Real launch/registry/quota ownership with controlled source transports. The shared-ready
/// fixture publication is explicit and does not claim actual Awake, graph initialization or game acceptance.</summary>
internal static class NativeLocalRunTerminationCaptureFactoryPendingRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Pending native factory reserves the existing process pool before any source or root access", PureReservation),
        ("Pending native factory accepts only the same exact launch and pre-reserved capture", ExactReservation),
        ("Pending native factory stays Held until shared readiness and returns the same initialized capture", HeldAndReady),
        ("Pending native factory rejects old Closed same-address session evidence before root cleanup", HistoricalSession),
        ("Pending native factory reservation survives abandoned managed handles", StrongRetention),
        ("Pending native factory shares legacy capacity and full quota leaves caller roots untouched", SharedCapacity),
        ("Pending native factory rejects worker attachment and cleanup callback reentry before root reads", ThreadAndReentry),
        ("Pending native factory preflight rejects another factory's root without adopting or releasing it", ForeignOwnedHandoff),
        // Unknown attempts retain their factory quota without retry or test-only resets.
        ("Pending native factory failed attachment retains an already-adopted root and shared unknown state", AttachmentFailure),
        ("Pending native factory controller prevalidation leaves caller ownership and allows a subsequent correct handoff", PreAdoptionFailure),
        ("Pending native factory capture cleanup failure still releases its independent controller root once", CaptureCleanupFailure),
        ("Pending native factory controller free failure still closes the reserved capture independently", ControllerCleanupFailure)
    };

    private static void PureReservation()
    {
        using var fixture = new Fixture(); var factories = NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount;
        var captures = NativeLocalResultCapture.RetainedProcessCaptureCount;
        var factory = fixture.ReserveFactory(); Equal(0L, factory.ControllerIdentity);
        Equal(factories + 1, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        Throws<InvalidOperationException>(() => factory.Create(fixture.Registry, fixture.Session));
        Equal(0, fixture.Native.FieldReads); Equal(0, fixture.Native.Invocations); Equal(0, fixture.Native.NewRoots);
        Equal(0, fixture.Backend.SourceReads); Check(!fixture.Capture.NativeWorkAttempted);
        factory.CancelPendingFactory(); Equal(factories, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        Equal(captures, NativeLocalResultCapture.RetainedProcessCaptureCount); // Factory cancellation does not cancel capture capacity.
        Check(fixture.Capture.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
        fixture.Capture.CancelEmptyPendingReservation(); Equal(captures - 1, NativeLocalResultCapture.RetainedProcessCaptureCount);
        Equal(LocalRunSessionPhase.PendingCommit, fixture.Registry.Read(fixture.Session).Phase); Equal(0, fixture.Bundle.Frees);
        fixture.Factory = null;
    }

    private static void ExactReservation()
    {
        using var fixture = new Fixture(); using var other = new Fixture();
        var baseline = NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount;
        Throws<InvalidOperationException>(() => NativeLocalRunTerminationCaptureFactory.ReservePending(fixture.Native.Api, fixture.Main,
            Profile(), IntPtr.Zero, fixture.Launch, other.Capture));
        Equal(baseline, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        var owner = fixture.ReserveFactory();
        Throws<InvalidOperationException>(() => fixture.ReserveFactory());
        Equal(baseline + 1, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        owner.CancelPendingFactory(); fixture.Factory = null;
    }

    private static void HeldAndReady()
    {
        using var fixture = new Fixture(); var factory = fixture.ReserveFactory(); fixture.BindAndAttach();
        Equal(0, fixture.ControllerTargetReads); Equal(0, fixture.Backend.Builds); Equal(0, fixture.Backend.SourceReads);
        Throws<InvalidOperationException>(() => factory.Create(fixture.Registry, fixture.Session));
        fixture.Registry.Commit(fixture.Claim);
        Throws<InvalidOperationException>(() => factory.Create(fixture.Registry, fixture.Session));
        Check(!fixture.Capture.NativeWorkAttempted); Equal(0, fixture.Backend.SourceReads);
        fixture.PublishControlledReady();
        var count = NativeLocalResultCapture.RetainedProcessCaptureCount;
        var capture = factory.Create(fixture.Registry, fixture.Session);
        Check(ReferenceEquals(fixture.Capture, capture)); Check(capture.IsInitialized); Equal(2, capture.RootRecords);
        Equal(count, NativeLocalResultCapture.RetainedProcessCaptureCount); Equal(0, fixture.Backend.Invocations);
        capture.Capture(); Equal(1, fixture.Backend.Invocations);
        fixture.Retire(); capture.Dispose(); // The handler can have already disposed this exact owner.
        factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session);
        Equal(1, fixture.ControllerFrees); Check(capture.IsClosed); fixture.Factory = null;
    }

    private static void HistoricalSession()
    {
        using var fixture = new Fixture();
        var old = fixture.Registry.RegisterPrepared(new(new("factory.pending", "old-address"), 1, "chart"), fixture.Backend.Source.Metadata + 20, new Lease());
        var oldClaim = fixture.Registry.BeginCommit(old);
        fixture.Registry.BindPendingController(oldClaim, fixture.Backend.Source.Controller, new Lease()); fixture.Registry.Commit(oldClaim);
        Retire(fixture.Registry, old);
        var factory = fixture.ReserveFactory(); fixture.BindAndAttach();
        Throws<InvalidOperationException>(() => factory.Create(fixture.Registry, old));
        Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, old));
        Equal(0, fixture.ControllerFrees); Equal(0, fixture.ControllerTargetReads); Equal(0, fixture.Backend.SourceReads);
        fixture.Retire(); factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session); fixture.Factory = null;
        Equal(1, fixture.ControllerFrees); Check(fixture.Capture.IsClosed);
    }

    private static void StrongRetention()
    {
        using var fixture = new Fixture(); var weak = Abandon(fixture);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Check(weak.TryGetTarget(out var owner));
        Equal(0L, owner!.ControllerIdentity); Check(fixture.Capture.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
        owner.CancelPendingFactory(); fixture.Capture.CancelEmptyPendingReservation();
        Equal(0, fixture.Native.Invocations); Equal(0, fixture.Backend.SourceReads);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NativeLocalRunTerminationCaptureFactory> Abandon(Fixture fixture)
    { var owner = fixture.ReserveFactory(); fixture.Factory = null; return new(owner); }

    private static void SharedCapacity()
    {
        using var fixture = new Fixture(); var baseline = NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount;
        var registry = new LocalRunSessionRegistry(fixture.Main);
        var session = registry.RegisterPrepared(new(new("factory.pending", "legacy-capacity"), 1, "chart"), 72_990_000, new Lease());
        var claim = registry.BeginCommit(session); registry.BindPendingController(claim, 72_990_001, new Lease()); registry.Commit(claim);
        var owners = new List<NativeLocalRunTerminationCaptureFactory>(); var frees = 0; var targetReads = 0;
        try
        {
            for (uint index = 0; index < 32 - baseline; index++)
            {
                var root = new NativeRuntime.RootHandle(500 + index, _ => frees++, _ => { targetReads++; return new(72_990_001); }, fixture.Main.VerifyAccess);
                owners.Add(new NativeLocalRunTerminationCaptureFactory(fixture.Native.Api, fixture.Main, Profile(), IntPtr.Zero,
                    registry, session, 72_990_001, root));
            }
            Equal(32, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
            Throws<InvalidOperationException>(() => fixture.ReserveFactory());
            Check(fixture.Capture.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
            var rejected = new NativeRuntime.RootHandle(599, _ => frees++, _ => { targetReads++; return new(72_990_001); }, fixture.Main.VerifyAccess);
            Throws<InvalidOperationException>(() => new NativeLocalRunTerminationCaptureFactory(fixture.Native.Api, fixture.Main, Profile(), IntPtr.Zero,
                registry, session, 72_990_001, rejected));
            Equal(0, targetReads); Equal(0, frees); Equal(new IntPtr(72_990_001), rejected.Target); rejected.Dispose();
            Equal(1, targetReads); Equal(1, frees);
        }
        finally
        {
            Retire(registry, session);
            foreach (var owner in owners) owner.ReleaseAfterConsumersEnded(registry, session);
        }
        Equal(baseline, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
    }

    private static void ThreadAndReentry()
    {
        using var fixture = new Fixture(); var factory = fixture.ReserveFactory(); fixture.Bind();
        var root = fixture.ControllerRoot();
        Task.Run(() => Throws<InvalidOperationException>(() => factory.AttachPendingControllerBackend(root, fixture.Backend.Source.Controller, fixture.Backend))).GetAwaiter().GetResult();
        Equal(0, fixture.ControllerTargetReads); Equal(0L, factory.ControllerIdentity);
        factory.AttachPendingControllerBackend(root, fixture.Backend.Source.Controller, fixture.Backend);
        fixture.BeforeControllerFree = () => {
            Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
            Throws<InvalidOperationException>(() => factory.Create(fixture.Registry, fixture.Session));
        };
        fixture.Retire(); factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session); fixture.Factory = null;
        Equal(1, fixture.ControllerFrees); Equal(0, fixture.ControllerTargetReads); Equal(0, fixture.Backend.SourceReads);
    }

    private static void AttachmentFailure()
    {
        using var fixture = new Fixture(); var factory = fixture.ReserveFactory(); fixture.Bind();
        // A live reservation may already have an attached backend. Preflight stays read-only;
        // the subsequent once-only attachment rejects, after the factory has recorded its root.
        fixture.Capture.AttachPendingBackend(fixture.Backend); var root = fixture.ControllerRoot();
        Throws<InvalidOperationException>(() => factory.AttachPendingControllerBackend(root, fixture.Backend.Source.Controller, fixture.Backend));
        Check(factory.CleanupUnknown); Equal(LocalRunLaunchCommitPhase.Unknown, fixture.Launch.Read().Phase);
        Check(factory.OwnsControllerRoot(root));
        Equal(fixture.Backend.Source.Controller, factory.ControllerIdentity); Equal(0, fixture.ControllerFrees); Equal(0, fixture.ControllerTargetReads);
        var next = fixture.ControllerRoot();
        Throws<InvalidOperationException>(() => factory.AttachPendingControllerBackend(next, fixture.Backend.Source.Controller, fixture.Backend));
        Check(!factory.OwnsControllerRoot(next));
        next.Dispose(); Equal(1, fixture.ControllerFrees); // Rejected second root remains caller-owned.
        fixture.Retire(); Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Equal(2, fixture.ControllerFrees); Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Equal(2, fixture.ControllerFrees); fixture.PreserveUnknown = true;
    }

    private static void PreAdoptionFailure()
    {
        using var fixture = new Fixture(); var factory = fixture.ReserveFactory(); fixture.Bind(); var root = fixture.ControllerRoot();
        Throws<InvalidOperationException>(() => factory.VerifyPendingControllerHandoff(root, fixture.Backend.Source.Controller + 1));
        Throws<InvalidOperationException>(() => factory.AttachPendingControllerBackend(root, fixture.Backend.Source.Controller + 1, fixture.Backend));
        Check(!factory.CleanupUnknown); Equal(0L, factory.ControllerIdentity); Equal(0, fixture.ControllerTargetReads);
        Check(!factory.OwnsControllerRoot(root));
        root.Dispose(); Equal(1, fixture.ControllerFrees);
        var correct = fixture.ControllerRoot(); factory.VerifyPendingControllerHandoff(correct, fixture.Backend.Source.Controller);
        Check(!factory.OwnsControllerRoot(correct)); Equal(0, fixture.ControllerTargetReads);
        factory.AttachPendingControllerBackend(correct, fixture.Backend.Source.Controller, fixture.Backend);
        fixture.Retire(); factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session); fixture.Factory = null;
        Check(fixture.Capture.IsClosed); Equal(2, fixture.ControllerFrees);
    }

    private static void ForeignOwnedHandoff()
    {
        using var fixture = new Fixture(); using var other = new Fixture();
        var factory = fixture.ReserveFactory(); fixture.Bind(); other.Bind(); var foreignRoot = other.ControllerRoot();
        var foreignOwner = new NativeLocalRunTerminationCaptureFactory(other.Native.Api, other.Main, Profile(), IntPtr.Zero,
            other.Registry, other.Session, other.Backend.Source.Controller, foreignRoot);
        try
        {
            Throws<InvalidOperationException>(() => factory.VerifyPendingControllerHandoff(foreignRoot, fixture.Backend.Source.Controller));
            Throws<InvalidOperationException>(() => factory.AttachPendingControllerBackend(foreignRoot, fixture.Backend.Source.Controller, fixture.Backend));
            Check(foreignOwner.OwnsControllerRoot(foreignRoot)); Check(!factory.OwnsControllerRoot(foreignRoot));
            Check(!factory.CleanupUnknown); Equal(0L, factory.ControllerIdentity);
            Equal(0, other.ControllerFrees); Equal(0, other.ControllerTargetReads); Equal(0, fixture.Backend.SourceReads);
            var own = fixture.ControllerRoot(); factory.VerifyPendingControllerHandoff(own, fixture.Backend.Source.Controller);
            factory.AttachPendingControllerBackend(own, fixture.Backend.Source.Controller, fixture.Backend);
            fixture.Retire(); factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session); fixture.Factory = null;
            Equal(1, fixture.ControllerFrees); Equal(0, other.ControllerFrees);
        }
        finally { other.Retire(); foreignOwner.ReleaseAfterConsumersEnded(other.Registry, other.Session); }
        Equal(1, other.ControllerFrees); Equal(0, other.ControllerTargetReads);
    }

    private static void CaptureCleanupFailure()
    {
        using var fixture = new Fixture(); var factory = fixture.ReserveFactory(); fixture.BindAndAttach(); fixture.PublishControlledReady();
        factory.Create(fixture.Registry, fixture.Session); fixture.Backend.FailFree = 2; fixture.Retire();
        Throws<AggregateException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Check(factory.CleanupUnknown && fixture.Capture.CleanupUnknown); Equal(1, fixture.ControllerFrees); Equal(2, fixture.Backend.FreeAttempts);
        Equal(1, fixture.Capture.RootRecords);
        Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Equal(1, fixture.ControllerFrees); Equal(2, fixture.Backend.FreeAttempts); fixture.PreserveUnknown = true;
    }

    private static void ControllerCleanupFailure()
    {
        using var fixture = new Fixture(); var factory = fixture.ReserveFactory(); fixture.BindAndAttach(); fixture.PublishControlledReady();
        factory.Create(fixture.Registry, fixture.Session); fixture.FailControllerFree = true; fixture.Retire();
        Throws<IOException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Check(factory.CleanupUnknown && fixture.Capture.IsClosed); Equal(2, fixture.Backend.FreeAttempts); Equal(1, fixture.ControllerFrees);
        fixture.FailControllerFree = false; Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Equal(1, fixture.ControllerFrees); fixture.PreserveUnknown = true;
    }

    private static AdapterBuildProfile Profile()
        => new("fixture", "unsupported", "fixture", "fixture", "fixture", "fixture", new Dictionary<string, TypeBinding>());
    private static void Retire(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        if (registry.Read(session).Phase == LocalRunSessionPhase.Closed) return;
        registry.BeginReturn(session); var token = registry.VerifyConsumersEnded(session, new Verifier())!; Check(registry.Close(session, token));
    }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled pending factory test only; not native consumer-end acceptance."; return true; } }
    private sealed class Lease : IDisposable
    { internal int Frees; public void Dispose() => Frees++; }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly FakeNative Native = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunSessionRegistry.CommitClaim Claim;
        internal readonly Lease Bundle = new();
        internal readonly LocalRunLaunchCommitState Launch;
        internal readonly NativeLocalResultCapture Capture;
        internal readonly Backend Backend;
        internal NativeLocalRunTerminationCaptureFactory? Factory;
        internal int ControllerFrees, ControllerTargetReads;
        internal bool PreserveUnknown, FailControllerFree;
        internal Action? BeforeControllerFree;
        internal Fixture()
        {
            Registry = new(Main); Backend = new(Main);
            Session = Registry.RegisterPrepared(new(new("factory.pending", "song"), 1, "chart"), Backend.Source.Metadata, Bundle);
            Claim = Registry.BeginCommit(Session); Launch = new(Main, Registry, Claim, Bundle);
            Capture = NativeLocalResultCapture.ReservePending(Main, Registry, Claim, Bundle); Launch.AttachCapture(Capture);
        }
        internal NativeLocalRunTerminationCaptureFactory ReserveFactory()
        {
            var owner = NativeLocalRunTerminationCaptureFactory.ReservePending(Native.Api, Main, Profile(), IntPtr.Zero, Launch, Capture);
            Factory = owner; return owner;
        }
        internal void Bind() => Registry.BindPendingController(Claim, Backend.Source.Controller, new Lease());
        internal NativeRuntime.RootHandle ControllerRoot()
            => new(400, _ => { ControllerFrees++; BeforeControllerFree?.Invoke(); if (FailControllerFree) throw new IOException("Unknown independent controller root free."); },
                _ => { ControllerTargetReads++; return new(Backend.Source.Controller); }, Main.VerifyAccess);
        internal void BindAndAttach()
        { Bind(); Factory!.AttachPendingControllerBackend(ControllerRoot(), Backend.Source.Controller, Backend); }
        internal void PublishControlledReady()
        {
            if (Registry.Read(Session).Phase == LocalRunSessionPhase.PendingCommit) Registry.Commit(Claim);
            // This test-only publication supplies shared readiness to isolate factory behavior.
            // It does not bypass a production native preflight or claim actual initialization.
            var ready = Launch.Read() with { Phase = LocalRunLaunchCommitPhase.CommittedReady, Controller = Backend.Source.Controller,
                Information = Backend.Source.Information, Progress = Backend.Source.Information + 20, ActivationAttempted = true, NativeAttempted = true };
            typeof(LocalRunLaunchCommitState).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Launch, ready);
        }
        internal void Retire() => NativeLocalRunTerminationCaptureFactoryPendingRegression.Retire(Registry, Session);
        public void Dispose()
        {
            try
            {
                Retire();
                if (!PreserveUnknown)
                {
                    Factory?.ReleaseAfterConsumersEnded(Registry, Session);
                    Capture.Dispose();
                }
            }
            finally { Native.Dispose(); Main.Dispose(); }
        }
    }
    private sealed class Backend : INativeLocalResultCaptureBackend
    {
        private static long _nextIdentity = 72_000_000;
        private readonly MainThreadDispatcher _main;
        internal readonly NativeLocalResultSource Source;
        private readonly long _result;
        internal int Builds, SourceReads, Invocations, FreeAttempts;
        internal uint FailFree;
        internal Backend(MainThreadDispatcher main)
        { _main = main; var id = Interlocked.Add(ref _nextIdentity, 64); Source = new(id + 1, id + 2, id + 3, id + 4); _result = id + 5; }
        public bool CleanupUnknown => false;
        public void RequireSupportedBuild() { _main.VerifyAccess(); Builds++; }
        public NativeLocalResultSource ReadSource() { _main.VerifyAccess(); SourceReads++; return Source; }
        private NativeRuntime.RootHandle Root(uint id, long value)
            => new(id, handle => { FreeAttempts++; if (handle == FailFree) throw new IOException("Unknown capture root free."); }, _ => new(value), _main.VerifyAccess);
        public NativeRuntime.RootHandle RetainController() => Root(1, Source.Controller);
        public NativeRuntime.RootHandle RetainInformation() => Root(2, Source.Information);
        public void InvokeControllerOver(Action<NativeRuntime.RootHandle> publish) { Invocations++; publish(Root(3, _result)); }
        public NativeLocalResultValues ReadResult(NativeRuntime.RootHandle result)
        { Equal(_result, result.Target.ToInt64()); return new(Source.Metadata, 10, .5, 1, 2, 3, 4, false, false); }
    }
}
