using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeUnityEventMutationSnapshot(UnityEventOperation Operation, ulong NativeSequence, long Generation,
    long Receiver, long Arg0, long Arg1, long Caller, long Method, bool Captured, bool Completed, bool NativeReturned,
    bool ConfirmedReturned, uint Flags, NativeUnityEventGraph? Before, NativeUnityEventGraph? After,
    NativeUnityEventInvokable? BeforeInvokable = null, NativeUnityEventInvokable? AfterInvokable = null);

internal sealed partial class NativeLocalUnityEventLedger
{
    private const int MaximumMutationRecords = 512;
    private long _mutationCount, _mutationReturned, _typedMutationsInFlight;
    private MutationContext? _mutationCurrent;
    private readonly List<TypedEventWriterRecord> _typedEventWriters = new();
    private TypedEventWriterRecord[] _typedWriterSnapshot = Array.Empty<TypedEventWriterRecord>();
    internal readonly record struct MutationReceiverIdentity(long Calls, long Group);
    internal sealed class TypedEventWriterRecord
    {
        internal readonly long Event;
        internal long WriterGeneration;
        internal long[] Calls = Array.Empty<long>();
        internal long[] PersistentGroups = Array.Empty<long>();
        internal long[] Invokables = Array.Empty<long>();
        internal TypedEventWriterRecord(long eventIdentity) => Event = eventIdentity;
    }
    // Tracks non-generic UnityEventBase / InvokableCallList / PersistentCallGroup /
    // InvokableCall writers targeting an owned onTween<Single> without adding onTween
    // to Cutout void _events (ChildEventRole). Generic UnityEvent<Single> /
    // InvokableCall<Single> add/remove methods remain unhooked by UnityEventOperationProfile
    // until generic writer hooks (M3B/M3C), so Pre==Mid==Post==Dispose SingleEventGraph
    // equality is also enforced alongside ReadTypedEventWriterGeneration.
    internal void RegisterTypedEventWriter(long eventIdentity, long callsIdentity, long persistentGroupIdentity,
        IEnumerable<long>? invokableIdentities = null)
    {
        _main.VerifyAccess(); Known();
        if (eventIdentity == 0)
            throw new InvalidOperationException("Typed event writer tracking requires a non-zero event identity.");
        var record = _typedEventWriters.SingleOrDefault(row => row.Event == eventIdentity);
        if (record is null)
        {
            if (_typedEventWriters.Count >= MaximumEvents)
                throw new InvalidOperationException("Distinct typed Single event writer capacity exhausted.");
            record = new TypedEventWriterRecord(eventIdentity);
            _typedEventWriters.Add(record);
            Volatile.Write(ref _typedWriterSnapshot, _typedEventWriters.ToArray());
        }
        if (callsIdentity != 0)
        {
            var priorCalls = Volatile.Read(ref record.Calls);
            if (!priorCalls.Contains(callsIdentity))
            {
                if (priorCalls.Length >= MaximumMutationRecords)
                    throw new InvalidOperationException("Typed event m_Calls tracking budget exhausted.");
                Volatile.Write(ref record.Calls, priorCalls.Append(callsIdentity).ToArray());
            }
        }
        if (persistentGroupIdentity != 0)
        {
            var priorGroups = Volatile.Read(ref record.PersistentGroups);
            if (!priorGroups.Contains(persistentGroupIdentity))
            {
                if (priorGroups.Length >= MaximumMutationRecords)
                    throw new InvalidOperationException("Typed event m_PersistentCalls tracking budget exhausted.");
                Volatile.Write(ref record.PersistentGroups, priorGroups.Append(persistentGroupIdentity).ToArray());
            }
        }
        if (invokableIdentities is not null)
        {
            var prior = Volatile.Read(ref record.Invokables);
            var added = invokableIdentities.Where(id => id != 0 && !prior.Contains(id)).Distinct().ToArray();
            if (added.Length != 0)
            {
                if (prior.Length + added.Length > MaximumRoots)
                    throw new InvalidOperationException("Typed event invokable tracking budget exhausted.");
                Volatile.Write(ref record.Invokables, prior.Concat(added).ToArray());
            }
        }
    }
    internal long ReadTypedEventWriterGeneration(long eventIdentity)
    {
        _main.VerifyAccess(); Known();
        if (eventIdentity == 0)
            throw new InvalidOperationException("Typed Single writer-generation read requires a non-zero event identity.");
        var record = _typedEventWriters.SingleOrDefault(row => row.Event == eventIdentity)
            ?? throw new InvalidOperationException("Typed Single event has no registered writer-generation tracker.");
        return record.WriterGeneration;
    }
    private void TrackMutationReceivers(EventRecord row, NativeUnityEventGraph graph)
    {
        foreach (var list in Lists(graph)) TrackMutationInvokables(row, list);
        var prior = Volatile.Read(ref row.MutationReceivers);
        var identity = new MutationReceiverIdentity(graph.Calls, graph.PersistentGroup);
        if (prior.Contains(identity)) return;
        if (prior.Length >= MaximumMutationRecords) throw new InvalidOperationException("Mutation receiver identity budget exhausted.");
        Volatile.Write(ref row.MutationReceivers, prior.Append(identity).ToArray());
    }
    private void TrackMutationInvokables(EventRecord row, NativeUnityEventList list)
    {
        // Every identity comes from a complete rooted backend snapshot. Roots
        // stay retained after list removal, so an address cannot silently become
        // another invokable while this historical source table remains owned.
        var prior = Volatile.Read(ref row.MutationInvokables);
        var next = prior.Concat(list.Rows.Select(value => value.Object)).Distinct().ToArray();
        if (next.Any(value => value == 0) || next.Length > MaximumRoots)
            throw new InvalidOperationException("Historical listener mutation identities exceed their rooted source budget.");
        foreach (var value in list.Rows)
        {
            if (value.Class == 0 || row.MutationInvokableClasses.TryGetValue(value.Object, out var priorClass) && priorClass != value.Class)
                throw new InvalidOperationException("A retained historical invokable changed its exact class identity.");
            row.MutationInvokableClasses[value.Object] = value.Class;
        }
        if (next.Length != prior.Length) Volatile.Write(ref row.MutationInvokables, next);
    }
    internal sealed class MutationRecord
    {
        internal readonly UnityEventOperation Operation;
        internal readonly NativeOperationFrame Frame;
        internal readonly EventRecord Event;
        internal readonly long Generation;
        internal NativeUnityEventGraph? Before, After;
        internal NativeUnityEventInvokable? BeforeInvokable, AfterInvokable;
        internal bool Captured, Completed, NativeReturned, ConfirmedReturned;
        internal uint Flags;
        internal MutationRecord(UnityEventOperation operation, NativeOperationFrame frame, EventRecord row)
            => (Operation, Frame, Event, Generation) = (operation, frame, row, row.MutationGeneration);
        internal NativeUnityEventMutationSnapshot Snapshot() => new(Operation, Frame.Sequence, Generation, Frame.Self.ToInt64(),
            Frame.Arg0.ToInt64(), Frame.Arg1.ToInt64(), Frame.Caller.ToInt64(), Frame.Method.ToInt64(), Captured, Completed,
            NativeReturned, ConfirmedReturned, Flags, Before, After, BeforeInvokable, AfterInvokable);
    }
    internal sealed class MutationContext : IDisposable
    {
        private readonly NativeLocalUnityEventLedger _owner;
        internal readonly MutationContext? Parent;
        internal readonly NativeOperationFrame Frame;
        internal readonly MutationRecord[] Records;
        internal readonly TypedEventWriterRecord[] TypedRecords;
        internal bool Completed, Disposed;
        internal MutationContext(NativeLocalUnityEventLedger owner, NativeOperationFrame frame,
            MutationRecord[] records, TypedEventWriterRecord[] typedRecords)
            => (_owner, Parent, Frame, Records, TypedRecords) = (owner, owner._mutationCurrent, frame, records, typedRecords);
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (Disposed || !Completed || !ReferenceEquals(_owner._mutationCurrent, this))
            { _owner.Fault("Mutation cleanup has no exact completed physical context."); return; }
            Disposed = true; _owner._mutationCurrent = Parent;
        }
    }
    private EventRecord[] FindMutationEvents(UnityEventOperation operation, long receiver)
    {
        if (receiver == 0) return Array.Empty<EventRecord>();
        var kind = UnityEventOperationProfile.Get(operation).Receiver;
        return _events.Take(_count).Where(row => kind switch {
            UnityEventOperationReceiver.Event => Interlocked.Read(ref row.Event) == receiver,
            UnityEventOperationReceiver.Calls => Volatile.Read(ref row.MutationReceivers).Any(value => value.Calls == receiver),
            UnityEventOperationReceiver.PersistentGroup => Volatile.Read(ref row.MutationReceivers).Any(value => value.Group == receiver),
            UnityEventOperationReceiver.Invokable => Volatile.Read(ref row.MutationInvokables).Contains(receiver),
            _ => false }).ToArray();
    }
    private TypedEventWriterRecord[] FindTypedMutationEvents(UnityEventOperation operation, long receiver)
    {
        if (receiver == 0) return Array.Empty<TypedEventWriterRecord>();
        var kind = UnityEventOperationProfile.Get(operation).Receiver;
        return _typedEventWriters.Where(row => kind switch {
            UnityEventOperationReceiver.Event => row.Event == receiver,
            UnityEventOperationReceiver.Calls => Volatile.Read(ref row.Calls).Contains(receiver),
            UnityEventOperationReceiver.PersistentGroup => Volatile.Read(ref row.PersistentGroups).Contains(receiver),
            UnityEventOperationReceiver.Invokable => Volatile.Read(ref row.Invokables).Contains(receiver),
            _ => false }).ToArray();
    }
    internal MutationContext? CaptureMutation(UnityEventOperation operation, NativeOperationFrame frame)
    {
        _main.VerifyAccess();
        var receiver = frame.Self.ToInt64();
        var rows = FindMutationEvents(operation, receiver);
        var typedRows = FindTypedMutationEvents(operation, receiver);
        if (rows.Length == 0 && typedRows.Length == 0) return null;
        Guard(); _authority.VerifyObservation();
        var total = checked(rows.Length + typedRows.Length);
        if (_busy || !frame.IsValid || frame.Kind != UnityEventOperationProfile.Get(operation).Kind
            || _mutationCount > MaximumMutationRecords - total)
        { Fault("Owned mutation has invalid physical identity, reentered a native read or exhausted its record budget."); return null; }
        MutationContext? context = null;
        try
        {
            var records = new MutationRecord[rows.Length];
            for (var index = 0; index < rows.Length; ++index)
            {
                var row = rows[index]; row.MutationGeneration = checked(row.MutationGeneration + 1);
                var record = new MutationRecord(operation, frame, row); row.Mutations.Add(record);
                _mutationCount = checked(_mutationCount + 1); records[index] = record;
            }
            foreach (var typed in typedRows)
            {
                typed.WriterGeneration = checked(typed.WriterGeneration + 1);
                _mutationCount = checked(_mutationCount + 1);
                Interlocked.Increment(ref _typedMutationsInFlight);
            }
            context = new(this, frame, records, typedRows); _mutationCurrent = context;
            _busy = true;
            foreach (var record in records)
            {
                var delegateWrite = UnityEventOperationProfile.Get(operation).Receiver == UnityEventOperationReceiver.Invokable;
                if (delegateWrite) VerifyNativeTransport(requireActive: false);
                var capture = delegateWrite ? _backend.CaptureDelegateMutation(record.Event.Slot, operation, frame) : null;
                if (delegateWrite && capture is null)
                    throw new InvalidOperationException("Delegate writer capture omitted its actual retained receiver chain.");
                if (delegateWrite) VerifyNativeTransport(requireActive: false);
                var graph = Freeze(capture?.Graph ?? _backend.CaptureMutation(record.Event.Slot, operation, frame)); Known();
                Validate(graph, record.Event.Event); TrackMutationReceivers(record.Event, graph); CheckBudget(record.Event.Slot, graph);
                if (capture is not null) record.BeforeInvokable = FreezeMutationInvokable(record, capture.Invokable);
                record.Before = graph; record.Captured = true;
            }
            return context;
        }
        catch (Exception error)
        {
            Fault(error.Message);
            // A failed capture retains both acquired roots and the exact scalar
            // completion duty. Do not turn it into an unobserved successful op.
            if (context is not null) return context;
            throw;
        }
        finally { _busy = false; }
    }
    internal void CompleteMutation(MutationContext context, NativeOperationFrame frame, NativeOperationExitFlags flags)
    {
        _main.VerifyAccess();
        if (context.Completed || context.Disposed || !ReferenceEquals(_mutationCurrent, context)
            || !SameMutationFrame(context.Frame, frame))
        { Fault("Mutation completion crossed its retained physical generation."); return; }
        // Mark exact native completion before any optional native graph read.
        context.Completed = true;
        foreach (var record in context.Records)
        { record.Completed = true; record.NativeReturned = (flags & NativeOperationExitFlags.Returned) != 0
            && (flags & NativeOperationExitFlags.Abnormal) == 0; record.Flags = (uint)flags; }
        for (var index = 0; index < context.TypedRecords.Length; ++index)
            Interlocked.Decrement(ref _typedMutationsInFlight);
        if (flags != NativeOperationExitFlags.Returned)
        { Fault("Owned listener mutation original exited abnormally; partial writes remain possible."); return; }
        if (Unknown) return;
        try
        {
            _authority.VerifyObservation(); _busy = true;
            foreach (var record in context.Records)
            {
                var delegateWrite = UnityEventOperationProfile.Get(record.Operation).Receiver == UnityEventOperationReceiver.Invokable;
                if (delegateWrite) VerifyNativeTransport(requireActive: false);
                var capture = delegateWrite ? _backend.ReadDelegateMutation(record.Event.Slot, record.Operation, frame) : null;
                if (delegateWrite && capture is null)
                    throw new InvalidOperationException("Delegate writer completion omitted its actual final field chain.");
                if (delegateWrite) VerifyNativeTransport(requireActive: false);
                var graph = capture is null ? ReadGraph(record.Event.Slot) : Freeze(capture.Graph);
                Validate(graph, record.Event.Event); TrackMutationReceivers(record.Event, graph); CheckBudget(record.Event.Slot, graph); Known();
                if (capture is not null) record.AfterInvokable = FreezeMutationInvokable(record, capture.Invokable);
                record.After = graph;
                if (record.Before is not null && (!GraphEqual(record.Before, graph)
                    || !MutationInvokableEqual(record.BeforeInvokable, record.AfterInvokable))) record.Event.Changed = true;
                record.ConfirmedReturned = record.Captured;
                if (record.ConfirmedReturned) _mutationReturned = checked(_mutationReturned + 1);
            }
            for (var index = 0; index < context.TypedRecords.Length; ++index)
                _mutationReturned = checked(_mutationReturned + 1);
        }
        catch (Exception error) { Fault(error.Message); }
        finally { _busy = false; }
    }
    internal void ObserveMutationFault(UnityEventOperation operation, NativeOperationFrame frame, string failure)
    {
        // Worker callbacks may inspect only fixed managed scalar identities.
        // Receiver tables are immutable managed arrays published atomically.
        // No worker enumerates the mutable operation records or reads objects.
        var kind = UnityEventOperationProfile.Get(operation).Receiver;
        var receiver = frame.Self.ToInt64();
        foreach (var row in _events)
        {
            if (Interlocked.Read(ref row.Event) == 0) continue;
            if (kind == UnityEventOperationReceiver.Event && Interlocked.Read(ref row.Event) == receiver
                || kind == UnityEventOperationReceiver.Calls && Volatile.Read(ref row.MutationReceivers).Any(value => value.Calls == receiver)
                || kind == UnityEventOperationReceiver.PersistentGroup && Volatile.Read(ref row.MutationReceivers).Any(value => value.Group == receiver)
                || kind == UnityEventOperationReceiver.Invokable && Volatile.Read(ref row.MutationInvokables).Contains(receiver))
            { Fault(failure); return; }
        }
        foreach (var typed in Volatile.Read(ref _typedWriterSnapshot))
        {
            if (kind == UnityEventOperationReceiver.Event && typed.Event == receiver
                || kind == UnityEventOperationReceiver.Calls && Volatile.Read(ref typed.Calls).Contains(receiver)
                || kind == UnityEventOperationReceiver.PersistentGroup && Volatile.Read(ref typed.PersistentGroups).Contains(receiver)
                || kind == UnityEventOperationReceiver.Invokable && Volatile.Read(ref typed.Invokables).Contains(receiver))
            { Fault(failure); return; }
        }
    }
    private static NativeUnityEventInvokable FreezeMutationInvokable(MutationRecord record, NativeUnityEventInvokable value)
    {
        if (value.Object != record.Frame.Self.ToInt64() || value.Class == 0
            || !Volatile.Read(ref record.Event.MutationInvokables).Contains(value.Object)
            || !record.Event.MutationInvokableClasses.TryGetValue(value.Object, out var expectedClass) || value.Class != expectedClass
            || value.Leaves is null || value.Leaves.Count > MaximumLeaves
            || value.Leaves.Any(leaf => leaf is null || leaf.Delegate == 0 || leaf.Class == 0))
            throw new InvalidOperationException("The actual delegate writer does not belong to its exact historical rooted invokable source.");
        return FreezeInvokable(value);
    }
    private static bool MutationInvokableEqual(NativeUnityEventInvokable? first, NativeUnityEventInvokable? second)
        => first is null && second is null || first is not null && second is not null
            && first.Object == second.Object && first.Class == second.Class && first.Opaque == second.Opaque
            && first.Delegate == second.Delegate && first.InvocationList == second.InvocationList
            && first.CachedBoolean == second.CachedBoolean && first.Leaves.SequenceEqual(second.Leaves);
    private static bool SameMutationFrame(NativeOperationFrame a, NativeOperationFrame b)
        => a.Version == b.Version && a.Size == b.Size && a.Kind == b.Kind && a.Reserved == b.Reserved && a.Self == b.Self
            && a.Arg0 == b.Arg0 && a.Arg1 == b.Arg1 && a.Caller == b.Caller && a.Method == b.Method && a.Sequence == b.Sequence;
}
