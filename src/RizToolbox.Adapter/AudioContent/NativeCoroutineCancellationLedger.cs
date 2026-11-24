using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeCoroutineCancellationOperation
{
    StopEnumerator, StopHandle, StopAll, StopManagedHandle, StopManagedEnumerator, StopNamed
}

/// <summary>The table is copied at construction. Both callbacks must inspect only
/// managed scalar tracker state; neither may acquire roots, read objects or call native code.</summary>
internal sealed record NativeCoroutineCancellationIdentityProvider(object Owner, long Receiver,
    Func<IReadOnlyList<NativeCoroutineCancellationIdentity>> Read,
    Func<NativeCoroutineCancellationIdentity, bool> IsCurrent);

internal sealed record NativeCoroutineCancellationRequestSnapshot(long RequestId, long ParentRequestId, long RootRequestId,
    NativeCoroutineCancellationOperation Operation, ulong NativeSequence, long Receiver, long Arg0, long Arg1,
    long Method, long Caller, bool Requested, bool Captured, bool NativeCompleted, bool NativeReturned, bool Abnormal,
    bool Disposed, bool MatchUnproven, bool IdentityChanged, uint Flags,
    IReadOnlyList<NativeCoroutineCancellationIdentity> Before, IReadOnlyList<NativeCoroutineCancellationIdentity> Matched,
    IReadOnlyList<NativeCoroutineCancellationIdentity> After);

internal sealed record NativeCoroutineCancellationSnapshot(int Providers, long ObservedRequests,
    long NativeReturnedRequests, long AbnormalRequests, long Outstanding, long UntrackedRequests,
    bool HasObservedRequests, bool CanConfirmRequestIdle, bool Pending, bool EngineCancelled, bool Unknown,
    string? Failure, IReadOnlyList<NativeCoroutineCancellationRequestSnapshot> Requests,
    IReadOnlyList<NativeCoroutineCancellationDetachCandidate>? DetachCandidates = null,
    long PhysicalCalls = 0, long ForeignMasks = 0, long UntrackedEngineAssociations = 0,
    IReadOnlyList<NativeCoroutineCancellationCompletionSnapshot>? Completions = null);

internal sealed record NativeCoroutineCancellationCompletionSnapshot(long CandidateId, long RequestId, long RootRequestId,
    ulong DetachSequence, ulong StartSequence, long Node, long NodeGeneration);

/// <summary>A value-only candidate captured before one actual Detach original.
/// It proves neither Detach return nor engine/scheduler/async cancellation.</summary>
internal sealed record NativeCoroutineCancellationDetachCandidate(long CandidateId, long RequestId, long ParentRequestId,
    long RootRequestId, NativeCoroutineCancellationOperation Operation, ulong RequestSequence, ulong DetachSequence,
    long EngineIdentityId, ulong StartSequence, long Node, long NodeGeneration, long NativeOwner, ulong EnumeratorHandle,
    NativeCoroutineCancellationIdentity Tracker, bool NamedMatchUnproven);

/// <summary>Request-only scalar responsibility. No Stop call, object/native read,
/// engine cancellation, natural-terminal predicate or release permission lives here.
/// The coordinator must retain this ledger and its providers while Pending/Unknown.
/// Parent/root IDs correlate physical nesting, never a number of logical cancels.</summary>
internal sealed class NativeCoroutineCancellationLedger
{
    internal const int MaximumProviders = 32, MaximumRequests = 512, MaximumIdentities = 256,
        MaximumPhysicalCalls = 2048, MaximumDetachCandidates = 2048;
    internal const int MaximumSupportedIdentities = 65536;
    private readonly IMainThreadDispatcher _main;
    private readonly int _maximumIdentities;
    private readonly NativeCoroutineCancellationIdentityProvider[] _providers;
    private readonly List<RequestContext> _requests = new();
    private readonly List<RequestContext> _physical = new();
    private readonly List<NativeCoroutineCancellationDetachCandidate> _detachCandidates = new();
    private readonly HashSet<NativeEngineCoroutineIdentityLedger.Call> _detachCalls = new();
    private readonly List<CompletionCandidate> _completionCandidates = new();
    private NativeCoroutineCancellationCompletionReceipt? _completion;
    private RequestContext? _current;
    private bool _reading;
    private int _unknown;
    private long _observed, _nativeReturned, _abnormal, _untracked, _foreignMasks, _untrackedEngineAssociations;
    private string? _failure;

