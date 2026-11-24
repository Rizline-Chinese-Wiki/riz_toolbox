using System.Reflection;
using System.Runtime.CompilerServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Real session registry and NativeLocalResultCapture, driven through the host
/// termination queue. Controlled native exports/backends are not game acceptance.</summary>
internal static class LocalRunTerminationHandlerRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Termination admission requires an exact committed controller, five gates and explicit sink/commands", Admission),
        ("Pending termination slots reserve before controller publication and permit only pure empty cancellation", PendingReservation),
        ("Pending controller publication is held until its exact factory is adopted without a second work slot", PendingController),
        ("Early pending LevelOver stays held and registry Commit alone cannot enable capture", PendingEarlyTerminal),
        ("Every non-ready exact command stays held through the registry commit gap", PendingCommands),
        ("Pending reservations share the existing fixed work capacity before native attempts", PendingCapacity),
        ("LevelOver only marks and queues; duplicate tails mark again without synchronous capture", QueueAndDrain),
        ("Queued actual capture survives provider withdrawal and preserves cached result ownership", WithdrawalAndRetirement),
        ("Completed result UI cannot retire native consumers or release the registered factory", PresentationIsNotRetirement),
        ("Termination worker and nested pump reject reentry before native or UI operations", ThreadAndReentry),
        ("Retry and result boundaries execute only explicit exact host commands", ExplicitCommands),
        ("Termination fixed process slots reject duplicate factories and release only verified records", Capacity),
        ("Native termination factory rejects historical same-address session proof before Create or release", NativeFactoryExactSession),
        ("Native termination factory strongly retains an abandoned owned root before queue registration", NativeFactoryStrongRetention),
        ("Native termination factory reserves bounded process capacity before adopting a caller root", NativeFactoryCapacity),
        ("Native termination factory rejects worker and release callback reentry before native root access", NativeFactoryReentry),
        // Unknown responsibility deliberately stays process-retained. No reset/force-free.
        ("Rejected terminal context and unknown native observation cannot queue a fake success", UnknownBoundary),
        ("Production NativeRuntime capture factory rejects unsupported build before reading its owned root", NativeFactoryAdmission),
        ("Native termination factory unknown free keeps process ownership and never retries", NativeFactoryUnknownFree),
        ("Production native result-source cleanup failure is retained by the termination slot", NativeBackendFailure),
        ("Capture creation, invocation and UI failures retain responsibility without retry", CaptureAndUiFailures),
        ("Result presentation must publish synchronously and late publication is rejected", LatePresentation),
        ("Unknown host command and retirement disposal failures are sticky and never repeated", CommandAndCleanupFailure)
    };
    private static void Admission()
    {
        using var f = new Fixture(register: false, commit: false);
        Throws<InvalidOperationException>(() => f.Register()); Equal(0, f.Factory.Creates);
        f.Registry.Commit(f.Commit);
        var noSink = new LocalRunTerminationHandler(f.Main, f.Registry, f.Boundaries, null, f.Commands);
        Throws<InvalidOperationException>(() => noSink.ValidateHost(f.Registry));
        Throws<InvalidOperationException>(() => noSink.RegisterCommitted(f.Session, f.Factory));
        var noCommands = new LocalRunTerminationHandler(f.Main, f.Registry, f.Boundaries, f.Sink, null);
        Throws<InvalidOperationException>(() => noCommands.ValidateHost(f.Registry));
        Throws<InvalidOperationException>(() => f.Handler.ValidateHost(new LocalRunSessionRegistry(f.Main)));
        Throws<InvalidOperationException>(() => noCommands.RegisterCommitted(f.Session, f.Factory));
        f.Boundaries.State = f.Boundaries.State with { AllFiveGatesInstalled = false };
        Throws<InvalidOperationException>(() => f.Handler.ValidateHost(f.Registry));
        Throws<InvalidOperationException>(() => f.Register());
        f.Boundaries.State = f.Boundaries.State with { AllFiveGatesInstalled = true, NativeBoundaryStateKnown = false };
        Throws<InvalidOperationException>(() => f.Register());
        f.Boundaries.State = f.Boundaries.State with { NativeBoundaryStateKnown = true, InFlightNativeBoundaries = 1 };
        Throws<InvalidOperationException>(() => f.Register());
        f.Boundaries.State = f.Boundaries.State with { InFlightNativeBoundaries = 0 };
        f.Handler.ValidateHost(f.Registry);
        var exact = f.Factory.ControllerIdentity; f.Factory.DeclaredController = exact + 1;
        Throws<InvalidOperationException>(() => f.Register()); f.Factory.DeclaredController = exact;
        f.Register(); Throws<InvalidOperationException>(() => f.Register());
        Throws<InvalidOperationException>(() => noSink.Handle(LocalBoundaryKind.ControllerLevelOver, f.Session, exact));
        Equal(0, f.Factory.Creates); Equal(0, f.Factory.Releases); Check(f.Handler.HasPendingWork);
    }
    private static void QueueAndDrain()
    {
        using var f = new Fixture(); f.Boundaries.Events.Clear(); f.Boundaries.State = f.Boundaries.State with { InFlightNativeBoundaries = 1 };
        f.Queue(); Equal("mark,read", string.Join(",", f.Boundaries.Events));
        Equal(0, f.Factory.Creates); Equal(0, f.Backend.Invocations); Check(f.Handler.Read(f.Session).Queued);
        f.Queue(); Equal(2, f.Boundaries.Marks); Equal(0, f.Factory.Creates);
        f.Handler.Tick(); Equal(0, f.Factory.Creates); // Same native frame remains forbidden.
        f.Advance(); f.Handler.Tick(); Equal(0, f.Factory.Creates); // Real around is still in flight.
        f.Boundaries.State = f.Boundaries.State with { InFlightNativeBoundaries = 0 };
        f.Handler.Tick(); Equal(1, f.Factory.Creates); Equal(1, f.Backend.Invocations); Equal(1, f.Sink.Presents);
        f.Queue(); f.Advance(); f.Handler.Tick(); Equal(3, f.Boundaries.Marks);
        Equal(1, f.Backend.Invocations); Equal(1, f.Sink.Presents); Check(f.Handler.HasPendingWork);
    }
    private static void PendingReservation()
    {
        var baseline = LocalRunTerminationHandler.RetainedProcessWorkCount;
        using var f = new PendingFixture(); Equal(baseline + 1, LocalRunTerminationHandler.RetainedProcessWorkCount);
        var snapshot = f.Handler.Read(f.Session); Check(snapshot.PendingReservation && snapshot.Held && !snapshot.ControllerAttached);
        Check(f.Handler.HasPendingWork && !f.Launch.HasNativeAttempt && f.Launch.CanCancelPureReservation);
        Equal(0L, f.Registry.Read(f.Session).ControllerIdentity); Equal(0, f.Factory.Creates);
        Throws<InvalidOperationException>(() => f.Handler.ReservePending(f.Launch));
        Throws<InvalidOperationException>(() => f.Handler.RegisterCommitted(f.Session, f.Factory));
        using var other = new PendingFixture(reserve: false);
        Throws<InvalidOperationException>(() => f.Handler.ReservePending(other.Launch));
        var noSink = new LocalRunTerminationHandler(other.Main, other.Registry, other.Boundaries, null, other.Commands);
        Throws<InvalidOperationException>(() => noSink.ReservePending(other.Launch)); Check(!noSink.HasPendingWork);
        Check(f.Handler.CancelPurePending(f.Launch)); Check(!f.Handler.HasPendingWork);
        Equal(baseline, LocalRunTerminationHandler.RetainedProcessWorkCount); Equal(0, f.Factory.Releases);
        Equal(LocalRunSessionPhase.PendingCommit, f.Registry.Read(f.Session).Phase); Check(!f.Launch.IsCommittedReady);
        Throws<InvalidOperationException>(() => f.Handler.CancelPurePending(f.Launch));
    }
    private static void PendingController()
    {
        using var f = new PendingFixture(); var baseline = LocalRunTerminationHandler.RetainedProcessWorkCount;
        f.BindController(attach: false); Check(f.Launch.HasNativeAttempt && !f.Launch.CanCancelPureReservation);
        Check(!f.Handler.CancelPurePending(f.Launch));
        f.Handler.Handle(LocalBoundaryKind.ControllerRetry, f.Session, f.Backend.Source.Controller);
        f.Handler.Tick(); Check(!f.Handler.Read(f.Session).Unknown); Equal(0, f.Commands.Calls);
        var exact = f.Factory.DeclaredController; f.Factory.DeclaredController++;
        Throws<InvalidOperationException>(() => f.Handler.AttachPendingController(f.Launch, f.Factory));
        f.Factory.DeclaredController = exact; f.Handler.AttachPendingController(f.Launch, f.Factory);
        Equal(baseline, LocalRunTerminationHandler.RetainedProcessWorkCount); Check(f.Handler.Read(f.Session).ControllerAttached);
        Check(f.Handler.IsPendingPrepared(f.Launch)); Check(!f.Launch.IsCommittedReady);
        Throws<InvalidOperationException>(() => f.Handler.AttachPendingController(f.Launch, f.Factory));
        Throws<InvalidOperationException>(() => f.Handler.CompletePending(f.Launch));
        Equal(0, f.Factory.Creates); Equal(0, f.Backend.Invocations); Equal(0, f.Factory.Releases);
    }
    private static void PendingEarlyTerminal()
    {
        using var f = new PendingFixture(); f.BindController(attach: false); f.Boundaries.Events.Clear();
        f.Handler.Handle(LocalBoundaryKind.ControllerLevelOver, f.Session, f.Backend.Source.Controller);
        var held = f.Handler.Read(f.Session);
        Check(held.Held && held.EarlyTerminal && !held.Queued && !held.CaptureAttempted && !held.Unknown);
        Check(f.Launch.Read().EarlyTerminal); Equal(0, f.Boundaries.Marks); Equal(0, f.Boundaries.Events.Count);
        f.Handler.AttachPendingController(f.Launch, f.Factory); Check(!f.Handler.IsPendingPrepared(f.Launch));
        Throws<InvalidOperationException>(() => f.Launch.PrepareCommit());
        f.Boundaries.State = f.Boundaries.State with { Frame = 500 }; f.Handler.Tick(); Equal(0, f.Factory.Creates);
        // A caller bypassing the coordinator cannot turn registry state into shared readiness.
        f.Registry.Commit(f.Commit); f.Handler.Tick(); Check(!f.Launch.IsCommittedReady && f.Handler.Read(f.Session).Held);
        Throws<InvalidOperationException>(() => f.Handler.CompletePending(f.Launch));
        Equal(0, f.Backend.Invocations); Equal(0, f.Sink.Presents); Check(!f.Handler.CancelPurePending(f.Launch));
    }
    private static void PendingCommands()
    {
        using var f = new PendingFixture(); f.BindController();
        f.Handler.Handle(LocalBoundaryKind.ControllerRetry, f.Session, f.Backend.Source.Controller);
        Task.Run(() => {
            Throws<InvalidOperationException>(() => f.Handler.ReservePending(f.Launch));
            Throws<InvalidOperationException>(() => f.Handler.AttachPendingController(f.Launch, f.Factory));
            Throws<InvalidOperationException>(() => f.Handler.CompletePending(f.Launch));
            Throws<InvalidOperationException>(() => f.Handler.Handle(LocalBoundaryKind.ControllerRetry, f.Session, f.Backend.Source.Controller));
        }).GetAwaiter().GetResult();
        f.Registry.Commit(f.Commit); var scene = f.Backend.Source.Controller + 50;
        f.Registry.BindResultScene(f.Session, scene, 1, new Lease());
        foreach (var boundary in new[] { LocalBoundaryKind.ResultStart, LocalBoundaryKind.ResultRetry, LocalBoundaryKind.ResultBack })
            f.Handler.Handle(boundary, f.Session, scene);
        Throws<InvalidOperationException>(() => f.Handler.Handle(LocalBoundaryKind.ResultBack, f.Session, scene + 1));
        f.Handler.Tick(); Equal(0, f.Commands.Calls); Equal(0, f.Factory.Creates); Equal(0, f.Backend.Invocations);
        Check(f.Handler.Read(f.Session).Held && !f.Handler.Read(f.Session).Unknown); Check(!f.Launch.IsCommittedReady);
    }
    private static void PendingCapacity()
    {
        var baseline = LocalRunTerminationHandler.RetainedProcessWorkCount; var fixtures = new List<PendingFixture>();
        var available = Math.Min(32 - baseline, 32 - LocalRunSessionRegistry.RetainedProcessSessionCount);
        try
        {
            for (var index = 0; index < available; index++) fixtures.Add(new());
            Equal(baseline + available, LocalRunTerminationHandler.RetainedProcessWorkCount);
            Check(LocalRunTerminationHandler.RetainedProcessWorkCount <= 32);
            foreach (var fixture in fixtures) { Check(!fixture.Launch.HasNativeAttempt); Equal(0, fixture.Factory.Creates); Equal(0, fixture.Backend.Invocations); }
            var first = fixtures[0]; Throws<InvalidOperationException>(() => first.Handler.ReservePending(first.Launch));
            Check(first.Handler.CancelPurePending(first.Launch)); first.Dispose(); fixtures.RemoveAt(0);
            using var replacement = new PendingFixture(); Check(replacement.Handler.HasPendingWork); Equal(0, replacement.Factory.Creates);
        }
        finally { foreach (var fixture in fixtures) fixture.Dispose(); }
        Equal(baseline, LocalRunTerminationHandler.RetainedProcessWorkCount);
    }
    private static void WithdrawalAndRetirement()
    {
        using var f = new Fixture(); f.Queue(); f.Registry.MarkProviderWithdrawn(f.Session); f.Advance(); f.Handler.Tick();
        Equal(1, f.Backend.Invocations); Check(f.Registry.Read(f.Session).ProviderWithdrawn);
        var result = f.Registry.Read(f.Session).Result!;
        Check(ReferenceEquals(result, f.Sink.Result)); Check(ReferenceEquals(result, f.Factory.Capture!.Capture()));
        Equal(f.Backend.ResultIdentity, result.NativeResultIdentity); Equal(12345L, result.Copy.Score);
        Check(!result.Copy.AssociatedWithObservedStart); Check(f.Handler.HasPendingWork);
        f.RetireSession(); f.Handler.Tick(); Check(f.Handler.HasPendingWork); Equal(0, f.Factory.Releases);
        f.Sink.View.Ended = true; f.Handler.Tick(); Check(!f.Handler.HasPendingWork);
        Equal(1, f.Factory.Releases); Equal(1, f.Sink.View.Disposals); Equal(3, f.Backend.Frees);
    }
    private static void PresentationIsNotRetirement()
    {
        using var f = new Fixture(); f.Queue(); f.Advance(); f.Handler.Tick(); f.Sink.View.Ended = true;
        f.Advance(); f.Handler.Tick(); f.Handler.Tick();
        Equal(LocalRunSessionPhase.ResultReady, f.Registry.Read(f.Session).Phase);
        Check(!f.Registry.Read(f.Session).ProtectionRetired && !f.Registry.Read(f.Session).ReturnRequested);
        Check(f.Handler.HasPendingWork); Equal(0, f.Factory.Releases); Equal(0, f.Backend.Frees); Equal(0, f.Sink.View.Disposals);
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture();
        Task.Run(() => {
            Throws<InvalidOperationException>(() => f.Queue()); Throws<InvalidOperationException>(() => f.Handler.Tick());
            Throws<InvalidOperationException>(() => f.Register()); Throws<InvalidOperationException>(() => { _ = f.Handler.HasPendingWork; });
        }).GetAwaiter().GetResult();
        Equal(0, f.Boundaries.Marks); Equal(0, f.Backend.Invocations);
        f.Queue(); f.Advance();
        f.Backend.BeforeInvoke = () => {
            Throws<InvalidOperationException>(() => f.Handler.Tick()); Throws<InvalidOperationException>(() => f.Queue());
            Throws<InvalidOperationException>(() => f.Register()); Check(f.Handler.HasPendingWork);
        };
        f.Sink.BeforePublish = () => Throws<InvalidOperationException>(() => f.Handler.Tick());
        f.Handler.Tick(); Equal(1, f.Backend.Invocations); Equal(1, f.Sink.Presents); Check(!f.Handler.Read(f.Session).Unknown);
    }
    private static void ExplicitCommands()
    {
        using var f = new Fixture(); var controller = f.Backend.Source.Controller;
        Throws<InvalidOperationException>(() => f.Handler.Handle(LocalBoundaryKind.ControllerRetry, f.Session, controller + 1));
        Equal(0, f.Commands.Calls); f.Handler.Handle(LocalBoundaryKind.ControllerRetry, f.Session, controller); Equal(1, f.Commands.Calls);
        var resultScene = controller + 100;
        f.Registry.BindResultScene(f.Session, resultScene, 1, new Lease());
        foreach (var boundary in new[] { LocalBoundaryKind.ResultStart, LocalBoundaryKind.ResultRetry, LocalBoundaryKind.ResultBack })
            f.Handler.Handle(boundary, f.Session, resultScene);
        Equal(4, f.Commands.Calls); Equal(0, f.Factory.Creates); Equal(0, f.Backend.Invocations);
    }
    private static void Capacity()
    {
        var fixtures = new List<Fixture>(); var baseline = LocalRunTerminationHandler.RetainedProcessWorkCount;
        var available = Math.Min(32 - baseline, 32 - LocalRunSessionRegistry.RetainedProcessSessionCount);
        try
        {
            for (var index = 0; index < available; index++) fixtures.Add(new());
            Equal(baseline + available, LocalRunTerminationHandler.RetainedProcessWorkCount);
            Check(LocalRunTerminationHandler.RetainedProcessWorkCount <= 32);
            var first = fixtures[0];
            var duplicate = new LocalRunTerminationHandler(first.Main, first.Registry, first.Boundaries, first.Sink, first.Commands);
            Throws<InvalidOperationException>(() => duplicate.RegisterCommitted(first.Session, first.Factory));
            Equal(0, first.Factory.Creates); Equal(0, first.Factory.Releases);
            first.Dispose(); fixtures.RemoveAt(0); Equal(baseline + available - 1, LocalRunTerminationHandler.RetainedProcessWorkCount);
        }
        finally { foreach (var fixture in fixtures) fixture.Dispose(); }
        Equal(baseline, LocalRunTerminationHandler.RetainedProcessWorkCount);
    }
    private static void UnknownBoundary()
    {
        using var rejected = new Fixture(); rejected.Boundaries.MarkAccepted = false; rejected.Boundaries.Events.Clear(); rejected.Queue();
        Check(rejected.Handler.Read(rejected.Session).Unknown && !rejected.Handler.Read(rejected.Session).Queued);
        Equal("mark", string.Join(",", rejected.Boundaries.Events)); rejected.Boundaries.MarkAccepted = true;
        rejected.Queue(); rejected.Advance(); rejected.Handler.Tick(); Equal(0, rejected.Factory.Creates); Check(rejected.Handler.HasPendingWork);
        using var unknown = new Fixture(); unknown.Queue(); unknown.Advance();
        unknown.Boundaries.State = unknown.Boundaries.State with { NativeBoundaryStateKnown = false };
        unknown.Handler.Tick(); Check(unknown.Handler.Read(unknown.Session).Unknown); unknown.Boundaries.State = unknown.Boundaries.State with { NativeBoundaryStateKnown = true };
        unknown.Handler.Tick(); Equal(0, unknown.Factory.Creates); Equal(0, unknown.Backend.Invocations);
    }
    private static void NativeFactoryAdmission()
    {
        using var f = new Fixture(register: false); using var native = new FakeNative(); var targetReads = 0; var rootFrees = 0;
        var owned = new NativeRuntime.RootHandle(1, _ => rootFrees++, _ => { targetReads++; return new(f.Backend.Source.Controller); }, f.Main.VerifyAccess);
        var profile = new AdapterBuildProfile("fixture", "unsupported", "fixture", "fixture", "fixture", "fixture", new Dictionary<string, TypeBinding>());
        var production = new NativeLocalRunTerminationCaptureFactory(native.Api, f.Main, profile, IntPtr.Zero,
            f.Registry, f.Session, f.Backend.Source.Controller, owned);
        f.Handler.RegisterCommitted(f.Session, production); f.Registered = true;
        f.Queue(); Equal(0, targetReads); f.Advance(); f.Handler.Tick();
        Check(f.Handler.Read(f.Session).Unknown); Equal(0, targetReads); Equal(0, rootFrees); Equal(0, native.Invocations);
        f.Handler.Tick(); Equal(0, native.Invocations); Check(f.Handler.HasPendingWork);
    }

    private static AdapterBuildProfile UnsupportedFactoryProfile()
        => new("fixture", "unsupported", "fixture", "fixture", "fixture", "fixture", new Dictionary<string, TypeBinding>());

    private static void NativeFactoryExactSession()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        var registry = new LocalRunSessionRegistry(main); const long address = 78_990_001;
        LocalRunSessionRegistry.SessionHandle Register(long metadata)
        {
            var session = registry.RegisterPrepared(new(new("termination.test", "reused-address"), 1, "chart"), metadata, new Lease());
            var claim = registry.BeginCommit(session); registry.BindPendingController(claim, address, new Lease()); registry.Commit(claim);
            return session;
        }
        var oldSession = Register(78_990_000); Retire(registry, oldSession);
        var currentSession = Register(78_990_100); var targetReads = 0; var frees = 0;
        var root = new NativeRuntime.RootHandle(91, _ => frees++, _ => { targetReads++; return new(address); }, main.VerifyAccess);
        var baseline = NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount;
        var factory = new NativeLocalRunTerminationCaptureFactory(native.Api, main, UnsupportedFactoryProfile(), IntPtr.Zero,
            registry, currentSession, address, root);
        Equal(baseline + 1, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(registry, oldSession));
        Throws<InvalidOperationException>(() => factory.Create(registry, oldSession));
        Equal(0, targetReads); Equal(0, frees); Equal(0, native.Invocations);
        Check(registry.IsProtected(LocalBoundaryKind.ControllerLevelOver, address));
        Retire(registry, currentSession); factory.ReleaseAfterConsumersEnded(registry, currentSession);
        Equal(1, frees); Equal(0, targetReads); Equal(baseline, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        // A retired session cannot adopt another root even if its historical number matches.
        using var rejected = new NativeRuntime.RootHandle(92, _ => frees++, _ => { targetReads++; return new(address); }, main.VerifyAccess);
        Throws<InvalidOperationException>(() => new NativeLocalRunTerminationCaptureFactory(native.Api, main, UnsupportedFactoryProfile(), IntPtr.Zero,
            registry, currentSession, address, rejected));
        Equal(1, frees); Equal(0, targetReads); Equal(baseline, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
    }

    private static void NativeFactoryStrongRetention()
    {
        using var fixture = new Fixture(register: false); using var native = new FakeNative();
        var frees = 0; var baseline = NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount;
        var weak = AbandonNativeFactory(fixture, native.Api, () => frees++);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weak.TryGetTarget(out var factory)); Equal(0, frees);
        Equal(baseline + 1, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        fixture.RetireSession(); factory!.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session);
        Equal(1, frees); Equal(baseline, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NativeLocalRunTerminationCaptureFactory> AbandonNativeFactory(Fixture fixture, NativeRuntime api, Action free)
    {
        var root = new NativeRuntime.RootHandle(93, _ => free(), _ => new(fixture.Backend.Source.Controller), fixture.Main.VerifyAccess);
        return new(new NativeLocalRunTerminationCaptureFactory(api, fixture.Main, UnsupportedFactoryProfile(), IntPtr.Zero,
            fixture.Registry, fixture.Session, fixture.Backend.Source.Controller, root));
    }

    private static void NativeFactoryCapacity()
    {
        using var fixture = new Fixture(register: false); using var native = new FakeNative();
        var baseline = NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount; var frees = 0; var targetReads = 0;
        var owned = new List<NativeLocalRunTerminationCaptureFactory>();
        for (uint index = 0; index < 32 - baseline; index++)
        {
            var root = new NativeRuntime.RootHandle(100 + index, _ => frees++, _ => { targetReads++; return new(fixture.Backend.Source.Controller); }, fixture.Main.VerifyAccess);
            owned.Add(new NativeLocalRunTerminationCaptureFactory(native.Api, fixture.Main, UnsupportedFactoryProfile(), IntPtr.Zero,
                fixture.Registry, fixture.Session, fixture.Backend.Source.Controller, root));
        }
        var rejected = new NativeRuntime.RootHandle(199, _ => frees++, _ => { targetReads++; return new(fixture.Backend.Source.Controller); }, fixture.Main.VerifyAccess);
        Throws<InvalidOperationException>(() => new NativeLocalRunTerminationCaptureFactory(native.Api, fixture.Main, UnsupportedFactoryProfile(), IntPtr.Zero,
            fixture.Registry, fixture.Session, fixture.Backend.Source.Controller, rejected));
        Equal(32, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount); Equal(0, targetReads); Equal(0, frees);
        // Rejection leaves this root usable and caller-owned; it is not hidden in the full pool.
        Equal(fixture.Backend.Source.Controller, rejected.Target.ToInt64()); rejected.Dispose(); Equal(1, frees);
        fixture.RetireSession(); foreach (var factory in owned) factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session);
        Equal(owned.Count + 1, frees); Equal(1, targetReads);
        Equal(baseline, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
    }

    private static void NativeFactoryReentry()
    {
        using var fixture = new Fixture(register: false); using var native = new FakeNative();
        var targetReads = 0; var frees = 0; NativeLocalRunTerminationCaptureFactory? factory = null;
        var root = new NativeRuntime.RootHandle(200, _ => {
            frees++;
            Throws<InvalidOperationException>(() => factory!.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
            Throws<InvalidOperationException>(() => factory!.Create(fixture.Registry, fixture.Session));
        }, _ => { targetReads++; return new(fixture.Backend.Source.Controller); }, fixture.Main.VerifyAccess);
        factory = new NativeLocalRunTerminationCaptureFactory(native.Api, fixture.Main, UnsupportedFactoryProfile(), IntPtr.Zero,
            fixture.Registry, fixture.Session, fixture.Backend.Source.Controller, root);
        Task.Run(() => {
            Throws<InvalidOperationException>(() => factory.Create(fixture.Registry, fixture.Session));
            Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        }).GetAwaiter().GetResult();
        Equal(0, targetReads); Equal(0, frees);
        fixture.RetireSession(); factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session);
        Equal(1, frees); Equal(0, targetReads); Equal(0, native.Invocations);
    }

    private static void NativeFactoryUnknownFree()
    {
        using var fixture = new Fixture(register: false); using var native = new FakeNative(); var frees = 0;
        var baseline = NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount;
        var root = new NativeRuntime.RootHandle(201, _ => { frees++; throw new IOException("Unknown native factory root free."); },
            _ => new(fixture.Backend.Source.Controller), fixture.Main.VerifyAccess);
        var factory = new NativeLocalRunTerminationCaptureFactory(native.Api, fixture.Main, UnsupportedFactoryProfile(), IntPtr.Zero,
            fixture.Registry, fixture.Session, fixture.Backend.Source.Controller, root);
        fixture.RetireSession(); Throws<IOException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Check(factory.CleanupUnknown); Equal(1, frees);
        Equal(baseline + 1, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
        Throws<InvalidOperationException>(() => factory.ReleaseAfterConsumersEnded(fixture.Registry, fixture.Session));
        Equal(1, frees); Equal(baseline + 1, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
    }
    private static void NativeBackendFailure()
    {
        // Exercise production ReadSource and the actual NativeRuntime publication cleanup.
        // Reflection skips only pinned-image discovery for controlled exports; not game acceptance.
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        using var borrowed = native.Api.Root(native.Instance); native.FieldType = native.Object;
        native.FieldBytes = BitConverter.GetBytes(native.OtherObject.ToInt64()); native.FailFreeHandle = 2;
        var registry = new LocalRunSessionRegistry(main); var boundaries = new Boundaries(); var sink = new Sink();
        var session = registry.RegisterPrepared(new(new("termination.test", "native-read"), 1, "chart"), 78_999_999, new Lease());
        var commit = registry.BeginCommit(session); registry.BindPendingController(commit, native.Instance.ToInt64(), new Lease()); registry.Commit(commit);
        var profile = new AdapterBuildProfile("fixture", WorkshopChartModule.Fingerprint, "fixture", "2022.3.62f2", "fixture", "fixture", new Dictionary<string, TypeBinding>());
        var backend = new NativeLocalResultCaptureBackend(native.Api, main, profile, new(1), borrowed);
        typeof(NativeLocalResultCaptureBackend).GetField("_verified", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, true);
        typeof(NativeLocalResultCaptureBackend).GetField("_normal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, native.Api.ObjectClass(native.Instance));
        var factory = new Factory(main, native.Instance.ToInt64(), backend);
        var handler = new LocalRunTerminationHandler(main, registry, boundaries, sink, new Commands()); handler.RegisterCommitted(session, factory);
        handler.Handle(LocalBoundaryKind.ControllerLevelOver, session, native.Instance.ToInt64());
        boundaries.State = boundaries.State with { Frame = boundaries.State.Frame + 1 }; handler.Tick();
        Check(handler.Read(session).Unknown && handler.HasPendingWork); Equal(1, factory.Creates); Equal(1, native.FieldReads);
        Check(factory.Capture is not null && factory.Capture.CleanupUnknown); Equal(1, native.FreeHandleAttempts);
        handler.Tick(); Equal(1, factory.Creates); Equal(1, native.FieldReads); Equal(0, native.Invocations);
        Retire(registry, session); handler.Tick(); Equal(0, factory.Releases); Check(handler.HasPendingWork);
    }
    private static void CaptureAndUiFailures()
    {
        using var create = new Fixture(); create.Backend.ThrowReadSource = true; create.Queue(); create.Advance(); create.Handler.Tick();
        Check(create.Handler.Read(create.Session).Unknown); Check(create.Factory.Capture is not null);
        create.Backend.ThrowReadSource = false; create.Handler.Tick(); Equal(1, create.Factory.Creates); Equal(0, create.Backend.Invocations);
        using var invoke = new Fixture(); invoke.Backend.ThrowAfterPublish = true; invoke.Queue(); invoke.Advance(); invoke.Handler.Tick();
        Check(invoke.Handler.Read(invoke.Session).Unknown); Equal(1, invoke.Backend.Invocations); Equal(3, invoke.Factory.Capture!.RootRecords);
        invoke.Backend.ThrowAfterPublish = false; invoke.Handler.Tick(); Equal(1, invoke.Backend.Invocations); Equal(0, invoke.Sink.Presents);
        using var ui = new Fixture(); ui.Sink.ThrowAfterPublish = true; ui.Queue(); ui.Advance(); ui.Handler.Tick();
        Check(ui.Handler.Read(ui.Session).Unknown && ui.Handler.Read(ui.Session).PresentationPublished);
        ui.Sink.ThrowAfterPublish = false; ui.Handler.Tick(); Equal(1, ui.Backend.Invocations); Equal(1, ui.Sink.Presents); Equal(0, ui.Sink.View.Disposals);
    }
    private static void LatePresentation()
    {
        using var f = new Fixture(); f.Sink.DeferPublication = true; f.Queue(); f.Advance(); f.Handler.Tick();
        Check(f.Handler.Read(f.Session).Unknown && !f.Handler.Read(f.Session).PresentationPublished);
        Throws<InvalidOperationException>(() => f.Sink.Deferred!(f.Sink.View)); f.Handler.Tick();
        Equal(1, f.Sink.Presents); Equal(1, f.Backend.Invocations); Check(f.Handler.HasPendingWork);
    }
    private static void CommandAndCleanupFailure()
    {
        using var command = new Fixture(); command.Commands.Throw = true;
        command.Handler.Handle(LocalBoundaryKind.ControllerRetry, command.Session, command.Backend.Source.Controller);
        Check(command.Handler.Read(command.Session).Unknown); command.Commands.Throw = false;
        command.Handler.Handle(LocalBoundaryKind.ControllerRetry, command.Session, command.Backend.Source.Controller); Equal(1, command.Commands.Calls);
        using var cleanup = new Fixture(); cleanup.Queue(); cleanup.Advance(); cleanup.Handler.Tick(); cleanup.Sink.View.Ended = true;
        cleanup.Sink.View.ThrowDispose = true; cleanup.RetireSession(); cleanup.Handler.Tick();
        Check(cleanup.Handler.Read(cleanup.Session).Unknown && cleanup.Handler.HasPendingWork);
        Equal(1, cleanup.Sink.View.Disposals); Equal(1, cleanup.Factory.Releases); Equal(3, cleanup.Backend.Frees);
        cleanup.Sink.View.ThrowDispose = false; cleanup.Handler.Tick(); Equal(1, cleanup.Sink.View.Disposals); Equal(1, cleanup.Factory.Releases);
    }
    private static void Retire(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        if (registry.Read(session).Phase == LocalRunSessionPhase.Closed) return;
        registry.BeginReturn(session); var proof = registry.VerifyConsumersEnded(session, new Verifier())!; Check(registry.Close(session, proof));
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunSessionRegistry.CommitClaim Commit;
        internal readonly Boundaries Boundaries = new(); internal readonly Sink Sink = new(); internal readonly Commands Commands = new();
        internal readonly Backend Backend; internal readonly Factory Factory; internal readonly LocalRunTerminationHandler Handler;
        internal bool Registered; private bool _disposed;
        internal Fixture(bool register = true, bool commit = true)
        {
            Registry = new(Main); Backend = new(Main); Factory = new(Main, Backend.Source.Controller, Backend);
            Session = Registry.RegisterPrepared(new(new("termination.test", "song"), 1, "chart"), Backend.Source.Metadata, new Lease());
            Commit = Registry.BeginCommit(Session); Registry.BindPendingController(Commit, Backend.Source.Controller, new Lease());
            if (commit) Registry.Commit(Commit);
            Handler = new(Main, Registry, Boundaries, Sink, Commands); if (register) Register();
        }
        internal void Register() { Handler.RegisterCommitted(Session, Factory); Registered = true; }
        internal void Queue() => Handler.Handle(LocalBoundaryKind.ControllerLevelOver, Session, Backend.Source.Controller);
        internal void Advance() => Boundaries.State = Boundaries.State with { Frame = Boundaries.State.Frame + 1 };
        internal void RetireSession() => Retire(Registry, Session);
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { RetireSession(); Sink.View.Ended = true; if (Registered) Handler.Tick(); }
            finally { Main.Dispose(); }
        }
    }
    private sealed class PendingFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session; internal readonly LocalRunSessionRegistry.CommitClaim Commit;
        internal readonly LocalRunLaunchCommitState Launch; internal readonly Boundaries Boundaries = new(); internal readonly Sink Sink = new();
        internal readonly Commands Commands = new(); internal readonly Backend Backend; internal readonly Factory Factory;
        internal readonly LocalRunTerminationHandler Handler; private bool _disposed;
        internal PendingFixture(bool reserve = true)
        {
            Registry = new(Main); Backend = new(Main); Factory = new(Main, Backend.Source.Controller, Backend);
            var bundle = new Lease(); Session = Registry.RegisterPrepared(new(new("termination.pending", "song"), 1, "chart"), Backend.Source.Metadata, bundle);
            Commit = Registry.BeginCommit(Session); Launch = new(Main, Registry, Commit, bundle);
            Handler = new(Main, Registry, Boundaries, Sink, Commands); if (reserve) Handler.ReservePending(Launch);
        }
        internal void BindController(bool attach = true)
        {
            Registry.BindPendingController(Commit, Backend.Source.Controller, new Lease());
            if (attach) Handler.AttachPendingController(Launch, Factory);
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { Retire(Registry, Session); Sink.View.Ended = true; Handler.Tick(); }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Boundaries : ILocalRunTerminationBoundaryState
    {
        public bool IsReadyForHost(LocalRunSessionRegistry registry) => State.AllFiveGatesInstalled && State.NativeBoundaryStateKnown && State.InFlightNativeBoundaries == 0;
        internal LocalRunTerminationBoundarySnapshot State = new(true, 100, true, 0);
        internal bool MarkAccepted = true; internal int Marks; internal readonly List<string> Events = new();
        public bool MarkTerminal(LocalRunSessionRegistry.SessionHandle session, long exactController)
        { Marks++; Events.Add("mark"); return MarkAccepted; }
        public LocalRunTerminationBoundarySnapshot Read(LocalRunSessionRegistry.SessionHandle session, long exactController)
        { Events.Add("read"); return State; }
    }
    private sealed class Sink : ILocalRunTerminationResultSink
    {
        internal readonly Presentation View = new(); internal int Presents; internal LocalRunCapturedResult? Result;
        internal bool ThrowAfterPublish, DeferPublication; internal Action? BeforePublish;
        internal Action<ILocalRunTerminationResultPresentation>? Deferred;
        public void Present(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result, Action<ILocalRunTerminationResultPresentation> publish)
        {
            Presents++; Result = result; BeforePublish?.Invoke();
            if (DeferPublication) { Deferred = publish; return; }
            publish(View); if (ThrowAfterPublish) throw new IOException("Controlled UI outcome is unknown after publication.");
        }
    }
    private sealed class Presentation : ILocalRunTerminationResultPresentation
    {
        internal bool Ended, ThrowDispose; internal int Disposals;
        public bool ConsumersEnded => Ended;
        public void Dispose() { Disposals++; if (ThrowDispose) throw new IOException("Controlled UI callback cleanup is unknown."); }
    }
    private sealed class Commands : ILocalRunTerminationCommands
    {
        internal int Calls; internal bool Throw;
        public void Execute(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long exactReceiver)
        { Calls++; if (Throw) throw new IOException("Controlled host command outcome is unknown."); }
    }
    private sealed class Factory : ILocalRunTerminationCaptureFactory
    {
        private readonly MainThreadDispatcher _main; private readonly INativeLocalResultCaptureBackend _backend;
        internal long DeclaredController; internal int Creates, Releases; internal NativeLocalResultCapture? Capture;
        public long ControllerIdentity => DeclaredController;
        internal Factory(MainThreadDispatcher main, long controller, INativeLocalResultCaptureBackend backend)
            => (_main, DeclaredController, _backend) = (main, controller, backend);
        public NativeLocalResultCapture Create(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
        {
            Creates++;
            try { return Capture = NativeLocalResultCapture.CreateBackend(_main, _backend, registry, session); }
            catch (NativeLocalResultCaptureException error) { Capture = error.Responsibility; throw; }
        }
        public void ReleaseAfterConsumersEnded(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
        { Equal(LocalRunSessionPhase.Closed, registry.Read(session).Phase); Releases++; }
    }
    private sealed class Backend : INativeLocalResultCaptureBackend
    {
        private static long NextIdentity = 78_000_000;
        private readonly MainThreadDispatcher _main;
        internal readonly NativeLocalResultSource Source; internal readonly long ResultIdentity;
        internal int Invocations, Frees; internal bool ThrowReadSource, ThrowAfterPublish; internal Action? BeforeInvoke;
        public bool CleanupUnknown => ThrowReadSource;
        internal Backend(MainThreadDispatcher main)
        {
            _main = main; var identity = Interlocked.Add(ref NextIdentity, 100);
            Source = new(identity + 1, identity + 2, identity + 3, identity + 4); ResultIdentity = identity + 5;
        }
        public void RequireSupportedBuild() => _main.VerifyAccess();
        public NativeLocalResultSource ReadSource()
        { _main.VerifyAccess(); if (ThrowReadSource) throw new IOException("Controlled source publication cleanup is unknown."); return Source; }
        private NativeRuntime.RootHandle Root(uint handle, long identity)
            => new(handle, _ => Frees++, _ => new(identity), _main.VerifyAccess);
        public NativeRuntime.RootHandle RetainController() => Root(1, Source.Controller);
        public NativeRuntime.RootHandle RetainInformation() => Root(2, Source.Information);
        public void InvokeControllerOver(Action<NativeRuntime.RootHandle> publish)
        {
            _main.VerifyAccess(); Invocations++; BeforeInvoke?.Invoke(); publish(Root(3, ResultIdentity));
            if (ThrowAfterPublish) throw new IOException("Controlled actual native output cleanup is unknown.");
        }
        public NativeLocalResultValues ReadResult(NativeRuntime.RootHandle result)
        { Equal(ResultIdentity, result.Target.ToInt64()); return new(Source.Metadata, 12345, .5, 1, 2, 3, 4, false, false); }
    }
    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    {
        public bool Verify(LocalRunRetirementRequest request, out string evidence)
        { evidence = "Controlled termination fixture consumers ended; not actual native retirement evidence."; return true; }
    }
}
