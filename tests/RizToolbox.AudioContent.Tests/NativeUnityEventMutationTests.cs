using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

/// <summary>Actual production mutation ledger/context methods with controlled
/// graphs, roots, operation frames and authority. No native hook, game metadata,
/// original exception or engine mutation is executed by these cases.</summary>
internal static class NativeUnityEventMutationTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Mutation ledger retains add and remove generations when the final graph equals the initial graph", RestoredGraph),
        ("Mutation ledger pairs nested upper and lower operations before their parent context retires", NestedOperations),
        ("Mutation ledger does not adopt foreign receivers across the allowlisted operation ABI set", ForeignOperations),
        ("Mutation ledger before capture failure retains scalar native completion and controlled roots", BeforeFailure),
        ("Mutation ledger after read failure separates native returned from confirmed graph capture", AfterFailure),
        ("Mutation ledger abnormal original flags complete scalar responsibility without reading a post graph", AbnormalOriginal),
        ("Mutation ledger wrong frame duplicate and out of order completion preserve unknown responsibility", InvalidCompletion),
        ("Mutation ledger worker faults recognize current and historical receivers without backend reads", WorkerFaults)
    };
    private static void Check(bool value, string message = "Mutation ledger assertion failed.")
    { if (!value) throw new InvalidOperationException(message); }

    private static void RestoredGraph()
    {
        using var f = new Fixture(); var initial = f.Backend.Graph;
        var add = f.Frame(UnityEventOperation.AddListener);
        var first = f.Capture(UnityEventOperation.AddListener, add);
        var pending = f.Owner.Read(); Equal(1L, pending.NativeInFlight); Equal(1L, pending.ObservedMutations);
        Check(!f.Owner.TryConfirmNativeIdle());
        var added = new NativeUnityEventInvokable(701, 702, true, Array.Empty<NativeUnityEventLeaf>());
        f.Backend.Graph = initial with { RuntimeCalls = initial.RuntimeCalls with { Version = 1, Rows = new[] { added } } };
        f.Complete(first, add); Check(!f.Owner.TryConfirmNativeIdle()); first.Dispose();
        var saved = f.Owner.Read().EventSnapshots.Single().Mutations!;
        Equal(1, saved.Count);
        var remove = f.Frame(UnityEventOperation.RemoveListener);
        var second = f.Capture(UnityEventOperation.RemoveListener, remove);
        f.Backend.Graph = initial; f.Complete(second, remove); second.Dispose();
        var state = f.Owner.Read(); var row = state.EventSnapshots.Single(); var records = row.Mutations!;
        Equal(2L, state.ObservedMutations); Equal(2L, state.ReturnedMutations); Equal(2L, row.MutationGeneration);
        Equal(1L, records[0].Generation); Equal(2L, records[1].Generation);
        Check(records[0].NativeSequence != records[1].NativeSequence);
        Equal(UnityEventOperation.AddListener, records[0].Operation); Equal(UnityEventOperation.RemoveListener, records[1].Operation);
        Check(records.All(value => value.Captured && value.Completed && value.NativeReturned && value.ConfirmedReturned));
        Equal(0, records[0].Before!.RuntimeCalls.Rows.Count); Equal(1, records[0].After!.RuntimeCalls.Rows.Count);
        Equal(1, records[1].Before!.RuntimeCalls.Rows.Count); Equal(0, records[1].After!.RuntimeCalls.Rows.Count);
        Check(row.ChangedSinceCapture && !state.Unknown && state.Pending && !state.MutationClosed
            && state.InlineExecutionUnobserved && !state.DispatchPathVerified);
        Equal(0L, state.NativeInFlight); Check(f.Owner.TryConfirmNativeIdle());
        Equal(1, saved.Count); // A previously returned snapshot stays immutable.
    }

    private static void NestedOperations()
    {
        using var f = new Fixture();
        var upperFrame = f.Frame(UnityEventOperation.AddListener); var upper = f.Capture(UnityEventOperation.AddListener, upperFrame);
        var lowerFrame = f.Frame(UnityEventOperation.AddRuntimeCall); var lower = f.Capture(UnityEventOperation.AddRuntimeCall, lowerFrame);
        Check(ReferenceEquals(lower.Parent, upper)); Equal(2L, f.Owner.Read().NativeInFlight);
        f.Backend.Graph = f.Backend.Graph with { NeedsUpdate = true };
        f.Complete(lower, lowerFrame); lower.Dispose(); Equal(1L, f.Owner.Read().NativeInFlight);
        Check(!upper.Completed && lower.Completed && lower.Disposed && !f.Owner.TryConfirmNativeIdle());
        f.Complete(upper, upperFrame); upper.Dispose();
        var state = f.Owner.Read(); var records = state.EventSnapshots.Single().Mutations!;
        Equal(2, records.Count); Equal(2L, state.ReturnedMutations); Equal(0L, state.NativeInFlight);
        Equal(101L, records[0].Receiver); Equal(201L, records[1].Receiver);
        Check(records.All(value => value.Completed && value.ConfirmedReturned) && !state.Unknown && !state.MutationClosed);
        Check(f.Owner.TryConfirmNativeIdle());
    }

    private static void ForeignOperations()
    {
        using var f = new Fixture(); var parentFrame = f.Frame(UnityEventOperation.AddCall);
        var parent = f.Capture(UnityEventOperation.AddCall, parentFrame);
        var reads = f.Backend.Reads; var captures = f.Backend.Captures; var roots = f.Backend.RootRecords;
        foreach (var operation in UnityEventOperationProfile.All)
        {
            var frame = f.Frame(operation.Operation, receiver: 9999);
            Check(frame.IsValid && f.Owner.CaptureMutation(operation.Operation, frame) is null);
            f.Owner.ObserveMutationFault(operation.Operation, frame, "foreign observation has no owned receiver");
        }
        Equal(reads, f.Backend.Reads); Equal(captures, f.Backend.Captures); Equal(roots, f.Backend.RootRecords);
        Equal(1L, f.Owner.Read().ObservedMutations); Equal(1L, f.Owner.Read().NativeInFlight); Check(!f.Owner.Read().Unknown);
        f.Complete(parent, parentFrame); parent.Dispose(); Check(f.Owner.TryConfirmNativeIdle());
    }

    private static void BeforeFailure()
    {
        using var f = new Fixture(); var beforeRoots = f.Backend.RootRecords; f.Backend.FailCapture = true;
        var frame = f.Frame(UnityEventOperation.RemoveListener);
        var context = f.Capture(UnityEventOperation.RemoveListener, frame);
        var pending = f.Owner.Read(); var record = pending.EventSnapshots.Single().Mutations!.Single();
        Check(pending.Unknown && !record.Captured && record.Before is null && !record.Completed && !record.NativeReturned);
        Check(f.Backend.RootRecords > beforeRoots); var roots = f.Backend.RootRecords;
        var reads = f.Backend.Reads; var captures = f.Backend.Captures;
        f.Complete(context, frame); context.Dispose();
        record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        Check(context.Completed && context.Disposed && record.Completed && record.NativeReturned && !record.ConfirmedReturned
            && !record.Captured && record.After is null);
        Equal(0L, f.Owner.Read().NativeInFlight); Equal(0L, f.Owner.Read().ReturnedMutations);
        Equal(reads, f.Backend.Reads); Equal(captures, f.Backend.Captures); Equal(roots, f.Backend.RootRecords);
        f.NoMoreReadsAfterUnknown();
    }

    private static void AfterFailure()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.AddCall);
        var context = f.Capture(UnityEventOperation.AddCall, frame);
        var roots = f.Backend.RootRecords; var reads = f.Backend.Reads; f.Backend.FailRead = true;
        f.Complete(context, frame); context.Dispose();
        var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        Check(record.Captured && record.Before is not null && record.Completed && record.NativeReturned
            && !record.ConfirmedReturned && record.After is null && f.Owner.Read().Unknown);
        Equal(reads + 1, f.Backend.Reads); Equal(roots, f.Backend.RootRecords);
        Equal(0L, f.Owner.Read().NativeInFlight); Equal(0L, f.Owner.Read().ReturnedMutations);
        f.NoMoreReadsAfterUnknown();
    }

    private static void AbnormalOriginal()
    {
        foreach (var flags in new[] { NativeOperationExitFlags.Abnormal,
            NativeOperationExitFlags.Abnormal | NativeOperationExitFlags.EnterFaulted,
            NativeOperationExitFlags.Returned | NativeOperationExitFlags.EnterFaulted })
        {
            using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.RemoveAllListeners);
            var context = f.Capture(UnityEventOperation.RemoveAllListeners, frame);
            var reads = f.Backend.Reads; var roots = f.Backend.RootRecords;
            f.Owner.CompleteMutation(context, frame, flags); context.Dispose();
            var state = f.Owner.Read(); var record = state.EventSnapshots.Single().Mutations!.Single();
            Check(record.Captured && record.Completed
                && record.NativeReturned == ((flags & NativeOperationExitFlags.Returned) != 0) && !record.ConfirmedReturned
                && record.After is null && state.Unknown && context.Disposed);
            Equal((uint)flags, record.Flags); Equal(reads, f.Backend.Reads); Equal(roots, f.Backend.RootRecords);
            Equal(0L, state.NativeInFlight); f.NoMoreReadsAfterUnknown();
        }
    }

    private static void InvalidCompletion()
    {
        foreach (var changedField in new[] { "sequence", "caller", "argument", "method" })
        {
            using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.AddCall); var context = f.Capture(UnityEventOperation.AddCall, frame);
            var changed = changedField switch {
                "sequence" => frame with { Sequence = frame.Sequence + 1 }, "caller" => frame with { Caller = new(9) },
                "argument" => frame with { Arg0 = new(10) }, _ => frame with { Method = new(11) }
            };
            var reads = f.Backend.Reads;
            f.Owner.CompleteMutation(context, changed, NativeOperationExitFlags.Returned); context.Dispose();
            var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
            Check(f.Owner.Read().Unknown && !record.Completed && !context.Completed && !context.Disposed);
            Equal(1L, f.Owner.Read().NativeInFlight); Equal(reads, f.Backend.Reads); f.NoMoreReadsAfterUnknown();
        }
        using (var f = new Fixture())
        {
            var frame = f.Frame(UnityEventOperation.AddCall); var context = f.Capture(UnityEventOperation.AddCall, frame);
            f.Complete(context, frame); var reads = f.Backend.Reads;
            f.Complete(context, frame); context.Dispose();
            Check(f.Owner.Read().Unknown && context.Disposed); Equal(reads, f.Backend.Reads);
            Equal(1L, f.Owner.Read().ObservedMutations); Equal(1L, f.Owner.Read().ReturnedMutations);
            Equal(0L, f.Owner.Read().NativeInFlight); f.NoMoreReadsAfterUnknown();
        }
        using (var f = new Fixture())
        {
            var outerFrame = f.Frame(UnityEventOperation.AddCall); var outer = f.Capture(UnityEventOperation.AddCall, outerFrame);
            var innerFrame = f.Frame(UnityEventOperation.AddRuntimeCall); var inner = f.Capture(UnityEventOperation.AddRuntimeCall, innerFrame);
            var reads = f.Backend.Reads; f.Complete(outer, outerFrame); outer.Dispose();
            Check(f.Owner.Read().Unknown && !outer.Completed && !outer.Disposed && !inner.Completed);
            Equal(2L, f.Owner.Read().NativeInFlight); Equal(reads, f.Backend.Reads); f.NoMoreReadsAfterUnknown();
        }
    }

    private static void WorkerFaults()
    {
        foreach (var operation in new[] { UnityEventOperation.AddCall, UnityEventOperation.ClearRuntimeCalls,
            UnityEventOperation.InitializePersistentCalls })
        {
            using var f = new Fixture();
            var original = f.Frame(UnityEventOperation.AddCall); var context = f.Capture(UnityEventOperation.AddCall, original);
            f.Backend.Graph = f.Backend.Graph with { Calls = 901, PersistentGroup = 902 };
            f.Complete(context, original); context.Dispose();
            var receiver = operation == UnityEventOperation.AddCall ? 101 : operation == UnityEventOperation.ClearRuntimeCalls ? 901 : 902;
            var frame = f.Frame(operation, receiver); var reads = f.Backend.Reads; var captures = f.Backend.Captures;
            var roots = f.Backend.RootRecords; var activity = f.Backend.ActivityReads;
            Task.Run(() => {
                f.Owner.ObserveMutationFault(operation, f.Frame(operation, 9999), "foreign worker");
                Throws<InvalidOperationException>(() => f.Owner.CaptureMutation(operation, frame));
            }).GetAwaiter().GetResult();
            Check(!f.Owner.Read().Unknown);
            Task.Run(() => f.Owner.ObserveMutationFault(operation, frame, "owned operation fault on worker")).GetAwaiter().GetResult();
            Check(f.Owner.Read().Unknown); Equal(reads, f.Backend.Reads); Equal(captures, f.Backend.Captures);
            Equal(roots, f.Backend.RootRecords); Equal(activity, f.Backend.ActivityReads);
            Equal(1L, f.Owner.Read().ObservedMutations); Equal(0L, f.Owner.Read().NativeInFlight);
            f.NoMoreReadsAfterUnknown();
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Authority Authority = new();
        internal readonly Backend Backend = new();
        internal readonly NativeLocalUnityEventLedger Owner;
        private long _sequence;
        internal Fixture()
        {
            Owner = NativeLocalUnityEventLedger.CaptureBackend(Main, Backend, Authority, 0x20000000);
            Authority.Capturing = false;
        }
        internal NativeOperationFrame Frame(UnityEventOperation operation, long? receiver = null)
        {
            var row = UnityEventOperationProfile.Get(operation);
            var self = receiver ?? (row.Receiver switch {
                UnityEventOperationReceiver.Event => Backend.Graph.Event,
                UnityEventOperationReceiver.Calls => Backend.Graph.Calls, _ => Backend.Graph.PersistentGroup });
            return new() { Version = 1, Size = 64, Kind = row.Kind, Self = new(self),
                Arg0 = row.Kind == NativeOperationKind.Void0 ? IntPtr.Zero : new(501),
                Arg1 = row.Kind == NativeOperationKind.Reference2 ? new(502) : IntPtr.Zero,
                Method = new(601), Caller = new(0x20001234), Sequence = checked((ulong)Interlocked.Increment(ref _sequence)) };
        }
        internal NativeLocalUnityEventLedger.MutationContext Capture(UnityEventOperation operation, NativeOperationFrame frame)
            => Owner.CaptureMutation(operation, frame) ?? throw new InvalidOperationException("The owned controlled operation was not captured.");
        internal void Complete(NativeLocalUnityEventLedger.MutationContext context, NativeOperationFrame frame)
            => Owner.CompleteMutation(context, frame, NativeOperationExitFlags.Returned);
        internal void NoMoreReadsAfterUnknown()
        {
            var reads = Backend.Reads; var captures = Backend.Captures; var roots = Backend.RootRecords; var activity = Backend.ActivityReads;
            Throws<InvalidOperationException>(() => Owner.CaptureMutation(UnityEventOperation.AddCall, Frame(UnityEventOperation.AddCall)));
            Throws<InvalidOperationException>(() => Owner.TryConfirmNativeIdle());
            Equal(reads, Backend.Reads); Equal(captures, Backend.Captures); Equal(roots, Backend.RootRecords); Equal(activity, Backend.ActivityReads);
        }
        public void Dispose() => Main.Dispose(); // No false release of retained process roots/contexts.
    }
    private sealed class Authority : ICutoutListenerAuthority
    {
        internal bool Capturing = true;
        private int _faults;
        public bool Unknown => Volatile.Read(ref _faults) != 0;
        public void VerifyBeforeCapture() { if (!Capturing || Unknown) throw new InvalidOperationException("Controlled capture window is closed."); }
        public void VerifyObservation() { if (Capturing || Unknown) throw new InvalidOperationException("Controlled observation is unknown."); }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() => Interlocked.Increment(ref _faults);
    }
    private sealed class Backend : ICutoutListenerBackend
    {
        internal NativeUnityEventGraph Graph = new(101, 201, 202, false, false,
            new(301, 0, Array.Empty<NativeUnityEventInvokable>()), new(302, 0, Array.Empty<NativeUnityEventInvokable>()),
            new(303, 0, Array.Empty<NativeUnityEventInvokable>()), 304, 0, Array.Empty<NativeUnityEventPersistent>(), 0);
        private readonly HashSet<long> _roots = new();
        internal int Reads, Captures, ActivityReads;
        internal bool FailCapture, FailRead;
        public bool CleanupUnknown => false;
        public int RootRecords => _roots.Count;
        public void Initialize(NativeLocalUnityEventLedger owner) => owner.RegisterEvent(Graph.Event);
        public NativeUnityEventGraph ReadGraph(int eventSlot)
        { ++Reads; Check(eventSlot == 0); if (FailRead) throw new IOException("Controlled post graph read failure."); RetainGraph(); return Graph; }
        public NativeUnityEventGraph CaptureMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
        {
            ++Captures; Check(eventSlot == 0 && frame.IsValid && frame.Kind == UnityEventOperationProfile.Get(operation).Kind);
            foreach (var value in new[] { frame.Self.ToInt64(), frame.Arg0.ToInt64(), frame.Arg1.ToInt64() }) if (value != 0) _roots.Add(value);
            if (FailCapture) throw new IOException("Controlled capture failure after reference retention.");
            RetainGraph(); return Graph;
        }
        private void RetainGraph()
        {
            foreach (var value in new[] { Graph.Event, Graph.Calls, Graph.PersistentGroup, Graph.PersistentCalls.Object,
                Graph.RuntimeCalls.Object, Graph.ExecutingCalls.Object, Graph.CandidatesList }) _roots.Add(value);
            foreach (var row in Graph.RuntimeCalls.Rows.Concat(Graph.ExecutingCalls.Rows).Concat(Graph.PersistentCalls.Rows)) _roots.Add(row.Object);
        }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList) => throw new InvalidOperationException("Mutation cases never synthesize Prepare.");
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() { ++ActivityReads; return new(0, true); }
    }
}
