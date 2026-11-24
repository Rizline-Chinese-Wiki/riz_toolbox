using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenChildEventRegistrationSnapshot(long Membership, int? Slot, long Tween, long Event,
    NativeTweenChildEventRole Role, bool RootAdopted, bool SlotRetained, bool GraphCaptured, bool Failed, bool Production);
internal sealed record NativeTweenChildEventInvocationSnapshot(long Membership, int Slot, long Event, long Generation,
    ulong Transaction, NativeTweenSourceSite Site, bool NativeObserved, int Dispatches, bool AfterObserved,
    bool ContextDisposed, bool PhysicalExited, bool EffectsEnded, bool Production);

internal sealed partial class NativeLocalUnityEventLedger
{
    private readonly List<TweenChildRegistration> _tweenChildRegistrations = new();
    private readonly List<TweenChildInvocation> _tweenChildInvocations = new();
    private bool _adoptingTweenChildEvents;
    private int? _triggerEventCountBeforeTweenChildren;

    private sealed class TweenChildRegistration
    {
        internal readonly NativeLocalTweenWorkOwner.ChildEventMembership Membership;
        internal int? Slot; internal bool RootAdopted, SlotRetained, GraphCaptured, Failed;
        internal TweenChildRegistration(NativeLocalTweenWorkOwner.ChildEventMembership membership) => Membership = membership;
        internal NativeTweenChildEventRegistrationSnapshot Snapshot() => new(Membership.Id, Slot, Membership.Tween, Membership.Event,
            Membership.Role, RootAdopted, SlotRetained, GraphCaptured, Failed, Membership.Production);
    }
    private sealed class TweenChildInvocation
    {
        internal readonly TweenChildRegistration Registration;
        internal readonly NativeLocalTweenWorkOwner Work;
        internal readonly NativeLocalTweenWorkOwner.InlineEventSource Source;
        internal readonly ListenerEffectInvocation Effects;
        internal ChildEffectsReceipt? Receipt;
        internal TweenChildInvocation(TweenChildRegistration registration, NativeLocalTweenWorkOwner.InlineEventSource source,
            ListenerEffectInvocation effects, NativeLocalTweenWorkOwner work)
            => (Registration, Source, Effects, Work) = (registration, source, effects, work);
        internal NativeTweenChildEventInvocationSnapshot Snapshot(bool ended) => new(Registration.Membership.Id, Effects.Event.Slot,
            Effects.Event.Event, Effects.Generation, Source.Snapshot.Transaction, Source.Snapshot.Site, Effects.NativeObserved,
            Effects.Dispatches.Count, Effects.AfterObserved, Effects.ContextDisposed, Effects.PhysicalExited,
            ended, Registration.Membership.Production);
    }
    private sealed class ChildEffectsReceipt
    {
        private readonly NativeLocalUnityEventLedger _owner;
        private readonly TweenChildInvocation _child;
        private readonly EmptyListenerEffectsReceipt _empty;
        internal ChildEffectsReceipt(NativeLocalUnityEventLedger owner, TweenChildInvocation child)
        {
            if (!owner.CanIssueTweenChildEffects(child) || child.Effects.EmptyReceipt is not { } empty)
                throw new InvalidOperationException("The child source has no complete actual empty listener-effects receipt.");
            (_owner, _child, _empty) = (owner, child, empty);
        }
        internal bool IsFor(NativeLocalUnityEventLedger owner, TweenChildInvocation child)
            => ReferenceEquals(owner, _owner) && ReferenceEquals(child, _child)
                && ReferenceEquals(child.Effects.EmptyReceipt, _empty) && _empty.IsFor(owner, child.Effects)
                && owner.CanIssueTweenChildEffects(child);
    }
    internal void RequireTweenChildAdoption(NativeLocalTweenWorkOwner work, LocalRunLaunchCommitState? launch)
    {
        _main.VerifyAccess(); Guard(); _authority.VerifyBeforeCapture();
        if (_initializing || _busy || _adoptingTweenChildEvents || _current is not null || _mutationCurrent is not null || _physicalDepth != 0
            || _activeObserverRetirement is not null || _backend is not INativeTweenChildEventBackend
            || launch is null && (_launch is not null || _backend is NativeLocalUnityEventBackend)
            || launch is not null && (!ReferenceEquals(_launch, launch) || !work.IsFor(launch, this)
                || _backend is not NativeLocalUnityEventBackend || launch.Read().ActivationAttempted))
            throw new InvalidOperationException("Child event registration requires this idle preactivation listener owner and exact source launch.");
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known || activity.InFlight != 0 || _events.Any(row => row.Invoke._active || row.Prepare._active || Interlocked.Read(ref row.InFlight) != 0))
            throw new InvalidOperationException("Child event registration cannot join an already-entered physical Invoke or Prepare.");
    }
    internal void AdoptTweenChildEvent(NativeLocalTweenWorkOwner.ChildEventMembership membership)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(membership); membership.RequireFor(this);
        RequireTweenChildAdoptionOwner(membership);
        var backend = (INativeTweenChildEventBackend)_backend;
        var registration = new TweenChildRegistration(membership); _tweenChildRegistrations.Add(registration);
        _adoptingTweenChildEvents = _busy = true;
        try
        {
            var identity = backend.AdoptBorrowedTweenChildEventRoot(this, membership); registration.RootAdopted = true; Known();
            if (identity == 0 || identity != membership.Event) throw new InvalidOperationException("Independently adopted child root differs from its exact borrower.");
            _triggerEventCountBeforeTweenChildren ??= _count;
            var row = Find(identity);
            if (row is null)
            {
                if (_count == MaximumEvents) throw new InvalidOperationException("Distinct child UnityEvent capacity exhausted.");
                row = _events[_count++]; Interlocked.Exchange(ref row.Event, identity);
            }
            registration.Slot = row.Slot;
            // Publish both retained slot responsibilities before ReadGraph can
            // throw. A failed graph never rolls back roots or reuses its slot.
            backend.RetainTweenChildEventSlot(this, membership, row.Slot, identity); registration.SlotRetained = true;
            var graph = ReadGraph(row.Slot); Known();
            if (row.Initial is null) row.Initial = graph;
            else if (!GraphEqual(row.Initial, graph)) throw new InvalidOperationException("A shared preactivation child event changed during membership adoption.");
            CheckBudget(); Known(); registration.GraphCaptured = true; membership.PublishSlot(this, row.Slot);
        }
        catch (Exception error) { registration.Failed = true; Fault("Tween child event registration: " + error.Message); throw; }
        finally { _busy = _adoptingTweenChildEvents = false; }
    }

    /// <summary>Appends a child event during its exact Tween native Before.
    /// The existing parent Invoke may remain open; the joining event itself
    /// must have no active generation or concurrent graph mutation.</summary>
    internal void RequireDynamicTweenChildAdoption(NativeLocalTweenWorkOwner work, LocalRunLaunchCommitState? launch)
    {
        _main.VerifyAccess(); Guard(); _authority.VerifyObservation();
        work.RequireCurrentDynamicSourceJoin(this);
        if (_initializing || _busy || _adoptingTweenChildEvents || _mutationCurrent is not null
            || _activeObserverRetirement is not null || _backend is not INativeTweenChildEventBackend
            || !ReferenceEquals(_launch, launch) || (launch is not null
                ? _backend is not NativeLocalUnityEventBackend || !work.IsFor(launch, this) || !launch.Read().ActivationAttempted
                : _backend is NativeLocalUnityEventBackend || !work.IsControlledDynamicListenerSource(this)))
            throw new InvalidOperationException("Dynamic child registration requires this exact active Tween/listener native Before source.");
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known || activity.InFlight < 0 || _events.Any(row => row.Prepare._active)
            || _current is { } current && (!current._active || current._after || !current._native
                || current._record is not { } parent || PhysicalTop != parent.Event || parent.Generation != current._generation))
            throw new InvalidOperationException("Dynamic child registration lost its exact current listener parent or stable graph read interval.");
    }

    internal void AdoptDynamicTweenChildEvent(NativeLocalTweenWorkOwner.ChildEventMembership membership)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(membership); membership.RequireFor(this);
        RequireDynamicTweenChildAdoption(membership.Owner, membership.Launch);
        var backend = (INativeTweenChildEventBackend)_backend;
        var registration = new TweenChildRegistration(membership); _tweenChildRegistrations.Add(registration);
        _adoptingTweenChildEvents = _busy = true;
        try
        {
            var identity = backend.AdoptBorrowedTweenChildEventRoot(this, membership); registration.RootAdopted = true; Known();
            if (identity == 0 || identity != membership.Event)
                throw new InvalidOperationException("Dynamically adopted child root differs from its exact source borrower.");
            _triggerEventCountBeforeTweenChildren ??= _count;
            var row = Find(identity);
            if (row is null)
            {
                if (_count == MaximumEvents) throw new InvalidOperationException("Distinct child UnityEvent capacity exhausted.");
                row = _events[_count++]; Interlocked.Exchange(ref row.Event, identity);
            }
            if (row.Invoke._active || row.Prepare._active || Interlocked.Read(ref row.InFlight) != 0)
                throw new InvalidOperationException("A dynamic child cannot join an already active event generation.");
            registration.Slot = row.Slot;
            backend.RetainTweenChildEventSlot(this, membership, row.Slot, identity); registration.SlotRetained = true;
            var graph = ReadGraph(row.Slot); Known(); var verify = ReadGraph(row.Slot); Known();
            if (!GraphEqual(graph, verify)) throw new InvalidOperationException("The joining dynamic child graph changed across its paired complete reads.");
            if (row.Initial is null) row.Initial = graph;
            else if (!GraphEqual(row.Initial, graph))
                throw new InvalidOperationException("A shared dynamic child event differs from its retained initial graph.");
            CheckBudget(); Known(); registration.GraphCaptured = true; membership.PublishSlot(this, row.Slot);
            membership.Owner.RequireCurrentDynamicSourceJoin(this);
        }
        catch (Exception error) { registration.Failed = true; Fault("Dynamic Tween child event registration: " + error.Message); throw; }
        finally { _busy = _adoptingTweenChildEvents = false; }
    }

    internal void RequireControlledDynamicTweenChildAdoption(NativeLocalTweenWorkOwner work)
    {
        _main.VerifyAccess(); Guard();
        if (_initializing || _busy || _adoptingTweenChildEvents || _current is not null || _mutationCurrent is not null
            || _physicalDepth != 0 || _activeObserverRetirement is not null || _backend is not INativeTweenChildEventBackend
            || _launch is not null || _backend is NativeLocalUnityEventBackend
            || !_tweenChildRegistrations.Any(value => ReferenceEquals(value.Membership.Owner, work)))
            throw new InvalidOperationException("Controlled dynamic child adoption requires an idle controlled work/listener pair.");
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known || activity.InFlight != 0
            || _events.Any(row => row.Invoke._active || row.Prepare._active || Interlocked.Read(ref row.InFlight) != 0))
            throw new InvalidOperationException("Controlled dynamic child adoption crossed active listener work.");
    }

    internal void AdoptControlledDynamicTweenChildEvent(NativeLocalTweenWorkOwner.ChildEventMembership membership)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(membership); membership.RequireFor(this);
        RequireControlledDynamicTweenChildAdoption(membership.Owner);
        var backend = (INativeTweenChildEventBackend)_backend;
        var registration = new TweenChildRegistration(membership); _tweenChildRegistrations.Add(registration);
        _adoptingTweenChildEvents = _busy = true;
        try
        {
            var identity = backend.AdoptBorrowedTweenChildEventRoot(this, membership); registration.RootAdopted = true; Known();
            if (identity == 0 || identity != membership.Event)
                throw new InvalidOperationException("Controlled dynamic child root differs from its exact source borrower.");
            _triggerEventCountBeforeTweenChildren ??= _count;
            var row = Find(identity);
            if (row is null)
            {
                if (_count == MaximumEvents) throw new InvalidOperationException("Distinct child UnityEvent capacity exhausted.");
                row = _events[_count++]; Interlocked.Exchange(ref row.Event, identity);
            }
            registration.Slot = row.Slot;
            backend.RetainTweenChildEventSlot(this, membership, row.Slot, identity); registration.SlotRetained = true;
            var graph = ReadGraph(row.Slot); Known();
            if (row.Initial is null) row.Initial = graph;
            else if (!GraphEqual(row.Initial, graph))
                throw new InvalidOperationException("A shared controlled dynamic child event changed its retained graph.");
            CheckBudget(); Known(); registration.GraphCaptured = true; membership.PublishSlot(this, row.Slot);
        }
        catch (Exception error) { registration.Failed = true; Fault("Controlled dynamic Tween child event registration: " + error.Message); throw; }
        finally { _busy = _adoptingTweenChildEvents = false; }
    }
    private void RequireTweenChildAdoptionOwner(NativeLocalTweenWorkOwner.ChildEventMembership membership)
    {
        Guard(); _authority.VerifyBeforeCapture();
        if (_initializing || _busy || _adoptingTweenChildEvents || _physicalDepth != 0 || _current is not null || _mutationCurrent is not null
            || _activeObserverRetirement is not null || _backend is not INativeTweenChildEventBackend
            || _tweenChildRegistrations.Any(value => ReferenceEquals(value.Membership, membership))
            || membership.Production != (_launch is not null && _backend is NativeLocalUnityEventBackend)
            || _launch?.Read().ActivationAttempted == true)
            throw new InvalidOperationException("Child membership cannot be appended outside its exact preactivation adoption interval.");
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known || activity.InFlight != 0) throw new InvalidOperationException("Child adoption crossed active native listener transport.");
    }
    // Call after ObserveActualTweenInlineEntry has issued its private source
    // association, and after CaptureNativeInvoke began the real effect record.
    internal void ObserveRegisteredTweenChildEntry(InvokeContext context, NativeVoidFrame frame)
    {
        _main.VerifyAccess(); if (_tweenInlineWork is not { } work) return;
        if (!RequireActualTweenInlineContext(work, context, frame)) return;
        AssociateRegisteredTweenChildEntry(work, context);
    }
    internal void ObserveControlledRegisteredTweenChildEntryForTests(NativeLocalTweenWorkOwner work, InvokeContext context, NativeVoidFrame frame)
    {
        RequireControlledRegisteredTweenChildContextForTests(work, context, frame);
        var source = work.AssociateControlledChildInvokeForTests(this, context, frame);
        if (source is not null) _tweenInlineSources.Add(source);
        AssociateRegisteredTweenChildEntry(work, context);
    }
    internal void RequireControlledRegisteredTweenChildContextForTests(NativeLocalTweenWorkOwner work, InvokeContext context, NativeVoidFrame frame)
    {
        _main.VerifyAccess(); Guard();
        if (_launch is not null || _backend is NativeLocalUnityEventBackend || !frame.IsValid
            || !context._native || !context._active || context._after || !ReferenceEquals(_current, context)
            || context._receiver != frame.Self.ToInt64() || context._nativeCaller != frame.Caller.ToInt64()
            || context._nativeMethod != frame.Method.ToInt64() || PhysicalTop != context._receiver || context._record is null
            || !_tweenChildRegistrations.Any(value => value.Membership.IsFor(work, this)))
            throw new InvalidOperationException("Controlled child entry requires its exact registered broker Invoke, never a production or mask context.");
    }
    private void AssociateRegisteredTweenChildEntry(NativeLocalTweenWorkOwner work, InvokeContext context)
    {
        var sources = _tweenInlineSources.Where(value => value.IsFor(work, this, context)).ToArray();
        if (sources.Length == 0) return; // A registered event can also be called by another actual source.
        if (sources.Length != 1) { Fault("Child Invoke has multiple private source associations."); throw new InvalidOperationException("Ambiguous child source association."); }
        try
        {
            var source = sources[0]; var membership = work.FindActualChildEventMembership(this, source, context)
                ?? throw new InvalidOperationException("Actual child source has no preregistered rooted field membership.");
            var registration = _tweenChildRegistrations.Single(value => ReferenceEquals(value.Membership, membership));
            if (!registration.GraphCaptured || context._record is not { } row || registration.Slot != row.Slot || membership.EventSlot != row.Slot
                || row.Event != membership.Event || _tweenChildInvocations.Any(value => ReferenceEquals(value.Source, source)))
                throw new InvalidOperationException("Child source did not enter its complete preregistered physical event slot.");
            var invocation = CurrentListenerEffects(context)
                ?? throw new InvalidOperationException("The actual child Invoke has no existing listener-effects generation.");
            if (!invocation.NativeObserved || !ReferenceEquals(invocation.Event, row) || invocation.Generation != context._generation)
                throw new InvalidOperationException("Child association crossed its actual listener-effects generation.");
            _tweenChildInvocations.Add(new(registration, source, invocation, work));
        }
        catch (Exception error) { Fault("Tween child Invoke: " + error.Message); throw; }
    }
    internal IReadOnlyList<NativeTweenChildEventRegistrationSnapshot> ReadTweenChildRegistrations()
    { _main.VerifyAccess(); return Array.AsReadOnly(_tweenChildRegistrations.Select(value => value.Snapshot()).ToArray()); }
    internal IReadOnlyList<NativeTweenChildEventInvocationSnapshot> ReadTweenChildInvocations()
    { _main.VerifyAccess(); return Array.AsReadOnly(_tweenChildInvocations.Select(value => value.Snapshot(ReadTweenChildEffects(value))).ToArray()); }
    private bool CanIssueTweenChildEffects(TweenChildInvocation child)
    {
        var membership = child.Registration.Membership; var effects = child.Effects;
        return !Unknown && _tweenChildInvocations.Contains(child) && _tweenChildRegistrations.Contains(child.Registration)
            && child.Registration is { GraphCaptured: true, Failed: false, SlotRetained: true }
            && child.Registration.Slot == effects.Event.Slot && membership.EventSlot == effects.Event.Slot
            && membership.Event == effects.Event.Event && effects.NativeObserved && effects.AfterObserved && effects.ContextDisposed && effects.PhysicalExited
            && effects.EmptyReceipt?.IsFor(this, effects) == true
            && child.Work.HasReturnedActualChildSource(this, child.Source, membership, effects.Event.Event, effects.Generation);
    }
    private bool ReadTweenChildEffects(TweenChildInvocation child)
    {
        if (!CanIssueTweenChildEffects(child)) return false;
        child.Receipt ??= new(this, child); return child.Receipt.IsFor(this, child);
    }
    internal bool ReadActualTweenChildEffects(NativeLocalTweenWorkOwner work, NativeLocalTweenWorkOwner.InlineEventSource source)
    {
        _main.VerifyAccess();
        var child = _tweenChildInvocations.SingleOrDefault(value => ReferenceEquals(value.Source, source) && value.Registration.Membership.IsFor(work, this));
        return child is not null && ReadTweenChildEffects(child);
    }
    internal IReadOnlyList<long> ReadOriginalTriggerEventIdentities()
    { _main.VerifyAccess(); return Array.AsReadOnly(_events.Take(_triggerEventCountBeforeTweenChildren ?? _count).Select(value => value.Event).ToArray()); }
}