    internal NativeCoroutineCancellationLedger(IMainThreadDispatcher main,
        IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers, int maximumIdentities = MaximumIdentities)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(providers);
        if (providers.Count is 0 or > MaximumProviders) throw new ArgumentOutOfRangeException(nameof(providers));
        if (maximumIdentities is < 1 or > MaximumSupportedIdentities) throw new ArgumentOutOfRangeException(nameof(maximumIdentities));
        _maximumIdentities = maximumIdentities;
        _main = main; _providers = providers.ToArray();
        for (var index = 0; index < _providers.Length; ++index)
        {
            var provider = _providers[index];
            if (provider is null || provider.Owner is null || provider.Receiver == 0 || provider.Read is null || provider.IsCurrent is null
                || _providers.Take(index).Any(prior => ReferenceEquals(prior.Owner, provider.Owner)))
                throw new ArgumentException("An immutable, distinct owner and actual receiver scalar provider is required.", nameof(providers));
        }
    }

    internal bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal sealed class RequestContext : IDisposable
    {
        private readonly NativeCoroutineCancellationLedger _owner;
        internal readonly long RequestId;
        internal readonly RequestContext? Parent;
        internal readonly long RootRequestId;
        internal readonly int ThreadId = Environment.CurrentManagedThreadId;
        internal readonly NativeCoroutineCancellationOperation Operation;
        internal readonly NativeOperationFrame Frame;
        internal readonly bool ForeignMask;
        internal long ParentRequestId => NearestOwned(Parent)?.RequestId ?? 0;
        internal bool Captured, NativeCompleted, NativeReturned, Abnormal, Disposed, MatchUnproven, IdentityChanged;
        internal uint Flags;
        internal NativeCoroutineCancellationIdentity[] Before = Array.Empty<NativeCoroutineCancellationIdentity>();
        internal NativeCoroutineCancellationIdentity[] Matched = Array.Empty<NativeCoroutineCancellationIdentity>();
        internal NativeCoroutineCancellationIdentity[] After = Array.Empty<NativeCoroutineCancellationIdentity>();
        internal RequestContext(NativeCoroutineCancellationLedger owner, long id,
            NativeCoroutineCancellationOperation operation, NativeOperationFrame frame, bool foreignMask = false)
        {
            (_owner, RequestId, Parent, Operation, Frame) = (owner, id, owner._current, operation, frame);
            ForeignMask = foreignMask;
            RootRequestId = Parent?.RootRequestId ?? id;
            MatchUnproven = true;
        }
        public void Dispose() => _owner.Release(this);
        internal bool IsOwnedBy(NativeCoroutineCancellationLedger owner) => ReferenceEquals(_owner, owner);
        internal NativeCoroutineCancellationRequestSnapshot Snapshot() => new(RequestId, ParentRequestId, RootRequestId,
            Operation, Frame.Sequence, Frame.Self.ToInt64(), Frame.Arg0.ToInt64(), Frame.Arg1.ToInt64(),
            Frame.Method.ToInt64(), Frame.Caller.ToInt64(), true, Captured, NativeCompleted, NativeReturned, Abnormal,
            Disposed, MatchUnproven, IdentityChanged, Flags, Copy(Before), Copy(Matched), Copy(After));
    }

    internal RequestContext? Capture(NativeCoroutineCancellationOperation operation, NativeOperationFrame frame,
        bool receiverCurrent = true)
    {
        _main.VerifyAccess();
        // An old epoch's physical parent must mask a new epoch even when Unity reuses its receiver.
        var owned = receiverCurrent && OwnsReceiver(frame.Self.ToInt64());
        if (!owned && NearestOwned(_current) is null) return null;
        _completion = null; // A new physical request cannot borrow an older terminal receipt.
        // Register scalar responsibility before validating arguments or consulting
        // even the read-only providers. Null Stop arguments can still throw natively.
        if (owned) _observed = checked(_observed + 1);
        if (_physical.Count == MaximumPhysicalCalls || owned && _requests.Count == MaximumRequests)
        { _untracked = checked(_untracked + 1); Fault("Cancellation request record capacity exhausted; responsibility remains pending."); return null; }
        // A foreign child masks its owned ancestor without reading providers or
        // claiming an owned request ID. Its physical completion remains exact.
        var context = new RequestContext(this, owned ? _observed : 0, operation, frame, !owned);
        _physical.Add(context); _current = context;
        if (owned) _requests.Add(context);
        else ++_foreignMasks;
        var reading = false;
        try
        {
            if (!frame.IsValid || !ValidOperationFrame(operation, frame))
                throw new InvalidOperationException("Cancellation request has no exact typed physical frame.");
            if (context.ForeignMask) return context;
            if (_reading) throw new InvalidOperationException("Cancellation request reentered its scalar identity reader.");
            if (Unknown) return context;
            _reading = true; reading = true;
            context.Before = ReadCurrent(frame.Self.ToInt64());
            RequireKnown();
            context.Matched = operation switch {
                NativeCoroutineCancellationOperation.StopAll => context.Before.ToArray(),
                NativeCoroutineCancellationOperation.StopNamed => Array.Empty<NativeCoroutineCancellationIdentity>(),
                _ => context.Before.Where(identity => identity.MatchesReference(ReferenceKind(operation),
                    frame.Self.ToInt64(), frame.Arg0.ToInt64())).ToArray()
            };
            if (operation is not (NativeCoroutineCancellationOperation.StopAll or NativeCoroutineCancellationOperation.StopNamed)
                && context.Matched.Length > 1)
                throw new InvalidOperationException("Cancellation argument matches multiple current owner records.");
            context.MatchUnproven = operation == NativeCoroutineCancellationOperation.StopNamed
                || operation != NativeCoroutineCancellationOperation.StopAll && context.Matched.Length == 0;
            context.Captured = true;
        }
        catch (Exception error) { Fault(error.Message); }
        finally { if (reading) _reading = false; }
        return context;
    }

    internal void Complete(RequestContext context, NativeOperationFrame frame, NativeOperationExitFlags flags)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(context);
        if (!context.IsOwnedBy(this) || context.NativeCompleted || context.Disposed
            || context.ThreadId != Environment.CurrentManagedThreadId || !ReferenceEquals(_current, context)
            || !SameFrame(context.Frame, frame))
        { Fault("Cancellation completion crossed its exact owner, thread, frame or physical parent."); return; }
        // Completion is the native request boundary, not an engine acknowledgement.
        // Preserve this fact and its cleanup even if observation is already unknown.
        context.NativeCompleted = true; context.Flags = (uint)flags;
        context.NativeReturned = (flags & NativeOperationExitFlags.Returned) != 0 && (flags & NativeOperationExitFlags.Abnormal) == 0;
        context.Abnormal = (flags & NativeOperationExitFlags.Abnormal) != 0;
        if (!context.ForeignMask && context.NativeReturned) _nativeReturned = checked(_nativeReturned + 1);
        if (!context.ForeignMask && context.Abnormal) _abnormal = checked(_abnormal + 1);
        if (flags != NativeOperationExitFlags.Returned)
        { Fault("Cancellation request exited abnormally or with unconfirmed observer flags."); return; }
        if (context.ForeignMask || Unknown || !context.Captured) return;
        var reading = false;
        try
        {
            if (_reading) throw new InvalidOperationException("Cancellation completion reentered its scalar reader.");
            _reading = true; reading = true; context.After = ReadCurrent(frame.Self.ToInt64()); RequireKnown();
            // A new Start, retirement or reused scalar address during Stop may
            // change the affected population. Never lend it an old snapshot.
            if (!SamePopulation(context.Before, context.After))
            { context.IdentityChanged = true; Fault("Coroutine identities changed during the cancellation request."); }
        }
        catch (Exception error) { Fault(error.Message); }
        finally { if (reading) _reading = false; }
    }

    internal void ObserveFault(NativeCoroutineCancellationOperation operation, NativeOperationFrame frame, string reason)
    {
        // Safe on arbitrary native threads: immutable managed table and scalars only.
        // No provider callback, tracker enumeration, native/object read or cleanup.
        _ = operation;
        if (OwnsReceiver(frame.Self.ToInt64()) || _main.IsMainThread && NearestOwned(_current) is not null) Fault(reason);
    }
    internal NativeCoroutineCancellationSnapshot Read()
    {
        _main.VerifyAccess();
        var outstanding = _untracked + _untrackedEngineAssociations + _physical.LongCount(context => !context.Disposed);
        var observed = _observed != 0; var unknown = Unknown;
        var completed = !unknown && outstanding == 0 && _current is null && _completion?.IsCurrentFor(this) == true;
        return new(_providers.Length, _observed, _nativeReturned, _abnormal, outstanding, _untracked, observed,
            !unknown && outstanding == 0 && _current is null, unknown || observed && !completed, completed, unknown,
            Volatile.Read(ref _failure), Array.AsReadOnly(_requests.Select(context => context.Snapshot()).ToArray()),
            Array.AsReadOnly(_detachCandidates.ToArray()), _physical.Count, _foreignMasks, _untrackedEngineAssociations,
            completed ? _completion!.ReadCompletedCandidates() : Array.Empty<NativeCoroutineCancellationCompletionSnapshot>());
    }
    private void Release(RequestContext context)
    {
        _main.VerifyAccess();
        if (!context.IsOwnedBy(this) || context.Disposed || !context.NativeCompleted
            || context.ThreadId != Environment.CurrentManagedThreadId || !ReferenceEquals(_current, context))
        { Fault("Cancellation cleanup has no exact completed physical request."); return; }
        context.Disposed = true; _current = context.Parent;
    }
    private static RequestContext? NearestOwned(RequestContext? context)
    { for (; context is not null; context = context.Parent) if (!context.ForeignMask) return context; return null; }

    /// <summary>Associates an actual open Detach scope with the innermost Stop.
    /// Only copied managed scalars are consumed. Every failure is sticky; the
    /// caller still owns and must Complete/Dispose the already captured scope.</summary>
    internal bool ObserveEngineDetach(NativeEngineCoroutineIdentityLedger.Call call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (!_main.IsMainThread)
        {
            if (call.Relevant) Fault("Owned engine Detach association arrived outside its main thread.");
            return false;
        }
        if (!call.Relevant || _current is null || Unknown) return false;
        var request = _current;
        try
        {
            if (request.ForeignMask)
                throw new InvalidOperationException("A foreign physical Stop masks its owned ancestor; owned Detach cannot borrow that request.");
            if (!request.Captured || request.NativeCompleted || request.Disposed || request.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Engine Detach has no current captured open Stop request.");
            if (_reading) throw new InvalidOperationException("Engine Detach association reentered the cancellation scalar reader.");
            if (!call.Owner.IsCurrentDetachCall(call))
                throw new InvalidOperationException("Engine Detach is not the actual open engine scope.");
            // Managed Start's wrapper publishes its handle after native Start
            // returned. A Stop in the same frame must observe that monotonic
            // upgrade without waiting for the next frame pump. Refresh keeps
            // the original slot/generation/Enumerator and validates the handle;
            // it cannot admit a replacement tracker or a caller-created Call.
            _reading = true;
            try { call.Owner.RefreshTrackedIdentities(); }
            finally { _reading = false; }
            RequireKnown();
            if (!ReferenceEquals(_current, request) || request.NativeCompleted || request.Disposed)
                throw new InvalidOperationException("The Stop scope changed during its engine identity upgrade.");
            var tuple = ReadEngineDetachTuple(call);
            if (_detachCalls.Count == MaximumDetachCandidates)
            {
                ++_untrackedEngineAssociations;
                throw new InvalidOperationException("Engine Detach association capacity exhausted; responsibility remains untracked.");
            }
            if (!_detachCalls.Add(call))
                throw new InvalidOperationException("One physical Detach cannot be associated more than once.");
            if (tuple.Tracker.Receiver != request.Frame.Self.ToInt64())
                throw new InvalidOperationException("Engine Detach belongs to another Stop receiver.");
            var providers = _providers.Where(provider => ReferenceEquals(provider.Owner, tuple.ProviderOwner)
                && provider.Receiver == tuple.ProviderReceiver).ToArray();
            if (providers.Length != 1)
                throw new InvalidOperationException("Engine Detach does not belong to this immutable cancellation provider table.");
            var frozen = request.Before.Where(row => SameIdentity(row, tuple.Tracker)).ToArray();
            if (frozen.Length != 1)
                throw new InvalidOperationException("Engine Detach has no exact frozen Stop identity and tracker generation.");
            var named = request.Operation == NativeCoroutineCancellationOperation.StopNamed;
            if (!named && (request.MatchUnproven || !request.Matched.Any(row => SameIdentity(row, frozen[0]))))
                throw new InvalidOperationException("Engine Detach differs from the Stop argument's exact matched identity.");
            _reading = true;
            try
            {
                var current = ReadCurrent(tuple.Tracker.Receiver);
                RequireKnown();
                if (current.Count(row => SameIdentity(row, frozen[0])) != 1
                    || !ReferenceEquals(_current, request) || request.NativeCompleted || request.Disposed
                    || !SameEngineDetachTuple(tuple, ReadEngineDetachTuple(call)))
                    throw new InvalidOperationException("Engine Detach or its current tracker tuple changed during scalar verification.");
                _detachCandidates.Add(new(_detachCandidates.Count + 1L, request.RequestId, request.ParentRequestId,
                    request.RootRequestId, request.Operation, request.Frame.Sequence, call.Frame.Sequence,
                    tuple.EngineIdentityId, tuple.StartSequence, tuple.Node, tuple.NodeGeneration, tuple.NativeOwner,
                    tuple.EnumeratorHandle, frozen[0], named));
                _completionCandidates.Add(new(this, request, call, tuple.Identity, tuple.NodeRecord, _detachCandidates[^1]));
                return true;
            }
            finally { _reading = false; }
        }
        catch (Exception error) { Fault(error.Message); return false; }
    }
    // The copied DetachCandidate above remains diagnostic. Only these retained
    // actual call/request/identity references can participate in completion.
    internal sealed class CompletionCandidate
    {
        private readonly NativeCoroutineCancellationLedger _owner;
        private readonly RequestContext _request;
        private readonly NativeEngineCoroutineIdentityLedger.Call _call;
        private readonly NativeEngineCoroutineIdentityLedger.IdentityRecord _identity;
        private readonly NativeEngineCoroutineNodeLedger.NodeRecord _node;
        internal readonly NativeCoroutineCancellationDetachCandidate Identity;
        internal CompletionCandidate(NativeCoroutineCancellationLedger owner, RequestContext request,
            NativeEngineCoroutineIdentityLedger.Call call, NativeEngineCoroutineIdentityLedger.IdentityRecord identity,
            NativeEngineCoroutineNodeLedger.NodeRecord node, NativeCoroutineCancellationDetachCandidate snapshot)
            => (_owner, _request, _call, _identity, _node, Identity) = (owner, request, call, identity, node, snapshot);
        internal bool IsForIdentityLedger(NativeEngineCoroutineIdentityLedger identities)
            => ReferenceEquals(_call.Owner, identities) && _call.NodeCall is { } nodeCall
                && identities.Wraps(nodeCall.Owner);
        internal bool TryReadTerminal(NativeCoroutineCancellationLedger owner, out NativeEngineCoroutineIdentitySnapshot? selected)
        {
            owner._main.VerifyAccess(); selected = null;
            if (!ReferenceEquals(owner, _owner) || !owner._completionCandidates.Contains(this) || owner.Unknown
                || !_request.Captured || !_request.NativeCompleted || !_request.NativeReturned || _request.Abnormal || !_request.Disposed
                || _request.IdentityChanged || _call.Owner.Unknown || !_call.Completed || !_call.Disposed
                || _call.Frame.Kind != NativeEngineNodeKind.Detach || _call.Frame.Sequence != Identity.DetachSequence
                || !ReferenceEquals(_call.Identity, _identity) || !ReferenceEquals(_call.NodeCall?.Node, _node)
                || _call.NodeCall is not { Completed: true, Disposed: true } physical
                || physical.Completion.Flags != NativeEngineNodeExitFlags.Returned
                || !_node.DetachObserved || !_node.DetachReturned || !_node.FinalCleanup || _node.InlineCleanupUnproven
                || _node.Executions != 0 || _node.OpenCalls != 0 || !_node.Start.Completed || !_node.Start.Disposed
                || !ReferenceEquals(_identity.Node, _node) || !ReferenceEquals(_identity.Start.NodeCall, _node.Start)
                || !_identity.NativeStartReturned || !_identity.Start.Disposed
                || _identity.Id != Identity.EngineIdentityId || _identity.StartSequence != Identity.StartSequence
                || _node.Pointer != Identity.Node || _node.Generation != Identity.NodeGeneration
                || _node.NativeOwner != Identity.NativeOwner || _node.Handle != Identity.EnumeratorHandle
                || _identity.StartSequence != _node.Start.Frame.Sequence || _identity.Tracker.Enumerator != Identity.Tracker.Enumerator
                || !ReferenceEquals(_identity.Tracker.Owner, Identity.Tracker.Owner)
                || _identity.Tracker.Receiver != Identity.Tracker.Receiver || _identity.Tracker.Slot != Identity.Tracker.Slot
                || _identity.Tracker.Generation != Identity.Tracker.Generation) return false;
            selected = _identity.Read(); return true;
        }
        internal bool Covers(RequestContext request, NativeCoroutineCancellationIdentity tracker)
        {
            if (!SameIdentity(Identity.Tracker, tracker)) return false;
            for (var ancestor = _request; ancestor is not null; ancestor = ancestor.Parent)
                if (ReferenceEquals(ancestor, request)) return true;
            return false;
        }
        internal NativeCoroutineCancellationCompletionSnapshot Read() => new(Identity.CandidateId, Identity.RequestId,
            Identity.RootRequestId, Identity.DetachSequence, Identity.StartSequence, Identity.Node, Identity.NodeGeneration);
    }
    internal IReadOnlyList<CompletionCandidate> ReadCompletionCandidates()
    { _main.VerifyAccess(); return Array.AsReadOnly(_completionCandidates.ToArray()); }
    internal bool HasCompleteRequestCoverage(IReadOnlyList<CompletionCandidate> candidates)
    {
        _main.VerifyAccess();
        if (Unknown || _observed == 0 || _untracked != 0 || _untrackedEngineAssociations != 0
            || _current is not null || _physical.Any(call => !call.NativeCompleted || !call.Disposed)
            || candidates.Count != _completionCandidates.Count || candidates.Any(candidate => !_completionCandidates.Contains(candidate))) return false;
        foreach (var request in _requests)
        {
            // Named matching is still an observed subset, never a complete
            // target population proof. Empty/no-match requests stay Pending.
            if (!request.Captured || !request.NativeCompleted || !request.NativeReturned || request.Abnormal || !request.Disposed
                || request.MatchUnproven || request.IdentityChanged || request.Operation == NativeCoroutineCancellationOperation.StopNamed) return false;
            if (request.Matched.Length == 0 || request.Matched.Any(tracker => !candidates.Any(candidate => candidate.Covers(request, tracker)))) return false;
        }
        return true;
    }
    internal void AcceptCompletion(NativeCoroutineCancellationCompletionReceipt receipt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.IsCurrentFor(this)) throw new InvalidOperationException("Cancellation completion was not issued by the actual bound engine coordinator.");
        _completion = receipt;
    }
    private readonly record struct EngineDetachTuple(long EngineIdentityId, ulong StartSequence, long Node,
        long NodeGeneration, long NativeOwner, ulong EnumeratorHandle, NativeCoroutineCancellationIdentity Tracker,
        object ProviderOwner, long ProviderReceiver, NativeEngineCoroutineIdentityLedger.IdentityRecord Identity,
        NativeEngineCoroutineNodeLedger.NodeRecord NodeRecord);
    private static bool SameEngineDetachTuple(EngineDetachTuple first, EngineDetachTuple second)
        => first.EngineIdentityId == second.EngineIdentityId && first.StartSequence == second.StartSequence
            && first.Node == second.Node && first.NodeGeneration == second.NodeGeneration && first.NativeOwner == second.NativeOwner
            && first.EnumeratorHandle == second.EnumeratorHandle && SameIdentity(first.Tracker, second.Tracker)
            && ReferenceEquals(first.ProviderOwner, second.ProviderOwner) && first.ProviderReceiver == second.ProviderReceiver
            && ReferenceEquals(first.Identity, second.Identity) && ReferenceEquals(first.NodeRecord, second.NodeRecord);
    private static EngineDetachTuple ReadEngineDetachTuple(NativeEngineCoroutineIdentityLedger.Call call)
    {
        if (!call.Owner.IsCurrentDetachCall(call))
            throw new InvalidOperationException("Engine Detach is not the actual ledger's current open physical call.");
        var frame = call.Frame;
        var identity = call.Identity ?? throw new InvalidOperationException("Engine Detach has no actual tracker binding.");
        var provider = identity.Provider ?? throw new InvalidOperationException("A yielded engine child has no independent managed Stop identity.");
        var nodeCall = call.NodeCall ?? throw new InvalidOperationException("Engine Detach has no actual node call.");
        var node = nodeCall.Node ?? throw new InvalidOperationException("Engine Detach has no actual node generation.");
        if (frame.Kind != NativeEngineNodeKind.Detach || !frame.IsValid
            || frame.SnapshotFlags != (NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid)
            || node.Generation <= 0 || node.Pointer == 0 || node.NativeOwner == 0 || node.FinalCleanup || node.OpenCalls <= 0
            || frame.Arg0.ToInt64() != node.Pointer || frame.Node.ToInt64() != node.Pointer
            || frame.Owner.ToInt64() != node.NativeOwner && !(frame.Owner == IntPtr.Zero && node.DetachObserved)
            || frame.EnumeratorType != 2 || frame.EnumeratorCache.ToInt64() != node.Enumerator || frame.EnumeratorHandle != node.Handle
            || !ReferenceEquals(identity.Node, node) || identity.Id <= 0 || identity.NativeOwner != node.NativeOwner
            || !ReferenceEquals(node.Start, identity.Start.NodeCall) || identity.Start.Frame.Kind != NativeEngineNodeKind.Start
            || identity.Start.Frame.Arg0.ToInt64() != node.NativeOwner || identity.Start.Frame.Arg1.ToInt64() != node.Enumerator
            || identity.StartSequence == 0 || identity.StartSequence != identity.Start.Frame.Sequence
            || identity.StartSequence != node.Start.Frame.Sequence || !nodeCall.Frame.Equals(frame)
            || !identity.Tracker.Known || !identity.Tracker.Published || identity.Tracker.Retiring
            || identity.Tracker.Enumerator != node.Enumerator || identity.Tracker.Slot < 0 || identity.Tracker.Generation < 0
            || !ReferenceEquals(identity.Tracker.Owner, provider.Owner) || identity.Tracker.Receiver != provider.Receiver)
            throw new InvalidOperationException("Engine Detach node, Start, owner, Enumerator or tracker tuple is inconsistent.");
        return new(identity.Id, identity.StartSequence, node.Pointer, node.Generation, node.NativeOwner, node.Handle,
            identity.Tracker, provider.Owner, provider.Receiver, identity, node);
    }
    private NativeCoroutineCancellationIdentity[] ReadCurrent(long receiver)
    {
        var current = new List<NativeCoroutineCancellationIdentity>();
        var slots = new Dictionary<object, HashSet<int>>(ReferenceEqualityComparer.Instance);
        foreach (var provider in _providers)
        {
            if (provider.Receiver != receiver) continue;
            RequireKnown(); var supplied = provider.Read(); RequireKnown();
            if (supplied is null || supplied.Count > _maximumIdentities - current.Count)
                throw new InvalidOperationException("Cancellation identity snapshot is absent or exceeds its bounded capacity.");
            var copy = supplied.ToArray();
            foreach (var identity in copy)
            {
                RequireKnown();
                if (!ReferenceEquals(identity.Owner, provider.Owner) || identity.Receiver != receiver || identity.Slot < 0
                    || identity.Generation < 0)
                    throw new InvalidOperationException("Cancellation provider returned a foreign or malformed owner identity.");
                // Closed initialization records remain readable diagnostics with
                // Known=false. They are no longer a current population member.
                // A retirement during Stop still changes Before/After and faults.
                if (!identity.Published || identity.Retiring) continue;
                if (identity.Enumerator == 0)
                    throw new InvalidOperationException("Published cancellation identity has no actual enumerator.");
                if (!identity.Known)
                    throw new InvalidOperationException("Cancellation provider has an unknown coroutine identity.");
                if (!provider.IsCurrent(identity))
                    throw new InvalidOperationException("Cancellation snapshot lost its current owner generation.");
                RequireKnown();
                if (!slots.TryGetValue(identity.Owner, out var ownedSlots))
                    slots.Add(identity.Owner, ownedSlots = new HashSet<int>());
                if (!ownedSlots.Add(identity.Slot))
                    throw new InvalidOperationException("Cancellation provider duplicated an owned slot.");
                current.Add(identity);
            }
        }
        return current.ToArray();
    }
    private bool OwnsReceiver(long receiver) => receiver != 0 && _providers.Any(provider => provider.Receiver == receiver);
    private static IReadOnlyList<NativeCoroutineCancellationIdentity> Copy(NativeCoroutineCancellationIdentity[] identities)
        => Array.AsReadOnly(identities.ToArray());
    private static bool SamePopulation(NativeCoroutineCancellationIdentity[] before, NativeCoroutineCancellationIdentity[] after)
    {
        if (before.Length != after.Length) return false;
        var owners = new Dictionary<object, Dictionary<int, NativeCoroutineCancellationIdentity>>(ReferenceEqualityComparer.Instance);
        foreach (var current in after)
        {
            if (!owners.TryGetValue(current.Owner, out var slots))
                owners.Add(current.Owner, slots = new Dictionary<int, NativeCoroutineCancellationIdentity>());
            if (!slots.TryAdd(current.Slot, current)) return false;
        }
        foreach (var previous in before)
            if (!owners.TryGetValue(previous.Owner, out var slots) || !slots.TryGetValue(previous.Slot, out var current)
                || !SameIdentity(previous, current)) return false;
        return true;
    }
    private static bool SameIdentity(NativeCoroutineCancellationIdentity a, NativeCoroutineCancellationIdentity b)
        => ReferenceEquals(a.Owner, b.Owner) && a.Receiver == b.Receiver && a.Slot == b.Slot && a.Generation == b.Generation
            && a.Enumerator == b.Enumerator && a.ObservedHandle == b.ObservedHandle && a.HandleConfirmed == b.HandleConfirmed
            && a.StartReturned == b.StartReturned && a.Published == b.Published && a.Retiring == b.Retiring
            && a.NaturalTerminal == b.NaturalTerminal && a.Known == b.Known;
    private static bool ValidOperationFrame(NativeCoroutineCancellationOperation operation, NativeOperationFrame frame)
        => operation switch {
            NativeCoroutineCancellationOperation.StopAll => frame.Kind == NativeOperationKind.Void0,
            NativeCoroutineCancellationOperation.StopEnumerator or NativeCoroutineCancellationOperation.StopHandle
                or NativeCoroutineCancellationOperation.StopManagedHandle or NativeCoroutineCancellationOperation.StopManagedEnumerator
                or NativeCoroutineCancellationOperation.StopNamed => frame.Kind == NativeOperationKind.Reference1,
            _ => false };
    private static NativeCoroutineCancellationReferenceKind ReferenceKind(NativeCoroutineCancellationOperation operation)
        => operation is NativeCoroutineCancellationOperation.StopEnumerator or NativeCoroutineCancellationOperation.StopManagedEnumerator
            ? NativeCoroutineCancellationReferenceKind.Enumerator : NativeCoroutineCancellationReferenceKind.CoroutineHandle;
    private static bool SameFrame(NativeOperationFrame a, NativeOperationFrame b)
        => a.Version == b.Version && a.Size == b.Size && a.Kind == b.Kind && a.Reserved == b.Reserved && a.Self == b.Self
            && a.Arg0 == b.Arg0 && a.Arg1 == b.Arg1 && a.Method == b.Method && a.Caller == b.Caller && a.Sequence == b.Sequence;
    private void RequireKnown()
    { if (Unknown) throw new InvalidOperationException("Cancellation request observation is unknown."); }
    private void Fault(string reason)
    {
        reason ??= "Cancellation request observation failed.";
        Volatile.Write(ref _failure, reason.Length > 768 ? reason[..768] : reason);
        Interlocked.Exchange(ref _unknown, 1);
    }
}
