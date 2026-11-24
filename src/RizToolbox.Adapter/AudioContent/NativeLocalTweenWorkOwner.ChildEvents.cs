using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal interface INativeTweenChildEventSource
{
    void BorrowTweenChildEvents(NativeLocalTweenWorkOwner owner,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle> adopt);
}
internal enum NativeTweenChildEventRole { OnStart, OnComplete }
internal sealed record NativeTweenChildEventMembershipSnapshot(long Membership, long Tween, long Event,
    NativeTweenChildEventRole Role, long Field, bool Production, int? EventSlot);

internal sealed partial class NativeLocalTweenWorkOwner
{
    private readonly object _childEventIssuer = new();
    private readonly List<ChildEventMembership> _childEventMemberships = new();
    private readonly Dictionary<InlineEventSource, RegisteredChildSource> _registeredChildSources = new();
    private NativeLocalUnityEventLedger? _childEventListeners;
    private bool _borrowingChildEvents, _childEventsAdopted;
    private sealed record RegisteredChildSource(ChildEventMembership Membership, InlineSourceEvent Actual);

    internal sealed class ChildEventMembership
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly NativeLocalUnityEventLedger _listeners;
        private readonly LocalRunLaunchCommitState? _launch;
        private readonly NativeRuntime.RootHandle _tweenRoot, _eventRoot;
        internal long Id { get; }
        internal long Tween { get; }
        internal long Event { get; }
        internal NativeExactReadField Field { get; }
        internal NativeTweenChildEventRole Role { get; }
        internal NativeLocalTweenWorkOwner Owner => _owner;
        internal LocalRunLaunchCommitState? Launch => _launch;
        internal bool Production => _owner._backend is NativeLocalTweenBackend && _launch is not null;
        internal int? EventSlot { get; private set; }
        internal ChildEventMembership(object issuer, NativeLocalTweenWorkOwner owner, NativeLocalUnityEventLedger listeners,
            NativeRuntime.RootHandle tweenRoot, NativeExactReadField field, NativeRuntime.RootHandle eventRoot,
            long tween, long receiver, NativeTweenChildEventRole role)
        {
            if (!ReferenceEquals(issuer, owner._childEventIssuer) || !owner._borrowingChildEvents
                || !ReferenceEquals(owner._childEventListeners, listeners))
                throw new InvalidOperationException("Only the actual backend borrowing scope can issue Tween child membership.");
            (_issuer, _owner, _listeners, _launch, _tweenRoot, _eventRoot) = (issuer, owner, listeners, owner._launch, tweenRoot, eventRoot);
            (Id, Tween, Event, Field, Role) = (owner._childEventMemberships.Count + 1L, tween, receiver, field, role);
        }
        internal void RequireFor(NativeLocalUnityEventLedger listeners)
        {
            _owner._main.VerifyAccess(); _owner.Known();
            RequireIdentity(listeners);
            if (_tweenRoot.Target.ToInt64() != Tween || _eventRoot.Target.ToInt64() != Event)
                throw new InvalidOperationException("Tween child membership lost its exact live source roots.");
        }
        private void RequireIdentity(NativeLocalUnityEventLedger listeners)
        {
            if (!ReferenceEquals(_issuer, _owner._childEventIssuer) || !_owner._childEventMemberships.Contains(this)
                || !ReferenceEquals(_listeners, listeners) || !ReferenceEquals(_owner._childEventListeners, listeners)
                || !ReferenceEquals(_launch, _owner._launch)
                || !_owner._sources.Contains(Tween) || Production && !listeners.IsFor(_launch!))
                throw new InvalidOperationException("Tween child membership crossed its private owner, launch, source roots or event identity.");
        }
        // The listener backend adopted its OWN event root before this slot was
        // published. After the work owner's final receipt, checking that old
        // registration uses the private identity and the listener's root;
        // borrowing the lender's now-freed handle remains forbidden.
        internal void RequireIndependentRegistration(NativeLocalUnityEventLedger listeners, int slot)
        {
            _owner._main.VerifyAccess(); RequireIdentity(listeners);
            if (slot < 0 || EventSlot != slot || _owner.Unknown)
                throw new InvalidOperationException("Tween child registration has no exact published listener slot.");
            if (!_owner.RetiredRootsUnavailable) RequireFor(listeners);
            else if (!_owner.HasRetirementReceipt())
                throw new InvalidOperationException("Tween child registration cannot cross an unconfirmed retirement.");
        }
        internal NativeRuntime.RootHandle BorrowEventRoot(NativeLocalUnityEventLedger listeners)
        { RequireFor(listeners); return _eventRoot; }
        internal bool IsFor(NativeLocalTweenWorkOwner owner, NativeLocalUnityEventLedger listeners)
            => ReferenceEquals(_owner, owner) && ReferenceEquals(_listeners, listeners) && ReferenceEquals(_issuer, owner._childEventIssuer)
                && owner._childEventMemberships.Contains(this) && ReferenceEquals(_launch, owner._launch);
        internal void PublishSlot(NativeLocalUnityEventLedger listeners, int slot)
        {
            RequireFor(listeners);
            if (EventSlot is not null && EventSlot != slot) throw new InvalidOperationException("Tween child membership was published through a different event slot.");
            EventSlot = slot;
        }
        internal NativeTweenChildEventMembershipSnapshot Snapshot() => new(Id, Tween, Event, Role, Field.Field.ToInt64(), Production, EventSlot);
    }

    internal IReadOnlyList<long> RequireActualChildEventBorrowing(NativeLocalTweenBackend backend)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(_backend, backend) || !_initializing || _ready || !_borrowingChildEvents || _top is not null
            || _launch is null || _listeners is null || !ReferenceEquals(_childEventListeners, _listeners)
            || !_listeners.IsFor(_launch) || _launch.Read().ActivationAttempted)
            throw new InvalidOperationException("Tween child borrowing requires this exact preactivation backend/source owner.");
        return Array.AsReadOnly(_sources.ToArray());
    }
    internal void AdoptActualTweenChildEvents(NativeLocalUnityEventLedger listeners)
    {
        VerifyTweenSourceOwner();
        if (_backend is not NativeLocalTweenBackend || !_initializing || _ready || _launch is null
            || !ReferenceEquals(_listeners, listeners) || !listeners.IsFor(_launch) || _launch.Read().ActivationAttempted)
            throw new InvalidOperationException("Actual Tween child adoption requires the retained production launch before activation.");
        AdoptTweenChildEventsCore(listeners);
    }
    internal void AdoptControlledTweenChildEventsForTests(NativeLocalUnityEventLedger listeners)
    {
        VerifyTweenSourceOwner();
        if (_backend is NativeLocalTweenBackend || _launch is not null || _listeners is not null || _operationRevision != 0)
            throw new InvalidOperationException("Controlled child adoption cannot confer production ownership or replace an active operation.");
        AdoptTweenChildEventsCore(listeners);
    }
    private void AdoptTweenChildEventsCore(NativeLocalUnityEventLedger listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        if (_childEventsAdopted || _borrowingChildEvents || _backend is not INativeTweenChildEventSource source)
            throw new InvalidOperationException("This backend has no unused actual Tween child borrowing source.");
        _childEventListeners = listeners; _borrowingChildEvents = true;
        try
        {
            listeners.RequireTweenChildAdoption(this, _launch);
            source.BorrowTweenChildEvents(this, (tweenRoot, field, eventRoot) => {
                Known(); ArgumentNullException.ThrowIfNull(tweenRoot); ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(eventRoot);
                var role = ChildEventRole(field);
                var tween = tweenRoot.Target.ToInt64(); var receiver = eventRoot.Target.ToInt64();
                if (!_sources.Contains(tween) || receiver == 0 || _childEventMemberships.Any(value => value.Tween == tween && value.Role == role))
                    throw new InvalidOperationException("Actual child borrowing repeated a field or named an unowned/null source.");
                var membership = new ChildEventMembership(_childEventIssuer, this, listeners, tweenRoot, field, eventRoot, tween, receiver, role);
                _childEventMemberships.Add(membership);
                listeners.AdoptTweenChildEvent(membership); Known();
            });
            Known(); _childEventsAdopted = true;
        }
        catch (Exception error) { Fault("Tween child adoption: " + error.Message); throw; }
        finally { _borrowingChildEvents = false; }
    }
    private static NativeTweenChildEventRole ChildEventRole(NativeExactReadField field)
    {
        if (field.IsStatic || field.Field == IntPtr.Zero || field.Type == IntPtr.Zero || field.DeclaringClass == IntPtr.Zero
            || field.FieldClass == IntPtr.Zero || field.TypeName != "UnityEngine.Events.UnityEvent" || field.Size != IntPtr.Size
            || (field.Flags & (0x10u | 0x40u)) != 0)
            throw new UnsupportedSignatureException("Tween children require exact instance zero-argument UnityEvent fields.");
        return field.Name switch {
            "onStart" when field.Offset == 0x88 => NativeTweenChildEventRole.OnStart,
            "onComplete" when field.Offset == 0x90 => NativeTweenChildEventRole.OnComplete,
            _ => throw new UnsupportedSignatureException("Only the actual onStart/onComplete fields belong to this zero-argument child owner.") };
    }
    internal ChildEventMembership? FindActualChildEventMembership(NativeLocalUnityEventLedger listeners, InlineEventSource source,
        NativeLocalUnityEventLedger.InvokeContext context)
    {
        _main.VerifyAccess();
        if (!source.IsFor(this, listeners, context)) throw new InvalidOperationException("Child membership requires the exact private actual Invoke/source association.");
        var actual = source.Snapshot;
        var role = actual.Site switch {
            NativeTweenSourceSite.UpdateOnStartFront or NativeTweenSourceSite.UpdateOnStartAppend or NativeTweenSourceSite.PlayOnStart => NativeTweenChildEventRole.OnStart,
            NativeTweenSourceSite.PlayOnComplete or NativeTweenSourceSite.ExecuteOnComplete => NativeTweenChildEventRole.OnComplete,
            _ => throw new InvalidOperationException("The actual source CALL is not a supported zero-argument child event.") };
        var membership = _childEventMemberships.SingleOrDefault(value => value.Tween == actual.Tween && value.Event == actual.Event && value.Role == role);
        if (membership is not null)
        {
            membership.RequireFor(listeners);
            var retained = _inlineSourceEvents.Single(value => value.Before.Transaction == actual.Transaction
                && value.InvokeGeneration == context._generation && ReferenceEquals(value.InvokeContext, context)
                && ReferenceEquals(value.InvokeOwner, listeners));
            if (_registeredChildSources.ContainsKey(source)) throw new InvalidOperationException("The same source token already owns a child event generation.");
            _registeredChildSources.Add(source, new(membership, retained));
        }
        return membership;
    }
    internal IReadOnlyList<NativeTweenChildEventMembershipSnapshot> ReadChildEventMemberships()
    { _main.VerifyAccess(); return Array.AsReadOnly(_childEventMemberships.Select(value => value.Snapshot()).ToArray()); }
}
