using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Native termination bridge rejects unsupported profiles before target access or native installation", Unsupported),
        ("Native termination registration requires committed exact graph and five known gates", Admission),
        ("Pending graph and queue reserve before native work and pure cancellation invalidates the shared launch", PendingReservations),
        ("Actual initializing info publishes a pure token before one verified shared commit enables capture", PendingInitialization),
        ("Controller-only pending responsibility retires only after exact closed consumer evidence", PendingControllerRetirement),
        ("Early terminal pending work cannot initialize a graph or publish ready", PendingEarlyTerminal),
        ("Launch coordinator reserves and cancels all four shared quotas without closing its bundle", CoordinatorReservation),
        ("Launch coordinator rejects a different ready bridge before spending any of its four quotas", CoordinatorWrongBridge),
        ("Launch coordinator rolls back capture and factory slots when factory or graph capacity rejects", CoordinatorCapacityRollback),
        ("Launch coordinator rejects cancellation after native-attempt accounting and rejects worker/reentry", CoordinatorAttemptAndReentry),
        ("Real worker LevelOver claim observes pending terminal ownership before main-thread delivery is suppressed", CoordinatorWorkerTerminal),
        ("Rooted progress identities preserve session/run generations and classify purely on workers", PureIdentity),
        ("Termination bridge counts real exact Progress around entries together with host native entries", NativeActivity),
        ("Progress mapping retirement requires closed consumer evidence and actual native drain", Retirement),
        ("A worker holding a token across mapping withdrawal defers roots and can drain on a later pump", WithdrawalDrain),
        ("Host pump retires bridge records and idle isolation before detaching its five native gates", HostPump),
        ("Native termination bridge fixed slots reserve responsibility before any graph operation", Capacity),
        ("Production frame source uses bounded NativeRuntime value publication and retains unknown cleanup", NativeFramePublication),
        ("Production progress graph verifies the reverse link instead of accepting a guessed receiver", NativeGraphFailure),
        ("LOCAL failure summaries expose the nested cause while keeping escaped snapshot text bounded", FailureSummary),
        // Unknown bridges/records deliberately remain strongly owned without reset/free retry.
        ("Detached binding and unknown frame observations cannot appear as a usable drained boundary", InactiveAndFrameFailure),
        ("Rejected Progress context stays faulted and never substitutes another run's terminal token", WrongContext),
        ("Failed graph registration and partial binding retain bounded process responsibility", RegistrationAndBindingFailure),
        ("An unknown bridge owner cannot stop a peer bridge's completed retirement", PeerIsolation),
        ("Root-free failures retire pure identities before freeing and never retry unknown records", RootFreeFailure),
        ("Failed initializing links and unsupported pending backends preserve shared unknown responsibility", PendingInitializationFailure),
        ("Failed native handoff leaves an adopted factory root owned until exact close without a second free", CoordinatorHandoffFailure)
    };
    private static void Unsupported()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative(); var transport = new Transport(); var log = new Logger();
        var host = LocalRunProtectionHost.Create(main, log, transport); var engine = new Engine(); var broker = new ExactHookBroker(engine, main, log);
        var profile = new AdapterBuildProfile("fixture", "unsupported", "fixture", "fixture", "fixture", "fixture", new Dictionary<string, TypeBinding>());
        var baseline = NativeLocalRunTerminationBoundaryState.RetainedProcessOwnerCount;
        Throws<UnsupportedSignatureException>(() => NativeLocalRunTerminationBoundaryState.Create(native.Api, main, profile, IntPtr.Zero, broker, host));
        Equal(0, native.Invocations); Equal(0, native.FieldReads); Equal(0, native.NewRoots); Equal(0, engine.Installs);
        Equal(baseline, NativeLocalRunTerminationBoundaryState.RetainedProcessOwnerCount); Check(host.RequestStop());
    }
    private static void Admission()
    {
        using var f = new Fixture(register: false, commit: false);
        Throws<InvalidOperationException>(() => f.Register()); Equal(0, f.Backend.Roots);
        f.Host.Sessions.Commit(f.Run.Commit); f.Transport.Known = false;
        Throws<InvalidOperationException>(() => f.Register()); Equal(0, f.Backend.Roots);
        f.Transport.Known = true; f.Transport.InFlight = 1;
        Throws<InvalidOperationException>(() => f.Register()); Equal(0, f.Backend.Roots);
        f.Transport.InFlight = 0; f.Register(); Equal(3, f.Backend.Roots);
        var before = f.Backend.TargetReads; Throws<InvalidOperationException>(() => f.Register()); Equal(before, f.Backend.TargetReads);
        Equal(1, f.Bridge.Describe().Records); Equal(1, f.Bridge.Describe().PublishedMappings);
        Check(f.Bridge.IsReadyForHost(f.Host.Sessions)); Check(!f.Bridge.IsReadyForHost(new LocalRunSessionRegistry(f.Main)));
        Throws<InvalidOperationException>(() => f.Bridge.Read(f.Run.Session, f.Run.Controller + 1));
        Throws<InvalidOperationException>(() => f.Bridge.MarkTerminal(f.Run.Session, f.Run.Controller + 1));
    }
    private static void PureIdentity()
    {
        using var f = new Fixture(); var second = f.AddRun(); f.Bridge.RegisterCommitted(second.Session, second.Borrowed);
        var expected = f.Host.Sessions.Read(f.Run.Session).Identity; var other = f.Host.Sessions.Read(second.Session).Identity;
        var reads = f.Backend.TargetReads; var graphReads = f.Backend.Verifications; var frames = f.Backend.FrameReads;
        Task.Run(() => {
            var first = f.Backend.Classifier!(new(f.Run.Progress))!; var next = f.Backend.Classifier!(new(second.Progress))!;
            Equal(expected.SessionId, first.SessionId); Equal(expected.RunGeneration, first.RunGeneration);
            Equal(other.SessionId, next.SessionId); Equal(other.RunGeneration, next.RunGeneration);
            Check(first != next); Check(f.Backend.Classifier(new(f.Run.Progress + 99)) is null);
            Throws<InvalidOperationException>(() => f.Bridge.Read(f.Run.Session, f.Run.Controller));
            Throws<InvalidOperationException>(() => f.Bridge.RegisterCommitted(second.Session, second.Borrowed));
            f.Backend.Progress(f.Run.Progress);
        }).GetAwaiter().GetResult();
        Equal(reads, f.Backend.TargetReads); Equal(graphReads, f.Backend.Verifications); Equal(frames, f.Backend.FrameReads);
        Equal(1L, f.Backend.Binding!.Describe().ClassifiedLocal); Check(f.Bridge.MarkTerminal(f.Run.Session, f.Run.Controller));
    }
    private static void PendingReservations()
    {
        using var f = new PendingFixture(); Equal(0, f.Backend.Roots); Equal(0, f.Backend.Verifications);
        Check(f.Handler.HasPendingReservation(f.Launch) && f.Bridge.HasPendingReservation(f.Launch));
        Check(!f.Launch.HasNativeAttempt && f.Launch.CanCancelPureReservation);
        Throws<InvalidOperationException>(() => f.Bridge.ReservePending(f.Launch));
        Check(f.Handler.CancelPurePending(f.Launch)); Check(!f.Handler.HasPendingReservation(f.Launch));
        Throws<InvalidOperationException>(() => f.Launch.NoteNativeAttempt());
        Check(f.Bridge.CancelPurePending(f.Launch)); Check(!f.Bridge.HasPendingReservation(f.Launch));
        f.Capture.CancelEmptyPendingReservation(); Equal(0, f.Backend.Roots);
    }
    private static void PendingInitialization()
    {
        using var f = new PendingFixture(); f.AttachController();
        Equal(1, f.Backend.Roots); Equal(0, f.Backend.InformationRetentions); Equal(0, f.Bridge.Describe().PublishedMappings);
        Check(f.Bridge.HasPendingController(f.Launch)); Check(!f.Bridge.IsPendingInitialized(f.Launch));
        f.Launch.NoteActivationAttempt(); f.Bridge.AttachInitializingGraph(f.Launch, f.Information);
        Equal(3, f.Backend.Roots); Equal(1, f.Backend.InitializingVerifications); Equal(0, f.Backend.Verifications);
        Equal(LocalRunLaunchCommitPhase.Initializing, f.Launch.Read().Phase); Check(!f.Launch.IsCommittedReady);
        var reads = f.Backend.TargetReads;
        Task.Run(() => {
            var token = f.Backend.Classifier!(new(f.Controller + 2))!;
            Equal(f.Launch.Identity.SessionId, token.SessionId); Equal(f.Launch.Identity.RunGeneration, token.RunGeneration);
        }).GetAwaiter().GetResult(); Equal(reads, f.Backend.TargetReads);
        f.Host.Sessions.MarkProviderWithdrawn(f.Session);
        f.Backend.PendingControllerInitialized = true; f.Transport.InFlight = 1;
        Check(!f.Bridge.VerifyInitializedGraph(f.Launch)); Equal(0, f.Backend.Verifications);
        f.Transport.InFlight = 0; f.Backend.OnProgress = _ => Check(!f.Bridge.VerifyInitializedGraph(f.Launch));
        f.Backend.Progress(f.Controller + 2); f.Backend.OnProgress = null; Equal(0, f.Backend.Verifications);
        Check(f.Bridge.VerifyInitializedGraph(f.Launch)); Check(f.Bridge.IsPendingInitialized(f.Launch)); Equal(1, f.Backend.Verifications);
        var verifiedReads = f.Backend.TargetReads; Check(f.Bridge.IsPendingInitialized(f.Launch)); Equal(verifiedReads, f.Backend.TargetReads);
        f.Launch.PrepareCommit(); Check(!f.Launch.IsCommittedReady); f.Launch.CommitPrepared(); Check(f.Launch.IsCommittedReady);
        f.Handler.CompletePending(f.Launch); Equal(LocalRunSessionPhase.Committed, f.Host.Sessions.Read(f.Session).Phase);
        Equal(0, f.ResultBackend.Invocations); f.Handler.Handle(LocalBoundaryKind.ControllerLevelOver, f.Session, f.Controller);
        Equal(0, f.ResultBackend.Invocations); f.Backend.FrameValue++;
        f.Handler.Tick(); Equal(1, f.ResultBackend.Invocations); Equal(1, f.Sink.Presents); Check(f.Handler.HasPendingWork);
    }
    private static void PendingControllerRetirement()
    {
        using var f = new PendingFixture(); f.AttachController(); Check(!f.Bridge.CancelPurePending(f.Launch));
        Check(!f.Bridge.TryRetire(f.Session)); Equal(0, f.Backend.Frees);
        f.Retire(); f.Handler.Tick(); Check(f.Bridge.TryRetire(f.Session));
        Equal(1, f.Backend.Frees); Equal(0, f.Backend.InformationRetentions); Equal(0, f.Bridge.Describe().Records);
    }
    private static void PendingEarlyTerminal()
    {
        using var f = new PendingFixture(); f.AttachController();
        f.Handler.Handle(LocalBoundaryKind.ControllerLevelOver, f.Session, f.Controller);
        Check(f.Launch.Read().EarlyTerminal); Throws<InvalidOperationException>(() => f.Launch.NoteActivationAttempt());
        Throws<InvalidOperationException>(() => f.Bridge.AttachInitializingGraph(f.Launch, f.Information));
        Equal(1, f.Backend.Roots); Equal(0, f.Backend.InitializingVerifications); Equal(0, f.ResultBackend.Invocations);
    }
    private static void PendingInitializationFailure()
    {
        using var failed = new PendingFixture(); failed.AttachController(); failed.Launch.NoteActivationAttempt();
        failed.Backend.InitializingReverseMatches = false;
        Throws<NativeLocalRunTerminationBoundaryStateException>(() => failed.Bridge.AttachInitializingGraph(failed.Launch, failed.Information));
        Check(failed.Bridge.Describe().CleanupUnknown); Equal(LocalRunLaunchCommitPhase.Unknown, failed.Launch.Read().Phase);
        Equal(3, failed.Backend.Roots); Equal(0, failed.Bridge.Describe().PublishedMappings);
        failed.Backend.InitializingReverseMatches = true;
        Throws<InvalidOperationException>(() => failed.Bridge.AttachInitializingGraph(failed.Launch, failed.Information)); Equal(3, failed.Backend.Roots);
        using var unsupported = new PendingFixture(pendingSupported: false); unsupported.AttachController(); unsupported.Launch.NoteActivationAttempt();
        Throws<NativeLocalRunTerminationBoundaryStateException>(() => unsupported.Bridge.AttachInitializingGraph(unsupported.Launch, unsupported.Information));
        Equal(1, unsupported.Backend.Roots); Equal(0, unsupported.Backend.InitializingVerifications);
        Equal(LocalRunLaunchCommitPhase.Unknown, unsupported.Launch.Read().Phase);
    }
    private static (int Capture, int Factory, int Queue, int Graph) Quotas()
        => (NativeLocalResultCapture.RetainedProcessCaptureCount, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount,
            LocalRunTerminationHandler.RetainedProcessWorkCount, NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount);
    private static void CoordinatorReservation()
    {
        using var f = new CoordinatorFixture(); var before = Quotas(); var owner = f.Reserve(); var reserved = Quotas();
        Equal(before.Capture + 1, reserved.Capture); Equal(before.Factory + 1, reserved.Factory);
        Equal(before.Queue + 1, reserved.Queue); Equal(before.Graph + 1, reserved.Graph);
        Equal(0, f.Owner.Backend.Roots); Equal(0, f.Owner.Backend.TargetReads); Equal(0, f.Native.NewRoots); Equal(0, f.Native.Invocations);
        var duplicate = ReservationFailure(() => f.Reserve()); Check(duplicate.Responsibility.IsCancelled); Equal(reserved, Quotas());
        owner.CancelEmpty(); owner.CancelEmpty(); Check(owner.IsCancelled); Equal(before, Quotas());
        Equal(0, f.Owner.Run.Bundle.Frees); Equal(LocalRunSessionPhase.PendingCommit, f.Owner.Host.Sessions.Read(f.Owner.Run.Session).Phase);
        Check(!f.Owner.Host.Sessions.Read(f.Owner.Run.Session).ProtectionRetired); Equal(0, f.Native.FieldReads);
        Throws<ObjectDisposedException>(() => owner.NoteNativeLaunchAttempt());
    }
    private static void CoordinatorCapacityRollback()
    {
        // Earlier fault cases intentionally retain unknown process owners. Exercise
        // both complete quotas in a fresh process without clearing those owners.
        if (!Environment.GetCommandLineArgs().Contains("--isolated-case", StringComparer.Ordinal))
        {
            IsolatedAudioRegression.Run("Launch coordinator rolls back capture and factory slots when factory or graph capacity rejects");
            return;
        }
        var empty = Quotas(); Equal((0, 0, 0, 0), empty);
        Equal(0, LocalRunSessionRegistry.RetainedProcessSessionCount);
        using (var f = new CoordinatorFixture())
        {
            var run = f.Owner.AddRun(); var freed = 0;
            var fillers = new List<NativeLocalRunTerminationCaptureFactory>();
            try
            {
                // Each factory owns a distinct independent root for the same exact
                // controlled controller. Giving every factory another registry run
                // exhausts session capacity at 31 factories: the target reservation
                // already owns its separate pending session without a factory.
                for (var index = 0; index < 32; ++index)
                {
                    var root = new NativeRuntime.RootHandle((uint)index + 1, _ => freed++,
                        _ => { f.Owner.Backend.TargetReads++; return new(run.Controller); }, f.Owner.Main.VerifyAccess);
                    try { fillers.Add(new NativeLocalRunTerminationCaptureFactory(f.Native.Api, f.Owner.Main, CoordinatorProfile(), IntPtr.Zero,
                        f.Owner.Host.Sessions, run.Session, run.Controller, root)); }
                    catch { root.Dispose(); throw; }
                }
                Equal(32, NativeLocalRunTerminationCaptureFactory.RetainedProcessFactoryCount);
                Equal(2, LocalRunSessionRegistry.RetainedProcessSessionCount);
                var before = Quotas(); var failure = ReservationFailure(() => f.Reserve());
                Check(failure.InnerException is InvalidOperationException error && error.Message.Contains("32-owner quota", StringComparison.Ordinal));
                Check(failure.Responsibility.IsCancelled); Equal(before, Quotas()); Equal(0, f.Owner.Backend.Roots);
                Equal(0, f.Owner.Backend.TargetReads); Equal(0, f.Native.Invocations); Equal(0, f.Native.NewRoots); Equal(0, f.Owner.Run.Bundle.Frees);
            }
            finally
            {
                f.Owner.Retire(run);
                foreach (var factory in fillers) factory.ReleaseAfterConsumersEnded(f.Owner.Host.Sessions, run.Session);
                Equal(32, freed); Equal(empty, Quotas());
            }
        }
        Equal(empty, Quotas()); Equal(0, LocalRunSessionRegistry.RetainedProcessSessionCount);
        using (var f = new CoordinatorFixture())
        {
            var fillers = new List<LocalRunLaunchCommitState>();
            try
            {
                while (NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount < 32)
                {
                    var run = f.Owner.AddRun(commit: false, bindController: false);
                    var launch = new LocalRunLaunchCommitState(f.Owner.Main, f.Owner.Host.Sessions, run.Commit, run.Bundle);
                    f.Owner.Bridge.ReservePending(launch); fillers.Add(launch);
                    // The controlled record has no native attempt, roots or published
                    // mapping. Close its no-consumer registry session, but deliberately
                    // leave bridge retirement for finally so graph quota really reaches 32.
                    f.Owner.Retire(run);
                }
                Equal(32, NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount);
                var before = Quotas(); var failure = ReservationFailure(() => f.Reserve());
                Check(failure.InnerException is InvalidOperationException error && error.Message.Contains("bounded graph slots", StringComparison.Ordinal));
                Check(failure.Responsibility.IsCancelled); Equal(before, Quotas()); Equal(0, f.Owner.Backend.Roots);
                Equal(0, f.Owner.Backend.TargetReads); Equal(0, f.Native.NewRoots); Equal(0, f.Native.Invocations); Equal(0, f.Owner.Run.Bundle.Frees);
            }
            finally { foreach (var launch in fillers) Check(f.Owner.Bridge.TryRetire(launch.Session)); }
        }
        Equal(empty, Quotas()); Equal(0, LocalRunSessionRegistry.RetainedProcessSessionCount);
    }
    private static void CoordinatorWrongBridge()
    {
        using var f = new CoordinatorFixture(); var backend = new Backend(f.Owner.Main);
        var other = NativeLocalRunTerminationBoundaryState.CreateBackend(f.Owner.Main, f.Owner.Host, backend);
        try
        {
            Check(other.IsReadyForHost(f.Owner.Host.Sessions)); Check(f.Owner.Bridge.IsReadyForHost(f.Owner.Host.Sessions));
            Check(!f.Handler.UsesBoundaryState(other)); var before = Quotas();
            Throws<InvalidOperationException>(() => NativeLocalRunLaunchReservation.Reserve(f.Native.Api, f.Owner.Main, CoordinatorProfile(), IntPtr.Zero,
                f.Owner.Host, f.Handler, other, f.Owner.Run.Bundle, f.Owner.Run.Commit));
            Equal(before, Quotas()); Equal(0, f.Native.NewRoots); Equal(0, f.Native.Invocations); Equal(0, backend.Roots); Equal(0, f.Owner.Backend.Roots);
        }
        finally { Check(other.TryStop()); }
    }
    private static void CoordinatorAttemptAndReentry()
    {
        using var f = new CoordinatorFixture(); var owner = f.Reserve(); var before = Quotas();
        Task.Run(() => {
            Throws<InvalidOperationException>(() => owner.NoteNativeLaunchAttempt()); Throws<InvalidOperationException>(() => owner.CancelEmpty());
            Throws<InvalidOperationException>(() => f.Reserve()); Throws<InvalidOperationException>(() => owner.ReleaseUntransferredAfterClose());
        }).GetAwaiter().GetResult(); Equal(before, Quotas()); Check(!owner.State.HasNativeAttempt);
        owner.NoteNativeLaunchAttempt(); Throws<InvalidOperationException>(() => owner.CancelEmpty()); Equal(before, Quotas());
        Equal(0, f.Owner.Backend.Roots); Equal(0, f.Native.Invocations);
        var controller = f.Owner.Run.Controller; var frees = 0;
        var owned = new NativeRuntime.RootHandle(777, _ => frees++, _ => new(controller), f.Owner.Main.VerifyAccess);
        using var borrowed = new NativeRuntime.RootHandle(778, _ => { }, _ => {
            Throws<InvalidOperationException>(() => owner.CancelEmpty()); Throws<InvalidOperationException>(() => owner.NoteNativeLaunchAttempt());
            Throws<InvalidOperationException>(() => owner.AttachPendingController(owned, f.Owner.Run.Borrowed)); return new(controller);
        }, f.Owner.Main.VerifyAccess);
        f.Owner.Host.Sessions.BindPendingController(f.Owner.Run.Commit, controller, new Lease());
        owner.AttachPendingController(owned, borrowed); Equal(0, frees); Check(f.Owner.Bridge.HasPendingController(owner.State));
        Throws<InvalidOperationException>(() => owner.AttachPendingController(owned, borrowed)); Equal(0, frees);
        f.Owner.Retire(f.Owner.Run); f.Handler.Tick(); owner.ReleaseUntransferredAfterClose(); Equal(1, frees);
    }
    private static void CoordinatorHandoffFailure()
    {
        using var f = new CoordinatorFixture(); var owner = f.Reserve(); owner.NoteNativeLaunchAttempt();
        var controller = f.Owner.Run.Controller; var rootReads = 0; var frees = 0;
        var owned = new NativeRuntime.RootHandle(779, _ => frees++, _ => { rootReads++; return new(controller); }, f.Owner.Main.VerifyAccess);
        f.Owner.Host.Sessions.BindPendingController(f.Owner.Run.Commit, controller, new Lease());
        // Use the production supported-build gate for this fallible graph-root handoff;
        // controlled discovery/binding is retained and no game operation is performed.
        var production = new NativeLocalRunTerminationBoundaryBackend(f.Native.Api, f.Owner.Main, CoordinatorProfile(), IntPtr.Zero,
            new(new Engine(), f.Owner.Main, new Logger()));
        f.Owner.Backend.ControllerRetention = production.RetainController;
        var failure = ReservationFailure(() => owner.AttachPendingController(owned, f.Owner.Run.Borrowed));
        Check(ReferenceEquals(owner, failure.Responsibility)); Equal(LocalRunLaunchCommitPhase.Unknown, owner.State.Read().Phase);
        var factory = (NativeLocalRunTerminationCaptureFactory)typeof(NativeLocalRunLaunchReservation)
            .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        Check(factory.OwnsControllerRoot(owned)); Equal(0, rootReads); Equal(0, frees); Equal(0, f.Native.Invocations); Equal(0, f.Native.NewRoots);
        Throws<InvalidOperationException>(() => owner.CancelEmpty());
        f.Owner.Retire(f.Owner.Run); owner.ReleaseUntransferredAfterClose(); Equal(0, frees); // Already transferred to the queue/factory.
        Check(!owner.UntransferredCleanupCompleted); Equal(LocalRunLaunchCommitPhase.Unknown, owner.State.Read().Phase);
        f.Handler.Tick(); Equal(1, frees); owner.ReleaseUntransferredAfterClose(); f.Handler.Tick(); Equal(1, frees); Equal(0, rootReads);
    }
    private static void CoordinatorWorkerTerminal()
    {
        using var f = new CoordinatorFixture(nativeLevelOver: true); var owner = f.Reserve(); owner.NoteNativeLaunchAttempt();
        var controller = f.Owner.Run.Controller;
        var independent = new NativeRuntime.RootHandle(780, _ => { }, _ => new(controller), f.Owner.Main.VerifyAccess);
        f.Owner.Host.Sessions.BindPendingController(f.Owner.Run.Commit, controller, new Lease());
        owner.AttachPendingController(independent, f.Owner.Run.Borrowed);
        Check(f.Handler.IsPendingPrepared(owner.State) && f.Owner.Bridge.HasPendingController(owner.State));
        var reads = f.Owner.Backend.TargetReads; var frames = f.Owner.Backend.FrameReads;
        Task.Run(() => f.Owner.Transport.InvokeLevelOver(controller)).GetAwaiter().GetResult();
        Equal(0, f.Owner.Transport.LevelOverOriginals); Equal(reads, f.Owner.Backend.TargetReads); Equal(frames, f.Owner.Backend.FrameReads);
        Check(!owner.State.IsCommittedReady); Check(!f.Handler.Read(f.Owner.Run.Session).Queued);
        Throws<InvalidOperationException>(() => owner.State.NoteActivationAttempt());
        Throws<InvalidOperationException>(() => owner.State.PrepareCommit()); Equal(0, f.Native.Invocations);
    }
    private static AdapterBuildProfile CoordinatorProfile()
        => new("fixture", "unsupported", "fixture", "fixture", "fixture", "fixture", new Dictionary<string, TypeBinding>());
    private static NativeLocalRunLaunchReservationException ReservationFailure(Action action)
    {
        try { action(); } catch (NativeLocalRunLaunchReservationException failure) { return failure; }
        throw new Exception("Expected an owned launch reservation failure.");
    }
    private static void NativeActivity()
    {
        using var f = new Fixture(); var outside = f.Bridge.Read(f.Run.Session, f.Run.Controller);
        Check(outside.AllFiveGatesInstalled && outside.NativeBoundaryStateKnown); Equal(0, outside.InFlightNativeBoundaries); Equal(100L, outside.Frame);
        f.Backend.OnProgress = receiver => {
            var state = f.Bridge.Read(f.Run.Session, f.Run.Controller);
            Equal(receiver == f.Run.Progress ? 1 : 0, state.InFlightNativeBoundaries);
            Check(state.NativeBoundaryStateKnown);
            if (receiver == f.Run.Progress) Check(f.Bridge.MarkTerminal(f.Run.Session, f.Run.Controller));
        };
        f.Backend.Progress(f.Run.Progress); f.Backend.Progress(f.Run.Progress + 99);
        f.Transport.InFlight = 2; Equal(10, f.Bridge.Read(f.Run.Session, f.Run.Controller).InFlightNativeBoundaries); // Five gate bindings.
        f.Transport.Known = false; var frames = f.Backend.FrameReads;
        Check(!f.Bridge.Read(f.Run.Session, f.Run.Controller).NativeBoundaryStateKnown); Equal(frames, f.Backend.FrameReads);
        f.Transport.Known = true; f.Transport.InFlight = 0;
    }
    private static void Retirement()
    {
        using var f = new Fixture(); Check(!f.Bridge.TryRetire(f.Run.Session)); Equal(0, f.Backend.Frees);
        f.Retire(f.Run); f.Backend.OnProgress = _ => Check(!f.Bridge.TryRetire(f.Run.Session));
        f.Backend.Progress(f.Run.Progress); Equal(0, f.Backend.Frees);
        Check(f.Bridge.TryRetire(f.Run.Session)); Equal(3, f.Backend.Frees); Equal(0, f.Bridge.Describe().PublishedMappings);
        Check(f.Backend.Classifier!(new(f.Run.Progress)) is null); Check(f.Bridge.TryRetire(f.Run.Session)); Equal(3, f.Backend.Frees);
    }
    private static void Capacity()
    {
        using var f = new Fixture(); var baseline = NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount - 1;
        var available = Math.Min(32 - NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount,
            32 - LocalRunSessionRegistry.RetainedProcessSessionCount);
        for (var index = 0; index < available; index++) { var run = f.AddRun(); f.Bridge.RegisterCommitted(run.Session, run.Borrowed); }
        Equal(baseline + 1 + available, NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount);
        Check(NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount <= 32);
        var before = f.Backend.Roots; Throws<InvalidOperationException>(() => f.Register()); Equal(before, f.Backend.Roots);
        f.Retire(f.Run); Check(f.Bridge.TryRetire(f.Run.Session));
        var replacement = f.AddRun(); f.Bridge.RegisterCommitted(replacement.Session, replacement.Borrowed); Equal(before + 3, f.Backend.Roots);
    }
    private static void WithdrawalDrain()
    {
        using var f = new Fixture(); f.Retire(f.Run);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); Task? worker = null;
        f.Backend.OnProgress = _ => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException("Controlled old-token worker did not drain."); };
        f.Transport.ReadCalls = 0;
        f.Transport.OnRead = count => {
            if (count != 5) return; // Last gate of the first native drain snapshot.
            worker = Task.Run(() => f.Backend.Progress(f.Run.Progress));
            Check(entered.Wait(5000));
        };
        try
        {
            Check(!f.Bridge.TryRetire(f.Run.Session));
            Equal(0, f.Bridge.Describe().PublishedMappings); Equal(0, f.Backend.Frees);
            Check(!f.Bridge.Describe().CleanupUnknown); Check(f.Backend.Classifier!(new(f.Run.Progress)) is null);
        }
        finally { f.Transport.OnRead = null; release.Set(); worker?.GetAwaiter().GetResult(); }
        Check(f.Bridge.TryRetire(f.Run.Session)); Equal(3, f.Backend.Frees);
    }
    private static void HostPump()
    {
        using var f = new Fixture(); Check(NativeLocalRunTerminationBoundaryState.HasPendingWorkForHost(f.Host));
        NativeLocalRunTerminationBoundaryState.TickForHost(f.Host); Check(f.Bridge.Describe().BindingActive);
        Check(!NativeLocalRunTerminationBoundaryState.HasUnknownWorkForHost(f.Host));
        f.Retire(f.Run); Check(!f.Host.RequestStop()); Check(!f.Bridge.IsReadyForHost(f.Host.Sessions));
        NativeLocalRunTerminationBoundaryState.TickForHost(f.Host);
        Check(!NativeLocalRunTerminationBoundaryState.HasPendingWorkForHost(f.Host)); Equal(3, f.Backend.Frees);
        Check(f.Bridge.Describe().Stopped); Check(f.Host.TryCompleteStop());
    }
    private static NativeLocalRunTerminationBoundaryBackend Production(FakeNative native, MainThreadDispatcher main)
    {
        var profile = new AdapterBuildProfile("fixture", WorkshopChartModule.Fingerprint, "fixture", "2022.3.62f2", "fixture", "fixture", new Dictionary<string, TypeBinding>());
        var backend = new NativeLocalRunTerminationBoundaryBackend(native.Api, main, profile, new(1), new(new Engine(), main, new Logger()));
        // Skip pinned-image/layout discovery only. Actual NativeRuntime value/field
        // publication remains in use; this controlled export test is not game acceptance.
        Field(backend, "_buildVerified", true); Field(backend, "_layoutVerified", true); return backend;
    }
    private static void Field(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void NativeFramePublication()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative(); var backend = Production(native, main);
        native.IsStatic = true; native.ReturnType = native.Int32; native.Result = native.Box(native.Int32, BitConverter.GetBytes(321));
        Field(backend, "_frame", native.Method); Equal(321, backend.Frame()); Equal(1, native.Invocations);
        Equal(native.NewRoots, native.FreedRoots); Equal(0, native.ActiveRoots);
        native.FailFreeHandle = 2; Throws<AggregateException>(() => backend.Frame()); Check(backend.CleanupUnknown);
        var attempts = native.FreeHandleAttempts; var invokes = native.Invocations;
        Throws<InvalidOperationException>(() => backend.Frame()); Equal(attempts, native.FreeHandleAttempts); Equal(invokes, native.Invocations);
    }
    private static void NativeGraphFailure()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative(); var backend = Production(native, main);
        Field(backend, "_normal", native.Api.ObjectClass(native.Instance));
        Field(backend, "_information", native.Api.ObjectClass(native.OtherObject)); Field(backend, "_progress", native.Api.ObjectClass(native.OtherObject));
        native.FieldType = native.Object;
        var reader = new GraphReader(native.Instance, native.OtherObject, new(42));
        var field = typeof(NativeRuntime).GetField("_fieldGet", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(native.Api, Delegate.CreateDelegate(field.FieldType, reader, typeof(GraphReader).GetMethod(nameof(GraphReader.Read))!));
        using var controller = native.Api.Root(native.Instance); using var information = native.Api.Root(native.OtherObject); using var progress = native.Api.Root(new(42));
        Throws<InvalidOperationException>(() => backend.VerifyGraph(controller, information, progress, 123)); Check(backend.CleanupUnknown);
        Equal(3, reader.Reads); var reads = reader.Reads;
        Throws<InvalidOperationException>(() => backend.VerifyGraph(controller, information, progress, 123)); Equal(reads, reader.Reads);
    }
    private static void InactiveAndFrameFailure()
    {
        using var inactive = new Fixture(); inactive.Backend.Binding!.Dispose();
        var state = inactive.Bridge.Read(inactive.Run.Session, inactive.Run.Controller);
        Check(!state.NativeBoundaryStateKnown && !state.AllFiveGatesInstalled); Check(!inactive.Bridge.MarkTerminal(inactive.Run.Session, inactive.Run.Controller));
        var roots = inactive.Backend.Roots; var next = inactive.AddRun();
        Throws<InvalidOperationException>(() => inactive.Bridge.RegisterCommitted(next.Session, next.Borrowed)); Equal(roots, inactive.Backend.Roots);
        using var failed = new Fixture(); failed.Backend.ThrowFrame = true;
        Check(!failed.Bridge.Read(failed.Run.Session, failed.Run.Controller).NativeBoundaryStateKnown); Check(failed.Bridge.Describe().CleanupUnknown);
        var frames = failed.Backend.FrameReads; failed.Backend.ThrowFrame = false;
        Check(!failed.Bridge.Read(failed.Run.Session, failed.Run.Controller).NativeBoundaryStateKnown); Equal(frames, failed.Backend.FrameReads);
        using var backwards = new Fixture(); Check(backwards.Bridge.Read(backwards.Run.Session, backwards.Run.Controller).NativeBoundaryStateKnown);
        backwards.Backend.FrameValue = 101; Check(backwards.Bridge.Read(backwards.Run.Session, backwards.Run.Controller).NativeBoundaryStateKnown);
        backwards.Backend.FrameValue = 100; Check(!backwards.Bridge.Read(backwards.Run.Session, backwards.Run.Controller).NativeBoundaryStateKnown);
        Check(backwards.Bridge.Describe().CleanupUnknown);
    }
    private static void WrongContext()
    {
        using var f = new Fixture(); f.Backend.OnProgress = _ => Check(!f.Bridge.MarkTerminal(f.Run.Session, f.Run.Controller));
        f.Backend.Progress(f.Run.Progress + 99); Check(f.Bridge.Describe().CleanupUnknown);
        Check(!f.Bridge.Read(f.Run.Session, f.Run.Controller).NativeBoundaryStateKnown);
    }
    private static void RegistrationAndBindingFailure()
    {
        using var failed = new Fixture(register: false); failed.Backend.ThrowVerify = true;
        var baseline = NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount;
        NativeLocalRunTerminationBoundaryStateException? failure = null;
        try { failed.Register(); } catch (NativeLocalRunTerminationBoundaryStateException error) { failure = error; }
        Check(failure is not null && ReferenceEquals(failure.Responsibility, failed.Bridge));
        Equal(baseline + 1, NativeLocalRunTerminationBoundaryState.RetainedProcessRecordCount); Equal(3, failed.Backend.Roots);
        Equal(0, failed.Bridge.Describe().PublishedMappings); Equal(1, failed.Bridge.Describe().Records); Equal(0, failed.Backend.Frees);
        failed.Backend.ThrowVerify = false; Throws<InvalidOperationException>(() => failed.Register()); Equal(3, failed.Backend.Roots);
        using var main = new MainThreadDispatcher(); var host = LocalRunProtectionHost.Create(main, new Logger(), new Transport());
        var backend = new Backend(main) { ThrowBind = true,
            BindError = new IOException("Controlled partial binding outcome is unknown. " + new string('\u754c', 1000)) };
        var before = NativeLocalRunTerminationBoundaryState.RetainedProcessOwnerCount;
        NativeLocalRunTerminationBoundaryStateException? bind = null;
        try { NativeLocalRunTerminationBoundaryState.CreateBackend(main, host, backend); } catch (NativeLocalRunTerminationBoundaryStateException error) { bind = error; }
        Check(bind is not null && bind.Responsibility.Describe().CleanupUnknown);
        Equal(before + 1, NativeLocalRunTerminationBoundaryState.RetainedProcessOwnerCount); Check(!bind!.Responsibility.TryStop()); Check(!host.RequestStop());
        var cause = bind.Responsibility.Describe().Failure!;
        Check(cause.Contains("during controlled Progress/Timeline binding", StringComparison.Ordinal));
        Check(cause.Contains("IOException: Controlled partial binding outcome is unknown.", StringComparison.Ordinal));
        Check(bind.Message.Contains(cause, StringComparison.Ordinal));
        Check(NativeLocalRunTerminationBoundaryState.HasUnknownWorkForHost(host));
        Equal(cause, bind.Responsibility.Describe().Failure); Equal(cause, host.Read().Failure);
        Check(System.Text.Json.JsonSerializer.Serialize(host.Read()).Length <= 1800);
        Equal(5, host.Read().RetainedBindings); Check(!host.Read().AcceptingNewRuns);
        if (OperatingSystem.IsWindows() && IntPtr.Size == 8)
        {
            using var native = new FakeNative();
            var profile = new AdapterBuildProfile("fixture", WorkshopChartModule.Fingerprint, "fixture", "2022.3.62f2",
                "fixture", "fixture", new Dictionary<string, TypeBinding>());
            var module = new LocalRunProtectionModule(native.Api, main, new Logger(), profile, "unused");
            typeof(LocalRunProtectionModule).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, host);
            var unavailable = module.Probe(); Check(unavailable.Permanent && !unavailable.Ready);
            Check(unavailable.Reason!.Contains(cause, StringComparison.Ordinal));
            Equal(unavailable.Reason, module.Probe().Reason); Equal(0, native.Invocations); Equal(0, native.NewRoots);
        }
    }
    private static void FailureSummary()
    {
        var nested = new InvalidOperationException("Wrapper hid the actual failure.",
            new MissingMemberException("Exact native member is absent.\r\n" + new string('\u754c', 1000)));
        var summary = LocalRunFailureDiagnostics.Describe("LOCAL setup retained", nested);
        Check(summary.Contains("MissingMemberException: Exact native member is absent.", StringComparison.Ordinal));
        Check(!summary.Contains('\r') && !summary.Contains('\n'));
        Check(System.Text.Json.JsonEncodedText.Encode(summary).EncodedUtf8Bytes.Length <= LocalRunFailureDiagnostics.MaximumEncodedLength);
        var ascii = LocalRunFailureDiagnostics.Describe("LOCAL setup retained", new IOException(new string('x', 1000)));
        Check(ascii.EndsWith("...", StringComparison.Ordinal));
        Check(System.Text.Json.JsonEncodedText.Encode(ascii).EncodedUtf8Bytes.Length <= LocalRunFailureDiagnostics.MaximumEncodedLength);
    }
    private static void PeerIsolation()
    {
        using var f = new Fixture(); var second = f.AddRun(); var backend = new Backend(f.Main) { ThrowFrame = true };
        var peer = NativeLocalRunTerminationBoundaryState.CreateBackend(f.Main, f.Host, backend); peer.RegisterCommitted(second.Session, second.Borrowed);
        Check(!peer.Read(second.Session, second.Controller).NativeBoundaryStateKnown);
        f.Retire(f.Run); f.Retire(second); NativeLocalRunTerminationBoundaryState.TickForHost(f.Host);
        Equal(0, f.Bridge.Describe().Records); Equal(3, f.Backend.Frees); Equal(1, peer.Describe().Records); Equal(0, backend.Frees);
        Check(NativeLocalRunTerminationBoundaryState.HasPendingWorkForHost(f.Host)); Check(NativeLocalRunTerminationBoundaryState.HasUnknownWorkForHost(f.Host));
    }
    private static void RootFreeFailure()
    {
        using var f = new Fixture(); f.Retire(f.Run); f.Backend.FailFree = 2;
        Check(!f.Bridge.TryRetire(f.Run.Session)); Check(f.Bridge.Describe().CleanupUnknown);
        Equal(0, f.Bridge.Describe().PublishedMappings); Check(f.Backend.Classifier!(new(f.Run.Progress)) is null);
        Equal(3, f.Backend.Frees); f.Backend.FailFree = 0; Check(!f.Bridge.TryRetire(f.Run.Session)); Equal(3, f.Backend.Frees);
    }
    private sealed record Run(LocalRunSessionRegistry.SessionHandle Session, LocalRunSessionRegistry.CommitClaim Commit,
        long Controller, long Progress, NativeRuntime.RootHandle Borrowed, Lease Bundle);
    private sealed class Fixture : IDisposable
    {
        private static long Next = 92_000_000;
        internal readonly MainThreadDispatcher Main = new(); internal readonly Transport Transport = new();
        internal readonly LocalRunProtectionHost Host; internal readonly Backend Backend;
        internal readonly NativeLocalRunTerminationBoundaryState Bridge; internal readonly Run Run;
        internal readonly LocalRunTerminationHandler? InstalledHandler;
        private readonly List<Run> _runs = new();
        internal Fixture(bool register = true, bool commit = true, bool bindController = true, bool nativeLevelOver = false,
            ILocalRunTerminationResultSink? installSink = null)
        {
            if (nativeLevelOver) Transport.EnableLevelOver(Main);
            Host = LocalRunProtectionHost.Create(Main, new Logger(), Transport); Backend = new(Main);
            Bridge = NativeLocalRunTerminationBoundaryState.CreateBackend(Main, Host, Backend);
            if (installSink is not null)
            {
                InstalledHandler = new(Main, Host.Sessions, Bridge, installSink, new PendingCommands());
                Host.InstallHandler(InstalledHandler);
            }
            Run = AddRun(commit, bindController);
            if (register) Register();
        }
        internal Run AddRun(bool commit = true, bool bindController = true)
        {
            var control = Interlocked.Add(ref Next, 100);
            var bundle = new Lease(); var session = Host.Sessions.RegisterPrepared(new(new("native-termination.test", "song"), 1, "chart"), control + 3, bundle);
            var pending = Host.Sessions.BeginCommit(session); if (bindController) Host.Sessions.BindPendingController(pending, control, new Lease());
            if (commit) Host.Sessions.Commit(pending);
            var borrowed = new NativeRuntime.RootHandle(99, _ => { }, _ => { Backend.TargetReads++; return new(control); }, Main.VerifyAccess);
            var run = new Run(session, pending, control, control + 2, borrowed, bundle); _runs.Add(run); return run;
        }
        internal void Register() => Bridge.RegisterCommitted(Run.Session, Run.Borrowed);
        internal void Retire(Run run)
        {
            if (Host.Sessions.Read(run.Session).Phase == LocalRunSessionPhase.Closed) return;
            Host.Sessions.BeginReturn(run.Session); var proof = Host.Sessions.VerifyConsumersEnded(run.Session, new Verifier())!;
            Check(Host.Sessions.Close(run.Session, proof));
        }
        public void Dispose()
        {
            try
            {
                Transport.Known = true; Transport.InFlight = 0; Backend.JudgeKnown = true; Backend.JudgeInFlight = 0;
                foreach (var run in _runs) { Retire(run); Bridge.TryRetire(run.Session); run.Borrowed.Dispose(); }
                if (Bridge.TryStop()) Host.RequestStop(); // Other peer owners may still retain this host.
            }
            finally { Main.Dispose(); }
        }
    }
    private sealed partial class Backend : INativeLocalRunTerminationBoundaryBackend
    {
        internal bool JudgeKnown = true;
        internal long JudgeInFlight;
        internal Func<LocalRunBoundaryActivity>? JudgeActivityOverride;
        public LocalRunBoundaryActivity ReadJudgeActivity() => JudgeActivityOverride?.Invoke() ?? new(JudgeInFlight, JudgeKnown);
        private static long NextAddress = 0x7B0000000;
        private readonly MainThreadDispatcher _main; private readonly Engine _engine = new(); private readonly ExactHookBroker _broker;
        private readonly ExactHookBroker.LocalProgressIsolationTargets _targets;
        internal ExactHookBroker.LocalProgressIsolationBinding? Binding;
        internal Func<IntPtr, ExactHookBroker.LocalProgressIdentity?>? Classifier;
        internal Action<long>? OnProgress; internal int Roots, Frees, TargetReads, Verifications, FrameReads, FrameValue = 100;
        internal int InformationRetentions, InitializingVerifications;
        internal bool PendingControllerInitialized = true, InitializingReverseMatches = true;
        internal bool InitializationEnabled;
        internal bool InitializationInputMatches = true;
        internal ExactHookBroker.LocalInitializationIsolationTargets? InitializationTargets;
        internal ExactHookBroker.LocalInitializationIsolationBinding? InitializationBinding;
        internal Action<long>? OnStart;
        internal Action? OnInitializationInput;
        internal Action<long>? OnRoot;
        internal Action<long, bool>? OnPlay;
        internal long InitializationController, ReturnedParameters;
        internal int GetterOriginals, ModeOriginals, TooltipOriginals;
        public bool RequiresInitializationIsolation => InitializationEnabled;
        internal Func<NativeRuntime.RootHandle, long, NativeRuntime.RootHandle>? ControllerRetention;
        internal uint FailFree; internal bool ThrowVerify, ThrowFrame, ThrowBind;
        internal Exception? BindError;
        public bool CleanupUnknown { get; private set; }
        public string? InstallationStage => ThrowBind ? "controlled Progress/Timeline binding" : null;
        internal Backend(MainThreadDispatcher main, string? dobby = null, IntPtr ownedTargets = default)
        {
            _main = main;
            _ownedTargets = ownedTargets;
            _broker = dobby is null ? new(_engine, main, new Logger()) : new(dobby, main, new Logger());
            var address = Interlocked.Add(ref NextAddress, 0x1000);
            VerifiedHookTarget Target(int offset, ExactHookAbi abi)
                => ownedTargets == IntPtr.Zero ? new("native-termination.fixture." + offset, new(address + offset), new(0x222), new(0x333), abi)
                    : OwnedTarget(offset switch { 0 => "OwnedUpdate", 0x100 => "OwnedMode", 0x200 => "OwnedInstantaneous", _ => "OwnedEndRange" }, abi);
            _targets = (ExactHookBroker.LocalProgressIsolationTargets)typeof(ExactHookBroker.LocalProgressIsolationTargets)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Enumerable.Repeat(typeof(VerifiedHookTarget), 4).ToArray(), null)!
                .Invoke(new object[] { Target(0, ExactHookAbi.InstanceVoid), Target(0x100, ExactHookAbi.StaticTimelineModeVoid),
                    Target(0x200, ExactHookAbi.StaticTimelineInstantaneousHandle), Target(0x300, ExactHookAbi.StaticTimelineEndRangeVoid) });
            _engine.Originals[_targets.Progress.Address] = new ExactHookBroker.InstanceVoid((receiver, _) => OnProgress?.Invoke(receiver.ToInt64()));
            _engine.Originals[_targets.Mode.Address] = new ExactHookBroker.StaticTimelineMode((_, _) => _engine.ReplaceModeCounter?.Invoke());
            _engine.Originals[_targets.Instantaneous.Address] = new ExactHookBroker.StaticTimelineInstantaneous((_, _, _, _, _, _, _) => new(123));
            _engine.Originals[_targets.EndRange.Address] = new ExactHookBroker.StaticTimelineEndRange((_, _, _) => { });
        }
        public void RequireSupportedBuild() => _main.VerifyAccess();
        public ExactHookBroker.LocalProgressIsolationBinding ResolveAndBind(Func<IntPtr, ExactHookBroker.LocalProgressIdentity?> classify)
        {
            Classifier = classify; if (ThrowBind) throw BindError ?? new IOException("Controlled partial binding outcome is unknown.");
            return Binding = _broker.BindLocalProgressIsolation(_targets, SharedPlayTarget is null ? classify : ClassifySharedPlay, SharedPlayReservation);
            ExactHookBroker.LocalProgressIdentity? ClassifySharedPlay(IntPtr receiver)
            {
                var identity = classify(receiver);
                if (identity is not null) ++SharedPlayClaims;
                return identity;
            }
        }
        internal void Progress(long receiver) => ((ExactHookBroker.InstanceVoid)_engine.Replacements[_targets.Progress.Address])(new(receiver), _targets.Progress.MethodInfo);
        private NativeRuntime.RootHandle Root(long pointer)
        {
            OnRoot?.Invoke(pointer);
            var handle = (uint)++Roots;
            return new(handle, value => { Frees++; if (value == FailFree) throw new IOException("Controlled native root free is unknown."); },
                _ => { TargetReads++; return new(pointer); }, _main.VerifyAccess);
        }
        public NativeRuntime.RootHandle RetainController(NativeRuntime.RootHandle borrowedController, long exactController)
        { if (ControllerRetention is not null) return ControllerRetention(borrowedController, exactController); Equal(exactController, borrowedController.Target.ToInt64()); return Root(exactController); }
        public NativeRuntime.RootHandle RetainInformation(NativeRuntime.RootHandle controller) => Root(controller.Target.ToInt64() + 1);
        public NativeRuntime.RootHandle RetainInitializingInformation(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information)
        { InformationRetentions++; Equal(controller.Target.ToInt64() + 1, information.Target.ToInt64()); return Root(information.Target.ToInt64()); }
        public NativeRuntime.RootHandle RetainProgress(NativeRuntime.RootHandle information) => Root(information.Target.ToInt64() + 1);
        public void VerifyGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information, NativeRuntime.RootHandle progress, long metadata)
        {
            Verifications++; Equal(controller.Target.ToInt64() + 1, information.Target.ToInt64());
            Equal(controller.Target.ToInt64() + 2, progress.Target.ToInt64()); Equal(controller.Target.ToInt64() + 3, metadata);
            if (!PendingControllerInitialized) throw new InvalidOperationException("Controlled original has not yet written controller.LevelInformation.");
            if (ThrowVerify) throw new IOException("Controlled graph verification is unknown after rooting.");
        }
        public void VerifyInitializingGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information, NativeRuntime.RootHandle progress, long metadata)
        {
            InitializingVerifications++; Equal(controller.Target.ToInt64() + 1, information.Target.ToInt64());
            Equal(controller.Target.ToInt64() + 2, progress.Target.ToInt64()); Equal(controller.Target.ToInt64() + 3, metadata);
            if (!InitializingReverseMatches) throw new InvalidOperationException("Controlled actual progress reverse link differs.");
        }
        public int Frame() { FrameReads++; if (ThrowFrame) { CleanupUnknown = true; throw new IOException("Controlled frame publication cleanup is unknown."); } return FrameValue; }
    }
    private sealed class PendingFixture : IDisposable
    {
        private static long Next = 94_000_000;
        internal readonly MainThreadDispatcher Main = new(); internal readonly Transport Transport = new();
        internal readonly LocalRunProtectionHost Host; internal readonly Backend Backend; internal readonly NativeLocalRunTerminationBoundaryState Bridge;
        internal readonly LocalRunSessionRegistry.SessionHandle Session; internal readonly LocalRunSessionRegistry.CommitClaim Claim;
        internal readonly LocalRunLaunchCommitState Launch; internal readonly LocalRunTerminationHandler Handler; internal readonly NativeLocalResultCapture Capture;
        internal readonly PendingResultBackend ResultBackend; internal readonly PendingSink Sink = new(); internal readonly long Controller;
        internal readonly NativeRuntime.RootHandle Borrowed, Information;
        internal readonly Lease ControllerOwner = new();
        internal readonly Lease Bundle;
        internal NativeInactiveControllerParameterBinding? ParameterBinding;
        internal InitializationSetterBackend? SetterBackend;
        internal PendingFixture(bool pendingSupported = true, bool initialization = false)
        {
            Host = LocalRunProtectionHost.Create(Main, new Logger(), Transport); Backend = new(Main)
                { PendingControllerInitialized = false, InitializationEnabled = initialization };
            Bridge = NativeLocalRunTerminationBoundaryState.CreateBackend(Main, Host,
                pendingSupported ? Backend : new LegacyBackend(Backend));
            Handler = new(Main, Host.Sessions, Bridge, Sink, new PendingCommands()); Host.InstallHandler(Handler);
            Controller = Interlocked.Add(ref Next, 100); var bundle = Bundle = new Lease();
            Backend.InitializationController = Controller; Backend.ReturnedParameters = Controller + 4;
            Session = Host.RegisterPrepared(new(new("native-termination.pending", "song"), 1, "chart"), Controller + 3, bundle);
            Claim = Host.Sessions.BeginCommit(Session); Launch = new(Main, Host.Sessions, Claim, bundle);
            Handler.ReservePending(Launch); Bridge.ReservePending(Launch);
            Capture = NativeLocalResultCapture.ReservePending(Main, Host.Sessions, Claim, bundle); Launch.AttachCapture(Capture);
            ResultBackend = new(Main, Controller);
            Borrowed = new(99, _ => { }, _ => new(Controller), Main.VerifyAccess);
            Information = new(98, _ => { }, _ => new(Controller + 1), Main.VerifyAccess);
        }
        internal void AttachController(bool bindParameters = true)
        {
            Launch.NoteNativeAttempt(); Host.Sessions.BindPendingController(Claim, Controller, ControllerOwner);
            Handler.AttachPendingController(Launch, new PendingFactory(Capture, Controller));
            Bridge.AttachPendingController(Launch, Borrowed); Capture.AttachPendingBackend(ResultBackend);
            if (Backend.InitializationEnabled && bindParameters)
            {
                SetterBackend = new(Main, Backend, Bundle, ControllerOwner);
                ParameterBinding = NativeInactiveControllerParameterBinding.BindBackend(Main, SetterBackend, Host.Sessions, Claim);
                Launch.AttachParameterBinding(ParameterBinding);
            }
        }
        internal void Retire()
        {
            if (Host.Sessions.Read(Session).Phase == LocalRunSessionPhase.Closed) return;
            Host.Sessions.BeginReturn(Session); var proof = Host.Sessions.VerifyConsumersEnded(Session, new Verifier())!; Check(Host.Sessions.Close(Session, proof));
        }
        public void Dispose()
        {
            try
            {
                Transport.Known = true; Transport.InFlight = 0; Retire(); Sink.View.Ended = true;
                if (SetterBackend is not null) SetterBackend.Closed = true;
                ParameterBinding?.Dispose();
                Handler.Tick(); Bridge.TryRetire(Session); Capture.Dispose(); Borrowed.Dispose(); Information.Dispose();
                Bridge.TryStop(); Host.RequestStop();
            }
            finally { Main.Dispose(); }
        }
    }
    private sealed class CoordinatorFixture : IDisposable
    {
        internal readonly Fixture Owner;
        internal readonly FakeNative Native = new(); internal readonly LocalRunTerminationHandler Handler;
        private readonly PendingSink _sink = new(); private readonly List<NativeLocalRunLaunchReservation> _reservations = new();
        internal CoordinatorFixture(bool nativeLevelOver = false)
        {
            Owner = new(register: false, commit: false, bindController: false, nativeLevelOver: nativeLevelOver, installSink: _sink);
            Handler = Owner.InstalledHandler ?? throw new InvalidOperationException("The controlled host handler was not installed before registration.");
        }
        internal NativeLocalRunLaunchReservation Reserve()
        {
            var owner = NativeLocalRunLaunchReservation.Reserve(Native.Api, Owner.Main, CoordinatorProfile(), IntPtr.Zero,
                Owner.Host, Handler, Owner.Bridge, Owner.Run.Bundle, Owner.Run.Commit);
            _reservations.Add(owner); return owner;
        }
        public void Dispose()
        {
            try
            {
                Owner.Retire(Owner.Run); _sink.View.Ended = true; Handler.Tick();
                NativeLocalRunTerminationBoundaryState.TickForHost(Owner.Host);
                foreach (var owner in _reservations) owner.ReleaseUntransferredAfterClose();
                Owner.Dispose();
            }
            finally { Native.Dispose(); }
        }
    }
    private sealed class PendingFactory : ILocalRunTerminationCaptureFactory
    {
        private readonly NativeLocalResultCapture _capture; public long ControllerIdentity { get; }
        internal PendingFactory(NativeLocalResultCapture capture, long controller) => (_capture, ControllerIdentity) = (capture, controller);
        public NativeLocalResultCapture Create(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session) => _capture;
        public void ReleaseAfterConsumersEnded(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session) => _capture.Dispose();
    }
    private sealed class PendingResultBackend : INativeLocalResultCaptureBackend
    {
        private readonly MainThreadDispatcher _main; private readonly long _controller; internal int Invocations;
        public bool CleanupUnknown => false;
        internal PendingResultBackend(MainThreadDispatcher main, long controller) => (_main, _controller) = (main, controller);
        public void RequireSupportedBuild() => _main.VerifyAccess();
        public NativeLocalResultSource ReadSource() => new(_controller, _controller + 1, _controller + 4, _controller + 3);
        private NativeRuntime.RootHandle Root(uint id, long pointer) => new(id, _ => { }, _ => new(pointer), _main.VerifyAccess);
        public NativeRuntime.RootHandle RetainController() => Root(101, _controller);
        public NativeRuntime.RootHandle RetainInformation() => Root(102, _controller + 1);
        public void InvokeControllerOver(Action<NativeRuntime.RootHandle> publish) { Invocations++; publish(Root(103, _controller + 5)); }
        public NativeLocalResultValues ReadResult(NativeRuntime.RootHandle result) => new(_controller + 3, 23456, .75, 1, 2, 3, 4, false, false);
    }
    private sealed class PendingSink : ILocalRunTerminationResultSink
    {
        internal readonly PendingPresentation View = new(); internal int Presents;
        public void Present(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result, Action<ILocalRunTerminationResultPresentation> publish)
        { Presents++; publish(View); }
    }
    private sealed class PendingPresentation : ILocalRunTerminationResultPresentation
    { internal bool Ended; public bool ConsumersEnded => Ended; public void Dispose() { } }
    private sealed class PendingCommands : ILocalRunTerminationCommands
    { public void Execute(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long exactReceiver) { } }
    private sealed class LegacyBackend : INativeLocalRunTerminationBoundaryBackend
    {
        private readonly Backend _inner; internal LegacyBackend(Backend inner) => _inner = inner;
        public bool CleanupUnknown => _inner.CleanupUnknown;
        public LocalRunBoundaryActivity ReadJudgeActivity() => _inner.ReadJudgeActivity();
        public void RequireSupportedBuild() => _inner.RequireSupportedBuild();
        public ExactHookBroker.LocalProgressIsolationBinding ResolveAndBind(Func<IntPtr, ExactHookBroker.LocalProgressIdentity?> classify) => _inner.ResolveAndBind(classify);
        public NativeRuntime.RootHandle RetainController(NativeRuntime.RootHandle borrowedController, long exactController) => _inner.RetainController(borrowedController, exactController);
        public NativeRuntime.RootHandle RetainInformation(NativeRuntime.RootHandle controller) => _inner.RetainInformation(controller);
        public NativeRuntime.RootHandle RetainProgress(NativeRuntime.RootHandle information) => _inner.RetainProgress(information);
        public void VerifyGraph(NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information, NativeRuntime.RootHandle progress, long metadata)
            => _inner.VerifyGraph(controller, information, progress, metadata);
        public int Frame() => _inner.Frame();
    }
    private sealed class Engine : INativeDetourEngine
    {
        internal Action? ReplaceModeCounter;
        internal readonly Dictionary<IntPtr, Delegate> Originals = new(), Replacements = new(); internal int Installs;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { Installs++; Replacements.Add(target.Address, replacement); return Originals[target.Address]; }
    }
    private sealed class GraphReader
    {
        private readonly IntPtr _controller, _information, _progress; internal int Reads;
        internal GraphReader(IntPtr controller, IntPtr information, IntPtr progress) => (_controller, _information, _progress) = (controller, information, progress);
        public void Read(IntPtr owner, IntPtr field, IntPtr output)
        {
            Reads++;
            // Forward controller->information->progress is correct. Only the reverse
            // progress.levelInformation is deliberately another identity.
            Marshal.WriteIntPtr(output, owner == _controller ? _information : owner == _information ? _progress : new IntPtr(43));
        }
    }
    private sealed class Transport : ILocalRunBoundaryTransport
    {
        private static long NextAddress = 0x7C0000000;
        private Engine? _engine; private ExactHookBroker? _broker; private ExactHookBroker.InstanceVoid? _levelOver;
        internal int LevelOverOriginals;
        internal bool Known = true; internal long InFlight; internal int ReadCalls; internal Action<int>? OnRead;
        internal void EnableLevelOver(MainThreadDispatcher main)
        { _engine = new(); _broker = new(_engine, main, new Logger()); }
        internal void InvokeLevelOver(long receiver) => (_levelOver ?? throw new InvalidOperationException("No real controlled LevelOver binding."))(new(receiver), new(0x222));
        public ILocalRunBoundaryBinding Bind(LocalBoundaryKind boundary, Func<long, bool> classify, Action<long> handle)
        {
            if (_broker is null || boundary != LocalBoundaryKind.ControllerLevelOver) return new Binding(this, boundary);
            var target = new VerifiedHookTarget("coordinator.fixture.levelover", new(Interlocked.Add(ref NextAddress, 0x100)),
                new(0x222), new(0x333), ExactHookAbi.InstanceVoid);
            _engine!.Originals[target.Address] = new ExactHookBroker.InstanceVoid((_, _) => LevelOverOriginals++);
            var token = (ExactHookBroker.LocalRunCommandTarget)typeof(ExactHookBroker.LocalRunCommandTarget)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(VerifiedHookTarget), typeof(ExactHookBroker.LocalRunCommand) }, null)!
                .Invoke(new object[] { target, ExactHookBroker.LocalRunCommand.ControllerLevelOver });
            var binding = _broker.BindLocalRunCommandPolicy(token,
                receiver => classify(receiver.ToInt64()) ? ExactHookBroker.LocalRunCommandDecision.ClaimLocal : ExactHookBroker.LocalRunCommandDecision.ContinueOriginal,
                receiver => handle(receiver.ToInt64()));
            _levelOver = (ExactHookBroker.InstanceVoid)_engine.Replacements[target.Address];
            return new NativeBinding(binding);
        }
        private sealed class NativeBinding : ILocalRunBoundaryBinding
        {
            private readonly ExactHookBroker.LocalRunCommandBinding _binding;
            internal NativeBinding(ExactHookBroker.LocalRunCommandBinding binding) => _binding = binding;
            public LocalRunBoundaryCounters Read()
            {
                var hook = _binding.DescribeHook(); var policy = _binding.DescribePolicy();
                return new(LocalBoundaryKind.ControllerLevelOver, hook.NativeCalls, hook.OriginalCalls, policy.Classified, policy.Continued,
                    policy.Claimed, policy.Skipped, policy.Faults, policy.WrongThreadClaims, policy.SuppressedPolicyReentries);
            }
            public LocalRunBoundaryActivity ReadActivity() { var state = _binding.ReadActivity(); return new(state.InFlight, state.Known); }
            public void Dispose() => _binding.Dispose();
        }
        private sealed class Binding : ILocalRunBoundaryBinding
        {
            private readonly Transport _owner; private readonly LocalBoundaryKind _boundary;
            internal Binding(Transport owner, LocalBoundaryKind boundary) => (_owner, _boundary) = (owner, boundary);
            public LocalRunBoundaryCounters Read() => new(_boundary, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            public LocalRunBoundaryActivity ReadActivity()
            { _owner.OnRead?.Invoke(++_owner.ReadCalls); return new(_owner.InFlight, _owner.Known); }
            public void Dispose() { }
        }
    }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    {
        public bool Verify(LocalRunRetirementRequest request, out string evidence)
        { evidence = "Controlled bridge fixture consumer proof only; not native game retirement acceptance."; return true; }
    }
    private sealed class Lease : IDisposable { internal int Frees; public void Dispose() { Frees++; } }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}
