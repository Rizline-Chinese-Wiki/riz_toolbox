using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Judgements;

internal sealed record HoldCoroutineIdentity(ObservedJudgementRun Run, long Owner, long Enumerator, long Note);
// These copied values are never accepted as admission input. Only a physical
// HoldSourceCapture may publish a binding into the lifecycle ledger.
internal readonly record struct HoldNativeOwnerPair(long ManagedReceiver, long NativeOwner, IntPtr ManagedClass);
internal sealed class HoldNativeOwnerBinding
{
    internal NativeJudgementBackend.HoldSourceCapture Source { get; }
    internal HoldCoroutineIdentity Identity { get; }
    internal HoldNativeOwnerPair Pair { get; }
    internal HoldNativeOwnerBinding(NativeJudgementBackend.HoldSourceCapture source, HoldCoroutineIdentity identity)
    {
        Source = source;
        Identity = identity;
        Pair = source.NativeOwnerPair
            ?? throw new InvalidOperationException("A first Hold owner binding requires a nonzero physical native owner.");
    }
}
internal sealed record HoldCoroutineRecordSnapshot(long Sequence, HoldCoroutineIdentity Identity, long Handle,
    bool StartObserved, bool StartReturned, bool MoveObserved, long MoveCalls, long YieldedMoves,
    bool NaturalCompletion, bool CancellationRequested, long CancellationRequests, bool Retired, bool Unknown, int InFlight,
    HoldNativeOwnerPair NativeOwnerPair, bool AwaitingIndependentDeath);
internal sealed record HoldCoroutineLifecycleSnapshot(bool Accepting, bool Unknown, bool FullCoverageKnown,
    ObservedJudgementRun? Run, int Records, int InFlight, int NaturallyCompleted, int PendingCancellation,
    int RetiredRecords, int RootResponsibilities, int UnknownRootFrees,
    IReadOnlyList<HoldCoroutineRecordSnapshot> Rows, string? Reason);
internal readonly record struct HoldLifecycleDiagnosticSample(string? SessionId, string? RunId, long? SceneGeneration,
    long? StartReturnedAndExitedSequence, long? NaturalExitSequence, bool Fault, string? Reason);

/// <summary>Tracks actual scheduled Hold iterators, not all possible native iterators or engine quiescence.
/// A Start entry is published before its original can synchronously call MoveNext. Completed identities
/// and their roots remain until run retirement, so a later address cannot silently become that identity.</summary>
internal sealed partial class HoldCoroutineLifecycle : IDisposable
{
    internal const int MaximumRecords = NativeSeekRuntimeNoteView.MaximumNotes;
    internal const int MaximumInFlight = 4096;
    private const int MaximumRetiredGenerations = 65536;
    private static readonly object ResponsibilityGate = new();
    // Keep the original owner and failed IDisposable reachable even after module withdrawal.
    private static readonly HashSet<HoldCoroutineLifecycle> Responsibilities = new();
    private readonly object _gate = new();
    private readonly IMainThreadDispatcher _main;
    private readonly int _maximumRecords, _maximumInFlight;
    private readonly Dictionary<long, Record> _records = new();
    private readonly Dictionary<long, Record> _handles = new();
    private readonly HashSet<RootResponsibility> _roots = new();
    private readonly Queue<RootResponsibility> _pendingFrees = new();
    private readonly HashSet<GenerationKey> _retired = new();
    private readonly Dictionary<GenerationKey, Epoch> _epochs = new();
    private Epoch? _current;
    private bool _disposed, _unknown, _admissionClosed, _draining;
    private int _inFlight, _unknownRootFrees;
    private long _sequence;
    private string? _reason;

