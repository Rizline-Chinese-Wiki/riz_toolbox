using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeUnityEventListenerEffectState { Pending, NoListenerEffects, Unaccounted, Unknown }
internal sealed record NativeUnityEventListenerEffectInvocation(int Slot, long Event, long Generation, bool NativeObserved,
    long Caller, long Method, long NativeToken, long MidReturnPc, long ExecutingList, int ExecutingVersion,
    int Invokables, int Leaves, int Dispatches, int ReturnedDispatches, bool AfterObserved, bool ContextDisposed,
    bool PhysicalExited, bool ForeignChildInvoked, NativeUnityEventListenerEffectState State, string Detail,
    NativeUnityEventInline? Inline, NativeUnityEventGraph? Post, IReadOnlyList<NativeUnityEventDispatchSnapshot> DispatchSnapshots);
internal sealed record NativeUnityEventListenerMembershipSource(int Slot, long Event, UnityEventOperation Operation,
    ulong NativeSequence, long Generation, long Receiver, long Arg0, long Arg1, long Caller, long Method,
    bool ConfirmedReturned, IReadOnlyList<long> AddedInvokables, IReadOnlyList<long> RemovedInvokables,
    string Detail, NativeUnityEventInvokable? BeforeInvokable = null, NativeUnityEventInvokable? AfterInvokable = null);
internal sealed record NativeUnityEventListenerEffectsSnapshot(long PhysicalInvocations, int TrackedInvocations,
    int CompletedEmptyInvocations, int UnaccountedInvocations, bool AllInvokedEffectsAccounted, bool Unknown,
    bool FutureMembershipWritersClosed, IReadOnlyList<NativeUnityEventListenerEffectInvocation> Invocations,
    IReadOnlyList<NativeUnityEventListenerMembershipSource> MembershipSources, string Detail);
internal enum NativeUnityEventWriterSourceObligation
{ ObservedMutationChannels, AdoptedEventIdentities, SerializationCallbacks, DirectFieldAndListWriters,
    GenericAndSharedInvokableWriters, ExternalEventReferences, FutureEventSourceMembership }
internal sealed record NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation Obligation,
    NativeLocalActiveConsumerProofState State, string Detail);
internal sealed record NativeUnityEventWriterSourceSnapshot(NativeLocalActiveConsumerProofState State,
    IReadOnlyList<NativeUnityEventWriterSourceCheck> Sources, string Detail);

/// <summary>Accounts effects of actual invocation generations. Normal return of
/// a nonempty listener is never an asynchronous-consumer completion receipt.
/// The only positive issuer here proves an independently observed empty actual
/// executing list through After, context disposal and the physical probe exit.
/// Future mutation/serialization writers are a separate, still open contract.</summary>
internal sealed partial class NativeLocalUnityEventLedger : INativeLocalActiveConsumerClosureSource
{
    private const int MaximumListenerEffectInvocations = 512;
    private readonly object _listenerEffectsIssuer = new();
    private readonly List<ListenerEffectInvocation> _listenerEffectInvocations = new();
    private readonly Dictionary<InvokeContext, ListenerEffectInvocation> _listenerEffectCurrent = new();
    private ListenerWriterSourceClosure? _listenerWriterSource;

