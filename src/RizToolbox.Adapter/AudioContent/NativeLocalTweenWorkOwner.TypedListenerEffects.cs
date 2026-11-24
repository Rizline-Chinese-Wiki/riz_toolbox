using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenTypedEffectTransferSnapshot(ulong ChildSequence, long PlayObservation,
    ulong SourceEpoch, ulong JournalCutoff, IReadOnlyList<long> Occurrences);

internal sealed partial class NativeLocalTweenWorkOwner
{
    private readonly object _typedTransferIssuer = new();
    private readonly object _typedEmptyReceiptIssuer = new();
    private readonly Dictionary<TypedEventInvocation, Dictionary<ulong, TypedListenerEffect>> _typedListenerEffects = new();
    private TypedListenerEffect? _typedListenerRead;

    private sealed class EmptySingleListenerEffectsReceipt
    {
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly TypedEventInvocation _invocation;
        private readonly LocalRunLaunchCommitState? _launch;
        private readonly ulong _sequence, _token;
        private readonly long _event;
        internal EmptySingleListenerEffectsReceipt(object issuer, NativeLocalTweenWorkOwner owner, TypedEventInvocation invocation)
        {
            if (!ReferenceEquals(issuer, owner._typedEmptyReceiptIssuer) || !owner.CanIssueEmptySingleListenerEffects(invocation))
                throw new InvalidOperationException("Only a verified drained empty Single invocation can issue this receipt.");
            (_owner, _invocation, _launch, _sequence, _token, _event) = (owner, invocation, owner._launch,
                invocation.Frame.Sequence, invocation.Executing.NativeToken, invocation.Membership.Event);
        }
        internal bool IsFor(NativeLocalTweenWorkOwner owner, TypedEventInvocation invocation)
            => ReferenceEquals(_owner, owner) && ReferenceEquals(_invocation, invocation)
                && ReferenceEquals(_launch, owner._launch) && invocation.Frame.Sequence == _sequence
                && invocation.Executing.NativeToken == _token && invocation.Membership.Event == _event
                && owner.CanIssueEmptySingleListenerEffects(invocation);
    }

    private static bool SingleExecutingListEqual(NativeSingleExecutingList first, NativeSingleExecutingList second)
        => first.Event == second.Event && first.List == second.List && first.Array == second.Array
            && first.Count == second.Count && first.Version == second.Version
            && first.Items.SequenceEqual(second.Items);

    private static bool SingleEventGraphEqual(NativeSingleEventGraph first, NativeSingleEventGraph second)
        => first.Event == second.Event && first.Calls == second.Calls && first.PersistentGroup == second.PersistentGroup
            && first.CallsDirty == second.CallsDirty && first.NeedsUpdate == second.NeedsUpdate
            && SingleExecutingListEqual(first.PersistentGroupCalls, second.PersistentGroupCalls)
            && SingleExecutingListEqual(first.PersistentCalls, second.PersistentCalls)
            && SingleExecutingListEqual(first.RuntimeCalls, second.RuntimeCalls)
            && SingleExecutingListEqual(first.ExecutingCalls, second.ExecutingCalls);

