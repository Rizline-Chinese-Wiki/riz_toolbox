using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenCompletionSnapshot(ulong SourceEpoch, ulong JournalCutoff, int QueueWork,
    int InstantWork, int ConcreteTargets, IReadOnlyList<string> Pending, bool Unknown);
internal interface INativeTweenCompletionWithdrawalBackend
{
    void VerifyTweenCompletionBindingsWithdrawn(NativeLocalTweenWorkOwner owner);
}

internal sealed partial class NativeLocalTweenWorkOwner
{
    private readonly object _completionIssuer = new();
    private CompletionInterval? _completionCurrent;
    private NativeTweenCompletionSnapshot? _completionSnapshot;
    private FrozenCompletion? _preparedCompletion;

    internal sealed class FrozenCompletion
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly CompletionInterval _original;
        internal NativeTweenSource.Withdrawal Withdrawal { get; }
        internal bool NativeCommitted { get; private set; }
        internal FrozenCompletion(object issuer, NativeLocalTweenWorkOwner owner, CompletionInterval original, NativeTweenSource.Withdrawal withdrawal)
        {
            if (!ReferenceEquals(issuer, owner._completionIssuer) || !ReferenceEquals(owner._completionCurrent, original)
                || owner._preparedCompletion is not null)
                throw new InvalidOperationException("Only the exact live completion loan may freeze its one withdrawn generation.");
            (_issuer, _owner, _original, Withdrawal) = (issuer, owner, original, withdrawal);
        }
        internal void VerifyIdentity(NativeLocalTweenWorkOwner owner)
        {
            owner._main.VerifyAccess();
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(_issuer, owner._completionIssuer)
                || !ReferenceEquals(owner._preparedCompletion, this) || owner._completionCurrent is not { } loan
                || !loan.IsFrozenLoanFor(this, _original))
                throw new InvalidOperationException("The prepared completion belongs to another owner, frozen generation or synchronous reborrow.");
            loan.VerifyFrozenOwnership(owner, owner._launch, owner._coordinator, Withdrawal);
        }
        internal void ObserveCommit(NativeTweenRetirementContext.Cut cut)
        {
            VerifyIdentity(_owner); cut.VerifySourceWithdrawal(Withdrawal);
            if (!cut.Committed || !ReferenceEquals(cut.Owner, _owner))
                throw new InvalidOperationException("Frozen work facts require the actual same-owner committed native cut.");
            NativeCommitted = true;
        }
    }

    /// <summary>Borrowed synchronously by the actual Return transaction. It
    /// proves work facts only; it neither unregisters native slots nor frees a
    /// holder. Validation is impossible after the borrowing callback exits.</summary>
    internal sealed class CompletionInterval
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly long[] _sources;
        private readonly LocalRunLaunchCommitState? _launch;
        private readonly NativeLocalRunLaunchCoordinator? _coordinator;
        private readonly long _operations;
        private readonly int _occurrences, _typed, _children, _managers;
        private bool _prepared;
        private NativeTweenSource.Withdrawal? _withdrawal;
        private FrozenCompletion? _frozen;
        internal ulong SourceEpoch { get; }
        internal ulong JournalCutoff { get; private set; }
        internal IReadOnlyList<long> Sources => Array.AsReadOnly(_sources.ToArray());
        internal bool Production => _owner._backend is NativeLocalTweenBackend;
        internal CompletionInterval(object issuer, NativeLocalTweenWorkOwner owner)
        {
            if (!ReferenceEquals(issuer, owner._completionIssuer) || owner._completionCurrent is not null)
                throw new InvalidOperationException("Foreign or overlapping work completion interval.");
            (_issuer, _owner, _sources, _launch, _coordinator) = (issuer, owner, owner._sources, owner._launch, owner._coordinator);
            (SourceEpoch, JournalCutoff, _operations, _occurrences, _typed, _children, _managers) =
                (owner._sourceEpoch, owner._sourceReadSequence, owner._operationRevision, owner._sourceOccurrences.Count,
                    owner._typedInvocations.Count, owner._inlineSourceEvents.Count, owner._sourceManagers.Count);
        }
        internal void ValidateCurrent(NativeLocalTweenWorkOwner owner, LocalRunLaunchCommitState? launch,
            NativeLocalRunLaunchCoordinator? coordinator)
        {
            owner._main.VerifyAccess(); owner.Known();
            if (!ReferenceEquals(_issuer, owner._completionIssuer) || !ReferenceEquals(owner, _owner)
                || !ReferenceEquals(owner._completionCurrent, this) || !ReferenceEquals(_launch, launch)
                || !ReferenceEquals(_coordinator, coordinator) || !ReferenceEquals(_sources, owner._sources)
                || SourceEpoch != owner._sourceEpoch || JournalCutoff != owner._sourceReadSequence
                || _operations != owner._operationRevision || _occurrences != owner._sourceOccurrences.Count
                || _typed != owner._typedInvocations.Count || _children != owner._inlineSourceEvents.Count || _managers != owner._sourceManagers.Count
                || owner._top is not null || owner._reading || owner._typedEventTop is not null)
                throw new InvalidOperationException("Completion facts are stale, copied, outside their callback or crossed an owner/generation.");
            owner.RequireCompletionReturn();
            var beforeActivity = owner._backend.ReadActivity(); owner.Known();
            if (!beforeActivity.Known || beforeActivity.InFlight != 0)
                throw new InvalidOperationException("A physical borrower entered this completion interval before full validation.");
            owner.PollSourceJournal();
            if (JournalCutoff != owner._sourceReadSequence || owner.CollectCompletionPending().Count != 0)
                throw new InvalidOperationException("New source activity or incomplete work invalidated the borrowed completion interval.");
            owner.PollSourceJournal(); var activity = owner._backend.ReadActivity(); owner.Known();
            if (JournalCutoff != owner._sourceReadSequence || !activity.Known || activity.InFlight != 0
                || _operations != owner._operationRevision || _typed != owner._typedInvocations.Count || owner._top is not null || owner._typedEventTop is not null)
                throw new InvalidOperationException("Actual source or native work changed across full completion-facts verification.");
        }
        internal void RequireCutoff(ulong epoch, ulong sequence)
        {
            ValidateCurrent(_owner, _launch, _coordinator);
            if (epoch != SourceEpoch || sequence != JournalCutoff)
                throw new InvalidOperationException("The actual native cut does not match this work interval's continuous source cutoff.");
        }
        internal void PrepareWithdrawal()
        {
            if (_prepared || _withdrawal is not null) throw new InvalidOperationException("Completion withdrawal was already prepared.");
            ValidateCurrent(_owner, _launch, _coordinator);
            _prepared = true;
        }
        internal FrozenCompletion PrepareWithdrawal(NativeTweenSource.Withdrawal withdrawal)
        {
            ArgumentNullException.ThrowIfNull(withdrawal);
            if (!_prepared || _withdrawal is not null) throw new InvalidOperationException("Prepare the full live work interval before closing source enrollment.");
            // C closes enrollment and fixes its cutoff after the observation
            // sinks close. Other owners may have advanced the global journal
            // in that gap. Drain this first loan only, bounded by C's private
            // cutoff, and revalidate all owned work before freezing the facts.
            // A later busy retry uses FrozenCompletion and never polls C.
            VerifyPreparedWithdrawal(withdrawal, JournalCutoff);
            if (JournalCutoff != withdrawal.JournalCutoff) _owner.PollSourceJournal();
            _owner.RequireCompletionReturn();
            if (_owner.CollectCompletionPending().Count != 0)
                throw new InvalidOperationException("Owned work changed while its source withdrawal cutoff was being frozen.");
            VerifyPreparedWithdrawal(withdrawal, withdrawal.JournalCutoff);
            JournalCutoff = withdrawal.JournalCutoff;
            _withdrawal = withdrawal;
            VerifyFrozenOwnership(_owner, _launch, _coordinator, withdrawal);
            _owner.RequireWithdrawnCompletionBindings();
            _frozen = new FrozenCompletion(_issuer, _owner, this, withdrawal);
            _owner._preparedCompletion = _frozen;
            if (_owner._completionSnapshot is { } snapshot) _owner._completionSnapshot = snapshot with { JournalCutoff = JournalCutoff };
            return _frozen;
        }
        private void VerifyPreparedWithdrawal(NativeTweenSource.Withdrawal withdrawal, ulong readSequence)
        {
            _owner._main.VerifyAccess(); _owner.Known(); withdrawal.VerifyCurrent();
            if (!ReferenceEquals(_issuer, _owner._completionIssuer) || !ReferenceEquals(_owner._completionCurrent, this)
                || !_prepared || _withdrawal is not null || _frozen is not null || _owner._preparedCompletion is not null
                || !ReferenceEquals(_launch, _owner._launch) || !ReferenceEquals(_coordinator, _owner._coordinator)
                || !ReferenceEquals(_sources, _owner._sources) || !ReferenceEquals(withdrawal.Owner, _owner)
                || !ReferenceEquals(withdrawal.Reader, _owner._sourceJournal) || !ReferenceEquals(withdrawal.Roots, _owner._sourceRootSet)
                || !ReferenceEquals(withdrawal.Roots.Withdrawal, withdrawal) || withdrawal.Roots.EnrollmentOpen || withdrawal.Unknown
                || withdrawal.SourceEpoch != SourceEpoch || SourceEpoch != _owner._sourceEpoch
                || withdrawal.JournalCutoff < JournalCutoff || readSequence != _owner._sourceReadSequence
                || withdrawal.Roots.LastReadSequence != readSequence || _operations != _owner._operationRevision
                || _occurrences != _owner._sourceOccurrences.Count || _typed != _owner._typedInvocations.Count
                || _children != _owner._inlineSourceEvents.Count || _managers != _owner._sourceManagers.Count
                || _owner._top is not null || _owner._reading || _owner._typedEventTop is not null)
                throw new InvalidOperationException("Prepared completion changed its exact owner, work generation or frozen source withdrawal.");
        }
        private bool SameFrozenGeneration(CompletionInterval original)
            => ReferenceEquals(_sources, original._sources) && ReferenceEquals(_launch, original._launch)
                && ReferenceEquals(_coordinator, original._coordinator) && SourceEpoch == original.SourceEpoch && JournalCutoff == original.JournalCutoff
                && _operations == original._operations && _occurrences == original._occurrences && _typed == original._typed
                && _children == original._children && _managers == original._managers;
        internal bool IsFrozenLoanFor(FrozenCompletion frozen, CompletionInterval original)
            => ReferenceEquals(_frozen, frozen) && SameFrozenGeneration(original);
        internal static CompletionInterval BorrowFrozen(object issuer, NativeLocalTweenWorkOwner owner, FrozenCompletion frozen)
        {
            if (!ReferenceEquals(issuer, owner._completionIssuer) || !ReferenceEquals(owner._preparedCompletion, frozen))
                throw new InvalidOperationException("Foreign prepared completion reborrow issuer.");
            return new CompletionInterval(issuer, owner) { _prepared = true, _withdrawal = frozen.Withdrawal, _frozen = frozen };
        }
        internal void ObserveNativeCommitted(NativeTweenRetirementContext.Cut cut)
            => (_frozen ?? throw new InvalidOperationException("No actual source withdrawal was prepared for this interval.")).ObserveCommit(cut);
        internal void VerifyFrozenOwnership(NativeLocalTweenWorkOwner owner, LocalRunLaunchCommitState? launch,
            NativeLocalRunLaunchCoordinator? coordinator, NativeTweenSource.Withdrawal withdrawal)
        {
            owner._main.VerifyAccess();
            if (!ReferenceEquals(_issuer, owner._completionIssuer) || !ReferenceEquals(owner, _owner) || !_prepared
                || !ReferenceEquals(owner._completionCurrent, this) || !ReferenceEquals(_launch, launch)
                || !ReferenceEquals(_coordinator, coordinator) || !ReferenceEquals(_sources, owner._sources)
                || !ReferenceEquals(_withdrawal, withdrawal) || !ReferenceEquals(withdrawal.Owner, owner)
                || !ReferenceEquals(withdrawal.Reader, owner._sourceJournal) || !ReferenceEquals(withdrawal.Roots, owner._sourceRootSet)
                || !ReferenceEquals(withdrawal.Roots.Withdrawal, withdrawal) || withdrawal.Roots.EnrollmentOpen || withdrawal.Unknown
                || withdrawal.SourceEpoch != SourceEpoch || withdrawal.JournalCutoff != JournalCutoff
                || SourceEpoch != owner._sourceEpoch || JournalCutoff != owner._sourceReadSequence
                || _operations != owner._operationRevision || _occurrences != owner._sourceOccurrences.Count
                || _typed != owner._typedInvocations.Count || _children != owner._inlineSourceEvents.Count || _managers != owner._sourceManagers.Count
                || owner._top is not null || owner._reading || owner._typedEventTop is not null || Volatile.Read(ref owner._fault) != 0)
                throw new InvalidOperationException("Frozen completion lost its synchronous private owner/generation/source-withdrawal identity.");
        }
    }

    private void RequireWithdrawnCompletionBindings()
    {
        if (_backend is INativeTweenCompletionWithdrawalBackend withdrawn) withdrawn.VerifyTweenCompletionBindingsWithdrawn(this);
        else if (_backend is NativeLocalTweenBackend)
            throw new InvalidOperationException("Production withdrawn completion lacks its actual closed-binding responsibility witness.");
    }
    internal bool TryBorrowWithdrawnCompletion(FrozenCompletion frozen, Action<CompletionInterval> consume)
    {
        ArgumentNullException.ThrowIfNull(frozen); ArgumentNullException.ThrowIfNull(consume); _main.VerifyAccess();
        if (_completionCurrent is not null || !ReferenceEquals(_preparedCompletion, frozen))
            throw new InvalidOperationException("Withdrawn completion cannot reenter or borrow another owner's prepared work facts.");
        var interval = CompletionInterval.BorrowFrozen(_completionIssuer, this, frozen);
        _completionCurrent = interval;
        try
        {
            frozen.VerifyIdentity(this); RequireWithdrawnCompletionBindings();
            if (!frozen.NativeCommitted)
            {
                Known(); RequireCompletionReturn();
                var activity = _backend.ReadActivity(); Known();
                if (!activity.Known) throw new InvalidOperationException("Withdrawn native transport became Unknown before retry.");
                if (activity.InFlight != 0 || _top is not null || _typedEventTop is not null) return false;
                var actual = Freeze(_backend.Refresh()); Known();
                if (_current is null || _sources.Any(source => Locations(actual, source).Any()
                    || _current.Tweens.SingleOrDefault(row => row.Object == source) != actual.Tweens.SingleOrDefault(row => row.Object == source)))
                    throw new InvalidOperationException("An owned scheduling/effect graph changed after observation enrollment closed.");
                foreach (var source in _sources)
                {
                    if (!_tweenDeaths.TryGetValue(source, out var death) || ReadTweenDeathReceipt(death.Source.Root) is null)
                        throw new InvalidOperationException("Withdrawn retry lost its retained concrete source death ticket.");
                    var observed = ((INativeLocalTweenDeathBackend)_backend).ReadTweenDeathObservation(death.Source.Root);
                    if (observed.Alive || observed.CachedNative != 0 || observed.ManagedReceiver != source)
                        throw new InvalidOperationException("A concrete source revived after enrollment withdrawal.");
                    if (!_targetDeathSets.TryGetValue(source, out var targets))
                        throw new InvalidOperationException("Withdrawn retry lost its concrete target coverage.");
                    ValidateTargetSet(targets, requireDead: targets.Binding.Component is not null);
                }
                if (_operations.Any(context => context.Skipped
                    && (_typedInvocations.Any(inv => ReferenceEquals(inv.Parent, context))
                        || _operations.Any(op => ReferenceEquals(op.Parent, context)))))
                    throw new InvalidOperationException("Withdrawn Tween completion has a skipped parent with child invocations.");
                if (_typedInvocations.Count != _typedEffects.Count)
                    throw new InvalidOperationException("Withdrawn Single typed invocation/effect count mismatch.");
                if (_typedEvents.Count != 0 || _typedInvocations.Count != 0 || _typedEffects.Count != 0)
                {
                    if (_backend is not INativeTweenTypedEventGraphSource graphSource)
                        throw new InvalidOperationException("Withdrawn Single typed events lack a typed event graph source.");
                    var typedActivity = graphSource.ReadTypedEventActivity(); Known();
                    if (!typedActivity.Known || typedActivity.InFlight != 0)
                        throw new InvalidOperationException("Withdrawn Single typed event transport became active or Unknown before retry.");
                    foreach (var invocation in _typedInvocations)
                    {
                        if (invocation.Parent.Skipped || !invocation.Parent.NativeReturned
                            || !invocation.Parent.Completed || !invocation.Parent.Disposed
                            || !invocation.Returned || !invocation.Disposed || invocation.Abnormal
                            || invocation.Failure is not null || invocation.ForeignChildInvoked)
                            throw new InvalidOperationException("Withdrawn Single typed invocation lacks its completed normal parent or event outcome.");
                        var executing = invocation.Executing.Read();
                        if (!executing.MidObserved || executing.Count is not { } count || count < 0
                            || !executing.ParentReturned || !executing.ParentDisposed || executing.ParentAbnormal)
                            throw new InvalidOperationException("Withdrawn Single typed invocation lacks its verified executing-list snapshot.");
                        if (invocation.DisposeGraph is not { } disposedGraph)
                            throw new InvalidOperationException("Withdrawn Single typed invocation lacks its verified dispose event graph.");
                        invocation.Membership.RequireFor(this, (INativeTweenTypedEventSource)_backend!);
                        var liveGraph = graphSource.ReadTypedEventGraph(this, invocation.Membership.EventRoot); Known();
                        if (!SingleEventGraphEqual(liveGraph, disposedGraph)
                            || graphSource.ReadTypedEventWriterGeneration(this, invocation.Membership.EventRoot) != invocation.DisposeWriterGeneration)
                            throw new InvalidOperationException("An owned Single event graph or writer generation changed after observation withdrawal.");
                        if (invocation.EmptyReceipt is not null)
                        {
                            if (count != 0 || executing.Dispatches.Count != 0
                                || !invocation.EmptyReceipt.IsFor(this, invocation))
                                throw new InvalidOperationException("Withdrawn Single typed invocation lost its verified empty listener-effects receipt.");
                        }
                        else
                        {
                            // Never re-issue EmptyReceipt on a withdrawn retry (preserves 1.16 null-empty-receipt);
                            // an invocation with EmptyReceipt == null may pass ONLY via verified non-empty listener effect transfers.
                            if (count == 0 || executing.Dispatches.Count != count
                                || !_typedListenerEffects.TryGetValue(invocation, out var children)
                                || children.Count != count
                                || !executing.Dispatches.All(dispatch => children.TryGetValue(dispatch.ChildSequence, out var child)
                                    && child.Completed && child.Pending.Length == 0
                                    && child.Transfer is { } transfer && transfer.IsFor(this, child, dispatch))
                                || ReadTypedListenerEffects(invocation).Pending.Length != 0)
                                throw new InvalidOperationException("Withdrawn Single typed invocation lacks verified empty or transferred listener effects.");
                        }
                    }
                    foreach (var typed in _typedEffects)
                    {
                        if (TypedEffectPending(typed) is { Length: > 0 } reason)
                            throw new InvalidOperationException("Withdrawn Single typed effect remains pending: " + reason);
                    }
                    typedActivity = graphSource.ReadTypedEventActivity(); Known();
                    if (!typedActivity.Known || typedActivity.InFlight != 0)
                        throw new InvalidOperationException("Withdrawn Single typed event transport became active or Unknown during retry.");
                }
                activity = _backend.ReadActivity(); Known();
                if (!activity.Known || activity.InFlight != 0) return false;
                frozen.VerifyIdentity(this);
            }
            consume(interval); return true;
        }
        catch (Exception error) { Fault("Withdrawn Tween completion: " + error.Message); throw; }
        finally { _completionCurrent = null; }
    }

    private void RequireCompletionReturn()
    {
        VerifyTweenSourceOwner();
        if (!_sealRequested || !_admissionSealed || _tweenDeathReturn is not { } request)
            throw new InvalidOperationException("Work completion requires its actual classified Return and sealed complete source set.");
        request.Require();
        if (_backend is NativeLocalTweenBackend)
        {
            if (_launch is null || _coordinator is null || _sourceJournal?.Production != true)
                throw new InvalidOperationException("Production work completion lost its exact launch/coordinator/source publication.");
            _coordinator.RequireTweenAdmissionSealRequest(this, _launch);
        }
    }
    internal bool TryBorrowCompletionInterval(LocalRunLaunchCommitState launch, NativeLocalRunLaunchCoordinator coordinator,
        Action<CompletionInterval> consume)
    {
        ArgumentNullException.ThrowIfNull(consume); VerifyTweenSourceOwner();
        if (_backend is not NativeLocalTweenBackend || !ReferenceEquals(_launch, launch) || !ReferenceEquals(_coordinator, coordinator))
            throw new InvalidOperationException("Only this actual production Return transaction borrows its completion facts.");
        return BorrowCompletionCore(consume);
    }
    internal bool TryBorrowControlledCompletionIntervalForTests(Action<CompletionInterval> consume)
    {
        ArgumentNullException.ThrowIfNull(consume); VerifyTweenSourceOwner();
        if (_backend is NativeLocalTweenBackend || _launch is not null || _coordinator is not null || _tweenDeathReturn is null)
            throw new InvalidOperationException("Controlled work completion cannot authorize a production transaction.");
        // Actual controlled leases still have to return Sealed. No caller bool
        // can substitute for their private authority or classified Return token.
        if (_tweenDeaths.Count != _sources.Length || _tweenDeaths.Values.Any(row => row.Source.ReadSealedEpoch() == 0)) return false;
        _sealRequested = _admissionSealed = true;
        return BorrowCompletionCore(consume);
    }
    private bool BorrowCompletionCore(Action<CompletionInterval> consume)
    {
        RequireCompletionReturn();
        if (_completionCurrent is not null) throw new InvalidOperationException("Work completion borrowing reentered.");
        if (_top is not null || _reading || _typedEventTop is not null) return false;
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known) throw new InvalidOperationException("Completion observation transport is Unknown.");
        if (activity.InFlight != 0) return false;
        PollSourceJournal(); var pending = CollectCompletionPending();
        _completionSnapshot = new(_sourceEpoch, _sourceReadSequence, _sourceOccurrences.Values.Count(row => !row.Instant && _sources.Contains(row.Tween)),
            _sourceOccurrences.Values.Count(row => row.Instant && _sources.Contains(row.Tween)), _targetDeathSets.Count, pending, Unknown);
        if (pending.Count != 0) return false;
        var interval = new CompletionInterval(_completionIssuer, this); _completionCurrent = interval;
        try { interval.ValidateCurrent(this, _launch, _coordinator); consume(interval); return true; }
        finally { _completionCurrent = null; }
    }

    private List<string> CollectCompletionPending()
    {
        var pending = new List<string>();
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known) throw new InvalidOperationException("Completion work transport is Unknown.");
        if (activity.InFlight != 0 || _top is not null || _typedEventTop is not null || _reading)
        { pending.Add("A physical Tween/Single operation remains active."); return pending; }
        if (_sourceJournal is null || _sourceRootSet is null || _sourceEpoch == 0) pending.Add("No continuous actual source journal belongs to this work.");
        if (_current is null) { pending.Add("No complete current graph."); return pending; }
        var actualGraph = Freeze(_backend.Refresh()); Known();
        if (!NativeLocalTweenBackend.SameGraph(_current, actualGraph))
            throw new InvalidOperationException("The current retained final graph changed outside its completed exact source operations.");
        foreach (var context in _operations)
            if (!context.Captured || !context.Completed || !context.Disposed || context.NativeReturned == context.Skipped
                || context.Skipped && (context.ExitFlags != (uint)NativeOperationExitFlags.Skipped
                    || _typedInvocations.Any(inv => ReferenceEquals(inv.Parent, context))
                    || _operations.Any(op => ReferenceEquals(op.Parent, context))))
                pending.Add("Operation " + context.Observation + " lacks its exact normal or empty-skipped completed physical generation.");
        foreach (var operation in _sourceOperations.Values)
            if (operation.Failed || operation.Pairs.Count != 0 || operation.PairOrder.Count != 0 || operation.Move is not null
                || operation.Transient.Any(row => row is not null) || (operation.Skipped ? operation.Steps.Count != 0 : !operation.Committed))
                pending.Add("Source operation " + operation.Context.Observation + " has an uncommitted pair/transient/copy or invalid skipped duty.");
        foreach (var foreign in _sourceForeignOperations.Values)
            if (!foreign.NativeCallReturned || foreign.Pairs.Count != 0 || foreign.PairOrder.Count != 0)
                pending.Add("A foreign scalar child CALL has not closed its exact source parent.");
        foreach (var manager in _sourceManagers.Values)
        {
            var actual = _current.Managers.SingleOrDefault(row => row.Object == manager.Identity && row.Generation == manager.Generation);
            if (actual is not null) manager.RequireGraph(actual, commitWorkingCount: false);
            else if (!HasDetachedManagerAlias(manager)) pending.Add("An old manager scheduling alias has no completed exact Awake replacement.");
        }
        foreach (var source in _sources)
        {
            if (Locations(_current, source).Any()) pending.Add("Owned Tween " + source + " still has a current scheduling alias.");
            if (!_tweenDeaths.TryGetValue(source, out var death) || ReadTweenDeathReceipt(death.Source.Root) is null)
                pending.Add("Owned Tween " + source + " lacks its current concrete source death receipt.");
            if (!_targetDeathSets.TryGetValue(source, out var targets)) pending.Add("Owned Tween " + source + " has no concrete target baseline coverage.");
            else if (!targets.Coverage) pending.Add("Concrete target GameObject/Transform live baselines are missing.");
            else if (targets.Binding.Component is not null && ReadTweenTargetDeathReceipt(targets.Binding.TweenRoot) is null)
                pending.Add("Concrete target objects have not all died in two frames after the same Return/seal.");
            else ValidateTargetSet(targets, requireDead: targets.Binding.Component is not null);
            var currentInfo = _current.Tweens.SingleOrDefault(row => row.Object == source);
            var typedMember = _typedEvents.SingleOrDefault(row => row.Tween == source);
            if (currentInfo is null || currentInfo.OnTween != (typedMember?.Event ?? 0))
                pending.Add("The actual nullable typed event field changed outside its retained membership generation.");
        }
        foreach (var occurrence in _sourceOccurrences.Values.Where(row => _sources.Contains(row.Tween)))
        {
            if (!occurrence.Instant && (HasCurrentOccurrenceAlias(occurrence) || !HasActualOccurrenceExit(occurrence)))
                pending.Add("Occurrence " + occurrence.Id + " lacks its distinct actual source-slot exit.");
            if (occurrence.Instant && !_sourceOperations.Values.Any(row => ReferenceEquals(row.Instant, occurrence) && row.Committed
                    && row.Context.NativeReturned && row.Context.Completed && row.Context.Disposed))
                pending.Add("InstantWork " + occurrence.Id + " lacks its exact completed Play source scope.");
            foreach (var step in _sourceOperations.Values.SelectMany(row => row.Steps).Where(row => row.Occurrence == occurrence.Id && IsInlineEventSite(row.Site)))
                if (!_inlineSourceEvents.Any(row => row.Before.Transaction == step.Transaction && ReferenceEquals(row.Occurrence, occurrence)))
                    pending.Add("Owned occurrence has an earlier unowned inline effect source.");
            var observedInputs = _operations.SelectMany(context => (context.Before?.Tweens ?? Array.Empty<NativeTweenInfoObservation>())
                .Concat(context.After?.Tweens ?? Array.Empty<NativeTweenInfoObservation>())).Where(row => row.Object == occurrence.Tween).ToArray();
            if (observedInputs.Length == 0 || observedInputs.Any(row => row.TweenType != 12
                    || row.EffectValues.Message != 0 || row.EffectValues.MessageText.Length != 0))
                pending.Add("Occurrence " + occurrence.Id + " includes an unaccounted concrete setter or unknown inline activation branch.");
        }
        foreach (var admission in _admissions)
            if (!_sourceOccurrences.Values.Any(row => row.Admission == admission.Id)) pending.Add("An actual Add has no complete distinct source occurrence.");
        foreach (var inline in _inlineSourceEvents)
            if (!ReadActualChildEffectsEnded(inline)) pending.Add("Inline child " + inline.Before.Transaction + " has no actual effects-ended/transfer receipt.");
        foreach (var typed in _typedEffects)
            if (TypedEffectPending(typed) is { Length: > 0 } reason) pending.Add(reason);
        if (_typedInvocations.Count != _typedEffects.Count) pending.Add("A typed invocation has no retained effects responsibility.");
        if (!_typedEventsAdopted || !_typedEventSources.SetEquals(_sources)) pending.Add("Nullable onTween fields lack complete actual source coverage.");
        foreach (var context in _operations.Where(row => row.NativeReturned && row.Operation is NativeTweenOperation.Play or NativeTweenOperation.ExecuteTween))
        {
            var tween = SourceTween(context.Operation, context.Frame);
            var before = context.Before?.Tweens.SingleOrDefault(row => row.Object == tween);
            var after = context.After?.Tweens.SingleOrDefault(row => row.Object == tween);
            if (before is null || after is null || before.TweenType != 12 || after.TweenType != 12
                || before.EffectValues.Message != 0 || after.EffectValues.Message != 0
                || before.EffectValues.MessageText.Length != 0 || after.EffectValues.MessageText.Length != 0)
                pending.Add("A concrete native setter/unknown branch has no exact engine effects completion or accepted transfer.");
            if (before?.Target != after?.Target || before?.OnTween != after?.OnTween)
                pending.Add("The actual effect target or typed event field changed within a physical operation.");
        }
        return pending.Distinct().ToList();
    }
    private bool HasCurrentOccurrenceAlias(SourceOccurrence occurrence)
        => _sourceManagers.Values.Where(manager => !HasDetachedManagerAlias(manager)).Any(manager =>
            new[] { manager.Playing, manager.Scheduled, manager.Ready }.Any(container => container?.Items.Contains(occurrence) == true))
            || _sourceOperations.Values.Any(operation => operation.Transient.Contains(occurrence)
                || ReferenceEquals(operation.Move?.Occurrence, occurrence) || operation.Pairs.Values.Any(pair => ReferenceEquals(pair.Occurrence, occurrence)));
    private bool HasDetachedManagerAlias(SourceManager manager)
    {
        if (_current?.Managers.Any(row => row.Object == manager.Identity && row.Generation == manager.Generation) == true) return false;
        return _operations.Any(context => context.Operation == NativeTweenOperation.Awake && context.Captured && context.NativeReturned
            && context.Completed && context.Disposed && context.Before?.Managers.Any(row => row.Object == manager.Identity && row.Generation == manager.Generation) == true
            && context.After?.Managers.Any(row => row.Object == manager.Identity && row.Generation > manager.Generation
                && _current!.Managers.Any(current => current.Object == row.Object && current.Generation == row.Generation)
                && new[] { manager.Playing, manager.Scheduled, manager.Ready }.All(old => old is null
                    || !new[] { row.Playing, row.Scheduled, row.Ready }.Any(value => value?.Object == old.Identity || value?.Array == old.Array))) == true);
    }
    private bool HasActualOccurrenceExit(SourceOccurrence occurrence)
        => HasDetachedOccurrenceAliases(occurrence) || _sourceOperations.Values.Any(operation => operation.Committed
            && operation.Steps.Any(step => step.Occurrence == occurrence.Id && step.AfterSequence > step.BeforeSequence
                && (step.Site is NativeTweenSourceSite.PlayingTailClear or NativeTweenSourceSite.PlayingMoveClear
                    or NativeTweenSourceSite.ScheduledRemoveAdvance or NativeTweenSourceSite.ScheduledRemoveTail or NativeTweenSourceSite.ScheduledRemoveCurrent
                    || step.Redirected && step.Site is NativeTweenSourceSite.ReadyPopFront or NativeTweenSourceSite.ReadyPopAppend)));
    private bool HasDetachedOccurrenceAliases(SourceOccurrence occurrence)
        => occurrence.Locations.Count != 0 && occurrence.Locations.All(location => _sourceManagers.TryGetValue((location.Manager, location.Generation), out var manager)
            && HasDetachedManagerAlias(manager));

    internal NativeTweenCompletionSnapshot ReadWorkCompletion()
    {
        _main.VerifyAccess(); return _completionSnapshot ?? new(_sourceEpoch, _sourceReadSequence, 0, 0, _targetDeathSets.Count,
            new[] { "No current Return completion interval has been assessed." }, Unknown);
    }
}
