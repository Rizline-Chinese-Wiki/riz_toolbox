using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using System.Reflection;
using System.Runtime.CompilerServices;

// Actual three trackers, identity/node ledgers, and per-record permits. Engine
// frames and root backends remain controlled; no production native capability
// or Unity/GC/cancellation-completed evidence is granted by these cases.
internal static class NativeEngineObserverRetirementTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Terminal record with exact native final cleanup frees while active factory-only and inline records remain held", TerminalPartial),
        ("Retired terminal provider history survives Refresh and the same slot and native address start a distinct generation", TerminalReuse),
        ("Withdrawn terminal record retains one exact permit across engine redrain on a later pump", WithdrawRedrain),
        ("Failed terminal independent root free retains Unknown and its once-only permit without a second free", FreeFailure),
        ("A late callback during withdrawn record recheck faults authority before any root free", WithdrawLateEntry),
        ("Initialization Delay roots retire independently while Wait remains observable and its final cleanup is missing", InitializationPartial),
        ("Cutout record retires independently while another source record is still running", CutoutPartial),
        ("Observer retirement refuses records without actual Start or inline final cleanup evidence", MissingNativeProof),
        ("Withdrawn permit rejects changed tracker slot generation Enumerator handle and terminal identity", IdentityDrift),
        ("Retirement scalar reader reentry and duplicate issuance cannot manufacture a second permit", ReentryAndDuplicate),
        ("Permit BeginFree rechecks actual engine idle and failure after an attempted free is sticky", FreeBoundaries),
        ("Controlled retirement cannot bind production authority and all record retirement rejects workers before reads", BindingAndWorkers),
        ("Production retirement rejects an unissued authority carrying a real coordinator label and caller supplied idle callbacks", UnissuedProduction),
        ("Global engine activity changing during one independent root free holds every remaining root", IndividualRootBoundary),
        ("Scheduler record readiness remains retryable Pending without poisoning a known retirement source", SchedulerRecordPending),
        ("Withdrawn observer rechecks scheduler record readiness before each actual independent free", SchedulerWithdrawPending),
        ("Scheduler record readiness reentry cannot issue another retirement permit", SchedulerReadinessReentry)
    };
    private static void Check(bool value)
    { if (!value) throw new InvalidOperationException("Per-record engine retirement check failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed class Engine
    {
        internal readonly NativeEngineCoroutineIdentityLedger Ledger;
        internal readonly NativeEngineCoroutineObserverRetirementAuthority Authority;
        private readonly Dictionary<long, long> _managedToNative;
        internal long InFlight;
        internal bool Known = true;
        internal int Faults, ActivityReads;
        internal Action? OnActivity;
        private ulong _sequence;
        internal Engine(MainThreadDispatcher main, IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers,
            Func<bool>? known = null, Func<NativeEngineCoroutineIdentitySnapshot, bool>? recordReady = null)
        {
            _managedToNative = providers.Select(provider => provider.Receiver).Distinct().Select((receiver, index) => (receiver, native: 0xA000L + index * 0x1000))
                .ToDictionary(pair => pair.receiver, pair => pair.native);
            Ledger = new(main, new(main), providers, _managedToNative.Select(pair => new NativeEngineCoroutineOwnerPair(pair.Key, pair.Value)).ToArray());
            Authority = NativeEngineCoroutineObserverRetirementAuthority.ForControlled(main, Ledger, providers,
                () => { ++ActivityReads; OnActivity?.Invoke(); return new(InFlight, Known); }, known ?? (() => true), _ => ++Faults, recordReady);
        }
        private NativeEngineNodeFrame Start(long receiver, long enumerator) => new() { Version = 1, Size = 112, Kind = NativeEngineNodeKind.Start,
            Arg0 = new(_managedToNative[receiver]), Arg1 = new(enumerator), Arg3 = new(0x9000), Caller = new(0x8000), Sequence = ++_sequence };
        private NativeEngineNodeFrame Node(NativeEngineNodeKind kind, long receiver, long enumerator, long node) => new() {
            Version = 1, Size = 112, Kind = kind, Arg0 = new(node), Node = new(node), Owner = new(_managedToNative[receiver]),
            EnumeratorCache = new(enumerator), EnumeratorHandle = unchecked((ulong)enumerator + 10000), EnumeratorType = 2, ReferenceCount = 1,
            Caller = new(0x8000), Sequence = ++_sequence, SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid };
        private static NativeEngineNodeCompletion Completion(NativeEngineNodeKind kind, long output = 0, uint raw = 1) => new() {
            Version = 1, Size = 32, Flags = NativeEngineNodeExitFlags.Returned | NativeEngineNodeExitFlags.ReturnByteValid,
            RawReturn = raw, StartOutputNode = new(output),
            OutputFlags = kind == NativeEngineNodeKind.Start ? NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read : 0 };
        internal void StartObserved(long receiver, long enumerator, long node, bool inline = false)
        {
            var start = Start(receiver, enumerator); var a = Ledger.Capture(start);
            var execute = Node(NativeEngineNodeKind.Execute, receiver, enumerator, node); var b = Ledger.Capture(execute);
            Ledger.Complete(b, execute, Completion(execute.Kind)); b.Dispose();
            Ledger.Complete(a, start, Completion(start.Kind, inline ? 0 : node)); a.Dispose(); Check(!Ledger.Unknown);
        }
        internal void FinalRelease(long receiver, long enumerator, long node)
        {
            Ledger.RefreshTrackedIdentities(); Check(!Ledger.Unknown);
            var release = Node(NativeEngineNodeKind.Release, receiver, enumerator, node); var call = Ledger.Capture(release);
            Ledger.Complete(call, release, Completion(release.Kind, raw: 0)); call.Dispose(); Check(!Ledger.Unknown);
        }
    }
    private sealed class Terminal : IDisposable
    {
        internal readonly NativeLocalTerminalCoroutineRegression.Fixture F = new();
        internal readonly Engine Engine;
        internal Terminal()
        {
            Engine = new(F.Main, new[] { new NativeCoroutineCancellationIdentityProvider(F.Owner, 101,
                F.Owner.ReadCancellationIdentities, F.Owner.IsCurrentCancellationIdentity) }, () => !F.Owner.ObservationAuthorityUnknown);
            F.Owner.AttachControlledEngineRetirementAuthority(Engine.Authority); F.Authority.Before = false;
        }
        internal long Start(long node, bool finish = false, bool inline = false)
        {
            var value = F.Backend.Factory(TerminalCoroutineRole.Fade);
            F.Backend.EnumStart(TerminalCoroutineRole.Fade, value, () => Engine.StartObserved(101, value, node, inline));
            F.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 1);
            Engine.Ledger.RefreshTrackedIdentities();
            if (finish) Finish(value, node, !inline); Check(!F.Owner.Unknown); return value;
        }
        internal void Finish(long value, long node, bool release = true)
        {
            if (release) Engine.FinalRelease(101, value, node);
            F.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0); Check(!F.Owner.Unknown);
        }
        public void Dispose() => F.Dispose();
    }
    private static void TerminalPartial()
    {
        using var f = new Terminal(); var completed = f.Start(301, finish: true);
        f.Start(302); f.F.Backend.Factory(TerminalCoroutineRole.Fade); f.Start(303, finish: true, inline: true);
        var roots = f.F.Backend.Roots; Check(roots == 7);
        Check(f.F.Owner.RecycleNaturallyEnded() == 1 && f.F.Backend.Roots == roots - 2 && f.F.Backend.FreeCalls == 1);
        var permits = f.Engine.Authority.ReadPermits(); Check(permits.Count == 1 && permits[0].Tracker.Enumerator == completed
            && permits[0].Phase == NativeEngineObserverRetirementPhase.Completed);
        f.Engine.Ledger.RefreshTrackedIdentities(); Check(!f.Engine.Ledger.Unknown && !f.F.Owner.Unknown);
        Check(f.Engine.Ledger.Read().Pending && !f.Engine.Ledger.Read().EngineCancelled);
    }
    private static void TerminalReuse()
    {
        using var f = new Terminal(); var old = f.Start(301, finish: true);
        var oldIdentity = f.F.Owner.ReadCancellationIdentities().Single();
        Check(f.F.Owner.RecycleNaturallyEnded() == 1); f.Engine.Ledger.RefreshTrackedIdentities();
        var next = f.Start(301, finish: true); var nextIdentity = f.F.Owner.ReadCancellationIdentities().Single();
        Check(old != next && nextIdentity.Slot == oldIdentity.Slot && nextIdentity.Generation == oldIdentity.Generation + 1);
        Check(f.F.Owner.RecycleNaturallyEnded() == 1 && f.F.Backend.Roots == 0 && f.F.Backend.FreeCalls == 2);
        f.Engine.Ledger.RefreshTrackedIdentities(); var rows = f.Engine.Ledger.Read().Identities;
        Check(!f.Engine.Ledger.Unknown && rows.Count == 2 && rows[0].Node == rows[1].Node
            && rows[0].EngineGeneration == 1 && rows[1].EngineGeneration == 2);
    }
    private static void WithdrawRedrain()
    {
        using var f = new Terminal(); f.Start(301, finish: true); var withheld = false;
        f.Engine.OnActivity = () => {
            if (!withheld && f.F.Owner.ReadCancellationIdentities().Any(row => row.Retiring && !row.Published))
            { withheld = true; f.Engine.InFlight = 1; }
        };
        Check(f.F.Owner.RecycleNaturallyEnded() == 0 && withheld && f.F.Backend.FreeCalls == 0 && f.F.Backend.Roots == 2);
        Check(f.Engine.Authority.ReadPermits() is { Count: 1 } && f.Engine.Authority.ReadPermits()[0].Phase == NativeEngineObserverRetirementPhase.Withdrawn);
        f.Engine.OnActivity = null; f.Engine.InFlight = 0;
        Check(f.F.Owner.RecycleNaturallyEnded() == 1 && f.Engine.Authority.ReadPermits().Count == 1 && !f.F.Owner.Unknown);
        f.Engine.Ledger.RefreshTrackedIdentities(); Check(!f.Engine.Ledger.Unknown);
    }
    private static void FreeFailure()
    {
        using var f = new Terminal(); f.Start(301, finish: true); f.F.Backend.FailRootFree.Add(0);
        Throws<NativeLocalTerminalCoroutineException>(() => f.F.Owner.RecycleNaturallyEnded());
        Check(f.F.Backend.FreeCalls == 1 && f.F.Owner.Unknown && f.Engine.Authority.Unknown
            && f.Engine.Authority.ReadPermits()[0].Phase == NativeEngineObserverRetirementPhase.Unknown);
        Throws<InvalidOperationException>(() => f.F.Owner.RecycleNaturallyEnded()); Check(f.F.Backend.FreeCalls == 1);
    }
    private static void WithdrawLateEntry()
    {
        using var f = new Terminal(); var value = f.Start(301, finish: true); var entered = false;
        f.Engine.OnActivity = () => {
            if (entered || !f.F.Owner.ReadCancellationIdentities().Any(row => row.Retiring && !row.Published)) return;
            entered = true; f.F.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0);
        };
        Throws<NativeLocalTerminalCoroutineException>(() => f.F.Owner.RecycleNaturallyEnded());
        Check(entered && f.F.Owner.Unknown && f.F.Backend.FreeCalls == 0 && f.F.Backend.Roots == 2);
    }

    private sealed class InitialAuthority : IInitializationCoroutineAuthority
    {
        public long Controller => 101; public bool Unknown => false;
        public void VerifyBeforeCapture() { } public void VerifyGetterWindow() { } public void ObserveFaultFromAnyThread(string reason) { }
    }
    private sealed class InitialBackend : IInitializationCoroutineBackend
    {
        internal readonly NativeLocalInitializationCoroutineRegression.Backend Inner;
        internal NativeLocalInitializationCoroutineTracker Owner = null!;
        internal readonly List<int> RoleFrees = new();
        internal Action? AfterRootFree;
        internal InitialBackend(MainThreadDispatcher main) => Inner = new(main);
        public bool CleanupUnknown => Inner.CleanupUnknown;
        public int RootRecords => Inner.RootRecords;
        public bool SupportsPerRecordRetirement => true;
        public void Initialize(NativeLocalInitializationCoroutineTracker owner) { Owner = owner; Inner.Initialize(owner); }
        public void RetainEnumerator(InitializationCoroutineRole role, long enumerator) => Inner.RetainEnumerator(role, enumerator);
        public InitializationCoroutineObservation ReadEnumerator(InitializationCoroutineRole role) => Inner.ReadEnumerator(role);
        public long ReadWaitUntilCurrent(long parent, long predicate, bool retain) => Inner.ReadWaitUntilCurrent(parent, predicate, retain);
        public void RetainCoroutine(InitializationCoroutineRole role, long coroutine) => Inner.RetainCoroutine(role, coroutine);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => Inner.ReadActivity();
        public void Detach() => Inner.Detach();
        public void ReleaseRoots() => throw new InvalidOperationException("Per-record tests may never use a whole-backend fallback.");
        public void ReleaseEndedRoots(InitializationCoroutineRole role, long enumerator)
        {
            Check(Owner.IsRetiringForCleanup(role, enumerator)); var index = (int)role; RoleFrees.Add(index);
            var slots = role == InitializationCoroutineRole.WaitUntil && Inner.Roots[4] ? new[] { 4, index + 2, index } : new[] { index + 2, index };
            foreach (var slot in slots)
            {
                Owner.RequireEngineRetirementFree(role, enumerator);
                Check(Inner.Roots[slot]); Inner.Roots[slot] = false; ++Inner.RootFreeAttempts[slot]; AfterRootFree?.Invoke();
                Owner.RequireEngineRetirementFree(role, enumerator);
            }
        }
    }
    private static void InitializationPartial()
    {
        using var main = new MainThreadDispatcher(); var backend = new InitialBackend(main);
        var owner = NativeLocalInitializationCoroutineTracker.CaptureBackend(main, backend, new InitialAuthority(),
            NativeLocalInitializationCoroutineRegression.Backend.Module);
        var engine = new Engine(main, new[] { new NativeCoroutineCancellationIdentityProvider(owner, 101,
            owner.ReadCancellationIdentities, owner.IsCurrentCancellationIdentity) }, () => !owner.ObservationAuthorityUnknown);
        owner.AttachControlledEngineRetirementAuthority(engine.Authority);
        backend.Inner.Start(InitializationCoroutineRole.Delay, () => engine.StartObserved(101, 1001, 301));
        backend.Inner.Start(InitializationCoroutineRole.WaitUntil, () => engine.StartObserved(101, 1002, 302));
        engine.Ledger.RefreshTrackedIdentities(); backend.Inner.Move(InitializationCoroutineRole.Delay, 2, 1);
        engine.FinalRelease(101, 1001, 301); backend.Inner.Move(InitializationCoroutineRole.Delay, -1, 0);
        Check(!owner.TryReleaseAfterEnded() && backend.RootRecords == 2 && backend.RoleFrees.SequenceEqual(new[] { 0 })
            && backend.Inner.DetachCalls == 0 && !owner.Unknown);
        backend.Inner.Move(InitializationCoroutineRole.WaitUntil, 1, 1); Check(!owner.TryConfirmEnded());
        engine.FinalRelease(101, 1002, 302); backend.Inner.Move(InitializationCoroutineRole.WaitUntil, -1, 0);
        Check(owner.TryReleaseAfterEnded() && backend.RootRecords == 0 && backend.RoleFrees.SequenceEqual(new[] { 0, 1 })
            && backend.Inner.DetachCalls == 1 && owner.Read().Closed);
        engine.Ledger.RefreshTrackedIdentities(); Check(!engine.Ledger.Unknown && owner.TryConfirmEnded());
    }

    private sealed class CutoutAuthority : ICutoutCoroutineAuthority
    {
        internal bool Before = true; internal int Active; internal long Generation;
        public bool Unknown => false; public long Host => 201;
        public NativeCutoutEventObservation Source { get; } = new(101, 201, 202, 203, 204, 1, true,
            new[] { new NativeCutoutTrigger(301, 401, BitConverter.SingleToInt32Bits(1f)) });
        public void VerifyBeforeCapture() { Check(Before); } public void VerifyObservation() { Check(!Before); }
        public bool TryGetDispatch(out long generation) { generation = Generation; return Active == 1; }
        public bool TryConfirmSourceIdle() => Active == 0; public void ObserveFaultFromAnyThread() { }
    }
    private sealed class CutoutBackend : ICutoutCoroutineBackend
    {
        internal const long Module = 0x20000000;
        internal NativeLocalCutoutCoroutineTracker Owner = null!;
        internal readonly CutoutAuthority Source;
        internal readonly Dictionary<int, long> Slots = new();
        internal readonly Dictionary<long, CutoutCoroutineObservation> Values = new();
        internal int Roots, Frees; internal long Next = 8000;
        internal Action? AfterRootFree;
        internal CutoutBackend(CutoutAuthority source) => Source = source;
        public bool CleanupUnknown => false; public int RootRecords => Roots;
        public void Initialize(NativeLocalCutoutCoroutineTracker owner) => Owner = owner;
        public void RetainEnumerator(int slot, CutoutCoroutineRole role, long enumerator) { Slots.Add(slot, enumerator); ++Roots; }
        public CutoutCoroutineObservation ReadEnumerator(int slot, CutoutCoroutineRole role) => Values[Slots[slot]];
        public void RetainCoroutine(int slot, long coroutine) => ++Roots;
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Source.Active, true);
        public void ReleaseEndedRoots(int slot, long enumerator, long generation)
        {
            Check(Owner.IsRetiringForCleanup(slot, enumerator, generation) && Slots[slot] == enumerator); ++Frees;
            for (var index = 0; index < 2; ++index)
            {
                Owner.RequireEngineRetirementFree(slot, enumerator, generation);
                --Roots; AfterRootFree?.Invoke(); Owner.RequireEngineRetirementFree(slot, enumerator, generation);
            }
            Slots.Remove(slot);
        }
        internal long Start(Engine engine, long node)
        {
            var value = ++Next; Values[value] = new(value, 0, 301, 401, BitConverter.SingleToInt32Bits(1f), 0, 0, 0);
            ++Source.Generation; ++Source.Active;
            var frame = new NativeCoroutineFrame { Version = 1, Size = 48, Kind = NativeCoroutineKind.Start, Self = new(201),
                Enumerator = new(value), Method = new(1), Caller = new(Module + 0x38A6B2) };
            var context = Owner.CaptureStart(frame)!;
            try { engine.StartObserved(201, value, node); Owner.CompleteStart(context, frame, 9001); }
            finally { context.Dispose(); --Source.Active; }
            engine.Ledger.RefreshTrackedIdentities(); return value;
        }
        internal void Move(long value, int state, nuint result)
        {
            var frame = new NativeCoroutineFrame { Version = 1, Size = 48, Kind = NativeCoroutineKind.MoveNext, Self = new(value), Method = new(1), Caller = new(Module + 0x1234) };
            var context = Owner.CaptureMove(CutoutCoroutineRole.DelayedEvent, frame)!;
            try { Values[value] = Values[value] with { State = state }; Owner.CompleteMove(context, frame, result); }
            finally { context.Dispose(); }
        }
    }
    private static void CutoutPartial()
    {
        using var main = new MainThreadDispatcher(); var source = new CutoutAuthority(); var backend = new CutoutBackend(source);
        var owner = NativeLocalCutoutCoroutineTracker.CaptureBackend(main, backend, source, CutoutBackend.Module); source.Before = false;
        var engine = new Engine(main, new[] { new NativeCoroutineCancellationIdentityProvider(owner, 201,
            owner.ReadCancellationIdentities, owner.IsCurrentCancellationIdentity) }, () => !owner.ObservationAuthorityUnknown);
        owner.AttachControlledEngineRetirementAuthority(engine.Authority);
        var a = backend.Start(engine, 301); var b = backend.Start(engine, 302); backend.Move(a, 1, 1); backend.Move(b, 1, 1);
        engine.FinalRelease(201, a, 301); backend.Move(a, -1, 0);
        Check(owner.RecycleNaturallyEnded() == 1 && backend.Frees == 1 && backend.Roots == 2 && !owner.Unknown);
        engine.Ledger.RefreshTrackedIdentities(); Check(!engine.Ledger.Unknown);
        engine.FinalRelease(201, b, 302); backend.Move(b, -1, 0); Check(owner.RecycleNaturallyEnded() == 1 && backend.Roots == 0);
    }

    private sealed class Scalar : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly object Owner = new();
        internal NativeCoroutineCancellationIdentity Row;
        internal readonly Engine Engine;
        internal Action? OnRead;
        internal Scalar(bool started = true, bool inline = false, Func<NativeEngineCoroutineIdentitySnapshot, bool>? recordReady = null)
        {
            Row = new(Owner, 101, 0, 1, 1001, 0, false, false, true, false, false, true);
            Engine = new(Main, new[] { new NativeCoroutineCancellationIdentityProvider(Owner, 101,
                () => { OnRead?.Invoke(); return new[] { Row }; }, identity => Row.SameRecord(identity)) }, recordReady: recordReady);
            if (started) { Engine.StartObserved(101, 1001, 301, inline); Row = Row with { StartReturned = true }; Engine.Ledger.RefreshTrackedIdentities(); }
            if (started && !inline) Engine.FinalRelease(101, 1001, 301);
            Row = Row with { StartReturned = true, NaturalTerminal = true };
        }
        internal NativeEngineCoroutineObserverRetirementAuthority.Permit Withdraw()
        {
            Check(Engine.Authority.TryBegin(Row, out var permit)); Row = Row with { Published = false, Retiring = true };
            permit!.MarkWithdrawn(Row); return permit;
        }
        public void Dispose() => Main.Dispose();
    }
    private static void MissingNativeProof()
    {
        foreach (var mode in new[] { "no-start", "inline" })
        {
            using var f = new Scalar(started: mode != "no-start", inline: mode == "inline");
            Check(!f.Engine.Authority.TryBegin(f.Row, out var permit) && permit is null && !f.Engine.Authority.Unknown
                && f.Engine.Authority.ReadPermits().Count == 0 && f.Engine.Ledger.Read().Pending == (mode == "inline"));
        }
    }
    private static void IdentityDrift()
    {
        foreach (var mode in new[] { "slot", "generation", "enum", "handle", "terminal", "published" })
        {
            using var f = new Scalar(); var permit = f.Withdraw();
            f.Row = mode switch { "slot" => f.Row with { Slot = 1 }, "generation" => f.Row with { Generation = 2 },
                "enum" => f.Row with { Enumerator = 1002 }, "handle" => f.Row with { ObservedHandle = 2001 },
                "terminal" => f.Row with { NaturalTerminal = false }, _ => f.Row with { Published = true, Retiring = false } };
            Throws<InvalidOperationException>(() => permit.TryConfirmWithdrawnAndDrained()); Check(f.Engine.Authority.Unknown);
        }
    }
    private static void ReentryAndDuplicate()
    {
        using (var f = new Scalar())
        {
            f.OnRead = () => { f.OnRead = null; f.Engine.Authority.TryBegin(f.Row, out _); };
            Throws<InvalidOperationException>(() => f.Engine.Authority.TryBegin(f.Row, out _)); Check(f.Engine.Authority.Unknown);
        }
        using (var f = new Scalar())
        {
            Check(f.Engine.Authority.TryBegin(f.Row, out _));
            Throws<InvalidOperationException>(() => f.Engine.Authority.TryBegin(f.Row, out _));
            Check(f.Engine.Authority.Unknown && f.Engine.Authority.ReadPermits().Count == 1);
        }
    }
    private static void FreeBoundaries()
    {
        using var f = new Scalar(); var permit = f.Withdraw(); Check(permit.TryConfirmWithdrawnAndDrained());
        f.Engine.InFlight = 1; Check(!permit.BeginFree() && permit.Read().Phase == NativeEngineObserverRetirementPhase.Withdrawn);
        f.Engine.InFlight = 0; Check(permit.BeginFree()); f.Engine.Known = false;
        Throws<InvalidOperationException>(permit.CompleteFree); Check(f.Engine.Authority.Unknown);
        Throws<InvalidOperationException>(() => permit.BeginFree());
    }
    private static void BindingAndWorkers()
    {
        using var f = new Terminal(); var reads = f.Engine.ActivityReads;
        Throws<InvalidOperationException>(() => f.F.Owner.AttachEngineRetirementAuthority(null!, f.Engine.Authority));
        Throws<InvalidOperationException>(() => f.F.Owner.AttachControlledEngineRetirementAuthority(f.Engine.Authority));
        Task.Run(() => {
            Throws<InvalidOperationException>(() => f.F.Owner.RecycleNaturallyEnded());
            Throws<InvalidOperationException>(() => f.Engine.Authority.TryBegin(default, out _));
        }).GetAwaiter().GetResult(); Check(f.Engine.ActivityReads == reads && !f.Engine.Authority.Unknown);
    }
    private sealed class EmptyLease : IDisposable { public void Dispose() { } }
    private sealed class ControlledEnded : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled empty reservation cleanup; no native consumers were created."; return true; } }
    private static void UnissuedProduction()
    {
        using var f = new Scalar(); var registry = new LocalRunSessionRegistry(f.Main); var bundle = new EmptyLease();
        var session = registry.RegisterPrepared(new(new("issuer.fixture", "song"), 1, "chart"), 0x73000001, bundle);
        var launch = new LocalRunLaunchCommitState(f.Main, registry, registry.BeginCommit(session), bundle);
        // An uninitialized marker never runs native code. Even if an internal
        // caller supplies the right owner/launch labels and providers, it does
        // not obtain the coordinator's privately published authority reference.
        var owner = (NativeEngineCoroutineRuntimeCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(NativeEngineCoroutineRuntimeCoordinator));
        void Set(string name, object value) => typeof(NativeEngineCoroutineRuntimeCoordinator)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
        var providers = new[] { new NativeCoroutineCancellationIdentityProvider(f.Owner, 101, () => new[] { f.Row }, row => row.SameRecord(f.Row)) };
        Set("_main", f.Main); Set("_launch", launch); Set("_identities", f.Engine.Ledger); Set("_providers", providers);
        var reads = 0;
        var forged = NativeEngineCoroutineObserverRetirementAuthority.ForProduction(f.Main, owner, launch, f.Engine.Ledger,
            providers, () => { ++reads; return new(0, true); }, () => true, _ => { }, _ => true);
        Check(!forged.IsForProduction(owner, launch, f.Owner, 101));
        Throws<InvalidOperationException>(() => forged.TryBegin(f.Row, out _)); Check(reads == 0 && forged.Unknown);
        registry.BeginReturn(session); Check(registry.Close(session, registry.VerifyConsumersEnded(session, new ControlledEnded())!));
    }
    private static void SchedulerRecordPending()
    {
        var ready = false; var reads = 0;
        using var f = new Scalar(recordReady: selected => {
            ++reads; Check(selected.Node == 301 && selected.EngineGeneration == 1 && selected.StartSequence != 0);
            return ready;
        });
        Check(!f.Engine.Authority.TryBegin(f.Row, out var permit) && permit is null && reads == 1
            && !f.Engine.Authority.Unknown && f.Engine.Faults == 0 && f.Engine.Authority.ReadPermits().Count == 0);
        ready = true;
        Check(f.Engine.Authority.TryBegin(f.Row, out permit) && permit is not null && reads == 3
            && !f.Engine.Authority.Unknown && f.Engine.Faults == 0);
    }
    private static void SchedulerWithdrawPending()
    {
        var ready = true;
        using var f = new Scalar(recordReady: _ => ready);
        var permit = f.Withdraw(); ready = false;
        Check(!permit.TryConfirmWithdrawnAndDrained() && !permit.BeginFree()
            && permit.Read().Phase == NativeEngineObserverRetirementPhase.Withdrawn
            && !f.Engine.Authority.Unknown && f.Engine.Faults == 0);
        ready = true; Check(permit.BeginFree()); ready = false;
        Throws<InvalidOperationException>(permit.RequireFreeInProgress);
        Check(f.Engine.Authority.Unknown && permit.Read().Phase == NativeEngineObserverRetirementPhase.Unknown);
    }
    private static void SchedulerReadinessReentry()
    {
        Scalar? holder = null;
        using var f = new Scalar(recordReady: selected => { holder!.Engine.Authority.TryBegin(holder.Row, out _); return true; });
        holder = f;
        Throws<InvalidOperationException>(() => f.Engine.Authority.TryBegin(f.Row, out _));
        Check(f.Engine.Authority.Unknown && f.Engine.Authority.ReadPermits().Count == 0);
    }
    private static void IndividualRootBoundary()
    {
        foreach (var unknown in new[] { false, true })
        {
            using (var f = new Terminal())
            {
                f.Start(301, finish: true);
                f.F.Backend.DuringFree = _ => { if (unknown) f.Engine.Known = false; else f.Engine.InFlight = 1; };
                Throws<NativeLocalTerminalCoroutineException>(() => f.F.Owner.RecycleNaturallyEnded());
                Check(f.F.Backend.Frees.Count == 1 && f.F.Backend.Roots == 1 && f.Engine.Authority.Unknown);
                Throws<InvalidOperationException>(() => f.F.Owner.RecycleNaturallyEnded()); Check(f.F.Backend.Frees.Count == 1);
            }
            using (var main = new MainThreadDispatcher())
            {
                var backend = new InitialBackend(main);
                var owner = NativeLocalInitializationCoroutineTracker.CaptureBackend(main, backend, new InitialAuthority(), NativeLocalInitializationCoroutineRegression.Backend.Module);
                var engine = new Engine(main, new[] { new NativeCoroutineCancellationIdentityProvider(owner, 101, owner.ReadCancellationIdentities, owner.IsCurrentCancellationIdentity) }, () => !owner.ObservationAuthorityUnknown);
                owner.AttachControlledEngineRetirementAuthority(engine.Authority);
                backend.Inner.Start(InitializationCoroutineRole.Delay, () => engine.StartObserved(101, 1001, 301));
                engine.Ledger.RefreshTrackedIdentities(); backend.Inner.Move(InitializationCoroutineRole.Delay, 2, 1);
                engine.FinalRelease(101, 1001, 301); backend.Inner.Move(InitializationCoroutineRole.Delay, -1, 0);
                backend.AfterRootFree = () => { if (unknown) engine.Known = false; else engine.InFlight = 1; };
                Throws<NativeLocalInitializationCoroutineException>(() => owner.TryReleaseAfterEnded());
                Check(backend.RootRecords == 1 && backend.Inner.RootFreeAttempts.Sum() == 1 && engine.Authority.Unknown);
            }
            using (var main = new MainThreadDispatcher())
            {
                var source = new CutoutAuthority(); var backend = new CutoutBackend(source);
                var owner = NativeLocalCutoutCoroutineTracker.CaptureBackend(main, backend, source, CutoutBackend.Module); source.Before = false;
                var engine = new Engine(main, new[] { new NativeCoroutineCancellationIdentityProvider(owner, 201, owner.ReadCancellationIdentities, owner.IsCurrentCancellationIdentity) }, () => !owner.ObservationAuthorityUnknown);
                owner.AttachControlledEngineRetirementAuthority(engine.Authority);
                var value = backend.Start(engine, 301); backend.Move(value, 1, 1); engine.FinalRelease(201, value, 301); backend.Move(value, -1, 0);
                backend.AfterRootFree = () => { if (unknown) engine.Known = false; else engine.InFlight = 1; };
                Throws<NativeLocalCutoutCoroutineException>(() => owner.RecycleNaturallyEnded());
                Check(backend.RootRecords == 1 && backend.Frees == 1 && engine.Authority.Unknown);
            }
        }
    }
}