    private bool CanIssueEmptySingleListenerEffects(TypedEventInvocation invocation)
    {
        if (Unknown || !ReferenceEquals(invocation.Issuer, _typedEmptyReceiptIssuer)
            || !_typedInvocations.Contains(invocation) || _backend is not INativeTweenTypedEventGraphSource graphSource)
            return false;
        if (!_typedEffects.Any(effect => ReferenceEquals(effect.Invocation, invocation)
            && effect.Work is { } work
            && _sourceOccurrences.TryGetValue(work.Id, out var current)
            && ReferenceEquals(current, work)))
            return false;
        if (invocation.Parent.Skipped || !invocation.Parent.NativeReturned
            || !invocation.Parent.Completed || !invocation.Parent.Disposed)
            return false;
        if (!invocation.Returned || !invocation.Disposed || invocation.Abnormal || invocation.Failure is not null
            || invocation.ForeignChildInvoked)
            return false;
        var actual = invocation.Executing.Read();
        if (!actual.MidObserved || actual.Count != 0 || actual.List == 0 || invocation.Executing.NativeToken == 0
            || !actual.ParentReturned || !actual.ParentDisposed || actual.ParentAbnormal || actual.Dispatches.Count != 0)
            return false;
        if (_typedListenerEffects.TryGetValue(invocation, out var children) && children.Count != 0)
            return false;
        if (invocation.PreGraph is not { } pre || invocation.MidGraph is not { } mid
            || invocation.PostGraph is not { } post || invocation.DisposeGraph is not { } disposedGraph)
            return false;
        if (!SingleEventGraphEqual(pre, mid) || !SingleEventGraphEqual(mid, post) || !SingleEventGraphEqual(post, disposedGraph))
            return false;
        if (pre.Event != invocation.Membership.Event || pre.CallsDirty || pre.NeedsUpdate
            || pre.PersistentGroupCalls.Count != 0 || pre.PersistentCalls.Count != 0
            || pre.RuntimeCalls.Count != 0 || pre.ExecutingCalls.Count != 0 || pre.ExecutingCalls.List != actual.List)
            return false;
        if (invocation.PreWriterGeneration != invocation.MidWriterGeneration
            || invocation.MidWriterGeneration != invocation.PostWriterGeneration
            || invocation.PostWriterGeneration != invocation.DisposeWriterGeneration)
            return false;
        if (RetiredRootsUnavailable)
            return HasRetirementReceipt();
        if (graphSource.ReadTypedEventWriterGeneration(this, invocation.Membership.EventRoot) != invocation.DisposeWriterGeneration)
            return false;
        var activity = graphSource.ReadTypedEventActivity();
        var backendActivity = _backend.ReadActivity();
        return activity.Known && activity.InFlight == 0 && backendActivity.Known && backendActivity.InFlight == 0;
    }

    private sealed class TypedListenerEffect
    {
        internal readonly TypedEventInvocation Invocation;
        internal readonly NativeTweenSingleEventFrame Before;
        internal readonly NativeSingleDispatchTarget Target;
        internal readonly int OperationsBefore;
        internal readonly long OccurrenceBefore;
        internal readonly NativeTweenSource Journal;
        internal readonly NativeTweenSourceRootSet Roots;
        internal readonly ulong Epoch;
        internal NativeTweenTypedPlayConsumer? Consumer;
        internal TypedPlayEffectTransfer? Transfer;
        internal bool Completed;
        internal string Pending = "No exact direct closed TweenInfo.Play consumer was observed.";
        internal TypedListenerEffect(TypedEventInvocation invocation, NativeTweenSingleEventFrame frame,
            NativeSingleDispatchTarget target, NativeLocalTweenWorkOwner owner)
            => (Invocation, Before, Target, OperationsBefore, OccurrenceBefore, Journal, Roots, Epoch) =
                (invocation, frame, target, owner._operations.Count, owner._sourceOccurrenceId,
                    owner._sourceJournal!, owner._sourceRootSet!, owner._sourceEpoch);
    }

