using System.Runtime.CompilerServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

// Scalar completeness/request regressions exercise the exact production guard.
// A complete diagnostic assessment cannot create a production issuer or Close.
internal static class NativeLocalActiveConsumersEndedTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Active consumer assessment requires every distinct mandatory owner obligation", MandatoryCoverage),
        ("Each active consumer Unknown Pending or Missing obligation independently prevents Close", EachIncomplete),
        ("Duplicate malformed or unknown consumer obligation evidence cannot mint completeness", Malformed),
        ("Active return identity binds session controller result scene and result generation", ExactReturn),
        ("Active return proof refuses cleanup protection withdrawal closed and nonreturning snapshots", ReturnState),
        ("Consumer assessment snapshots do not borrow caller arrays or mutable detail lists", Snapshot),
        ("A complete scalar assessment cannot construct a retained production verifier", NoScalarAuthority),
        ("Actual registry BeginReturn preserves protection and no request diagnostic can close it", ActualRegistryReturn),
        ("Actual registry consumer token cannot cross session or return generation", RegistryTokenIdentity),
        ("Active globals closure rechecks all actual delegate targets without native writes", ActualGlobalClosure),
        ("Restoration history alone cannot hide a returned old delegate or changed manager", GlobalReentryAndOwner),
        ("Verified registry closure preserves actual detached-global observation until independent root retirement", GlobalAfterRegistryClose),
        ("Actual termination work retains its exact completion receipt until the real final-owner interval consumes it", TerminationReceiptLifetime),
        ("Zero observed listener dispatch cannot hide a nonempty actual Invoke list or missing inline execution proof", ListenerDispatchGap)
    };
    private static NativeLocalActiveConsumerCheck[] Complete()
        => Enum.GetValues<NativeLocalActiveConsumerObligation>().Select(kind => new NativeLocalActiveConsumerCheck(
            kind, NativeLocalActiveConsumerProofState.Proven, "Controlled exact owner obligation " + kind)).ToArray();
    private static void MandatoryCoverage()
    {
        var rows = Complete(); Equal(19, rows.Length);
        Check(NativeLocalActiveConsumersEndedVerifier.Assess(rows).MayClose);
        foreach (var kind in Enum.GetValues<NativeLocalActiveConsumerObligation>())
        {
            var value = NativeLocalActiveConsumersEndedVerifier.Assess(rows.Where(row => row.Obligation != kind));
            Check(!value.MayClose); Equal(19, value.Checks.Count);
            Equal(NativeLocalActiveConsumerProofState.Missing, value.Checks.Single(row => row.Obligation == kind).State);
        }
    }
    private static void EachIncomplete()
    {
        foreach (var kind in Enum.GetValues<NativeLocalActiveConsumerObligation>())
            foreach (var state in new[] { NativeLocalActiveConsumerProofState.Pending,
                NativeLocalActiveConsumerProofState.Missing, NativeLocalActiveConsumerProofState.Unknown })
            {
                var rows = Complete(); var index = Array.FindIndex(rows, row => row.Obligation == kind);
                rows[index] = rows[index] with { State = state, Detail = "Independent owner remains " + state };
                var value = NativeLocalActiveConsumersEndedVerifier.Assess(rows);
                Check(!value.MayClose); Check(value.Evidence.Contains(kind.ToString(), StringComparison.Ordinal));
            }
    }
    private static void Malformed()
    {
        var rows = Complete();
        var duplicate = NativeLocalActiveConsumersEndedVerifier.Assess(rows.Concat(new[] { rows[0] }));
        Check(!duplicate.MayClose); Equal(NativeLocalActiveConsumerProofState.Unknown, duplicate.Checks[0].State);
        Check(!NativeLocalActiveConsumersEndedVerifier.Assess(rows.Concat(new[] {
            new NativeLocalActiveConsumerCheck((NativeLocalActiveConsumerObligation)999, NativeLocalActiveConsumerProofState.Proven, "Unlisted source") })).MayClose);
        Check(!NativeLocalActiveConsumersEndedVerifier.Assess(rows.Select((row, i) => i == 0 ? row with { Detail = " " } : row)).MayClose);
        Check(!NativeLocalActiveConsumersEndedVerifier.Assess(rows.Select((row, i) => i == 0 ? row with { State = (NativeLocalActiveConsumerProofState)999 } : row)).MayClose);
    }
    private static LocalRunSessionSnapshot Returning()
        => new(new("active-close", new("test.owner", "song"), 3, "chart", 7), LocalRunSessionPhase.Returning,
            0x101, 0x202, 0x303, 4, true, true, true, false, false, 4, null, null, null);
    private static void ExactReturn()
    {
        var original = Returning(); var request = NativeLocalActiveConsumersEndedVerifier.Request(original);
        Check(NativeLocalActiveConsumersEndedVerifier.MatchesProtectedReturn(request, original));
        foreach (var changed in new[] { original with { Identity = original.Identity with { RunGeneration = 8 } },
            original with { Identity = original.Identity with { SessionId = "another-session" } },
            original with { ControllerIdentity = 0x203 }, original with { ResultSceneIdentity = 0x304 },
            original with { ResultSceneGeneration = 5 } })
            Check(!NativeLocalActiveConsumersEndedVerifier.MatchesProtectedReturn(request, changed));
    }
    private static void ReturnState()
    {
        var original = Returning(); var request = NativeLocalActiveConsumersEndedVerifier.Request(original);
        foreach (var changed in new[] { original with { ReturnRequested = false }, original with { CleanupAttempted = true },
            original with { ProtectionRetired = true }, original with { Phase = LocalRunSessionPhase.Closed },
            original with { Phase = LocalRunSessionPhase.CleanupUnknown }, original with { Phase = LocalRunSessionPhase.ResultReady } })
            Check(!NativeLocalActiveConsumersEndedVerifier.MatchesProtectedReturn(request, changed));
    }
    private static void Snapshot()
    {
        var rows = Complete(); var value = NativeLocalActiveConsumersEndedVerifier.Assess(rows);
        rows[0] = rows[0] with { State = NativeLocalActiveConsumerProofState.Unknown };
        Check(value.MayClose); Equal(NativeLocalActiveConsumerProofState.Proven, value.Checks[0].State);
        Check(value.Checks is not NativeLocalActiveConsumerCheck[]);
    }
    private static void NoScalarAuthority()
    {
        Check(NativeLocalActiveConsumersEndedVerifier.Assess(Complete()).MayClose);
        var owner = (NativeLocalRunLaunchCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(NativeLocalRunLaunchCoordinator));
        var host = (LocalRunProtectionHost)RuntimeHelpers.GetUninitializedObject(typeof(LocalRunProtectionHost));
        var registry = new LocalRunSessionRegistry(new MainThreadDispatcher());
        var session = registry.RegisterPrepared(new(new("active.false", "song"), 1, "chart"), 0x8101, new Lease());
        Throws<InvalidOperationException>(() => new NativeLocalActiveConsumersEndedVerifier(owner, host, session,
            new object(), NativeLocalActiveConsumersEndedVerifier.Request(Returning())));
        Check(!registry.Read(session).ProtectionRetired); // No native owner, no registry token.
        registry.BeginReturn(session);
        var token = registry.VerifyConsumersEnded(session, new ControlledVerifier())!;
        Check(registry.Close(session, token));
    }
    private static void ActualRegistryReturn()
    {
        var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main); var lease = new Lease();
        var session = registry.RegisterPrepared(new(new("active.registry", "song"), 1, "chart"), 0x8201, lease);
        registry.BeginReturn(session); var snapshot = registry.Read(session);
        var request = NativeLocalActiveConsumersEndedVerifier.Request(snapshot);
        Check(NativeLocalActiveConsumersEndedVerifier.MatchesProtectedReturn(request, snapshot));
        Check(!snapshot.ProtectionRetired && !snapshot.CleanupAttempted); Equal(0, lease.Frees);
        registry.BeginReturn(session); Equal(request, NativeLocalActiveConsumersEndedVerifier.Request(registry.Read(session)));
        var token = registry.VerifyConsumersEnded(session, new ControlledVerifier())!;
        Check(registry.Close(session, token)); Equal(1, lease.Frees);
    }
    private static void RegistryTokenIdentity()
    {
        var registry = new LocalRunSessionRegistry(new MainThreadDispatcher());
        var first = registry.RegisterPrepared(new(new("active.token", "one"), 1, "chart"), 0x8301, new Lease());
        var second = registry.RegisterPrepared(new(new("active.token", "two"), 1, "chart"), 0x8302, new Lease());
        registry.BeginReturn(first); registry.BeginReturn(second);
        var token = registry.VerifyConsumersEnded(first, new ControlledVerifier())!;
        Throws<InvalidOperationException>(() => registry.Close(second, token));
        Check(!registry.Read(second).ProtectionRetired); Check(registry.Close(first, token));
        Check(registry.Close(second, registry.VerifyConsumersEnded(second, new ControlledVerifier())!));
    }
    private static void ActualGlobalClosure()
    {
        using var f = new GlobalFixture();
        Equal(NativeLocalActiveConsumerProofState.Pending, f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences).State);
        f.Owner.RestoreIfOwned(); var writes = f.Backend.Writes; var captures = f.Backend.Captures;
        Equal(NativeLocalActiveConsumerProofState.Proven, f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences).State);
        Equal(captures + 6, f.Backend.Captures); Equal(writes, f.Backend.Writes);
        using var other = new GlobalFixture();
        Throws<InvalidOperationException>(() => f.Owner.ReadActiveConsumerClosure(other.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences));
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences))).GetAwaiter().GetResult();
        Equal(writes, f.Backend.Writes);
    }
    private static void GlobalReentryAndOwner()
    {
        using var f = new GlobalFixture(); f.Owner.RestoreIfOwned();
        f.Backend.Fields[2] = 200;
        Equal(NativeLocalActiveConsumerProofState.Pending, f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences).State);
        f.Backend.Fields[2] = 102; f.Backend.Current = false;
        Equal(NativeLocalActiveConsumerProofState.Unknown, f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences).State);
        f.Backend.Current = true;
        var rejected = 0;
        f.Backend.OnCapture = () => { Throws<InvalidOperationException>(() => f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences)); ++rejected; };
        Equal(NativeLocalActiveConsumerProofState.Proven, f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences).State);
        Equal(6, rejected); f.Backend.OnCapture = null;
    }
    private static void GlobalAfterRegistryClose()
    {
        using var f = new GlobalFixture(); f.Owner.RestoreIfOwned();
        var token = f.Registry.VerifyConsumersEnded(f.Session, new ControlledVerifier())!;
        Check(f.Registry.Close(f.Session, token));
        Check(!f.Owner.IsClosed); var writes = f.Backend.Writes;
        Equal(NativeLocalActiveConsumerProofState.Proven, f.Owner.ReadActiveConsumerClosure(f.Launch,
            NativeLocalActiveConsumerObligation.GlobalReferences).State);
        Equal(writes, f.Backend.Writes);
        // A caller-created coordinator label cannot consume those facts as a
        // typed active root-free authority after the registry has closed.
        var owner = (NativeLocalRunLaunchCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(NativeLocalRunLaunchCoordinator));
        Throws<InvalidOperationException>(() => f.Owner.ReleaseAfterActiveConsumersEnded(
            f.Owner.VerifyConsumersEnded(new GlobalTeardown())!, f.Launch, owner));
        Check(!f.Owner.IsClosed);
    }
    private static void TerminationReceiptLifetime()
    {
        using var main = new MainThreadDispatcher(); var registry = new LocalRunSessionRegistry(main); var bundle = new Lease();
        var session = registry.RegisterPrepared(new(new("active.retired-receipt", "song"), 1, "chart"), 0x9401, bundle);
        var claim = registry.BeginCommit(session); var launch = new LocalRunLaunchCommitState(main, registry, claim, bundle);
        var handler = new LocalRunTerminationHandler(main, registry, new EmptyBoundaries(registry), new EmptySink(), new EmptyCommands());
        handler.ReservePending(launch); Check(!handler.HasRetiredActiveConsumers(launch)); Equal(0, handler.RetiredActiveReceiptCount);
        registry.BeginReturn(session);
        Check(registry.Close(session, registry.VerifyConsumersEnded(session, new ControlledVerifier())!));
        Check(handler.HasPendingWork); handler.Tick(); Check(!handler.HasPendingWork);
        Check(handler.HasRetiredActiveConsumers(launch)); Equal(1, handler.RetiredActiveReceiptCount);
        var forged = (NativeLocalRunLaunchCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(NativeLocalRunLaunchCoordinator));
        Throws<InvalidOperationException>(() => handler.ConsumeRetiredActiveLaunch(launch, forged));
        Check(handler.HasRetiredActiveConsumers(launch)); Equal(1, handler.RetiredActiveReceiptCount);
        handler.Tick(); Equal(1, handler.RetiredActiveReceiptCount); Equal(1, bundle.Frees);
        // The handler is local and owns no native roots here. Production clears
        // its final strong launch reference only through the actual coordinator.
    }
    private static void ListenerDispatchGap()
    {
        var empty = new NativeUnityEventList(0x5510, 3, Array.Empty<NativeUnityEventInvokable>());
        var opaque = new NativeUnityEventInvokable(0x5520, 0x5530, true, Array.Empty<NativeUnityEventLeaf>());
        var nonempty = empty with { Rows = new[] { opaque } };
        NativeUnityEventGraph Graph(NativeUnityEventList executing) => new(0x5500, 0x5540, 0x5550, false, false,
            empty, empty, executing, 0x5560, 1, Array.Empty<NativeUnityEventPersistent>(), 0);
        var row = new NativeUnityEventLedgerEventSnapshot(0, 0x5500, 1, 1, 0, 1, true, false, Graph(nonempty),
            Graph(nonempty), null, Graph(nonempty), 0, 0, 0x5570, 0x5580, true,
            new(Graph(nonempty), nonempty, 1, 0x5570, 0x5590), Array.Empty<NativeUnityEventDispatchSnapshot>());
        var missed = new NativeLocalUnityEventLedgerSnapshot(1, 1, 1, 0, 0, 0, true, false, false, null,
            new[] { row }, InlineExecutionUnobserved: false, DispatchPathVerified: true);
        // Actual Invoke returned, actual captured executing list is nonempty,
        // but no child was observed. This was previously accepted by count==0.
        Check(!NativeLocalRunLaunchCoordinator.ListenerEffectsIndependentlyEmpty(missed));
        var observedEmptyRow = row with { Initial = Graph(empty), Pre = Graph(empty), Post = Graph(empty),
            Inline = new(Graph(empty), empty, 1, 0x5570, 0x5590) };
        var observedEmpty = missed with { EventSnapshots = new[] { observedEmptyRow } };
        Check(NativeLocalRunLaunchCoordinator.ListenerEffectsIndependentlyEmpty(observedEmpty));
        Check(!NativeLocalRunLaunchCoordinator.ListenerEffectsIndependentlyEmpty(observedEmpty with { InlineExecutionUnobserved = true }));
        Check(!NativeLocalRunLaunchCoordinator.ListenerEffectsIndependentlyEmpty(observedEmpty with { DispatchPathVerified = false }));
        Check(!NativeLocalRunLaunchCoordinator.ListenerEffectsIndependentlyEmpty(observedEmpty with {
            Invocations = 2, ReturnedInvocations = 2, EventSnapshots = new[] { observedEmptyRow with { Entered = 2, Exited = 2 } } }));
        Check(!NativeLocalRunLaunchCoordinator.ListenerEffectsIndependentlyEmpty(observedEmpty with {
            EventSnapshots = new[] { observedEmptyRow with { Returned = false } } }));
        var neverInvoked = missed with { Invocations = 0, ReturnedInvocations = 0,
            EventSnapshots = new[] { row with { Entered = 0, Exited = 0, NativeInvokeObserved = false, Inline = null } } };
        Check(NativeLocalRunLaunchCoordinator.ListenerEffectsIndependentlyEmpty(neverInvoked));
    }
    private sealed class EmptyBoundaries : ILocalRunTerminationBoundaryState
    {
        private readonly LocalRunSessionRegistry _registry;
        internal EmptyBoundaries(LocalRunSessionRegistry registry) => _registry = registry;
        public bool IsReadyForHost(LocalRunSessionRegistry registry) => ReferenceEquals(registry, _registry);
        public bool MarkTerminal(LocalRunSessionRegistry.SessionHandle session, long exactController) => false;
        public LocalRunTerminationBoundarySnapshot Read(LocalRunSessionRegistry.SessionHandle session, long exactController)
            => new(true, 1, true, 0);
    }
    private sealed class EmptySink : ILocalRunTerminationResultSink
    {
        public void Present(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result,
            Action<ILocalRunTerminationResultPresentation> publish) => throw new InvalidOperationException("No result is captured by this empty pending-work test.");
    }
    private sealed class EmptyCommands : ILocalRunTerminationCommands
    {
        public void Execute(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long exactReceiver)
            => throw new InvalidOperationException("No user/native command is invoked by this retirement-only test.");
    }
    private sealed class GlobalFixture : IDisposable
    {
        private static int _next;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunLaunchCommitState Launch;
        internal readonly GlobalBackend Backend;
        internal readonly OwnedNativeControllerGlobals Owner;
        private readonly NativeRuntime.RootHandle _controller;
        internal GlobalFixture()
        {
            var id = Interlocked.Increment(ref _next); var pointer = 0x7500_0000L + id * 1000L;
            Registry = new(Main); var bundle = new Lease();
            Session = Registry.RegisterPrepared(new(new("active.globals", "song"), id, "chart"), pointer + 1, bundle);
            var claim = Registry.BeginCommit(Session); Launch = new(Main, Registry, claim, bundle);
            Registry.BindPendingController(claim, pointer, new Lease()); Launch.NoteControllerBound(pointer);
            Backend = new(pointer, pointer + 2); Owner = OwnedNativeControllerGlobals.CaptureBackend(Main, Backend);
            Owner.BindActiveConsumerLaunch(Launch); Backend.Install();
            _controller = new(1, _ => { }, _ => new IntPtr(pointer), Main.VerifyAccess);
            Owner.CaptureInstalledFor(_controller); Registry.BeginReturn(Session);
        }
        public void Dispose()
        {
            Owner.ReleaseAfterConsumersEnded(Owner.VerifyConsumersEnded(new GlobalTeardown())!);
            if (Registry.Read(Session).Phase != LocalRunSessionPhase.Closed)
                Check(Registry.Close(Session, Registry.VerifyConsumersEnded(Session, new ControlledVerifier())!));
            _controller.Dispose(); Main.Dispose();
        }
    }
    private sealed class GlobalBackend : IControllerGlobalsBackend
    {
        private readonly long _controller;
        internal readonly long[] Fields = Enumerable.Range(100, 6).Select(value => (long)value).ToArray();
        internal bool Current = true, Flag;
        internal int Writes, Captures;
        internal Action? OnCapture;
        public long OwnerIdentity { get; }
        public bool IsCurrentOwner => Current;
        internal GlobalBackend(long controller, long owner) => (_controller, OwnerIdentity) = (controller, owner);
        internal void Install() { for (var i = 0; i < 6; ++i) Fields[i] = 200 + i; Flag = true; }
        public IControllerGlobalsReference RetainController(IntPtr controller) => new GlobalReference(controller.ToInt64());
        public IControllerGlobalsReference? CaptureDelegate(int index)
        { OnCapture?.Invoke(); ++Captures; return new GlobalReference(Fields[index]); }
        public void VerifyBaseline(int index, IControllerGlobalsReference? value) { }
        public bool TargetsController(int index, IControllerGlobalsReference value, long controller)
            => value.Identity >= 200 && value.Identity < 206 && controller == _controller;
        public bool ReadControllerSet() => Flag;
        public bool WriteDelegateIfSame(int index, long expected, IControllerGlobalsReference? baseline)
        { if (!Current || Fields[index] != expected) return false; Fields[index] = baseline?.Identity ?? 0; ++Writes; return true; }
        public bool WriteControllerSetIfSame(bool expected, bool baseline, IReadOnlyList<long> expectedDelegates)
        { if (!Current || Flag != expected || !Fields.SequenceEqual(expectedDelegates)) return false; Flag = baseline; ++Writes; return true; }
        public void Dispose() { }
    }
    private sealed class GlobalReference : IControllerGlobalsReference
    {
        public long Identity { get; }
        private bool _disposed;
        internal GlobalReference(long identity) => Identity = identity;
        public void Dispose() { if (_disposed) throw new InvalidOperationException("Repeated controlled global root free."); _disposed = true; }
    }
    private sealed class GlobalTeardown : IControllerGlobalsConsumersEndedVerifier
    {
        public bool Verify(ControllerGlobalsRetirementRequest request, out string evidence)
        { evidence = "Controlled global-backend teardown only; no Unity or production consumer authority."; return true; }
    }
    private sealed class Lease : IDisposable { internal int Frees; public void Dispose() => ++Frees; }
    private sealed class ControlledVerifier : ILocalRunConsumersEndedVerifier
    {
        public bool Verify(LocalRunRetirementRequest request, out string evidence)
        { evidence = "Controlled registry-only fixture teardown; no production consumer/source authority."; return true; }
    }
}