    private readonly record struct GenerationKey(string Session, string Run, long Scene, long Information, long Controller)
    {
        internal static GenerationKey Of(ObservedJudgementRun run)
            => new(run.SessionId, run.RunId, run.SceneGeneration, run.Information, run.Controller);
    }
    internal sealed class Epoch
    {
        internal ObservedJudgementRun Run;
        internal readonly long SequenceBase;
        internal readonly Dictionary<long, CancellationProvider> Providers = new();
        internal readonly Dictionary<long, OwnerState> RecordsByOwner = new();
        internal int InFlight, RetainedRoots;
        internal bool Retired, Unknown;
        internal Epoch(ObservedJudgementRun run, long sequenceBase) => (Run, SequenceBase) = (run, sequenceBase);
    }
    internal sealed class OwnerState
    {
        internal readonly Dictionary<long, Record> Records = new();
        internal HoldNativeOwnerPair? NativeOwnerPair;
        internal IntPtr ObservedManagedClass;
        internal bool AwaitingIndependentDeath;
        internal int InFlight, RetainedRoots;
    }
    internal sealed class Record
    {
        internal readonly long Sequence;
        internal readonly HoldCoroutineIdentity Identity;
        internal readonly Epoch Epoch;
        internal readonly OwnerState Owned;
        internal HoldNativeOwnerBinding? NativeOwnerBinding;
        internal long Handle, MoveCalls, YieldedMoves, CancellationRequests;
        internal bool StartObserved, StartReturned, MoveObserved, NaturalCompletion, CancellationRequested, Unknown, SeekStopped;
        internal int InFlight, Moving, RetainedRoots;
        internal Record(long sequence, HoldCoroutineIdentity identity, Epoch epoch, OwnerState owned)
            => (Sequence, Identity, Epoch, Owned) = (sequence, identity, epoch, owned);
    }
    internal sealed class RootResponsibility
    {
        internal readonly IDisposable Owner;
        internal Record? Record;
        internal bool ReleaseRequested, FreeAttempted;
        internal Exception? FreeFailure;
        internal RootResponsibility(IDisposable owner) => Owner = owner;
    }

