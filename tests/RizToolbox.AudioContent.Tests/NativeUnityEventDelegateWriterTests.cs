using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

// Exercises the real ledger's mutation/context/history algorithms with rooted
// controlled observations. This is not a game session or a native CAS execution.
internal static class NativeUnityEventDelegateWriterTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Delegate writer records the actual final multicast chain rather than the add argument", ActualCombinedChain),
        ("Delegate writer records a null final field after removing the last action without declaring future sources closed", ActualRemovedChain),
        ("One shared rooted invokable records one native writer sequence for each associated event", SharedInvokable),
        ("Delegate writer still observes a historical rooted invokable after every current list removes it", RemovedInvokable),
        ("Delegate writer snapshots do not change when later native chain observations change", ImmutableChain),
        ("Nested delegate writers preserve independent native contexts and final chain observations", NestedWriters),
        ("Delegate writer before capture failure keeps its exact completion duty and earlier adopted roots", CaptureFailure),
        ("Delegate writer after failure separates original return from unconfirmed final chain", FinalReadFailure),
        ("Abnormal delegate writer completes scalar duty without a post-chain native read", AbnormalWriter),
        ("Delegate writer wrong native completion identity cannot finish an owned generation", WrongCompletion),
        ("Delegate writer faults on workers recognize historical roots without reading native objects", WorkerWriter),
        ("Foreign or unobserved invokables never acquire an owned delegate writer context", ForeignWriter),
        ("Delegate writer rejects a changed rooted receiver class even after list removal", ReceiverClassDrift)
    };

    private static void ActualCombinedChain()
    {
        using var f = new Fixture(); var before = f.Backend.Current;
        var frame = f.Frame(UnityEventOperation.AddDelegate, argument: 9901);
        var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        f.Backend.Current = Invokable(9000, 9002, Leaf(8001), Leaf(9901));
        f.Complete(context, frame); context.Dispose();
        var state = f.Owner.Read(); var record = state.EventSnapshots.Single().Mutations!.Single();
        Check(!state.Unknown && record.Captured && record.ConfirmedReturned && record.Completed && record.NativeReturned);
        Equal(before.Delegate, record.BeforeInvokable!.Delegate); Equal(9000L, record.AfterInvokable!.Delegate);
        Check(record.AfterInvokable.Delegate != record.Arg0); Equal(2, record.AfterInvokable.Leaves.Count);
        Check(state.EventSnapshots.Single().ChangedSinceCapture);
        Equal(record.Before!.RuntimeCalls.Version, record.After!.RuntimeCalls.Version);
        var provenance = f.Owner.ReadListenerEffects().MembershipSources.Single();
        Equal(9000L, provenance.AfterInvokable!.Delegate); Equal(0, provenance.AddedInvokables.Count);
        Check(!f.Owner.ReadListenerEffects().FutureMembershipWritersClosed);
    }
    private static void ActualRemovedChain()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.RemoveDelegate, argument: 8001);
        var context = f.Capture(UnityEventOperation.RemoveDelegate, frame);
        f.Backend.Current = Invokable(0, 0); f.Complete(context, frame); context.Dispose();
        var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        Equal(8001L, record.BeforeInvokable!.Delegate); Equal(0L, record.AfterInvokable!.Delegate);
        Equal(0, record.AfterInvokable.Leaves.Count); Check(record.ConfirmedReturned);
        Check(f.Owner.Read().Pending && !f.Owner.Read().MutationClosed && !f.Owner.ReadListenerEffects().FutureMembershipWritersClosed);
        Check(f.Owner.ReadListenerWriterSources().State != NativeLocalActiveConsumerProofState.Proven);
    }
    private static void SharedInvokable()
    {
        using var f = new Fixture(2); var frame = f.Frame(UnityEventOperation.AddDelegate);
        var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        Equal(2, context.Records.Length); Equal(2L, f.Owner.Read().NativeInFlight);
        f.Backend.Current = Invokable(9001, 0, Leaf(9001)); f.Complete(context, frame); context.Dispose();
        var state = f.Owner.Read(); Equal(2L, state.ObservedMutations); Equal(2L, state.ReturnedMutations);
        var records = state.EventSnapshots.Select(row => row.Mutations!.Single()).ToArray();
        Check(records.All(record => record.NativeSequence == frame.Sequence && record.ConfirmedReturned));
        Check(records[0].Before!.Event != records[1].Before!.Event); Equal(2, f.Backend.FinalReads);
        Check(!state.Unknown);
    }
    private static void RemovedInvokable()
    {
        using var f = new Fixture();
        var remove = f.Frame(UnityEventOperation.ClearRuntimeCalls, receiver: 201);
        var membership = f.Capture(UnityEventOperation.ClearRuntimeCalls, remove);
        f.Backend.Present[0] = false; f.Complete(membership, remove); membership.Dispose();
        var frame = f.Frame(UnityEventOperation.AddDelegate); var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        f.Backend.Current = Invokable(9001, 0, Leaf(9001)); f.Complete(context, frame); context.Dispose();
        var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Last();
        Equal(0, record.Before!.RuntimeCalls.Rows.Count); Equal(0, record.After!.RuntimeCalls.Rows.Count);
        Equal(701L, record.BeforeInvokable!.Object); Equal(9001L, record.AfterInvokable!.Delegate);
        Check(record.ConfirmedReturned && !f.Owner.Read().Unknown);
        var reads = f.Backend.Reads;
        Task.Run(() => f.Owner.ObserveMutationFault(UnityEventOperation.AddDelegate, frame, "historical writer worker")).GetAwaiter().GetResult();
        Check(f.Owner.Read().Unknown); Equal(reads, f.Backend.Reads);
    }
    private static void ImmutableChain()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.AddDelegate);
        var mutable = new List<NativeUnityEventLeaf> { Leaf(9101) };
        var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        f.Backend.Current = Invokable(9101, 0, mutable.ToArray()) with { Leaves = mutable };
        f.Complete(context, frame); context.Dispose();
        var held = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        mutable.Clear(); mutable.Add(Leaf(9999)); f.Backend.Current = Invokable(9999, 0, Leaf(9999));
        Equal(9101L, held.AfterInvokable!.Delegate); Equal(9101L, held.AfterInvokable.Leaves.Single().Delegate);
        Equal(8001L, held.BeforeInvokable!.Delegate);
    }
    private static void NestedWriters()
    {
        using var f = new Fixture(); var outerFrame = f.Frame(UnityEventOperation.AddDelegate);
        var outer = f.Capture(UnityEventOperation.AddDelegate, outerFrame);
        var innerFrame = f.Frame(UnityEventOperation.RemoveDelegate); var inner = f.Capture(UnityEventOperation.RemoveDelegate, innerFrame);
        Check(ReferenceEquals(inner.Parent, outer)); f.Backend.Current = Invokable(0, 0);
        f.Complete(inner, innerFrame); inner.Dispose(); f.Backend.Current = Invokable(9201, 0, Leaf(9201));
        f.Complete(outer, outerFrame); outer.Dispose();
        var records = f.Owner.Read().EventSnapshots.Single().Mutations!;
        Equal(9201L, records[0].AfterInvokable!.Delegate); Equal(0L, records[1].AfterInvokable!.Delegate);
        Check(records.All(record => record.ConfirmedReturned) && !f.Owner.Read().Unknown);
    }
    private static void CaptureFailure()
    {
        using var f = new Fixture(); f.Backend.FailCapture = true; var roots = f.Backend.RootRecords;
        var frame = f.Frame(UnityEventOperation.AddDelegate); var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        Check(f.Owner.Read().Unknown && f.Backend.RootRecords > roots); var reads = f.Backend.Reads;
        f.Complete(context, frame); context.Dispose();
        var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        Check(record.Completed && record.NativeReturned && !record.Captured && !record.ConfirmedReturned && record.AfterInvokable is null);
        Equal(reads, f.Backend.Reads); Equal(0, f.Backend.FinalReads);
    }
    private static void FinalReadFailure()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.RemoveDelegate); var context = f.Capture(UnityEventOperation.RemoveDelegate, frame);
        f.Backend.FailFinal = true; f.Complete(context, frame); context.Dispose();
        var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        Check(f.Owner.Read().Unknown && record.BeforeInvokable is not null && record.AfterInvokable is null
            && record.Completed && record.NativeReturned && !record.ConfirmedReturned); Equal(1, f.Backend.FinalReads);
    }
    private static void AbnormalWriter()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.AddDelegate); var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        var reads = f.Backend.Reads; f.Owner.CompleteMutation(context, frame, NativeOperationExitFlags.Abnormal); context.Dispose();
        var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        Check(f.Owner.Read().Unknown && record.Completed && !record.NativeReturned && !record.ConfirmedReturned);
        Equal(reads, f.Backend.Reads); Equal(0, f.Backend.FinalReads);
    }
    private static void WrongCompletion()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.AddDelegate); var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        var reads = f.Backend.Reads; f.Complete(context, frame with { Sequence = frame.Sequence + 1 });
        context.Dispose(); Check(f.Owner.Read().Unknown && !context.Completed && !context.Disposed);
        Equal(1L, f.Owner.Read().NativeInFlight); Equal(reads, f.Backend.Reads);
    }
    private static void WorkerWriter()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.RemoveDelegate);
        var reads = f.Backend.Reads; var roots = f.Backend.RootRecords;
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.CaptureMutation(UnityEventOperation.RemoveDelegate, frame))).GetAwaiter().GetResult();
        Check(!f.Owner.Read().Unknown);
        Task.Run(() => f.Owner.ObserveMutationFault(UnityEventOperation.RemoveDelegate, frame, "actual owned writer on worker")).GetAwaiter().GetResult();
        Check(f.Owner.Read().Unknown); Equal(reads, f.Backend.Reads); Equal(roots, f.Backend.RootRecords);
    }
    private static void ForeignWriter()
    {
        using var f = new Fixture(); var frame = f.Frame(UnityEventOperation.AddDelegate, receiver: 9999);
        var reads = f.Backend.Reads; var roots = f.Backend.RootRecords;
        Check(f.Owner.CaptureMutation(UnityEventOperation.AddDelegate, frame) is null);
        f.Owner.ObserveMutationFault(UnityEventOperation.AddDelegate, frame, "foreign writer");
        Equal(reads, f.Backend.Reads); Equal(roots, f.Backend.RootRecords); Check(!f.Owner.Read().Unknown);
    }
    private static void ReceiverClassDrift()
    {
        using var f = new Fixture(); f.Backend.Present[0] = false;
        var frame = f.Frame(UnityEventOperation.AddDelegate); var context = f.Capture(UnityEventOperation.AddDelegate, frame);
        f.Backend.Current = Invokable(9001, 0, Leaf(9001)) with { Class = 799 };
        f.Complete(context, frame); context.Dispose();
        var record = f.Owner.Read().EventSnapshots.Single().Mutations!.Single();
        Check(record.BeforeInvokable is not null && record.AfterInvokable is null
            && record.Completed && record.NativeReturned && !record.ConfirmedReturned && f.Owner.Read().Unknown);
    }

    private static NativeUnityEventLeaf Leaf(long value) => new(value, 703, 801, 802, 803, 804, 805, 806, 0, 0, false, false, "Known.InstanceVoid()");
    private static NativeUnityEventInvokable Invokable(long action, long list, params NativeUnityEventLeaf[] leaves)
        => new(701, 702, action == 0 || leaves.Any(leaf => leaf.Opaque), leaves, action, list);
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Backend Backend;
        internal readonly Authority Authority = new();
        internal readonly NativeLocalUnityEventLedger Owner;
        private ulong _sequence;
        internal Fixture(int events = 1)
        {
            Backend = new(events); Owner = NativeLocalUnityEventLedger.CaptureBackend(Main, Backend, Authority, 0x20000000);
            Authority.Capturing = false;
        }
        internal NativeOperationFrame Frame(UnityEventOperation operation, long receiver = 701, long argument = 9901)
        {
            var declaration = UnityEventOperationProfile.Get(operation);
            return new() { Version = 1, Size = 64, Kind = declaration.Kind, Self = new(receiver),
                Arg0 = declaration.Kind == NativeOperationKind.Void0 ? IntPtr.Zero : new(argument), Caller = new(0x20001234),
                Method = new(0x20004321), Sequence = ++_sequence };
        }
        internal NativeLocalUnityEventLedger.MutationContext Capture(UnityEventOperation operation, NativeOperationFrame frame)
            => Owner.CaptureMutation(operation, frame) ?? throw new InvalidOperationException("Known rooted invokable was not captured.");
        internal void Complete(NativeLocalUnityEventLedger.MutationContext context, NativeOperationFrame frame)
            => Owner.CompleteMutation(context, frame, NativeOperationExitFlags.Returned);
        public void Dispose() => Main.Dispose(); // Individual cases run in isolated processes; no invented root-free permission.
    }
    private sealed class Authority : ICutoutListenerAuthority
    {
        internal bool Capturing = true;
        private int _fault;
        public bool Unknown => Volatile.Read(ref _fault) != 0;
        public void VerifyBeforeCapture() { if (!Capturing || Unknown) throw new InvalidOperationException("Capture is unavailable."); }
        public void VerifyObservation() { if (Capturing || Unknown) throw new InvalidOperationException("Observation is unavailable."); }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() => Interlocked.Exchange(ref _fault, 1);
    }
    private sealed class Backend : ICutoutListenerBackend
    {
        internal NativeUnityEventInvokable Current = Invokable(8001, 0, Leaf(8001));
        internal readonly bool[] Present;
        private readonly HashSet<long> _roots = new();
        internal int Reads, FinalReads, Captures;
        internal bool FailCapture, FailFinal;
        internal Backend(int events) => Present = Enumerable.Repeat(true, events).ToArray();
        public bool CleanupUnknown => false;
        public int RootRecords => _roots.Count;
        public void Initialize(NativeLocalUnityEventLedger owner)
        { for (var slot = 0; slot < Present.Length; ++slot) Equal(slot, owner.RegisterEvent(101 + slot)); }
        public NativeUnityEventGraph ReadGraph(int slot)
        {
            ++Reads; Check(slot >= 0 && slot < Present.Length);
            _roots.Add(101 + slot); _roots.Add(201 + slot); _roots.Add(701);
            foreach (var leaf in Current.Leaves) _roots.Add(leaf.Delegate);
            if (Current.Delegate != 0) _roots.Add(Current.Delegate);
            return new(101 + slot, 201 + slot, 401 + slot, false, false,
                new(501 + slot, 0, Array.Empty<NativeUnityEventInvokable>()),
                new(601 + slot, 0, Present[slot] ? new[] { Current } : Array.Empty<NativeUnityEventInvokable>()),
                new(901 + slot, 0, Array.Empty<NativeUnityEventInvokable>()), 1001 + slot, 0, Array.Empty<NativeUnityEventPersistent>(), 0);
        }
        public NativeUnityEventGraph CaptureMutation(int slot, UnityEventOperation operation, NativeOperationFrame frame)
        {
            ++Captures; Check(frame.IsValid && frame.Kind == UnityEventOperationProfile.Get(operation).Kind);
            _roots.Add(frame.Self.ToInt64()); if (frame.Arg0 != IntPtr.Zero) _roots.Add(frame.Arg0.ToInt64());
            if (FailCapture) throw new IOException("Controlled failure after actual argument adoption.");
            return ReadGraph(slot);
        }
        public NativeUnityEventDelegateMutationCapture CaptureDelegateMutation(int slot, UnityEventOperation operation, NativeOperationFrame frame)
            => new(CaptureMutation(slot, operation, frame), Current);
        public NativeUnityEventDelegateMutationCapture ReadDelegateMutation(int slot, UnityEventOperation operation, NativeOperationFrame frame)
        {
            ++FinalReads; if (FailFinal) throw new IOException("Controlled final-chain read failure.");
            return new(ReadGraph(slot), Current);
        }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList)
            => throw new InvalidOperationException("No synthetic Prepare in delegate writer cases.");
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
    }
}