    // A private exact-source owner, not a DTO flag or caller-issued permission.
    // It binds this observation attempt to the real launch/backend/event set
    // and its precise Invoke/Mutation generations. Unobserved writers remain
    // separate named duties even when every past actual Invoke was empty.
    private sealed class ListenerWriterSourceClosure
    {
        private readonly NativeLocalUnityEventLedger _owner;
        private readonly LocalRunLaunchCommitState _launch;
        private readonly NativeLocalUnityEventBackend _backend;
        private readonly long[] _events, _invocations, _mutations;
        internal ListenerWriterSourceClosure(NativeLocalUnityEventLedger owner,
            LocalRunLaunchCommitState launch, NativeLocalUnityEventBackend backend)
        {
            owner._main.VerifyAccess();
            if (!ReferenceEquals(owner._launch, launch) || !ReferenceEquals(owner._backend, backend) || owner.Unknown
                || !owner._ready || owner._initializing || owner._busy || owner._physicalDepth != 0
                || owner._current is not null || owner._mutationCurrent is not null)
                throw new InvalidOperationException("Writer source capture requires the exact idle retained production listener owner.");
            (_owner, _launch, _backend) = (owner, launch, backend);
            _events = owner._events.Take(owner._count).Select(row => row.Event).ToArray();
            _invocations = owner._events.Take(owner._count).Select(row => row.Generation).ToArray();
            _mutations = owner._events.Take(owner._count).Select(row => row.MutationGeneration).ToArray();
        }
        internal bool IsCurrent(NativeLocalUnityEventLedger owner)
        {
            owner._main.VerifyAccess();
            return ReferenceEquals(owner, _owner) && ReferenceEquals(owner._launch, _launch)
                && ReferenceEquals(owner._backend, _backend) && !owner.Unknown && owner._ready
                && !owner._initializing && !owner._busy && owner._physicalDepth == 0
                && owner._current is null && owner._mutationCurrent is null && _events.Length == owner._count
                && _events.Select((identity, index) => identity == owner._events[index].Event
                    && _invocations[index] == owner._events[index].Generation
                    && _mutations[index] == owner._events[index].MutationGeneration).All(value => value);
        }
        internal NativeUnityEventWriterSourceSnapshot Read(NativeLocalUnityEventLedger owner)
        {
            if (!IsCurrent(owner)) throw new InvalidOperationException("Writer source closure crossed its actual backend, launch, event set or mutation generation.");
            owner._authority.VerifyObservation();
            _backend.RequireWriterSourceIdentity(owner, _launch, _events);
            var activity = _backend.ReadActivity();
            if (!activity.Known || activity.InFlight < 0 || !IsCurrent(owner))
                throw new InvalidOperationException("Writer source native activity or its exact generations became Unknown.");
            var known = activity.InFlight == 0;
            var empty = _events.Length == 0;
            var checks = new[] {
                new NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation.ObservedMutationChannels,
                    known ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending,
                    "The actual fourteen allowlisted mutation bindings, including both exact invokable delegate writers, are retained and their physical activity is sampled; this does not exclude other writers."),
                new NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation.AdoptedEventIdentities,
                    NativeLocalActiveConsumerProofState.Proven,
                    "The exact production backend and captured Trigger/event set match this launch and Invoke/Mutation generations."),
                new NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation.SerializationCallbacks,
                    empty ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending,
                    empty ? "No UnityEvent object belongs to the current complete adopted set."
                        : "UnityEventBase serialization aliases at 0x1563CF0 and PersistentCall aliases at 0x1549E50 have no exact source-owned closure receipt."),
                new NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation.DirectFieldAndListWriters,
                    empty ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending,
                    empty ? "No adopted event/list/delegate object has a local direct-write obligation."
                        : "Direct event/list writes remain unclosed. The two observed InvokableCall delegate writers account their actual before/after chains without closing other writers."),
                new NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation.GenericAndSharedInvokableWriters,
                    empty ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending,
                    empty ? "No adopted event can contain a local generic/shared invokable writer."
                        : "Generic, inflated, shared and cached invokable writers have no exact metadata/receiver/source closure."),
                new NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation.ExternalEventReferences,
                    empty ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending,
                    empty ? "The current complete adopted set contains no event reference to transfer."
                        : "Other holders may retain ordinary managed UnityEvent/list/delegate references; receiver death or scene unload does not revoke them."),
                new NativeUnityEventWriterSourceCheck(NativeUnityEventWriterSourceObligation.FutureEventSourceMembership,
                    NativeLocalActiveConsumerProofState.Pending,
                    "The retained EventTrigger source has no exclusive direct/serialized Trigger.events/list-writer closure; an empty adopted set alone cannot seal future source membership.")
            };
            if (!IsCurrent(owner)) throw new InvalidOperationException("Writer source generations changed during actual validation.");
            var complete = checks.All(check => check.State == NativeLocalActiveConsumerProofState.Proven);
            return new(complete ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending,
                Array.AsReadOnly(checks), string.Join(" ", checks.Where(check => check.State != NativeLocalActiveConsumerProofState.Proven)
                    .Select(check => check.Obligation + ": " + check.Detail)));
        }
    }

    internal NativeUnityEventWriterSourceSnapshot ReadListenerWriterSources()
    {
        _main.VerifyAccess();
        if (Unknown) return new(NativeLocalActiveConsumerProofState.Unknown, Array.Empty<NativeUnityEventWriterSourceCheck>(),
            "The exact retained listener writer/source owner is Unknown.");
        if (_launch is null || _backend is not NativeLocalUnityEventBackend backend)
            return new(NativeLocalActiveConsumerProofState.Missing, Array.Empty<NativeUnityEventWriterSourceCheck>(),
                "Only the exact production launch/backend can establish a listener writer-source owner; controlled snapshots cannot issue closure.");
        if (_busy || _initializing || _physicalDepth != 0 || _current is not null || _mutationCurrent is not null)
            return new(NativeLocalActiveConsumerProofState.Pending, Array.Empty<NativeUnityEventWriterSourceCheck>(),
                "An actual listener or mutation scope is open; its writer-source generation is not sealed.");
        try
        {
            if (_listenerWriterSource is null || !_listenerWriterSource.IsCurrent(this))
                _listenerWriterSource = new(this, _launch, backend);
            return _listenerWriterSource.Read(this);
        }
        catch (Exception error)
        {
            Fault(error.Message);
            return new(NativeLocalActiveConsumerProofState.Unknown, Array.Empty<NativeUnityEventWriterSourceCheck>(),
                "Actual listener writer-source validation failed: " + error.Message);
        }
    }

    private NativeLocalActiveConsumerCheck ReadListenerConsumersClosure(NativeUnityEventListenerEffectsSnapshot effects,
        NativeLocalActiveConsumerObligation obligation)
    {
        if (effects.Unknown) return new(obligation, NativeLocalActiveConsumerProofState.Unknown, effects.Detail);
        if (!effects.AllInvokedEffectsAccounted) return new(obligation, NativeLocalActiveConsumerProofState.Pending, effects.Detail);
        // Neither the diagnostic FutureMembershipWritersClosed scalar nor a
        // completed-empty historical receipt can supply this distinct issuer.
        var writers = ReadListenerWriterSources();
        return new(obligation, writers.State, writers.State == NativeLocalActiveConsumerProofState.Proven
            ? "Actual past listener effects and every exact writer-source obligation have independent complete ownership."
            : "Past invoked effects are accounted, but future listener sources remain unclosed: " + writers.Detail);
    }

    private sealed class ListenerEffectInvocation
    {
        internal readonly object Issuer;
        internal readonly InvokeContext Context;
        internal readonly EventRecord Event;
        internal readonly long Generation;
        internal readonly List<DispatchRecord> Dispatches = new();
        internal bool NativeObserved, AfterObserved, ContextDisposed, PhysicalExited, ForeignChildInvoked;
        internal long Caller, Method, InlineMutationGeneration, ExitMutationGeneration;
        internal NativeUnityEventInline? Inline;
        internal NativeUnityEventGraph? Post;
        internal EmptyListenerEffectsReceipt? EmptyReceipt;
        internal ListenerEffectInvocation(object issuer, InvokeContext context, EventRecord row)
            => (Issuer, Context, Event, Generation) = (issuer, context, row, row.Generation);
    }
    private sealed class EmptyListenerEffectsReceipt
    {
        private readonly NativeLocalUnityEventLedger _owner;
        private readonly ListenerEffectInvocation _invocation;
        private readonly LocalRunLaunchCommitState? _launch;
        private readonly long _token;
        internal EmptyListenerEffectsReceipt(NativeLocalUnityEventLedger owner, ListenerEffectInvocation invocation)
        {
            if (!owner.CanIssueEmptyListenerEffects(invocation))
                throw new InvalidOperationException("No exact completed actual empty listener generation can issue this receipt.");
            (_owner, _invocation, _launch, _token) = (owner, invocation, owner._launch, invocation.Inline!.NativeToken);
        }
        internal bool IsFor(NativeLocalUnityEventLedger owner, ListenerEffectInvocation invocation)
            => ReferenceEquals(_owner, owner) && ReferenceEquals(_invocation, invocation)
                && ReferenceEquals(_launch, owner._launch) && invocation.Inline?.NativeToken == _token
                && owner.CanIssueEmptyListenerEffects(invocation);
    }

    private void BeginListenerEffectsInvocation(InvokeContext context)
    {
        _main.VerifyAccess();
        if (context._record is not { } row || !context._active || !ReferenceEquals(_current, context)
            || row.Generation <= 0 || context._generation != row.Generation || PhysicalTop != row.Event
            || _listenerEffectInvocations.Count >= MaximumListenerEffectInvocations)
            throw new InvalidOperationException("Actual UnityEvent effect history is incomplete, foreign or exhausted its bounded capacity.");
        if (_listenerEffectCurrent.TryGetValue(context, out var prior) && !prior.PhysicalExited)
            throw new InvalidOperationException("A reused listener context still owns an earlier physical generation.");
        var invocation = new ListenerEffectInvocation(_listenerEffectsIssuer, context, row);
        _listenerEffectInvocations.Add(invocation); _listenerEffectCurrent[context] = invocation;
    }
    private ListenerEffectInvocation? CurrentListenerEffects(InvokeContext context)
        => _listenerEffectCurrent.TryGetValue(context, out var invocation)
            && ReferenceEquals(invocation.Issuer, _listenerEffectsIssuer)
            && ReferenceEquals(invocation.Event, context._record) && invocation.Generation == context._generation
            ? invocation : null;

    private void ObserveListenerEffectsNativeEntry(InvokeContext context)
    {
        _main.VerifyAccess();
        if (context._record is null)
        {
            // A foreign nested event can publish work to another owner. Retain
            // that fact on every enclosing owned generation, even if its own
            // executing list later appears empty. The mask is not an issuer.
            for (var parent = context._parent; parent is not null; parent = parent._parent)
                if (CurrentListenerEffects(parent) is { } enclosing) enclosing.ForeignChildInvoked = true;
            return;
        }
        var invocation = CurrentListenerEffects(context)
            ?? throw new InvalidOperationException("Actual native Invoke has no retained effect generation.");
        if (!context._native || !context._active || invocation.NativeObserved)
            throw new InvalidOperationException("Actual native listener entry repeated or crossed its effect generation.");
        invocation.NativeObserved = true; invocation.Caller = context._nativeCaller; invocation.Method = context._nativeMethod;
    }
    private void ObserveListenerEffectsInline(InvokeContext context, NativeUnityEventInline actual)
    {
        var invocation = CurrentListenerEffects(context)
            ?? throw new InvalidOperationException("The actual inline-list identity has no retained listener effect generation.");
        if (!invocation.NativeObserved || invocation.Inline is not null || !ReferenceEquals(context._record!.Inline, actual))
            throw new InvalidOperationException("The actual inline-list effect source repeated or belongs to another generation.");
        invocation.Inline = actual; invocation.InlineMutationGeneration = context._record.MutationGeneration;
    }
    private void ObserveListenerEffectsDispatch(InvokeContext context, DispatchRecord dispatch)
    {
        var invocation = CurrentListenerEffects(context)
            ?? throw new InvalidOperationException("The actual listener dispatch has no retained effect generation.");
        if (!invocation.NativeObserved || invocation.Inline is null || invocation.AfterObserved)
            throw new InvalidOperationException("Actual listener dispatch crossed its retained inline-list effect source.");
        // Keep the exact private Before record: its After updates remain bound
        // to that record even when the event's latest-generation list is cleared.
        invocation.Dispatches.Add(dispatch);
    }
    private void ObserveListenerEffectsNativeAfter(InvokeContext context)
    {
        _main.VerifyAccess(); if (context._record is null) return;
        var invocation = CurrentListenerEffects(context)
            ?? throw new InvalidOperationException("Native Invoke After lost its exact listener effect generation.");
        if (!invocation.NativeObserved || invocation.AfterObserved || !context._after || !context._record.Returned
            || context._record.Post is null || context._record.Generation != invocation.Generation)
            throw new InvalidOperationException("Actual native listener After is incomplete or crossed its generation.");
        invocation.Post = context._record.Post; invocation.AfterObserved = true;
    }
    private void ObserveListenerEffectsContextDisposed(InvokeContext context)
    {
        _main.VerifyAccess(); if (context._record is null) return;
        var invocation = CurrentListenerEffects(context);
        if (invocation is null) { Fault("Listener context disposal lost its retained effect generation."); return; }
        if (invocation.ContextDisposed || context._active || !context._after || !invocation.AfterObserved)
        { Fault("Listener effect generation did not complete its exact native After and context disposal."); return; }
        invocation.ContextDisposed = true;
    }
    private void ObserveListenerEffectsPhysicalExit(long receiver)
    {
        _main.VerifyAccess(); var row = Find(receiver); if (row is null) return;
        var invocation = CurrentListenerEffects(row.Invoke);
        if (invocation is null) return; // Failed/masked capture remains visible in the physical-versus-tracked count.
        if (invocation.PhysicalExited || row.Generation != invocation.Generation
            || Interlocked.Read(ref row.Exited) != invocation.Generation || Interlocked.Read(ref row.InFlight) != 0)
        { Fault("Listener effect history crossed its actual physical probe exit generation."); return; }
        invocation.PhysicalExited = true; invocation.ExitMutationGeneration = row.MutationGeneration;
        if (CanIssueEmptyListenerEffects(invocation)) invocation.EmptyReceipt = new(this, invocation);
    }
    private bool CanIssueEmptyListenerEffects(ListenerEffectInvocation invocation)
    {
        var actual = invocation.Inline;
        return !Unknown && ReferenceEquals(invocation.Issuer, _listenerEffectsIssuer)
            && _listenerEffectInvocations.Contains(invocation) && invocation.Generation > 0
            && invocation.NativeObserved && invocation.Caller != 0 && invocation.Method != 0
            && invocation.AfterObserved && invocation.ContextDisposed && invocation.PhysicalExited
            && !invocation.ForeignChildInvoked && invocation.Dispatches.Count == 0
            && actual is not null && actual.NativeToken != 0 && actual.ParentCaller == invocation.Caller && actual.MidReturnPc != 0
            && actual.Graph.Event == invocation.Event.Event && actual.Graph.Calls == invocation.Event.Initial?.Calls
            && actual.Executing.Object != 0 && actual.Executing.Rows.Count == 0
            && actual.Graph.ExecutingCalls.Object == actual.Executing.Object
            && actual.Graph.ExecutingCalls.Version == actual.Executing.Version && actual.Graph.ExecutingCalls.Rows.Count == 0
            && invocation.InlineMutationGeneration == invocation.ExitMutationGeneration
            && invocation.Post is not null && GraphEqual(actual.Graph, invocation.Post);
    }

    internal NativeUnityEventListenerEffectsSnapshot ReadListenerEffects()
    {
        _main.VerifyAccess(); var invocations = new List<NativeUnityEventListenerEffectInvocation>(_listenerEffectInvocations.Count);
        var empty = 0; var unaccounted = 0; long physical = 0;
        foreach (var row in _events.Take(_count)) physical = checked(physical + Interlocked.Read(ref row.Entered));
        foreach (var invocation in _listenerEffectInvocations)
        {
            var actual = invocation.Inline; var invokableCount = actual?.Executing.Rows.Count ?? 0;
            var leaves = actual?.Executing.Rows.Sum(row => row.Leaves.Count) ?? 0;
            var completed = invocation.EmptyReceipt?.IsFor(this, invocation) == true;
            var nonempty = invokableCount != 0 || invocation.Dispatches.Count != 0 || invocation.ForeignChildInvoked;
            var state = Unknown ? NativeUnityEventListenerEffectState.Unknown
                : completed ? NativeUnityEventListenerEffectState.NoListenerEffects
                : nonempty ? NativeUnityEventListenerEffectState.Unaccounted : NativeUnityEventListenerEffectState.Pending;
            if (completed) ++empty;
            if (nonempty) ++unaccounted;
            var detail = completed ? "Actual empty executing list, unchanged post graph, native After, context Dispose and exact physical exit are paired."
                : nonempty ? "A nonempty, opaque, multicast, dispatched or foreign-child listener effect has no concrete completion owner; native return and listener removal do not end it."
                : "Complete actual inline-list identity, stable mutation generation or paired native completion remains missing.";
            invocations.Add(new(invocation.Event.Slot, invocation.Event.Event, invocation.Generation, invocation.NativeObserved,
                invocation.Caller, invocation.Method, actual?.NativeToken ?? 0, actual?.MidReturnPc ?? 0,
                actual?.Executing.Object ?? 0, actual?.Executing.Version ?? 0, invokableCount, leaves,
                invocation.Dispatches.Count, invocation.Dispatches.Count(row => row.Returned), invocation.AfterObserved,
                invocation.ContextDisposed, invocation.PhysicalExited, invocation.ForeignChildInvoked, state, detail, actual,
                invocation.Post, Array.AsReadOnly(invocation.Dispatches.Select(row => row.Snapshot()).ToArray())));
        }
        var sources = ReadListenerMembershipSources();
        var accounted = !Unknown && physical == _listenerEffectInvocations.Count && _returned == physical
            && empty == _listenerEffectInvocations.Count && _physicalDepth == 0 && _current is null && _mutationCurrent is null
            && _mutationCount == _mutationReturned && !_events.Any(row => Interlocked.Read(ref row.InFlight) != 0);
        return new(physical, _listenerEffectInvocations.Count, empty, unaccounted, accounted, Unknown, false,
            invocations.AsReadOnly(), sources,
            accounted ? "All actually invoked generations have private complete-empty receipts; this does not close future membership writers."
                : "An actual invocation, listener effect, mutation or physical completion remains unaccounted; retain its source responsibility.");
    }
    private IReadOnlyList<NativeUnityEventListenerMembershipSource> ReadListenerMembershipSources()
    {
        var sources = new List<NativeUnityEventListenerMembershipSource>();
        static long[] Members(NativeUnityEventGraph? graph) => graph is null ? Array.Empty<long>()
            : graph.PersistentCalls.Rows.Concat(graph.RuntimeCalls.Rows).Select(row => row.Object).Distinct().ToArray();
        foreach (var row in _events.Take(_count))
            foreach (var mutation in row.Mutations)
            {
                var before = Members(mutation.Before); var after = Members(mutation.After);
                sources.Add(new(row.Slot, row.Event, mutation.Operation, mutation.Frame.Sequence, mutation.Generation,
                    mutation.Frame.Self.ToInt64(), mutation.Frame.Arg0.ToInt64(), mutation.Frame.Arg1.ToInt64(),
                    mutation.Frame.Caller.ToInt64(), mutation.Frame.Method.ToInt64(), mutation.ConfirmedReturned,
                    Array.AsReadOnly(after.Except(before).ToArray()), Array.AsReadOnly(before.Except(after).ToArray()),
                    mutation.ConfirmedReturned
                        ? mutation.BeforeInvokable is not null
                            ? "Actual rooted invokable Delegate and complete leaf chains were read before and after this writer; argument identity is not the final field value, and earlier invoked effects remain owned."
                            : "Actual mutation Before/After identifies membership changes only; earlier invoked effects remain owned."
                        : "Actual mutation membership is unconfirmed; no future-source or effect-completion receipt is issued.",
                    mutation.BeforeInvokable, mutation.AfterInvokable));
            }
        return sources.AsReadOnly();
    }

    NativeLocalActiveConsumerCheck INativeLocalActiveConsumerClosureSource.ReadActiveConsumerClosure(
        LocalRunLaunchCommitState exactLaunch, NativeLocalActiveConsumerObligation obligation)
    {
        ArgumentNullException.ThrowIfNull(exactLaunch); _main.VerifyAccess();
        if (obligation != NativeLocalActiveConsumerObligation.UnityEventConsumers)
            return new(obligation, NativeLocalActiveConsumerProofState.Missing, "The listener effect owner has no authority over this consumer source.");
        if (_launch is null || !ReferenceEquals(_launch, exactLaunch) || _backend is not NativeLocalUnityEventBackend)
            return new(obligation, NativeLocalActiveConsumerProofState.Missing, "Listener effects require the same production launch and its actual retained backend.");
        if (Unknown) return new(obligation, NativeLocalActiveConsumerProofState.Unknown, "The retained actual listener effect/source authority is Unknown.");
        try
        {
            _authority.VerifyObservation();
            var effects = ReadListenerEffects();
            return ReadListenerConsumersClosure(effects, obligation);
        }
        catch (Exception error)
        {
            Fault(error.Message);
            return new(obligation, NativeLocalActiveConsumerProofState.Unknown, "Actual listener effect/source validation failed: " + error.Message);
        }
    }
}

