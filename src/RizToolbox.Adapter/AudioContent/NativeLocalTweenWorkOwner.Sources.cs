using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenSourceOccurrenceSnapshot(long Id, long Tween, long? Admission, bool Instant,
    bool QueueExited, string? ExitReason, IReadOnlyList<NativeTweenLocation> Locations);
internal sealed record NativeTweenSourceStepSnapshot(ulong Transaction, NativeTweenSourceSite Site,
    ulong BeforeSequence, ulong AfterSequence, long? Occurrence, bool Redirected);
internal sealed record NativeTweenSourceOperationSnapshot(long Observation, NativeTweenOperation Operation,
    ulong NativeSequence, bool Production, bool Committed, bool Failed, bool Skipped, int PendingPairs,
    IReadOnlyList<NativeTweenSourceStepSnapshot> Steps, IReadOnlyList<long?> TransientOccurrences, bool PendingMove);
internal sealed record NativeTweenForeignSourceOperationSnapshot(ulong NativeSequence, ulong ParentSequence,
    uint ThreadId, uint Depth, long Manager, long Tween, long? Occurrence, bool NativeCallReturned,
    int PendingPairs, IReadOnlyList<NativeTweenSourceStepSnapshot> Steps);
internal sealed record NativeTweenSourceReplaySnapshot(bool Attached, bool Production, bool Unknown, ulong Epoch,
    ulong LastSequence, IReadOnlyList<NativeTweenSourceOperationSnapshot> Operations,
    IReadOnlyList<NativeTweenSourceOccurrenceSnapshot> Occurrences,
    IReadOnlyList<NativeTweenInlineEventSnapshot>? InlineEvents = null,
    IReadOnlyList<NativeTweenForeignSourceOperationSnapshot>? ForeignOperations = null,
    IReadOnlyList<NativeTweenSingleEventSnapshot>? TypedEvents = null,
    IReadOnlyList<NativeTweenTypedEffectSnapshot>? TypedEffects = null);

/// <summary>Consumes only reader-issued, pre-rooted journal batches while the
/// actual retained native OperationContext is open. Graphs are replay inputs;
/// they never create native publication, effects completion or release authority.</summary>
internal sealed partial class NativeLocalTweenWorkOwner : INativeTweenSourceOwner
{
    private NativeTweenSource? _sourceJournal;
    private NativeTweenSourceRootSet? _sourceRootSet;
    private long[] _journalManagers = Array.Empty<long>();
    private ulong _sourceEpoch, _sourceReadSequence, _sourceLastTransaction;
    private long _sourceOccurrenceId;
    private readonly Dictionary<OperationContext, SourceOperation> _sourceOperations = new();
    private readonly Dictionary<(long Manager, long Generation), SourceManager> _sourceManagers = new();
    private readonly Dictionary<long, SourceOccurrence> _sourceOccurrences = new();
    private readonly Dictionary<ulong, ForeignSourceOperation> _sourceForeignOperations = new();
    private ulong _lastCompactedForeignSequence;

