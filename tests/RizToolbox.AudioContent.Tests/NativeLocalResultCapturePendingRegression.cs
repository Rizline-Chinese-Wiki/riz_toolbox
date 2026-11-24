using System.Reflection;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

/// <summary>Real registry/capture ownership with controlled transports. Native cases exercise the
/// production backend's no-read pending attachment and bounded publication failure, not game acceptance.</summary>
internal static class NativeLocalResultCapturePendingRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Pending result capacity reserves before controller binding with no source root result claim or native work", EmptyReservation),
        ("Pending result reservation accepts only its exact adopted bundle and fresh current claim", ExactAdmission),
        ("Pending native result attachment reads no caller target or null LevelInformation and empty cancellation leaves caller ownership", NativePendingAttachment),
        ("Pending result owner initializes after commit and captures on the same shared quota slot", CommitAndCapture),
        ("Pending result cancellation cannot release committed or returning responsibility without exact Close", CancelBoundary),
        ("Pending result worker and initialization callbacks cannot reenter attach cancel capture or cleanup", ThreadAndReentry),
        ("Pending result process slot strongly retains an abandoned pre-clone reservation", StrongRetention),
        ("Pending reservations and legacy actual-result Create share one 32-owner quota", SharedCapacity),
        // Unknown native attempts deliberately remain strongly owned without a force-free path.
        ("Pending result initialization with absent info is sticky and never consumes a result claim or retries", MissingInformation),
        ("Pending result partial root allocation failure retains its fixed record and shared quota", PartialRetentionFailure),
        ("Pending production source publication free failure remains bounded on the pre-reserved owner", NativeInitializationFailure)
    };

    private static void EmptyReservation()
    {
        using var fixture = new Fixture(); var baseline = NativeLocalResultCapture.RetainedProcessCaptureCount;
        var owner = fixture.Reserve(); Equal(baseline + 1, NativeLocalResultCapture.RetainedProcessCaptureCount);
        Check(owner.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
        Equal(0, owner.RootRecords); Check(!owner.NativeWorkAttempted && !owner.IsInitialized);
        Check(owner.Capture() is null); Equal(0, fixture.Backend.Builds); Equal(0, fixture.Backend.SourceReads);
        Equal(0, fixture.Backend.Roots); Equal(0, fixture.Backend.Invocations); Check(!fixture.Registry.Read(fixture.Session).ResultCallClaimed);
        owner.CancelEmptyPendingReservation(); Check(owner.IsClosed); Equal(baseline, NativeLocalResultCapture.RetainedProcessCaptureCount);
        Check(!owner.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
        Equal(LocalRunSessionPhase.PendingCommit, fixture.Registry.Read(fixture.Session).Phase); Equal(0, fixture.Bundle.Frees);
        owner.CancelEmptyPendingReservation(); Equal(baseline, NativeLocalResultCapture.RetainedProcessCaptureCount);
    }

    private static void ExactAdmission()
    {
        using var fixture = new Fixture(); var baseline = NativeLocalResultCapture.RetainedProcessCaptureCount;
        Throws<InvalidOperationException>(() => NativeLocalResultCapture.ReservePending(fixture.Main, fixture.Registry, fixture.Claim, new Lease()));
        Equal(baseline, NativeLocalResultCapture.RetainedProcessCaptureCount);
        var owner = fixture.Reserve();
        Throws<InvalidOperationException>(() => NativeLocalResultCapture.ReservePending(fixture.Main, fixture.Registry, fixture.Claim, fixture.Bundle));
        Check(!owner.IsPendingReservationFor(new LocalRunSessionRegistry(fixture.Main), fixture.Claim, fixture.Bundle));
        Check(!owner.IsPendingReservationFor(fixture.Registry, fixture.Claim, new Lease()));
        Throws<InvalidOperationException>(() => owner.AttachPendingBackend(fixture.Backend));
        Equal(0, fixture.Backend.Builds); Equal(0, fixture.Backend.SourceReads);
        fixture.Bind(); owner.AttachPendingBackend(fixture.Backend);
        Check(owner.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
        Throws<InvalidOperationException>(() => owner.AttachPendingBackend(fixture.Backend));
        owner.CancelEmptyPendingReservation(); Equal(baseline, NativeLocalResultCapture.RetainedProcessCaptureCount);
        Throws<InvalidOperationException>(() => NativeLocalResultCapture.ReservePending(fixture.Main, fixture.Registry, fixture.Claim, fixture.Bundle));
    }

    private static void NativePendingAttachment()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative(); var reads = 0; var frees = 0;
        native.FieldType = native.Object; native.FieldBytes = BitConverter.GetBytes(0L); // Pending info would read null.
        var registry = new LocalRunSessionRegistry(main); var bundle = new Lease();
        var session = registry.RegisterPrepared(new(new("capture.pending", "native-empty-info"), 1, "chart"), 69_900_000, bundle);
        var claim = registry.BeginCommit(session); var owner = NativeLocalResultCapture.ReservePending(main, registry, claim, bundle);
        var borrowed = new NativeRuntime.RootHandle(300, _ => frees++, _ => { reads++; return native.Instance; }, main.VerifyAccess);
        registry.BindPendingController(claim, native.Instance.ToInt64(), new Lease());
        owner.AttachPending(native.Api, UnsupportedProfile(), IntPtr.Zero, borrowed);
        Check(owner.Capture() is null); Throws<InvalidOperationException>(owner.InitializeWhenCommitted);
        Equal(0, reads); Equal(0, native.FieldReads); Equal(0, native.Invocations); Equal(0, native.NewRoots);
        Check(!owner.NativeWorkAttempted); owner.CancelEmptyPendingReservation(); Equal(0, frees);
        Equal(native.Instance, borrowed.Target); Equal(1, reads); borrowed.Dispose(); Equal(1, frees);
        Retire(registry, session); Equal(1, bundle.Frees);
    }

    private static void CommitAndCapture()
    {
        using var fixture = new Fixture(); var baseline = NativeLocalResultCapture.RetainedProcessCaptureCount;
        var owner = fixture.Reserve(); fixture.Bind(); owner.AttachPendingBackend(fixture.Backend);
        Throws<InvalidOperationException>(owner.InitializeWhenCommitted);
        Equal(0, fixture.Backend.SourceReads); Check(!owner.NativeWorkAttempted);
        fixture.Registry.Commit(fixture.Claim);
        Throws<InvalidOperationException>(() => NativeLocalResultCapture.CreateBackend(fixture.Main, fixture.Backend, fixture.Registry, fixture.Session));
        Equal(0, fixture.Backend.SourceReads); Equal(0, fixture.Backend.Roots);
        var result = owner.Capture()!;
        Check(owner.IsInitialized && owner.NativeWorkAttempted); Equal(2, owner.RootRecords);
        Check(!owner.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
        Equal(baseline + 1, NativeLocalResultCapture.RetainedProcessCaptureCount);
        Equal(1, fixture.Backend.Invocations); Check(ReferenceEquals(result, fixture.Registry.Read(fixture.Session).Result));
        Check(ReferenceEquals(result, owner.Capture())); Equal(1, fixture.Backend.Invocations);
        fixture.Retire(); owner.Dispose(); Equal(3, fixture.Backend.Frees); Equal(baseline, NativeLocalResultCapture.RetainedProcessCaptureCount);
    }

    private static void CancelBoundary()
    {
        using var fixture = new Fixture(); var owner = fixture.Reserve(); fixture.Bind(); owner.AttachPendingBackend(fixture.Backend);
        fixture.Registry.Commit(fixture.Claim);
        Throws<InvalidOperationException>(owner.CancelEmptyPendingReservation); Equal(0, fixture.Backend.Builds);
        fixture.Registry.BeginReturn(fixture.Session);
        Throws<InvalidOperationException>(owner.CancelEmptyPendingReservation); Throws<InvalidOperationException>(owner.Dispose);
        Equal(0, fixture.Backend.Builds); Equal(0, fixture.Backend.SourceReads);
        fixture.Retire(); owner.Dispose(); Check(owner.IsClosed); Equal(0, fixture.Backend.Frees);
    }

    private static void ThreadAndReentry()
    {
        using var fixture = new Fixture(); var owner = fixture.Reserve(); fixture.Bind(); owner.AttachPendingBackend(fixture.Backend);
        Task.Run(() => {
            Throws<InvalidOperationException>(owner.CancelEmptyPendingReservation); Throws<InvalidOperationException>(owner.InitializeWhenCommitted);
            Throws<InvalidOperationException>(() => owner.AttachPendingBackend(fixture.Backend)); Throws<InvalidOperationException>(() => owner.Capture());
        }).GetAwaiter().GetResult();
        Equal(0, fixture.Backend.Builds); Equal(0, fixture.Backend.SourceReads);
        fixture.Registry.Commit(fixture.Claim); fixture.Backend.BeforeBuild = () => {
            Throws<InvalidOperationException>(owner.CancelEmptyPendingReservation); Throws<InvalidOperationException>(owner.InitializeWhenCommitted);
            Throws<InvalidOperationException>(() => owner.Capture()); Throws<InvalidOperationException>(owner.Dispose);
        };
        owner.InitializeWhenCommitted(); fixture.Backend.BeforeBuild = null;
        Equal(1, fixture.Backend.Builds); Equal(2, owner.RootRecords); Check(!owner.CleanupUnknown);
        owner.Capture(); Equal(1, fixture.Backend.Invocations);
    }

    private static void StrongRetention()
    {
        using var fixture = new Fixture(); var weak = Abandon(fixture);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Check(weak.TryGetTarget(out var owner));
        Check(owner!.IsPendingReservationFor(fixture.Registry, fixture.Claim, fixture.Bundle));
        owner.CancelEmptyPendingReservation(); Check(owner.IsClosed); Equal(0, fixture.Backend.SourceReads);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NativeLocalResultCapture> Abandon(Fixture fixture)
    { var owner = fixture.Reserve(); fixture.Owner = null; return new(owner); }

    private static void SharedCapacity()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main);
        var baseline = NativeLocalResultCapture.RetainedProcessCaptureCount;
        var owners = new List<NativeLocalResultCapture>();
        try
        {
            // Exact Closed registry records can remain referenced by a known empty reservation.
            // This fills the capture quota without pretending there is another 32-slot reserve pool,
            // or relying on how much global registry capacity earlier unknown cases still retain.
            for (var index = 0; index < 32 - baseline; index++)
            {
                var bundle = new Lease();
                var session = registry.RegisterPrepared(new(new("capture.pending", "capacity"), 1, "chart"), 69_910_000 + index, bundle);
                var claim = registry.BeginCommit(session); owners.Add(NativeLocalResultCapture.ReservePending(main, registry, claim, bundle));
                Retire(registry, session);
            }
            Equal(32, NativeLocalResultCapture.RetainedProcessCaptureCount);
            using (var fixture = new Fixture())
            {
                fixture.Bind(); fixture.Registry.Commit(fixture.Claim);
                Throws<InvalidOperationException>(() => NativeLocalResultCapture.CreateBackend(fixture.Main, fixture.Backend, fixture.Registry, fixture.Session));
                Equal(0, fixture.Backend.SourceReads); Equal(0, fixture.Backend.Roots);
            }
            using var pending = new Fixture();
            Throws<InvalidOperationException>(() => pending.Reserve()); Equal(32, NativeLocalResultCapture.RetainedProcessCaptureCount);
            if (owners.Count != 0)
            {
                owners[0].Dispose(); owners.RemoveAt(0);
                var replacement = pending.Reserve(); replacement.CancelEmptyPendingReservation();
                Equal(31, NativeLocalResultCapture.RetainedProcessCaptureCount);
            }
        }
        finally { foreach (var owner in owners) owner.Dispose(); }
        Equal(baseline, NativeLocalResultCapture.RetainedProcessCaptureCount);
    }

    private static void MissingInformation()
    {
        using var fixture = new Fixture(); var owner = fixture.Reserve(); fixture.Bind(); owner.AttachPendingBackend(fixture.Backend);
        fixture.Registry.Commit(fixture.Claim); fixture.Backend.Source = fixture.Backend.Source with { Information = 0 };
        var baseline = NativeLocalResultCapture.RetainedProcessCaptureCount;
        var failure = CaptureFailure(owner.InitializeWhenCommitted); Check(ReferenceEquals(owner, failure.Responsibility));
        Check(owner.CleanupUnknown && owner.NativeWorkAttempted); Equal(0, owner.RootRecords); Equal(1, fixture.Backend.SourceReads);
        Throws<InvalidOperationException>(owner.InitializeWhenCommitted); Throws<InvalidOperationException>(owner.CancelEmptyPendingReservation);
        Throws<InvalidOperationException>(() => owner.Capture()); Equal(1, fixture.Backend.SourceReads);
        Check(!fixture.Registry.Read(fixture.Session).ResultCallClaimed); Equal(0, fixture.Backend.Invocations);
        fixture.Retire(); Throws<InvalidOperationException>(owner.Dispose); Equal(baseline, NativeLocalResultCapture.RetainedProcessCaptureCount);
        fixture.PreserveUnknown = true;
    }

    private static void PartialRetentionFailure()
    {
        using var fixture = new Fixture(); var owner = fixture.Reserve(); fixture.Bind(); owner.AttachPendingBackend(fixture.Backend);
        fixture.Registry.Commit(fixture.Claim); fixture.Backend.FailInformationRetention = true;
        CaptureFailure(owner.InitializeWhenCommitted); Check(owner.CleanupUnknown); Equal(1, owner.RootRecords); Equal(1, fixture.Backend.Roots);
        fixture.Backend.FailInformationRetention = false; Throws<InvalidOperationException>(owner.InitializeWhenCommitted);
        Equal(1, fixture.Backend.Roots); Equal(0, fixture.Backend.Frees); Equal(0, fixture.Backend.Invocations);
        fixture.Retire(); Throws<InvalidOperationException>(owner.Dispose); Equal(1, owner.RootRecords); fixture.PreserveUnknown = true;
    }

    private static void NativeInitializationFailure()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        using var borrowed = native.Api.Root(native.Instance); native.FieldType = native.Object;
        native.FieldBytes = BitConverter.GetBytes(native.OtherObject.ToInt64()); native.FailFreeHandle = 2;
        var profile = new AdapterBuildProfile("fixture", WorkshopChartModule.Fingerprint, "fixture", "2022.3.62f2", "fixture", "fixture", new Dictionary<string, TypeBinding>());
        var backend = new NativeLocalResultCaptureBackend(native.Api, main, profile, new(1), borrowed);
        typeof(NativeLocalResultCaptureBackend).GetField("_verified", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, true);
        typeof(NativeLocalResultCaptureBackend).GetField("_normal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, native.Api.ObjectClass(native.Instance));
        var registry = new LocalRunSessionRegistry(main); var bundle = new Lease();
        var session = registry.RegisterPrepared(new(new("capture.pending", "native-publication"), 1, "chart"), 69_920_000, bundle);
        var claim = registry.BeginCommit(session); var owner = NativeLocalResultCapture.ReservePending(main, registry, claim, bundle);
        registry.BindPendingController(claim, native.Instance.ToInt64(), new Lease()); owner.AttachPendingBackend(backend);
        Equal(0, native.FieldReads); registry.Commit(claim); var calls = NativePublicationCall.RetainedCallCount;
        var failure = CaptureFailure(owner.InitializeWhenCommitted); Check(ReferenceEquals(owner, failure.Responsibility));
        Check(owner.CleanupUnknown && backend.CleanupUnknown); Equal(1, native.FieldReads); Equal(1, native.FreeHandleAttempts);
        Equal(calls + 1, NativePublicationCall.RetainedCallCount); Equal(0, native.Invocations);
        Throws<InvalidOperationException>(owner.InitializeWhenCommitted); Throws<InvalidOperationException>(owner.CancelEmptyPendingReservation);
        Equal(1, native.FieldReads); Equal(1, native.FreeHandleAttempts);
        Retire(registry, session); Throws<InvalidOperationException>(owner.Dispose);
    }

    private static AdapterBuildProfile UnsupportedProfile()
        => new("fixture", "unsupported", "fixture", "fixture", "fixture", "fixture", new Dictionary<string, TypeBinding>());
    private static NativeLocalResultCaptureException CaptureFailure(Action action)
    { try { action(); } catch (NativeLocalResultCaptureException failure) { return failure; } throw new Exception("Expected retained capture preparation failure."); }
    private static void Retire(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        if (registry.Read(session).Phase == LocalRunSessionPhase.Closed) return;
        registry.BeginReturn(session); var token = registry.VerifyConsumersEnded(session, new FixtureVerifier())!; Check(registry.Close(session, token));
    }
    private sealed class FixtureVerifier : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled pending capture fixture retirement; not native game acceptance."; return true; } }
    private sealed class Lease : IDisposable
    { internal int Frees; public void Dispose() => Frees++; }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunSessionRegistry.CommitClaim Claim;
        internal readonly Backend Backend;
        internal readonly Lease Bundle = new();
        internal NativeLocalResultCapture? Owner;
        internal bool PreserveUnknown;
        internal Fixture()
        {
            Registry = new(Main); Backend = new(Main);
            Session = Registry.RegisterPrepared(new(new("capture.pending", "song"), 1, "chart"), Backend.Source.Metadata, Bundle);
            Claim = Registry.BeginCommit(Session);
        }
        internal NativeLocalResultCapture Reserve() => Owner = NativeLocalResultCapture.ReservePending(Main, Registry, Claim, Bundle);
        internal void Bind() => Registry.BindPendingController(Claim, Backend.Source.Controller, new Lease());
        internal void Retire() => NativeLocalResultCapturePendingRegression.Retire(Registry, Session);
        public void Dispose()
        { try { Retire(); if (!PreserveUnknown) Owner?.Dispose(); } finally { Main.Dispose(); } }
    }
    private sealed class Backend : INativeLocalResultCaptureBackend
    {
        private static long _nextIdentity = 69_000_000;
        private readonly MainThreadDispatcher _main;
        internal NativeLocalResultSource Source;
        internal readonly long Result;
        internal int Builds, SourceReads, Roots, Frees, Invocations;
        internal bool FailInformationRetention;
        internal Action? BeforeBuild;
        internal Backend(MainThreadDispatcher main)
        { _main = main; var id = Interlocked.Add(ref _nextIdentity, 16); Source = new(id + 1, id + 2, id + 3, id + 4); Result = id + 5; }
        public bool CleanupUnknown => false;
        public void RequireSupportedBuild() { _main.VerifyAccess(); Builds++; BeforeBuild?.Invoke(); }
        public NativeLocalResultSource ReadSource() { _main.VerifyAccess(); SourceReads++; return Source; }
        private NativeRuntime.RootHandle Root(uint id, long pointer)
        { Roots++; return new(id, _ => Frees++, _ => new(pointer), _main.VerifyAccess); }
        public NativeRuntime.RootHandle RetainController() => Root(1, Source.Controller);
        public NativeRuntime.RootHandle RetainInformation()
        { if (FailInformationRetention) throw new IOException("Unconfirmed native information root allocation."); return Root(2, Source.Information); }
        public void InvokeControllerOver(Action<NativeRuntime.RootHandle> publish) { Invocations++; publish(Root(3, Result)); }
        public NativeLocalResultValues ReadResult(NativeRuntime.RootHandle result)
        { Equal(Result, result.Target.ToInt64()); return new(Source.Metadata, 100, .5, 1, 2, 3, 4, false, false); }
    }
}