    // This receipt transfers historical effects into the same owner's existing occurrence
    // duties. It cannot finish an occurrence, close a writer, or authorize releasing a root.
    private sealed class TypedPlayEffectTransfer
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly TypedListenerEffect _child;
        private readonly LocalRunLaunchCommitState? _launch;
        private readonly SourceOccurrence[] _occurrences;
        private readonly OperationContext[] _operations;
        private readonly SourceOperation[] _sources;
        private readonly long _play;
        private readonly ulong _cutoff;
        internal TypedPlayEffectTransfer(object issuer, NativeLocalTweenWorkOwner owner, TypedListenerEffect child,
            OperationContext play, OperationContext[] operations, SourceOperation[] sources, SourceOccurrence[] occurrences)
        {
            if (!ReferenceEquals(issuer, owner._typedTransferIssuer) || !ReferenceEquals(owner._typedListenerRead, child)
                || child.Completed || child.Transfer is not null || occurrences.Length == 0)
                throw new InvalidOperationException("Only this exact completed Single child may transfer its retained Play effects.");
            (_issuer, _owner, _child, _launch, _play, _operations, _sources, _occurrences, _cutoff) =
                (issuer, owner, child, owner._launch, play.Observation, operations, sources, occurrences, owner._sourceReadSequence);
        }
        internal bool IsFor(NativeLocalTweenWorkOwner owner, TypedListenerEffect child, NativeTweenSingleDispatchSnapshot dispatch)
            => ReferenceEquals(_issuer, owner._typedTransferIssuer) && ReferenceEquals(owner, _owner)
                && ReferenceEquals(_launch, owner._launch) && ReferenceEquals(child, _child)
                && ReferenceEquals(child.Transfer, this) && child.Completed && !owner.Unknown && !child.Journal.Unknown
                && ReferenceEquals(owner._sourceJournal, child.Journal) && ReferenceEquals(owner._sourceRootSet, child.Roots)
                && owner._sourceEpoch == child.Epoch && owner._sourceReadSequence >= _cutoff
                && dispatch.ChildSequence == child.Before.ChildSequence && dispatch.Site == child.Before.Site
                && dispatch.Invokable == child.Target.Invokable && dispatch.Returned && !dispatch.Abnormal
                && dispatch.CreatedOccurrences.SequenceEqual(_occurrences.Select(row => row.Id))
                && _occurrences.All(row => owner._sourceOccurrences.TryGetValue(row.Id, out var actual)
                    && ReferenceEquals(actual, row) && owner._sources.Contains(row.Tween))
                && _operations.All(row => owner._operations.Contains(row) && row.Captured && row.Completed
                    && row.Disposed && row.NativeReturned && !row.Skipped && !row.ForeignMask)
                && _sources.All(row => owner._sourceOperations.TryGetValue(row.Context, out var actual)
                    && ReferenceEquals(actual, row) && SourceCommitted(row));
        internal NativeTweenTypedEffectTransferSnapshot Snapshot()
            => new(_child.Before.ChildSequence, _play, _child.Epoch, _cutoff,
                Array.AsReadOnly(_occurrences.Select(row => row.Id).ToArray()));
    }

    internal void RequireTypedListenerEffectRead(INativeTweenTypedListenerEffectBackend backend, IDisposable context,
        NativeTweenSingleEventFrame frame)
    {
        _main.VerifyAccess(); Known();
        if (!ReferenceEquals(_backend, backend) || _typedListenerRead is not { } child
            || !ReferenceEquals(context, child.Invocation) || !ReferenceEquals(_typedEventTop, child.Invocation)
            || !ReferenceEquals(_top, child.Invocation.Parent) || child.Completed
            || !NativeTweenSingleEventValidation.SameChild(child.Before, frame)
            || frame.Stage is not (NativeTweenSingleEventStage.Before or NativeTweenSingleEventStage.Returned)
            || frame.Flags != 0 || child.Invocation.Disposed || child.Invocation.Returned || child.Invocation.Abnormal
            || !ReferenceEquals(_sourceJournal, child.Journal) || !ReferenceEquals(_sourceRootSet, child.Roots)
            || _sourceEpoch != child.Epoch || child.Journal.Production != (_backend is NativeLocalTweenBackend))
            throw new InvalidOperationException("Single consumer inspection left its exact current child, owner or continuous source generation.");
    }

    private void BeginTypedListenerEffect(TypedEventInvocation invocation, NativeTweenSingleEventFrame frame,
        NativeSingleDispatchTarget target)
    {
        if (_sourceJournal is null || _sourceRootSet is null) return;
        if (!_typedListenerEffects.TryGetValue(invocation, out var children)) _typedListenerEffects.Add(invocation, children = new());
        var child = new TypedListenerEffect(invocation, frame, target, this); children.Add(frame.ChildSequence, child);
        if (_backend is not INativeTweenTypedListenerEffectBackend reader || target.Site != 1) return;
        _typedListenerRead = child;
        try { child.Consumer = reader.ReadTypedPlayConsumer(this, invocation, frame, target); }
        finally { _typedListenerRead = null; }
    }

    private static bool DescendantOf(OperationContext value, OperationContext parent)
    {
        for (var current = value.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, parent)) return true;
        return false;
    }
    private static bool SourceCommitted(SourceOperation source) => source.Committed && !source.Failed && !source.Skipped
        && source.Pairs.Count == 0 && source.PairOrder.Count == 0 && source.Move is null && source.Transient.All(row => row is null);

    private void CompleteTypedListenerEffect(TypedEventInvocation invocation, NativeTweenSingleEventFrame frame)
    {
        if (!_typedListenerEffects.TryGetValue(invocation, out var children)
            || !children.TryGetValue(frame.ChildSequence, out var child)) return;
        if (child.Completed) throw new InvalidOperationException("A Single consumer child cannot transfer its effects twice.");
        if (Unknown || frame.Stage != NativeTweenSingleEventStage.Returned || frame.Flags != 0
            || child.Consumer is null || _backend is not INativeTweenTypedListenerEffectBackend reader)
        {
            if (Unknown) child.Pending = "The current Single owner/source is Unknown; listener effects remain retained.";
            else if (frame.Stage != NativeTweenSingleEventStage.Returned || frame.Flags != 0)
                child.Pending = "The actual Single child has abnormal/missing-post/observer-fault responsibility; listener effects remain retained.";
            child.Completed = true; return;
        }
        _typedListenerRead = child;
        try
        {
            RequireTypedListenerEffectRead(reader, invocation, frame);
            var after = reader.ReadTypedPlayConsumer(this, invocation, frame, child.Target);
            PollSourceJournal();
            if (after is null || child.Consumer != after || !_sources.Contains(after.Tween))
            { child.Pending = "The Single Play delegate/target changed before its actual child return."; return; }
            var operations = _operations.Skip(child.OperationsBefore).ToArray();
            var direct = operations.Where(row => ReferenceEquals(row.Parent, invocation.Parent)).ToArray();
            if (direct.Length != 1 || direct[0] is not { Operation: NativeTweenOperation.Play } play
                || play.Frame.Self.ToInt64() != after.Tween || play.Frame.Method.ToInt64() != after.Method
                || play.Frame.Kind != NativeOperationKind.Void0 || play.Frame.Arg0 != IntPtr.Zero || play.Frame.Arg1 != IntPtr.Zero
                || operations.Any(row => !ReferenceEquals(row, play) && !DescendantOf(row, play))
                || operations.Any(row => !row.Captured || !row.Completed || !row.Disposed || !row.NativeReturned || row.Skipped || row.ForeignMask))
            { child.Pending = "The exact listener did not close one complete actual Play subtree in this same owner."; return; }
            var sources = operations.Where(row => SourceKind(row.Operation) is not null)
                .Select(row => _sourceOperations.GetValueOrDefault(row)).ToArray();
            if (sources.Any(row => row is null || !SourceCommitted(row)))
            { child.Pending = "The actual Play subtree has incomplete or Unknown source-journal responsibility."; return; }
            var occurrences = _sourceOccurrences.Values.Where(row => row.Id > child.OccurrenceBefore).ToArray();
            if (occurrences.Length == 0 || occurrences.Any(row => !_sources.Contains(row.Tween)
                || (row.Instant ? !sources.Any(source => ReferenceEquals(source!.Instant, row))
                    : !_admissions.Any(admission => admission.Id == row.Admission && admission.Confirmed
                        && operations.Any(context => context.Operation == NativeTweenOperation.Play && context.Observation == admission.Play)))))
            { child.Pending = "Some listener-created work has no exact accepted same-owner occurrence authority."; return; }
            // All source operations, including descendants and every distinct admission, stay in
            // the ordinary completion ledger. Transfer does not remove, complete or deduplicate them.
            child.Transfer = new(_typedTransferIssuer, this, child, play, operations, sources.Cast<SourceOperation>().ToArray(), occurrences);
            child.Pending = string.Empty;
        }
        finally { child.Completed = true; _typedListenerRead = null; }
    }

    private NativeTweenSingleListSnapshot ReadTypedListenerEffects(TypedEventInvocation invocation)
    {
        var actual = invocation.Executing.Read();
        var children = _typedListenerEffects.GetValueOrDefault(invocation);
        var dispatches = actual.Dispatches.Select(dispatch => children is null || !children.TryGetValue(dispatch.ChildSequence, out var child)
            ? dispatch : child.Transfer?.IsFor(this, child, dispatch) == true
                ? dispatch with { Pending = string.Empty, Transfer = child.Transfer.Snapshot() }
                : dispatch with { Pending = child.Pending.Length != 0 ? child.Pending
                    : "The private Single Play transfer no longer matches this current owner/source generation." }).ToArray();
        if (actual.Count == 0 && invocation.EmptyReceipt is null && CanIssueEmptySingleListenerEffects(invocation))
            invocation.EmptyReceipt = new EmptySingleListenerEffectsReceipt(_typedEmptyReceiptIssuer, this, invocation);
        var emptyVerified = actual.Count == 0 && invocation.EmptyReceipt?.IsFor(this, invocation) == true;
        var transferred = !Unknown && invocation.Failure is null && !invocation.ForeignChildInvoked
            && actual.MidObserved && actual.Count > 0
            && actual.ParentReturned && actual.ParentDisposed && !actual.ParentAbnormal
            && dispatches.Length == actual.Count && dispatches.All(row => row.Transfer is not null);
        var pending = emptyVerified || transferred
            ? string.Empty
            : invocation.Failure ?? (invocation.ForeignChildInvoked
                ? "A foreign Single invocation occurred inside this typed event; listener effects remain unconfirmed."
                : actual.Pending);
        return actual with { Dispatches = Array.AsReadOnly(dispatches), Pending = pending };
    }
}