    public void VerifyTweenSourceOwner()
    {
        _main.VerifyAccess();
        lock (Gate)
            if (!Owners.Contains(this) || !_ready && !_initializing || Unknown)
                throw new InvalidOperationException("Source journal owner is not this known retained Tween work owner.");
        if (_backend is NativeLocalTweenBackend) VerifyTweenAdmissionOwner();
    }
    internal void AttachSourceJournal(NativeTweenSource journal, NativeTweenSourceRootSet roots)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(journal); ArgumentNullException.ThrowIfNull(roots);
        if (_sourceJournal is not null || !ReferenceEquals(roots.Owner, this)
            || _backend is NativeLocalTweenBackend && !journal.Production)
            throw new InvalidOperationException("Only the exact actual source reader may attach to a production work owner.");
        // Runtime privately verifies Reader/root-set identity when reading. This
        // initial bounded read prevents a foreign root set becoming attachment.
        var batch = journal.ReadAvailable(roots, roots.LastReadSequence, 1);
        if (batch.LastSequence != roots.RetainedAfterSequence || batch.Records.Count != 0)
            throw new InvalidOperationException("Source attachment must precede this owner's first retained operation interval.");
        _sourceJournal = journal; _sourceRootSet = roots;
        _sourceEpoch = batch.Epoch; _sourceReadSequence = batch.LastSequence;
        RegisterJournalManagers();
    }
    private void RegisterJournalManagers()
    {
        if (_sourceRootSet is null) return;
        foreach (var manager in _managers)
            if (!_journalManagers.Contains(manager) && _sourceRootSet.TryGetAnchorKind(new(manager), out var kind)
                && kind == NativeTweenSourceObjectKind.Manager)
                Volatile.Write(ref _journalManagers, _journalManagers.Append(manager).ToArray());
    }
    internal void BeginSourceOperation(OperationContext context)
    {
        if (_sourceJournal is null) return;
        RequireSourceContext(context, completing: false);
        if (_sourceOperations.ContainsKey(context)) throw new InvalidOperationException("Source operation began twice.");
        PollSourceJournal();
        if (SourceKind(context.Operation) is null) return;
        var operation = new SourceOperation(context, _sourceJournal.Production);
        _sourceOperations.Add(context, operation);
        try
        {
            if (context.Operation == NativeTweenOperation.Update)
            {
                var manager = context.SharedManagerBefore ?? context.Before!.Managers.SingleOrDefault(row => row.Object == context.Frame.Self.ToInt64())
                    ?? throw new InvalidOperationException("Actual Update has no rooted manager Before graph.");
                if (manager.Object != context.Frame.Self.ToInt64())
                    throw new InvalidOperationException("The captured manager is not the receiver of this Update.");
                operation.Manager = SourceManagerFor(manager);
                operation.Manager.RequireGraph(manager);
            }
            else if (context.Operation == NativeTweenOperation.Play)
                operation.Instant = NewSourceOccurrence(context.Frame.Self.ToInt64(), null, instant: true);
            RefreshSourceOccurrences();
        }
        catch (Exception error) { operation.Failed = true; Fault("Tween source begin: " + error.Message); throw; }
    }
    internal void PollSourceJournal()
    {
        if (_sourceJournal is null || _sourceRootSet is null) return;
        VerifyTweenSourceOwner();
        try
        {
            var batch = _sourceJournal.ReadAvailable(_sourceRootSet, _sourceReadSequence, 8192);
            if (!ReferenceEquals(batch.Roots, _sourceRootSet) || !ReferenceEquals(batch.Roots.Owner, this)
                || batch.Epoch != _sourceEpoch || batch.AfterSequence != _sourceReadSequence
                || batch.Production != _sourceJournal.Production)
                throw new InvalidOperationException("Source batch crossed its private reader/root-set/epoch interval.");
            foreach (var bound in batch.Records)
            {
                if (!ReferenceEquals(bound.Roots, batch.Roots) || bound.Production != batch.Production)
                    throw new InvalidOperationException("Source row is not issued by this actual retained reader batch.");
                ApplySourceRecord(bound.Record);
            }
            _sourceReadSequence = batch.LastSequence;
        }
        catch (Exception error)
        {
            foreach (var operation in _sourceOperations.Values.Where(value => !value.Committed)) operation.Failed = true;
            Fault("Tween source replay: " + error.Message); throw;
        }
    }
    internal void CompleteSourceOperation(OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_sourceJournal is not null) RequireSourceContext(context, completing: true);
        if (context.Skipped && (_typedInvocations.Any(inv => ReferenceEquals(inv.Parent, context))
            || _operations.Any(op => ReferenceEquals(op.Parent, context))))
        {
            if (_sourceOperations.TryGetValue(context, out var skippedOperation)) skippedOperation.Failed = true;
            const string reason = "A skipped native original already executed child Tween/Single work.";
            Fault("Tween source commit: " + reason);
            throw new InvalidOperationException(reason);
        }
        if (_sourceJournal is null) return;
        if (SourceKind(context.Operation) is null) return;
        if (!_sourceOperations.TryGetValue(context, out var operation))
            throw new InvalidOperationException("Source completion lost its actual native Before context.");
        try
        {
            PollSourceJournal();
            if (context.Skipped)
            {
                if (operation.Pairs.Count != 0 || operation.Steps.Count != 0 || operation.Move is not null
                    || operation.Transient.Any(value => value is not null)
                    || _typedInvocations.Any(inv => ReferenceEquals(inv.Parent, context))
                    || _operations.Any(op => ReferenceEquals(op.Parent, context)))
                    throw new InvalidOperationException("A skipped native original already published source work.");
                // Before cannot know the native admission decision. An empty
                // skipped call closes only that scalar operation; it did not
                // execute an instant Play or end any existing queue/effects.
                operation.Skipped = true;
                if (operation.Instant is { } instant) { _sourceOccurrences.Remove(instant.Id); operation.Instant = null; }
                RefreshSourceOccurrences(); return;
            }
            if (!context.NativeReturned || context.After is null && context.SharedManagerAfter is null)
            { operation.Failed = true; return; }
            if (operation.Pairs.Count != 0 || operation.Move is not null || operation.Transient.Any(value => value is not null))
                throw new InvalidOperationException("An actual source transaction, move or transient activation has no explicit source exit.");
            if (operation.Manager is { } manager)
            {
                var final = context.SharedManagerAfter ?? context.After!.Managers.SingleOrDefault(row => row.Object == manager.Identity && row.Generation == manager.Generation)
                    ?? throw new InvalidOperationException("Update returned through a different actual manager generation.");
                manager.RequireGraph(final, commitWorkingCount: true);
            }
            operation.Committed = true;
            RefreshSourceOccurrences();
        }
        catch (Exception error) { operation.Failed = true; Fault("Tween source commit: " + error.Message); throw; }
    }
    // Drop only a fully replayed top-level read-only interval. The cursor and
    // revision remain monotonic. Queue moves, owned work and failed exits stay inspectable.
    private void CompactSharedUpdate(OperationContext context)
    {
        if (context.Parent is not null || _typedEventTop is not null || !ReferenceEquals(_operations[^1], context)
            || _typedInvocations.Any(call => ReferenceEquals(call.Parent, context))
            || !SameManager(context.SharedManagerBefore, context.SharedManagerAfter)
            || !_sourceOperations.TryGetValue(context, out var operation) || !operation.Committed || operation.Failed)
            return;
        foreach (var step in operation.Steps)
        {
            if (step.Redirected) return;
            if (step.Occurrence is null && step.Site is NativeTweenSourceSite.ReadyPopFront or NativeTweenSourceSite.ReadyPopAppend)
                continue;
            if (step.Site is not (NativeTweenSourceSite.PlayingExecuteExisting or NativeTweenSourceSite.TailDeadDecision)
                || step.Occurrence is not { } id || !_sourceOccurrences.TryGetValue(id, out var occurrence)
                || _sources.Contains(occurrence.Tween)) return;
        }
        var foreignChildren = _sourceForeignOperations.Values.Where(row => ReferenceEquals(row.Parent, operation)).ToArray();
        if (foreignChildren.Any(row => !row.NativeCallReturned || row.Pairs.Count != 0 || row.PairOrder.Count != 0)) return;
        foreach (var child in foreignChildren)
        {
            // The native operation entries share a monotonic process sequence.
            // Keep the closed top-level interval's high-water mark after dropping its rows.
            _lastCompactedForeignSequence = Math.Max(_lastCompactedForeignSequence, child.NativeSequence);
            _sourceForeignOperations.Remove(child.NativeSequence);
        }
        _sourceOperations.Remove(context);
        _operations.RemoveAt(_operations.Count - 1);
    }
    // Root connects this after the real Add After has been independently
    // captured. It describes that exact admission; public snapshots cannot call
    // this with a foreign/reused/incomplete OperationContext.
    internal void ObserveSourceAdmission(OperationContext context)
    {
        if (_sourceJournal is null) return;
        RequireSourceContext(context, completing: true);
        try
        {
            PollSourceJournal();
            if (context.Operation != NativeTweenOperation.AddTweenToWaitingList || !context.NativeReturned || context.Skipped
                || context.Before is null || context.After is null) return;
            var admission = _admissions.SingleOrDefault(row => row.Sequence == context.Frame.Sequence && row.Tween == context.Frame.Arg0.ToInt64());
            if (admission is not { Confirmed: true, AdmittedAt: { } at }) return;
            if (_sourceOccurrences.Values.Any(value => value.Admission == admission.Id))
                throw new InvalidOperationException("The same actual Add admission already owns its labelled occurrence.");
            var before = context.Before.Managers.Single(row => row.Object == at.Manager && row.Generation == at.Generation);
            var after = context.After.Managers.Single(row => row.Object == at.Manager && row.Generation == at.Generation);
            var manager = SourceManagerFor(before); manager.RequireGraph(before);
            var queue = at.Kind == NativeTweenLocationKind.Scheduled ? manager.Scheduled : manager.Ready;
            var actual = at.Kind == NativeTweenLocationKind.Scheduled ? after.Scheduled : after.Ready;
            if (queue is null || actual is null || at.Index != queue.Count || actual.Count != queue.Count + 1
                || !SourceContainer.NextVersion(queue.Version, actual.Version))
                throw new InvalidOperationException("Actual admission is not the next versioned labelled queue slot.");
            queue.RequireAppendStorage(actual.Array, actual.CapacityItems.Count);
            var label = NewSourceOccurrence(admission.Tween, admission.Id, false);
            queue.Resize(actual.Array, actual.CapacityItems.Count); queue.Write(at.Index, label, queue.At(at.Index)?.Tween ?? 0);
            queue.Count = actual.Count; queue.Version = actual.Version; manager.RequireGraph(after);
            RefreshSourceOccurrences();
        }
        catch (Exception error) { Fault("Tween source admission: " + error.Message); throw; }
    }
    private void RequireSourceContext(OperationContext context, bool completing)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(context);
        if (!_operations.Contains(context) || !ReferenceEquals(_top, context) || !context.Captured
            || context.Before is null && context.SharedManagerBefore is null
            || context.Disposed || context.Completed != completing || !context.Frame.IsValid)
            throw new InvalidOperationException("Source observation requires this exact current retained native operation context.");
    }
    private static NativeTweenSourceOperation? SourceKind(NativeTweenOperation operation) => operation switch {
        NativeTweenOperation.Update => NativeTweenSourceOperation.Update,
        NativeTweenOperation.Play => NativeTweenSourceOperation.Play,
        NativeTweenOperation.ExecuteTween => NativeTweenSourceOperation.Execute,
        _ => null };
    private SourceOperation? FindSourceOperation(NativeTweenSourceRecord record)
    {
        var candidates = _sourceOperations.Values.Where(row => row.Context.Frame.Sequence == record.UpdateSequence
            && SourceKind(row.Context.Operation) == record.Operation && !row.Committed && !row.Failed && !row.Skipped).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1) throw new InvalidOperationException("Native journal has no unique actual retained operation parent.");
        var operation = candidates[0]; var context = operation.Context;
        var ancestors = SourceAncestors(context).ToArray();
        var manager = context.Operation == NativeTweenOperation.Play
            ? ancestors.FirstOrDefault(parent => parent.Operation is NativeTweenOperation.Update or NativeTweenOperation.ExecuteTween)?.Frame.Self ?? IntPtr.Zero
            : context.Frame.Self;
        if (record.Epoch != _sourceEpoch || record.ThreadId != NativeEngineInlinePlatform.CurrentOsThreadId
            || context.Disposed || !IsCurrentSourceAncestor(context) || record.Manager != manager
            || record.Operation == NativeTweenSourceOperation.Play && record.Tween != context.Frame.Self
            || record.Operation == NativeTweenSourceOperation.Execute && record.Tween != context.Frame.Arg0)
            throw new InvalidOperationException("Source row crossed the actual operation's receiver, argument or native thread.");
        if (record.ParentUpdateSequence != (ancestors.FirstOrDefault()?.Frame.Sequence ?? 0) || record.Depth != ancestors.Length + 1
            || ancestors.Any(parent => !_operations.Contains(parent) || parent.Disposed || !_sourceOperations.ContainsKey(parent))
            || manager != IntPtr.Zero && context.SharedManagerBefore?.Object != manager.ToInt64()
                && context.Before?.Managers.Any(value => value.Object == manager.ToInt64()) != true)
            throw new InvalidOperationException("Source row crossed its actual native operation parent sequence or physical depth.");
        return operation;
    }
    private static IEnumerable<OperationContext> SourceAncestors(OperationContext context)
    {
        for (var parent = context.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.ForeignMask) throw new InvalidOperationException("An owned source crossed an uncovered foreign physical Tween parent.");
            if (SourceKind(parent.Operation) is not null) yield return parent;
        }
    }
    private bool IsCurrentSourceAncestor(OperationContext context)
    {
        for (var current = _top; current is not null; current = current.Parent)
            if (ReferenceEquals(current, context)) return true;
        return false;
    }
    private void ApplySourceRecord(NativeTweenSourceRecord record)
    {
        if (record.Stage != NativeTweenSourceStage.After)
        {
            if (record.Transaction <= _sourceLastTransaction)
                throw new InvalidOperationException("Native source transaction was reused or regressed after its prior issuance.");
            _sourceLastTransaction = record.Transaction;
        }
        var operation = FindSourceOperation(record);
        if (operation is null)
        {
            if (TryApplyForeignSourceRecord(record)) return;
            throw new InvalidOperationException("Native journal has no actual retained operation or exact foreign scalar source parent.");
        }
        if (record.Stage == NativeTweenSourceStage.Decision)
        {
            if (record.Site != NativeTweenSourceSite.TailDeadDecision)
                throw new InvalidOperationException("Unexpected native source decision site.");
            var occurrence = (operation.Manager?.Playing ?? throw new InvalidOperationException("Tail decision has no actual playing vector."))
                .RequireAt(record.SourceIndex, record.Tween.ToInt64(), allowNull: true);
            operation.Steps.Add(new(record.Transaction, record.Site, record.EventSequence, record.EventSequence,
                occurrence?.Id, (record.Flags & NativeTweenSourceFlags.Redirected) != 0));
            return;
        }
        if (record.Stage == NativeTweenSourceStage.Before)
        {
            if (operation.Pairs.ContainsKey(record.Transaction)) throw new InvalidOperationException("Native source transaction repeated its Before.");
            var occurrence = ResolveSourceOccurrence(operation, record);
            operation.Pairs.Add(record.Transaction, new(record, occurrence));
            operation.PairOrder.Add(record.Transaction);
            if (IsInlineEventSite(record.Site) && _sources.Contains(record.Tween.ToInt64())) BeginInlineSourceEvent(operation, record, occurrence);
            return;
        }
        // Failed validation retains its actual Before and all labels. It must
        // never turn an unknown native transaction into an empty successful one.
        if (!operation.Pairs.TryGetValue(record.Transaction, out var pair) || operation.PairOrder.LastOrDefault() != record.Transaction
            || !SameSourcePair(pair.Before, record))
            throw new InvalidOperationException("Native source After does not match its actual Before transaction.");
        ApplySourceAfter(operation, pair, record);
        if (IsInlineEventSite(record.Site) && _sources.Contains(record.Tween.ToInt64())) CompleteInlineSourceEvent(operation, record);
        if (IsExecuteSite(record.Site))
            foreach (var foreign in _sourceForeignOperations.Values.Where(value => ReferenceEquals(value.Parent, operation) && ReferenceEquals(value.Issuer, pair)))
            {
                if (foreign.Pairs.Count != 0) throw new InvalidOperationException("A foreign Execute returned with an unfinished actual scalar child source call.");
                foreign.NativeCallReturned = true;
            }
        operation.Pairs.Remove(record.Transaction); operation.PairOrder.RemoveAt(operation.PairOrder.Count - 1);
        operation.Steps.Add(new(record.Transaction, record.Site, pair.Before.EventSequence, record.EventSequence,
            pair.Occurrence?.Id, (record.Flags & NativeTweenSourceFlags.Redirected) != 0));
    }
    // A shared manager's Update also executes unrelated, already rooted queue
    // members. The owner does not fabricate a CLR OperationContext for them.
    // Their native child source sequence is retained only as a scalar extension
    // of the exact still-open parent Execute CALL, without local effect tokens.
    private bool TryApplyForeignSourceRecord(NativeTweenSourceRecord record)
    {
        if (record.Operation != NativeTweenSourceOperation.Execute || record.Site != NativeTweenSourceSite.ExecuteOnComplete
            || record.Tween == IntPtr.Zero || _sources.Contains(record.Tween.ToInt64())) return false;
        if (!_sourceForeignOperations.TryGetValue(record.UpdateSequence, out var foreign))
        {
            if (record.Stage != NativeTweenSourceStage.Before || record.UpdateSequence <= _lastCompactedForeignSequence
                || record.UpdateSequence <= record.ParentUpdateSequence) return false;
            var parents = _sourceOperations.Values.Where(value => value.Context.Operation == NativeTweenOperation.Update
                && value.Context.Frame.Sequence == record.ParentUpdateSequence && !value.Committed && !value.Failed
                && !value.Context.Disposed && IsCurrentSourceAncestor(value.Context)).ToArray();
            if (parents.Length != 1) return false;
            var parent = parents[0];
            if (parent.PairOrder.Count == 0 || !parent.Pairs.TryGetValue(parent.PairOrder[^1], out var issuer)
                || !IsExecuteSite(issuer.Before.Site) || issuer.Before.Tween != record.Tween
                || issuer.Occurrence is null || issuer.Occurrence.Tween != record.Tween.ToInt64()) return false;
            foreign = new(parent, issuer, record); _sourceForeignOperations.Add(record.UpdateSequence, foreign);
        }
        if (record.Epoch != _sourceEpoch || record.ThreadId != NativeEngineInlinePlatform.CurrentOsThreadId
            || record.ThreadId != foreign.ThreadId || record.Depth != foreign.Depth
            || record.Depth != SourceAncestors(foreign.Parent.Context).Count() + 2
            || record.ParentUpdateSequence != foreign.Parent.Context.Frame.Sequence || record.Manager != foreign.Parent.Context.Frame.Self
            || record.Tween.ToInt64() != foreign.Tween || foreign.NativeCallReturned || foreign.Parent.Context.Disposed
            || !IsCurrentSourceAncestor(foreign.Parent.Context) || foreign.Parent.PairOrder.LastOrDefault() != foreign.Issuer.Before.Transaction
            || !foreign.Parent.Pairs.TryGetValue(foreign.Issuer.Before.Transaction, out var actual) || !ReferenceEquals(actual, foreign.Issuer))
            throw new InvalidOperationException("Foreign scalar source crossed its actual native child, parent CALL, thread, manager or source occurrence.");
        if (record.Stage == NativeTweenSourceStage.Before)
        {
            if (!foreign.Pairs.TryAdd(record.Transaction, record)) throw new InvalidOperationException("Foreign scalar source repeated its Before.");
            foreign.PairOrder.Add(record.Transaction); return true;
        }
        if (record.Stage != NativeTweenSourceStage.After || foreign.PairOrder.LastOrDefault() != record.Transaction
            || !foreign.Pairs.TryGetValue(record.Transaction, out var before) || !SameSourcePair(before, record))
            throw new InvalidOperationException("Foreign scalar source After crossed its precise native Before.");
        foreign.Steps.Add(new(record.Transaction, record.Site, before.EventSequence, record.EventSequence, foreign.Issuer.Occurrence?.Id, false));
        foreign.Pairs.Remove(record.Transaction); foreign.PairOrder.RemoveAt(foreign.PairOrder.Count - 1); return true;
    }
    private SourceOccurrence? ResolveSourceOccurrence(SourceOperation operation, NativeTweenSourceRecord record)
    {
        var site = record.Site; var manager = operation.Manager;
        if (site is NativeTweenSourceSite.ReadyPopFront or NativeTweenSourceSite.ReadyPopAppend)
        {
            var lane = site == NativeTweenSourceSite.ReadyPopFront ? 0 : 1;
            RetireRejectedTransient(operation, lane, "Next actual ready pop proves the previous activation bypassed its playing insertion.");
            var queue = RequireSourceContainer(manager?.Ready, record);
            if (record.SourceIndex != queue.Count - 1)
                throw new InvalidOperationException("Ready pop did not name the actual current top index.");
            return queue.Count == 0 ? null : queue.RequireAt(record.SourceIndex, record.Tween.ToInt64(), allowNull: true);
        }
        if (site is NativeTweenSourceSite.PlayingInsertFront or NativeTweenSourceSite.PlayingInsertAppend)
        {
            RequireSourceContainer(manager?.Playing, record, playing: true).RequirePrior(record.DestinationIndex, record.Prior.ToInt64());
            var lane = site == NativeTweenSourceSite.PlayingInsertFront ? 0 : 1;
            if (record.Occurrence != operation.TransientTransactions[lane])
                throw new InvalidOperationException("Playing insertion crossed the exact ready-pop transaction.");
            return RequireTransient(operation, lane, record.Tween.ToInt64());
        }
        if (site == NativeTweenSourceSite.UpdateOnStartFront)
            return RequireTransient(operation, 0, record.Tween.ToInt64());
        if (site == NativeTweenSourceSite.UpdateOnStartAppend)
            return RequireTransient(operation, 1, record.Tween.ToInt64());
        if (site is NativeTweenSourceSite.ScheduledPushFront or NativeTweenSourceSite.ScheduledPushTail)
        {
            var ready = RequireSourceContainer(manager?.Ready, record);
            if (record.DestinationIndex != ready.Count) throw new InvalidOperationException("Scheduled push did not name the actual next ready slot.");
            return (manager?.Scheduled ?? throw new InvalidOperationException("Scheduled transfer has no source vector."))
                .RequireActiveAt(record.SourceIndex, record.Tween.ToInt64());
        }
        if (site == NativeTweenSourceSite.ScheduledSetItem)
        {
            var queue = RequireSourceContainer(manager?.Scheduled, record);
            queue.RequireActiveAt(record.DestinationIndex, record.Prior.ToInt64(), allowNull: true);
            return queue.RequireActiveAt(record.SourceIndex, record.Tween.ToInt64());
        }
        if (site is NativeTweenSourceSite.ScheduledRemoveAdvance or NativeTweenSourceSite.ScheduledRemoveTail or NativeTweenSourceSite.ScheduledRemoveCurrent)
            return RequireSourceContainer(manager?.Scheduled, record).RequireActiveAt(record.SourceIndex, record.Tween.ToInt64(), allowNull: true);
        if (site == NativeTweenSourceSite.PlayingMoveWrite)
        {
            var queue = RequireSourceContainer(manager?.Playing, record, playing: true);
            if (operation.Move is not null || record.SourceIndex == record.DestinationIndex)
                throw new InvalidOperationException("Playing move overlaps an unfinished move or copies its own slot.");
            queue.RequirePrior(record.DestinationIndex, record.Prior.ToInt64());
            return queue.RequireAt(record.SourceIndex, record.Tween.ToInt64());
        }
        if (site is NativeTweenSourceSite.PlayingMoveClear or NativeTweenSourceSite.PlayingTailClear)
        {
            var queue = RequireSourceContainer(manager?.Playing, record, playing: true);
            var value = queue.RequireAt(record.SourceIndex, record.Tween.ToInt64(), allowNull: true);
            queue.RequirePrior(record.SourceIndex, record.Prior.ToInt64());
            if (site == NativeTweenSourceSite.PlayingMoveClear && (operation.Move is not { } move
                || move.Array != queue.Array || move.Source != record.SourceIndex || !ReferenceEquals(move.Occurrence, value)
                || !ReferenceEquals(queue.At(move.Destination), value)))
                throw new InvalidOperationException("Playing clear has no exact preceding copied source slot.");
            if (site == NativeTweenSourceSite.PlayingTailClear && operation.Move is not null)
                throw new InvalidOperationException("Tail clear crossed an unfinished exact source move.");
            return value;
        }
        if (IsExecuteSite(site))
        {
            var queue = manager?.Playing ?? throw new InvalidOperationException("Execute source has no labelled playing vector.");
            if (record.Container.ToInt64() != manager.Identity || record.ArrayBefore.ToInt64() != queue.Array)
                throw new InvalidOperationException("Execute source crossed its actual manager or playing backing array.");
            return queue.RequireAt(record.SourceIndex, record.Tween.ToInt64(), allowNull: true);
        }
        if (site is NativeTweenSourceSite.PlayOnStart or NativeTweenSourceSite.PlayOnComplete) return operation.Instant;
        if (site == NativeTweenSourceSite.ExecuteOnComplete) return FindExecutingOccurrence(operation, record.Tween.ToInt64());
        throw new InvalidOperationException("Native source site has no implemented occurrence rule.");
    }
    private SourceOccurrence? FindExecutingOccurrence(SourceOperation execute, long tween)
    {
        var parent = SourceAncestors(execute.Context).FirstOrDefault();
        if (parent is not null && _sourceOperations.TryGetValue(parent, out var source)
            && source.PairOrder.Count != 0 && source.Pairs[source.PairOrder[^1]] is { } pair && IsExecuteSite(pair.Before.Site))
        {
            if (pair.Before.Tween.ToInt64() != tween || pair.Before.RelayReturnPc.ToInt64() + 5 != execute.Context.Frame.Caller.ToInt64())
                throw new InvalidOperationException("Execute child crossed the actual current source CALL or slot occurrence.");
            return pair.Occurrence;
        }
        return null; // Direct external Execute is diagnostic, never a slot receipt.
    }
    private void ApplySourceAfter(SourceOperation operation, SourcePair pair, NativeTweenSourceRecord after)
    {
        var before = pair.Before; var manager = operation.Manager; var site = before.Site;
        if (site is NativeTweenSourceSite.ReadyPopFront or NativeTweenSourceSite.ReadyPopAppend)
        {
            var queue = RequireSourceContainer(manager?.Ready, before);
            var success = (after.Flags & NativeTweenSourceFlags.PopSucceeded) != 0;
            if (success != ((after.RawResult & 0xff) != 0)) throw new InvalidOperationException("Actual TryPop bool differs from its source flags.");
            if (success)
            {
                if (queue.Count == 0 || (pair.Occurrence?.Tween ?? 0) != after.Tween.ToInt64())
                    throw new InvalidOperationException("Actual TryPop does not return the labelled source top slot.");
                queue.RequireAfter(after, queue.Count - 1, 1);
                queue.Items[--queue.Count] = null;
                var lane = site == NativeTweenSourceSite.ReadyPopFront ? 0 : 1;
                operation.Transient[lane] = pair.Occurrence;
                operation.TransientTransactions[lane] = pair.Occurrence is null ? 0 : before.Transaction;
                if ((after.Flags & (NativeTweenSourceFlags.ExactDead | NativeTweenSourceFlags.Redirected))
                    == (NativeTweenSourceFlags.ExactDead | NativeTweenSourceFlags.Redirected))
                    RetireRejectedTransient(operation, lane, "Actual exact-dead ready occurrence was discarded before inline activation.", pair);
            }
            else
            {
                if (queue.Count != 0 || after.Tween != IntPtr.Zero) throw new InvalidOperationException("TryPop reported empty while a labelled ready slot or returned item remains.");
                queue.RequireAfter(after, 0, 0);
            }
            queue.ApplyHeader(after); return;
        }
        if (site is NativeTweenSourceSite.PlayingInsertFront or NativeTweenSourceSite.PlayingInsertAppend)
        {
            var queue = RequireSourceContainer(manager?.Playing, before, playing: true); var lane = site == NativeTweenSourceSite.PlayingInsertFront ? 0 : 1;
            queue.RequireAfter(after, queue.Items.Count, 0, playing: true);
            queue.Write(after.DestinationIndex, pair.Occurrence, before.Prior.ToInt64()); operation.Transient[lane] = null;
            operation.TransientTransactions[lane] = 0;
            queue.ApplyHeader(after, playing: true); return;
        }
        if (site == NativeTweenSourceSite.PlayingMoveWrite)
        {
            var queue = RequireSourceContainer(manager?.Playing, before, playing: true);
            queue.RequireAfter(after, queue.Items.Count, 0, playing: true);
            queue.Write(after.DestinationIndex, pair.Occurrence, before.Prior.ToInt64());
            operation.Move = new(queue.Array, before.SourceIndex, before.DestinationIndex, pair.Occurrence);
            queue.ApplyHeader(after, playing: true); return;
        }
        if (site is NativeTweenSourceSite.PlayingMoveClear or NativeTweenSourceSite.PlayingTailClear)
        {
            var queue = RequireSourceContainer(manager?.Playing, before, playing: true);
            queue.RequireAfter(after, queue.Items.Count, 0, playing: true);
            queue.Write(after.SourceIndex, null, pair.Occurrence?.Tween ?? 0);
            if (site == NativeTweenSourceSite.PlayingMoveClear) operation.Move = null;
            queue.ApplyHeader(after, playing: true); return;
        }
        if (site is NativeTweenSourceSite.ScheduledPushFront or NativeTweenSourceSite.ScheduledPushTail)
        {
            var ready = manager?.Ready ?? throw new InvalidOperationException("Scheduled transfer has no actual ready container.");
            if (before.Container.ToInt64() != ready.Identity) throw new InvalidOperationException("Scheduled transfer targets another actual ready stack.");
            ready.RequireAfter(after, ready.Count + 1, 1, append: true);
            ready.Resize(after.ArrayAfter.ToInt64(), after.CapacityAfter);
            ready.Write(ready.Count, pair.Occurrence, ready.At(ready.Count)?.Tween ?? 0); ++ready.Count;
            ready.ApplyHeader(after); return;
        }
        if (site == NativeTweenSourceSite.ScheduledSetItem)
        {
            var queue = RequireSourceContainer(manager?.Scheduled, before);
            queue.RequireAfter(after, queue.Count, 1);
            queue.Write(after.DestinationIndex, pair.Occurrence, before.Prior.ToInt64()); queue.ApplyHeader(after); return;
        }
        if (site is NativeTweenSourceSite.ScheduledRemoveAdvance or NativeTweenSourceSite.ScheduledRemoveTail or NativeTweenSourceSite.ScheduledRemoveCurrent)
        {
            var queue = RequireSourceContainer(manager?.Scheduled, before); var index = after.SourceIndex;
            if (index < 0 || index >= queue.Count) throw new InvalidOperationException("Actual scheduled removal index exceeds the labelled vector.");
            queue.RequireAfter(after, queue.Count - 1, 1);
            if (queue.Items[index] is { } removed) removed.LastRemoval = "Actual " + site + " removed source slot " + index + ".";
            for (var at = index; at < queue.Count - 1; ++at) queue.Items[at] = queue.Items[at + 1];
            queue.Items[--queue.Count] = null; queue.ApplyHeader(after); return;
        }
        // Execute and child-event source pairs record actual invocation bounds;
        // their native return does not retire the occurrence or derived effects.
    }
    private static bool IsExecuteSite(NativeTweenSourceSite site) => site is NativeTweenSourceSite.PlayingExecuteFront
        or NativeTweenSourceSite.PlayingExecuteExisting or NativeTweenSourceSite.PlayingExecuteAppend;
    private static bool IsInlineEventSite(NativeTweenSourceSite site) => site is NativeTweenSourceSite.UpdateOnStartFront
        or NativeTweenSourceSite.UpdateOnStartAppend or NativeTweenSourceSite.PlayOnStart or NativeTweenSourceSite.PlayOnComplete
        or NativeTweenSourceSite.ExecuteOnComplete;
    private static SourceContainer RequireSourceContainer(SourceContainer? container, NativeTweenSourceRecord record, bool playing = false)
    {
        if (container is null || record.Container.ToInt64() != container.Identity)
            throw new InvalidOperationException("Source operation names a different actual labelled container.");
        container.RequireBefore(record, playing);
        return container;
    }
    private static SourceOccurrence RequireTransient(SourceOperation operation, int lane, long tween)
        => operation.Transient[lane] is { } value && value.Tween == tween ? value
            : throw new InvalidOperationException("Inline activation has no exact uninstalled ready-pop occurrence.");
    private static bool SameSourcePair(NativeTweenSourceRecord before, NativeTweenSourceRecord after)
        => before.Epoch == after.Epoch && before.UpdateSequence == after.UpdateSequence && before.ParentUpdateSequence == after.ParentUpdateSequence
            && before.Operation == after.Operation && before.Site == after.Site && before.Transaction == after.Transaction
            && before.ThreadId == after.ThreadId && before.Depth == after.Depth && before.SourcePc == after.SourcePc
            && before.Manager == after.Manager && before.Container == after.Container && before.SourceIndex == after.SourceIndex
            && before.DestinationIndex == after.DestinationIndex && before.Occurrence == after.Occurrence
            && before.ArrayBefore == after.ArrayBefore && before.CountBefore == after.CountBefore
            && before.CapacityBefore == after.CapacityBefore && before.VersionBefore == after.VersionBefore && before.Prior == after.Prior
            && (before.Site is NativeTweenSourceSite.ReadyPopFront or NativeTweenSourceSite.ReadyPopAppend || before.Tween == after.Tween)
            && after.EventSequence > before.EventSequence && (after.Flags & NativeTweenSourceFlags.Paired) != 0;
    private void RetireRejectedTransient(SourceOperation operation, int lane, string reason, SourcePair? exitingPair = null)
    {
        if (operation.Transient[lane] is not { } value) return;
        if (operation.Pairs.Values.Any(pair => !ReferenceEquals(pair, exitingPair) && ReferenceEquals(pair.Occurrence, value)))
            throw new InvalidOperationException("Ready activation cannot exit while its actual child CALL remains physically open.");
        value.ExitReason = reason; operation.Transient[lane] = null; operation.TransientTransactions[lane] = 0;
    }
    private SourceManager SourceManagerFor(NativeTweenManagerObservation graph)
    {
        var key = (graph.Object, graph.Generation);
        if (_sourceManagers.TryGetValue(key, out var value)) return value;
        value = new(graph, (tween, _) => NewSourceOccurrence(tween, null, false)); _sourceManagers.Add(key, value); return value;
    }
    private SourceOccurrence NewSourceOccurrence(long tween, long? admission, bool instant)
    {
        if (tween == 0) throw new InvalidOperationException("A null slot has no source occurrence identity.");
        var value = new SourceOccurrence(++_sourceOccurrenceId, tween, admission, instant); _sourceOccurrences.Add(value.Id, value); return value;
    }
    private void RefreshSourceOccurrences()
    {
        foreach (var occurrence in _sourceOccurrences.Values) occurrence.Locations.Clear();
        foreach (var manager in _sourceManagers.Values)
            foreach (var (kind, container) in new[] { (NativeTweenLocationKind.Playing, manager.Playing),
                (NativeTweenLocationKind.Scheduled, manager.Scheduled), (NativeTweenLocationKind.Ready, manager.Ready) })
                if (container is not null)
                    for (var index = 0; index < container.Items.Count; ++index)
                        if (container.Items[index] is { } value) value.Locations.Add(new(manager.Identity, manager.Generation, kind, index));
        foreach (var occurrence in _sourceOccurrences.Values)
        {
            occurrence.QueueExited = !occurrence.Instant && occurrence.Locations.Count == 0
                && !_sourceOperations.Values.Any(operation => operation.Transient.Contains(occurrence)
                    || operation.Pairs.Values.Any(pair => ReferenceEquals(pair.Occurrence, occurrence)));
            if (occurrence.QueueExited) occurrence.ExitReason ??= occurrence.LastRemoval;
        }
    }
    internal NativeTweenSourceReplaySnapshot ReadSourceReplay()
    {
        _main.VerifyAccess(); RefreshSourceOccurrences();
        return new(_sourceJournal is not null, _sourceJournal?.Production == true, Unknown || _sourceJournal?.Unknown == true,
            _sourceEpoch, _sourceReadSequence, Array.AsReadOnly(_sourceOperations.Values.Select(value => value.Snapshot()).ToArray()),
            Array.AsReadOnly(_sourceOccurrences.Values.Select(value => value.Snapshot()).ToArray()), ReadInlineSourceEvents(),
            Array.AsReadOnly(_sourceForeignOperations.Values.Select(value => value.Snapshot()).ToArray()), ReadTypedEvents(), ReadTypedEffects());
    }
    private sealed class SourceOccurrence
    {
        internal readonly long Id, Tween; internal readonly long? Admission; internal readonly bool Instant;
        internal bool QueueExited; internal string? ExitReason, LastRemoval; internal readonly List<NativeTweenLocation> Locations = new();
        internal SourceOccurrence(long id, long tween, long? admission, bool instant) => (Id, Tween, Admission, Instant) = (id, tween, admission, instant);
        internal NativeTweenSourceOccurrenceSnapshot Snapshot() => new(Id, Tween, Admission, Instant, QueueExited, ExitReason, Array.AsReadOnly(Locations.ToArray()));
    }
    private sealed record SourcePair(NativeTweenSourceRecord Before, SourceOccurrence? Occurrence);
    private sealed record SourceMove(long Array, int Source, int Destination, SourceOccurrence? Occurrence);
    private sealed class ForeignSourceOperation
    {
        internal readonly SourceOperation Parent; internal readonly SourcePair Issuer;
        internal readonly ulong NativeSequence; internal readonly uint ThreadId, Depth; internal readonly long Tween;
        internal bool NativeCallReturned;
        internal readonly Dictionary<ulong, NativeTweenSourceRecord> Pairs = new();
        internal readonly List<ulong> PairOrder = new(); internal readonly List<NativeTweenSourceStepSnapshot> Steps = new();
        internal ForeignSourceOperation(SourceOperation parent, SourcePair issuer, NativeTweenSourceRecord record)
            => (Parent, Issuer, NativeSequence, ThreadId, Depth, Tween) = (parent, issuer, record.UpdateSequence, record.ThreadId, record.Depth, record.Tween.ToInt64());
        internal NativeTweenForeignSourceOperationSnapshot Snapshot() => new(NativeSequence, Parent.Context.Frame.Sequence,
            ThreadId, Depth, Parent.Context.Frame.Self.ToInt64(), Tween, Issuer.Occurrence?.Id, NativeCallReturned,
            Pairs.Count, Array.AsReadOnly(Steps.ToArray()));
    }
    private sealed class SourceOperation
    {
        internal readonly OperationContext Context; internal readonly bool Production;
        internal SourceManager? Manager; internal SourceOccurrence? Instant;
        internal bool Committed, Failed, Skipped;
        internal readonly SourceOccurrence?[] Transient = new SourceOccurrence?[2];
        internal readonly ulong[] TransientTransactions = new ulong[2];
        internal SourceMove? Move;
        internal readonly Dictionary<ulong, SourcePair> Pairs = new();
        internal readonly List<ulong> PairOrder = new();
        internal readonly List<NativeTweenSourceStepSnapshot> Steps = new();
        internal SourceOperation(OperationContext context, bool production) => (Context, Production) = (context, production);
        internal NativeTweenSourceOperationSnapshot Snapshot() => new(Context.Observation, Context.Operation, Context.Frame.Sequence,
            Production, Committed, Failed, Skipped, Pairs.Count, Array.AsReadOnly(Steps.ToArray()),
            Array.AsReadOnly(Transient.Select(value => value?.Id).ToArray()), Move is not null);
    }
    private sealed class SourceManager
    {
        internal readonly long Identity, Generation, Class; internal readonly int TrackSize; internal int WorkingCount;
        internal SourceContainer? Playing, Scheduled, Ready;
        internal SourceManager(NativeTweenManagerObservation graph, Func<long, int, SourceOccurrence> create)
        {
            Identity = graph.Object; Generation = graph.Generation; Class = graph.Class; TrackSize = graph.TrackSize; WorkingCount = graph.WorkingCount;
            Playing = SourceContainer.From(graph.Playing, create); Scheduled = SourceContainer.From(graph.Scheduled, create);
            Ready = SourceContainer.From(graph.Ready, create);
        }
        internal void RequireGraph(NativeTweenManagerObservation graph, bool commitWorkingCount = false)
        {
            if (Identity != graph.Object || Generation != graph.Generation || Class != graph.Class || TrackSize != graph.TrackSize
                || graph.WorkingCount < 0 || graph.Playing is { } playing && graph.WorkingCount != playing.Count
                || !commitWorkingCount && (WorkingCount != graph.WorkingCount || Playing?.Count != graph.Playing?.Count)
                || !SourceContainer.Same(Playing, graph.Playing, playing: true)
                || !SourceContainer.Same(Scheduled, graph.Scheduled) || !SourceContainer.Same(Ready, graph.Ready))
                throw new InvalidOperationException("Actual full manager graph differs from its source-slot replay vector.");
            if (commitWorkingCount)
            {
                WorkingCount = graph.WorkingCount;
                if (Playing is not null) Playing.Count = graph.Playing!.Count;
            }
        }
    }
    private sealed class SourceContainer
    {
        internal long Identity, Array, Class; internal int Count, Version;
        internal readonly List<SourceOccurrence?> Items = new();
        internal static SourceContainer? From(NativeTweenContainerObservation? graph, Func<long, int, SourceOccurrence> create)
        {
            if (graph is null) return null;
            var value = new SourceContainer { Identity = graph.Object, Array = graph.Array, Class = graph.Class, Count = graph.Count, Version = graph.Version };
            for (var index = 0; index < graph.CapacityItems.Count; ++index)
                value.Items.Add(graph.CapacityItems[index] == 0 ? null : create(graph.CapacityItems[index], index));
            return value;
        }
        internal SourceOccurrence? At(int index)
            => index >= 0 && index < Items.Count ? Items[index] : throw new InvalidOperationException("Source slot exceeds the actual complete capacity.");
        internal SourceOccurrence? RequireAt(int index, long tween, bool allowNull = false)
        {
            var value = At(index);
            if ((value?.Tween ?? 0) != tween || !allowNull && value is null)
                throw new InvalidOperationException("Actual source item differs from this exact occurrence slot.");
            return value;
        }
        internal SourceOccurrence? RequireActiveAt(int index, long tween, bool allowNull = false)
        {
            if (index < 0 || index >= Count) throw new InvalidOperationException("Source index exceeds the actual logical queue size.");
            return RequireAt(index, tween, allowNull);
        }
        internal void RequirePrior(int index, long prior)
        { if ((At(index)?.Tween ?? 0) != prior) throw new InvalidOperationException("Actual destination prior differs from the labelled slot."); }
        internal void Write(int index, SourceOccurrence? value, long prior)
        {
            RequirePrior(index, prior);
            if (Items[index] is { } old && !ReferenceEquals(old, value)) old.LastRemoval = "Actual source write replaced labelled slot " + index + ".";
            Items[index] = value;
        }
        internal void RequireBefore(NativeTweenSourceRecord record, bool playing)
        {
            if (record.ArrayBefore.ToInt64() != Array || record.CapacityBefore != Items.Count
                || record.CountBefore != (playing ? Items.Count : Count) || record.VersionBefore != (playing ? 0 : Version)
                || record.Stage == NativeTweenSourceStage.Before && (record.ArrayAfter != record.ArrayBefore
                    || record.CountAfter != record.CountBefore || record.CapacityAfter != record.CapacityBefore || record.VersionAfter != record.VersionBefore))
                throw new InvalidOperationException("Actual source Before header differs from its entire labelled capacity, size or version.");
        }
        internal static bool NextVersion(int before, int after) => before >= 0 && before < int.MaxValue && after == before + 1;
        internal void RequireAppendStorage(long array, int capacity)
        {
            if (array == 0 || capacity > 4096 || (array == Array ? capacity != Items.Count : Count != Items.Count || capacity <= Items.Count))
                throw new InvalidOperationException("Source append changed storage outside an actual full-capacity growth.");
        }
        internal void RequireAfter(NativeTweenSourceRecord record, int count, int versionStep, bool playing = false, bool append = false)
        {
            if (record.CountAfter != count || (versionStep == 0 ? record.VersionAfter != (playing ? 0 : Version) : !NextVersion(Version, record.VersionAfter)))
                throw new InvalidOperationException("Actual source After size or version differs from its exact labelled transaction.");
            if (append) RequireAppendStorage(record.ArrayAfter.ToInt64(), record.CapacityAfter);
            else if (record.ArrayAfter.ToInt64() != Array || record.CapacityAfter != Items.Count)
                throw new InvalidOperationException("A non-growing source operation changed its actual complete backing capacity.");
        }
        internal void Resize(long array, int capacity)
        {
            if (array == 0 || capacity < Count || capacity > 4096) throw new InvalidOperationException("Source backing-array transition has no complete bound.");
            if (capacity < Items.Count && Items.Skip(capacity).Any(item => item is not null))
                throw new InvalidOperationException("A resized source array would drop a retained occurrence without a source exit.");
            if (capacity < Items.Count) Items.RemoveRange(capacity, Items.Count - capacity);
            while (Items.Count < capacity) Items.Add(null);
            Array = array;
        }
        internal void ApplyHeader(NativeTweenSourceRecord record, bool playing = false)
        {
            if (!playing && record.CountAfter != Count) throw new InvalidOperationException("Actual source count differs from its applied queue operation.");
            if (!playing) Version = record.VersionAfter;
        }
        internal static bool Same(SourceContainer? value, NativeTweenContainerObservation? graph, bool playing = false)
            => value is null && graph is null || value is not null && graph is not null && value.Identity == graph.Object
                && value.Array == graph.Array && value.Class == graph.Class && (playing || value.Count == graph.Count) && value.Version == graph.Version
                && value.Items.Select(item => item?.Tween ?? 0).SequenceEqual(graph.CapacityItems);
    }
}