    internal HoldCoroutineLifecycle(IMainThreadDispatcher main, int maximumRecords = MaximumRecords,
        int maximumInFlight = MaximumInFlight)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        if (maximumRecords is < 1 or > MaximumRecords || maximumInFlight is < 1 or > MaximumInFlight)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords), "Hold records and native invocations require bounded capacities.");
        (_main, _maximumRecords, _maximumInFlight) = (main, maximumRecords, maximumInFlight);
    }

    // Entry filtering must not construct the per-record Snapshot on every native MoveNext.
    internal bool Accepting
    {
        get { _main.VerifyAccess(); lock (_gate) return !_disposed && !_admissionClosed && !_draining; }
    }

    /// <summary>Wrong-thread rejection precedes adoption: in that case the caller still owns its capture.
    /// After VerifyAccess succeeds, this ledger owns the capture on both success and failure. A successful
    /// Start capture remains owned until run retirement; the backend may append its actual returned handle
    /// root to that same capture before CompleteStart. Repeated Move captures are invocation-local.</summary>
    internal StartScope BeginStart(HoldCoroutineIdentity identity, NativeJudgementBackend.HoldSourceCapture ownedCapture)
        => (StartScope)Begin(identity, ownedCapture, start: true);
    internal MoveScope BeginMove(HoldCoroutineIdentity identity, NativeJudgementBackend.HoldSourceCapture ownedCapture)
        => (MoveScope)Begin(identity, ownedCapture, start: false);

    private InvocationScope Begin(HoldCoroutineIdentity identity, NativeJudgementBackend.HoldSourceCapture ownedCapture, bool start)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(ownedCapture);
        lock (_gate)
        {
            var root = new RootResponsibility(ownedCapture);
            _roots.Add(root); KeepResponsibility();
            try
            {
                if (_disposed || _admissionClosed) throw new ObjectDisposedException(nameof(HoldCoroutineLifecycle));
                if (_draining) throw new InvalidOperationException("Hold capture reentered native root cleanup.");
                ValidateIdentity(identity);
                var capturedIdentity = ownedCapture.Identity;
                var observation = ownedCapture.NativeOwnerObservation;
                var observedPair = ownedCapture.NativeOwnerPair;
                if (!capturedIdentity.Run.SameGeneration(identity.Run)
                    || capturedIdentity.Run.Revision > identity.Run.Revision
                    || capturedIdentity.Owner != identity.Owner || capturedIdentity.Enumerator != identity.Enumerator
                    || capturedIdentity.Note != identity.Note || observation.ManagedReceiver != identity.Owner
                    || observation.ManagedClass == IntPtr.Zero
                    || (observation.CachedNativeOwner == 0) != (observedPair is null)
                    || observedPair is { } copied && (copied.ManagedReceiver != observation.ManagedReceiver
                        || copied.NativeOwner != observation.CachedNativeOwner || copied.ManagedClass != observation.ManagedClass))
                    throw new InvalidOperationException("Hold native owner observation is not sourced from this exact physical capture.");
                if (_current is null) ObserveRunCore(identity.Run);
                var epoch = _current!;
                if (!epoch.Run.SameGeneration(identity.Run) || identity.Run.Revision < epoch.Run.Revision)
                    throw new InvalidOperationException("Hold capture does not match the current run generation and revision.");
                if (_inFlight >= _maximumInFlight)
                    throw new InvalidOperationException("The bounded Hold invocation capacity was reached.");
                var present = _records.TryGetValue(identity.Enumerator, out var record);
                if (present)
                {
                    if (record!.Epoch.Retired || !ReferenceEquals(record.Epoch, epoch)
                        || record.Identity.Owner != identity.Owner || record.Identity.Note != identity.Note)
                        throw new InvalidOperationException("A retained Hold iterator changed its run, owner or note identity.");
                    var owned = record.Owned;
                    if (record.NativeOwnerBinding is null || owned.NativeOwnerPair is not { } bound
                        || record.NativeOwnerBinding.Pair != bound)
                        throw new InvalidOperationException("The retained Hold iterator has no original native owner binding.");
                    if (observation.ManagedReceiver != bound.ManagedReceiver
                        || observation.ManagedClass != bound.ManagedClass
                        || owned.ObservedManagedClass != bound.ManagedClass)
                        throw new InvalidOperationException("The retained Hold managed owner class changed before native owner observation.");
                    if (observedPair is null) owned.AwaitingIndependentDeath = true;
                    else if (owned.AwaitingIndependentDeath || observedPair.Value != bound)
                        throw new InvalidOperationException("A retained Hold native owner changed or revived after a stable zero observation.");
                    if (record.SeekStopped && (!start || !IsSeekRestart(record)))
                        throw new InvalidOperationException("A stopped Hold iterator has no exact controlled rescheduling ticket.");
                    if (start && record.StartObserved && !IsSeekRestart(record))
                        throw new InvalidOperationException("The same retained Hold iterator was scheduled more than once.");
                    if (record.NaturalCompletion || !start && record.Moving != 0)
                        throw new InvalidOperationException("A terminal or already moving Hold iterator was invoked again.");
                }
                else
                {
                    if (_records.Count >= _maximumRecords)
                        throw new InvalidOperationException("The bounded retained Hold identity capacity was reached.");
                    if (_sequence == long.MaxValue) throw new InvalidOperationException("Hold identity sequence exhausted.");
                    if (!epoch.RecordsByOwner.TryGetValue(identity.Owner, out var owned))
                    {
                        // Keep even a first zero observation for this exact epoch/managed owner.
                        // The owner map has its own bound because zero observations publish no records.
                        if (epoch.RecordsByOwner.Count >= _maximumRecords)
                            throw new InvalidOperationException("The bounded Hold owner observation capacity was reached.");
                        epoch.RecordsByOwner.Add(identity.Owner, owned = new OwnerState());
                        owned.ObservedManagedClass = observation.ManagedClass;
                    }
                    else if (owned.ObservedManagedClass != observation.ManagedClass)
                        throw new InvalidOperationException("A Hold managed owner class changed within its original epoch.");
                    if (observedPair is not { } firstPair)
                    {
                        owned.AwaitingIndependentDeath = true;
                        throw new InvalidOperationException("A Hold iterator with a stable zero native owner cannot establish a new source binding.");
                    }
                    if (owned.AwaitingIndependentDeath || owned.NativeOwnerPair is { } priorPair && priorPair != firstPair)
                        throw new InvalidOperationException("A Hold native owner changed or revived within its original epoch.");
                    record = new Record(++_sequence, identity, epoch, owned);
                    record.NativeOwnerBinding = new HoldNativeOwnerBinding(ownedCapture, identity);
                    owned.NativeOwnerPair ??= firstPair;
                }
                // Construct the scope before publishing counters; no native callbacks occur in this section.
                InvocationScope scope = start ? new StartScope(this, record!, root) : new MoveScope(this, record!, root);
                if (!present)
                {
                    _records.Add(identity.Enumerator, record!);
                    record!.Owned.Records.Add(identity.Enumerator, record);
                }
                epoch.Run = identity.Run;
                // The first orphan Move and the first subsequent Start can each own one lasting bundle.
                // In particular, the actual returned Coroutine root added to Start must outlive that call.
                if (!present || start) { root.Record = record; ++record!.RetainedRoots; ++record.Owned.RetainedRoots; ++epoch.RetainedRoots; }
                if (start)
                {
                    record!.StartObserved = true;
                    if (record.SeekStopped) { record.SeekStopped = false; record.StartReturned = false; record.CancellationRequested = false; }
                }
                else
                {
                    record!.MoveObserved = true; ++record.MoveCalls; ++record.Moving;
                    if (!record.StartObserved) MarkUnknown("A Hold MoveNext was observed without its scheduling Start.", record);
                }
                ++record!.InFlight; ++record.Owned.InFlight; ++epoch.InFlight; ++_inFlight;
                return scope;
            }
            catch (Exception error)
            {
                RequestRelease(root);
                MarkUnknown(error.Message);
                DrainRoots();
                throw;
            }
        }
    }

    internal void CompleteStart(StartScope scope, long handle)
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            RequireScope(scope);
            var record = scope.Entry;
            scope.Completed = true; record.StartReturned = true;
            if (handle == 0) { MarkUnknown("Hold StartCoroutine returned no handle.", record); return; }
            if (_handles.TryGetValue(handle, out var other) && !ReferenceEquals(other, record))
            { MarkUnknown("A retained Coroutine handle is already mapped to a different Hold iterator.", record); return; }
            record.Handle = handle; _handles[handle] = record;
        }
    }

    internal void CompleteMove(MoveScope scope, byte rawResult)
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            RequireScope(scope);
            scope.Completed = true;
            if (rawResult == 0) scope.Entry.NaturalCompletion = true;
            else ++scope.Entry.YieldedMoves;
        }
    }

    private void RequireScope(InvocationScope scope)
    {
        if (scope is null || !ReferenceEquals(scope.Owner, this) || scope.Disposed || scope.Completed)
        {
            MarkUnknown("A Hold completion has no matching live invocation scope.");
            throw new InvalidOperationException("A Hold completion has no matching live invocation scope.");
        }
        // Its original may have changed pause revision or retired this run. The physical result still
        // belongs to the entry identity, never to the now-current run, and is not compared to its revision.
    }

    internal void ObserveRun(ObservedJudgementRun? current)
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            if (_disposed) { DrainRoots(); return; }
            try { ObserveRunCore(current); }
            catch (Exception error) { MarkUnknown(error.Message); throw; }
            finally { DrainRoots(); }
        }
    }
    private void ObserveRunCore(ObservedJudgementRun? current)
    {
        if (current is not null) ValidateRun(current);
        if (_current is { } previous && current is not null && previous.Run.SameGeneration(current))
        {
            if (current.Revision < previous.Run.Revision)
                throw new InvalidOperationException("The observed Hold run revision moved backwards.");
            previous.Run = current; return;
        }
        if (current is not null && _retired.Contains(GenerationKey.Of(current)))
            throw new InvalidOperationException("A retired Hold run generation cannot be reactivated.");
        if (_current is { } old)
        {
            RetireEpoch(old);
            _retired.Add(GenerationKey.Of(old.Run));
            _current = null;
        }
        if (current is null) return;
        if (_retired.Count >= MaximumRetiredGenerations)
        {
            _admissionClosed = true;
            throw new InvalidOperationException("The bounded Hold run-generation history was exhausted.");
        }
        if (!_admissionClosed)
        {
            var epoch = new Epoch(current, _sequence);
            _epochs.Add(GenerationKey.Of(current), epoch); _current = epoch;
        }
    }

    /// <summary>Returns one stable, value-only provider per retained epoch/receiver. Start or orphan Move
    /// must publish its record first. Pause revisions do not replace this provider's ownership token.</summary>
    internal CancellationProvider GetCancellationProvider(ObservedJudgementRun run, long owner)
    {
        _main.VerifyAccess(); ValidateRun(run);
        lock (_gate)
        {
            if (owner == 0 || !_epochs.TryGetValue(GenerationKey.Of(run), out var epoch)
                || !epoch.RecordsByOwner.TryGetValue(owner, out var owned))
                throw new InvalidOperationException("A Hold cancellation provider requires an already published epoch and owner.");
            if (epoch.Providers.TryGetValue(owner, out var existing)) return existing;
            if (owned.RetainedRoots == 0 && owned.InFlight == 0)
                throw new InvalidOperationException("The Hold cancellation owner has no retained observation responsibility.");
            var provider = new CancellationProvider(this, epoch, owned, owner);
            epoch.Providers.Add(owner, provider); return provider;
        }
    }

    internal sealed class CancellationProvider
    {
        private readonly HoldCoroutineLifecycle _owner;
        private readonly Epoch _epoch;
        private readonly OwnerState _owned;
        internal long Receiver { get; }
        internal NativeCoroutineCancellationIdentityProvider ScalarProvider { get; }
        // These properties inspect only managed scalars, including on a transport fault thread.
        internal ObservedJudgementRun Run { get { lock (_owner._gate) return _epoch.Run; } }
        internal bool Retired { get { lock (_owner._gate) return _epoch.Retired || _owner._disposed; } }
        internal bool HasResponsibility
        { get { lock (_owner._gate) return _owned.RetainedRoots != 0 || _owned.InFlight != 0; } }
        internal HoldNativeOwnerPair NativeOwnerPair
        {
            get { lock (_owner._gate) return _owned.NativeOwnerPair
                ?? throw new InvalidOperationException("The Hold cancellation owner has no source-bound native owner."); }
        }
        internal bool AwaitingIndependentDeath
        { get { lock (_owner._gate) return _owned.AwaitingIndependentDeath; } }
        internal IReadOnlyList<HoldNativeOwnerBinding> NativeOwnerBindings
        {
            get
            {
                lock (_owner._gate)
                    return Array.AsReadOnly(_owned.Records.Values.OrderBy(record => record.Sequence)
                        .Select(record => record.NativeOwnerBinding
                            ?? throw new InvalidOperationException("A retained Hold record lost its source binding."))
                        .ToArray());
            }
        }
        // A late old Stop must not mark whichever epoch is now current. Transport fault callbacks
        // may call this on a worker; no dispatcher, provider reader, native call or root cleanup runs.
        internal void Fault(string reason)
        {
            lock (_owner._gate)
            {
                _epoch.Unknown = true; _owner._unknown = true;
                _owner._reason = string.IsNullOrWhiteSpace(reason) ? "Hold cancellation observation is unknown."
                    : reason.Length > 768 ? reason[..768] : reason;
            }
        }
        internal CancellationProvider(HoldCoroutineLifecycle owner, Epoch epoch, OwnerState owned, long receiver)
        {
            (_owner, _epoch, _owned, Receiver) = (owner, epoch, owned, receiver);
            ScalarProvider = new(this, receiver, Read, IsCurrent);
        }
        private NativeCoroutineCancellationIdentity Identity(Record record)
        {
            var retiring = _epoch.Retired || _owner._disposed;
            return new(this, Receiver, checked((int)(record.Sequence - _epoch.SequenceBase - 1)), record.Sequence,
                record.Identity.Enumerator, record.Handle, record.StartReturned && record.Handle != 0,
                record.StartReturned, record.StartObserved || record.MoveObserved, retiring, record.NaturalCompletion,
                !retiring && !_owner._admissionClosed && !_epoch.Unknown && !record.Unknown);
        }
        private IReadOnlyList<NativeCoroutineCancellationIdentity> Read()
        {
            _owner._main.VerifyAccess();
            lock (_owner._gate)
                return Array.AsReadOnly(_owned.Records.Values.Select(Identity).ToArray());
        }
        private bool IsCurrent(NativeCoroutineCancellationIdentity identity)
        {
            _owner._main.VerifyAccess();
            lock (_owner._gate) return IsCurrentCore(identity);
        }
        private bool IsCurrentCore(NativeCoroutineCancellationIdentity identity)
            => ReferenceEquals(identity.Owner, this) && identity.Receiver == Receiver
                && _owned.Records.TryGetValue(identity.Enumerator, out var record)
                && ReferenceEquals(record.Epoch, _epoch) && record.Identity.Owner == Receiver
                && Identity(record).SameRecord(identity);

        /// <summary>Pin this exact epoch/owner before Tick, metadata or the original Stop. Retired owners
        /// can still own a physical parent. A root already being freed cannot acquire a new lease.</summary>
        internal IDisposable AcquireObservation()
        {
            _owner._main.VerifyAccess();
            lock (_owner._gate)
            {
                if (_owner._draining || _owner._inFlight >= _owner._maximumInFlight
                    || _owned.RetainedRoots == 0 && _owned.InFlight == 0)
                {
                    _epoch.Unknown = true; _owner._unknown = true;
                    _owner._reason = "Hold Stop observation cannot acquire its exact retained epoch responsibility.";
                    throw new InvalidOperationException(_owner._reason);
                }
                var scope = new ObservationScope(_owner, _epoch, _owned);
                ++_owned.InFlight; ++_epoch.InFlight; ++_owner._inFlight;
                _owner.KeepResponsibility(); return scope;
            }
        }
        internal void ObserveCancellation(NativeCoroutineCancellationIdentity exactMatched)
        {
            _owner._main.VerifyAccess();
            lock (_owner._gate)
            {
                if (!IsCurrentCore(exactMatched))
                {
                    _epoch.Unknown = true; _owner._unknown = true;
                    _owner._reason = "Hold cancellation does not match its exact current provider, slot, generation and iterator.";
                    throw new InvalidOperationException(_owner._reason);
                }
                RequestCancellation(_owned.Records[exactMatched.Enumerator]);
            }
        }
    }

    private sealed class ObservationScope : IDisposable
    {
        private readonly HoldCoroutineLifecycle _owner;
        private readonly Epoch _epoch;
        private readonly OwnerState _owned;
        private bool _disposed;
        internal ObservationScope(HoldCoroutineLifecycle owner, Epoch epoch, OwnerState owned)
            => (_owner, _epoch, _owned) = (owner, epoch, owned);
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            lock (_owner._gate)
            {
                if (_disposed) return;
                _disposed = true;
                --_owned.InFlight; --_epoch.InFlight; --_owner._inFlight;
                // This is a physical request lease, never a natural iterator completion or engine receipt.
                _owner.DrainRoots();
            }
        }
    }

    /// <summary>Only a request, including a normally returned Stop wrapper. Both zero identities mean
    /// all tracked iterators for this exact owner. Neither cancellation nor retirement synthesizes false.</summary>
    internal void ObserveCancellation(long owner, long enumerator = 0, long handle = 0)
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            if (owner == 0) throw new ArgumentException("An exact retained coroutine owner is required.", nameof(owner));
            if (enumerator != 0 || handle != 0)
            {
                Record? record = null;
                if (enumerator != 0) _records.TryGetValue(enumerator, out record);
                if (handle != 0)
                {
                    _handles.TryGetValue(handle, out var byHandle);
                    if (enumerator != 0 && !ReferenceEquals(record, byHandle))
                    { MarkUnknown("Hold cancellation iterator and handle identities disagree."); return; }
                    record = byHandle;
                }
                if (record is null || record.Identity.Owner != owner)
                { MarkUnknown("Hold cancellation has no matching retained scheduling identity."); return; }
                RequestCancellation(record); return;
            }
            foreach (var record in _records.Values)
                if (record.Identity.Owner == owner) RequestCancellation(record);
        }
    }
    private static void RequestCancellation(Record record)
    { record.CancellationRequested = true; ++record.CancellationRequests; }

    // Fault callbacks may run on a worker: only managed identities and flags are touched here.
    internal void Fault(string reason) { lock (_gate) MarkUnknown(reason); }
    private void MarkUnknown(string reason, Record? record = null)
    {
        _unknown = true; _reason = string.IsNullOrWhiteSpace(reason) ? "Hold lifecycle observation is unknown." : reason;
        if (record is not null) record.Unknown = true;
        else if (_current is not null) _current.Unknown = true;
    }

    private void ReleaseScope(InvocationScope scope)
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            if (scope.Disposed) return;
            scope.Disposed = true;
            if (!scope.Completed) MarkUnknown("A Hold original or its observation did not complete normally.", scope.Entry);
            if (scope is MoveScope) --scope.Entry.Moving;
            --scope.Entry.InFlight; --scope.Entry.Owned.InFlight; --scope.Entry.Epoch.InFlight; --_inFlight;
            if (scope.Retention.Record is null) RequestRelease(scope.Retention);
            DrainRoots();
        }
    }

    private void KeepResponsibility() { lock (ResponsibilityGate) Responsibilities.Add(this); }
    private void RequestRelease(RootResponsibility root)
    {
        if (root.ReleaseRequested || root.FreeAttempted) return;
        root.ReleaseRequested = true; _pendingFrees.Enqueue(root);
    }
    private void RetireEpoch(Epoch epoch)
    {
        epoch.Retired = true;
        foreach (var root in _roots)
            if (ReferenceEquals(root.Record?.Epoch, epoch)) RequestRelease(root);
        // A zero-only owner has no published record or cancellation provider.
        // The retired generation key now prevents reactivation, so its tombstone
        // need not accumulate across the bounded history of old epochs.
        foreach (var owner in epoch.RecordsByOwner.Where(item => item.Value.NativeOwnerPair is null
            && item.Value.Records.Count == 0).Select(item => item.Key).ToArray())
            epoch.RecordsByOwner.Remove(owner);
    }
    private void DrainRoots()
    {
        if (_inFlight != 0 || _draining) return;
        _draining = true;
        try
        {
            // A normal Move releases only its own temporary capture, in constant time. Run retirement
            // queues lasting roots once. Bound this pass so reentry cannot feed an unbounded cleanup loop.
            var remaining = _pendingFrees.Count;
            while (remaining-- > 0)
            {
                var root = _pendingFrees.Dequeue();
                if (root.FreeAttempted) continue;
                root.FreeAttempted = true; // Publication precedes native cleanup/reentry; never retry an uncertain free.
                try
                {
                    root.Owner.Dispose(); _roots.Remove(root);
                    if (root.Record is { } record)
                    {
                        --record.Epoch.RetainedRoots;
                        --record.Owned.RetainedRoots;
                        if (--record.RetainedRoots == 0 && record.Epoch.Retired)
                        {
                            if (record.Handle != 0 && _handles.TryGetValue(record.Handle, out var mapped) && ReferenceEquals(record, mapped))
                                _handles.Remove(record.Handle);
                            _records.Remove(record.Identity.Enumerator);
                            record.Owned.Records.Remove(record.Identity.Enumerator);
                        }
                    }
                }
                catch (Exception error)
                {
                    root.FreeFailure = error; ++_unknownRootFrees; _admissionClosed = true;
                    MarkUnknown("Hold root cleanup is unknown; its original owner and exception remain retained.", root.Record);
                }
            }
        }
        finally
        {
            _draining = false;
            if (_roots.Count == 0 && _inFlight == 0) lock (ResponsibilityGate) Responsibilities.Remove(this);
        }
    }

    internal HoldLifecycleDiagnosticSample ReadDiagnosticSample((string? Session, string? Run, long? Scene)? reportedRun)
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            var run = _current?.Run;
            long? start = null, naturalExit = null;
            if (_current is not null && !_unknown && reportedRun != (run?.SessionId, run?.RunId, run?.SceneGeneration))
            {
                foreach (var record in _records.Values)
                {
                    // A nonzero retained handle and a drained invocation prove
                    // this exact Start capture and the matching original exit.
                    if (!ReferenceEquals(record.Epoch, _current) || record.Unknown || record.Epoch.Unknown
                        || !record.StartObserved || !record.StartReturned || record.Handle == 0 || record.InFlight != 0)
                        continue;
                    if (record.NaturalCompletion) { start = naturalExit = record.Sequence; break; }
                    start ??= record.Sequence;
                }
            }
            return new(run?.SessionId, run?.RunId, run?.SceneGeneration, start, naturalExit, _unknown, _reason);
        }
    }

    internal HoldCoroutineLifecycleSnapshot Snapshot()
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            var rows = _records.Values.OrderBy(record => record.Sequence).Select(record => new HoldCoroutineRecordSnapshot(
                record.Sequence, record.Identity, record.Handle, record.StartObserved, record.StartReturned, record.MoveObserved,
                record.MoveCalls, record.YieldedMoves, record.NaturalCompletion, record.CancellationRequested,
                record.CancellationRequests, record.Epoch.Retired, record.Unknown || record.Epoch.Unknown, record.InFlight,
                record.NativeOwnerBinding?.Pair ?? throw new InvalidOperationException("A retained Hold record lost its source binding."),
                record.Owned.AwaitingIndependentDeath)).ToArray();
            return new(!_disposed && !_admissionClosed && !_draining, _unknown, false, _current?.Run, rows.Length, _inFlight,
                rows.Count(row => row.NaturalCompletion), rows.Count(row => row.CancellationRequested && !row.NaturalCompletion),
                rows.Count(row => row.Retired), _roots.Count, _unknownRootFrees, Array.AsReadOnly(rows), _reason);
        }
    }
    private static void ValidateIdentity(HoldCoroutineIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity); ValidateRun(identity.Run);
        if (identity.Owner == 0 || identity.Enumerator == 0 || identity.Note == 0)
            throw new ArgumentException("Exact non-null Hold owner, iterator and note identities are required.", nameof(identity));
    }
    private static void ValidateRun(ObservedJudgementRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!ValidId(run.SessionId) || !ValidId(run.RunId) || run.Information == 0 || run.Controller == 0
            || run.SceneGeneration < 0 || run.Revision < 0)
            throw new ArgumentException("Hold lifecycle requires a real observed run identity.", nameof(run));
    }
    private static bool ValidId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);

    public void Dispose()
    {
        _main.VerifyAccess();
        lock (_gate)
        {
            if (_disposed) { DrainRoots(); return; }
            _disposed = true;
            if (_current is not null) { RetireEpoch(_current); _current = null; }
            DrainRoots();
        }
    }

    internal abstract class InvocationScope : IDisposable
    {
        internal readonly HoldCoroutineLifecycle Owner;
        internal readonly Record Entry;
        internal readonly RootResponsibility Retention;
        internal HoldCoroutineIdentity Identity => Entry.Identity;
        internal bool Completed { get; set; }
        internal bool Disposed { get; set; }
        internal InvocationScope(HoldCoroutineLifecycle owner, Record entry, RootResponsibility retention)
            => (Owner, Entry, Retention) = (owner, entry, retention);
        public void Dispose() => Owner.ReleaseScope(this);
    }
    internal sealed class StartScope : InvocationScope
    {
        internal StartScope(HoldCoroutineLifecycle owner, Record entry, RootResponsibility retention) : base(owner, entry, retention) { }
    }
    internal sealed class MoveScope : InvocationScope
    {
        internal MoveScope(HoldCoroutineLifecycle owner, Record entry, RootResponsibility retention) : base(owner, entry, retention) { }
    }
}