internal sealed partial class NativeLocalUnityEventBackend
{
    // Read-only identity check for the real source owner. Binding presence is
    // only a fact about the supported observations; it is not a closed-world
    // proof for direct, serialized, generic or external writers.
    internal void RequireWriterSourceIdentity(NativeLocalUnityEventLedger exactOwner, LocalRunLaunchCommitState exactLaunch,
        IReadOnlyList<long> exactEvents)
    {
        _main.VerifyAccess(); RequireObservationKnown();
        if (!_ready || !ReferenceEquals(_owner, exactOwner) || !ReferenceEquals(_launch, exactLaunch) || !_source.IsFor(exactLaunch)
            || _invokeBinding is null || _prepareBinding is null || _invokeProbe is null
            || _mutationBindings.Count != UnityEventOperationProfile.All.Count || _mutationTargets.Count != UnityEventOperationProfile.All.Count
            || _eventCount != exactEvents.Count || !_events.Take(_eventCount).SequenceEqual(exactEvents))
            throw new InvalidOperationException("Actual listener writer source lost its exact backend, full observation bindings or registered event identities.");
        var originalEvents = exactOwner.ReadOriginalTriggerEventIdentities();
        var childEvents = new List<long>();
        foreach (var (membership, slot) in _tweenChildRootResponsibilities) {
            if (slot is null) throw new InvalidOperationException("The child event has no independently retained slot.");
            membership.RequireIndependentRegistration(exactOwner, slot.Value);
            if (slot is null || membership.EventSlot != slot || slot < 0 || slot >= _eventCount
                || _events[slot.Value] != membership.Event || !_rootSlots.ContainsKey(membership.Event))
                throw new InvalidOperationException("Actual Tween child source lost its independently rooted event slot.");
            // This holder outlives the Tween backend. Never consult the former
            // lender's root after its private final retirement receipt.
            if (Pointer(membership.Event).ToInt64() != membership.Event)
                throw new InvalidOperationException("The listener's independent child event root changed identity.");
            childEvents.Add(membership.Event);
        }
        if (!originalEvents.Concat(childEvents).Distinct().OrderBy(identity => identity)
            .SequenceEqual(exactEvents.OrderBy(identity => identity)))
            throw new InvalidOperationException("Actual event membership differs from its original Trigger and private Tween child source union.");
        var source = _source.Read();
        if (source.Unknown || source.Session != exactLaunch.Identity || source.Source is not { } actual
            || actual.Controller != exactLaunch.Read().Controller
            || !actual.Triggers.Select(trigger => trigger.Events).Distinct().OrderBy(identity => identity)
                .SequenceEqual(originalEvents.OrderBy(identity => identity)))
            throw new InvalidOperationException("Actual Trigger/event membership differs from the retained listener source set.");
    }
}
