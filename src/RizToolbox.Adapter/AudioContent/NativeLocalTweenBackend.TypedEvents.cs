using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend : INativeTweenTypedEventSource, INativeTweenDynamicTypedEventSource, INativeTweenTypedEventGraphSource
{
    private readonly List<NativeLocalTweenWorkOwner.TypedEventMembership> _typedEventMemberships = new();
    private ExactHookBroker.Binding? _typedEventBinding;
    private VerifiedHookTarget? _typedEventTarget;

    public void BorrowTypedTweenEvents(NativeLocalTweenWorkOwner owner,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt)
    {
        _main.VerifyAccess(); RequireKnown(); ArgumentNullException.ThrowIfNull(adopt);
        if (!ReferenceEquals(_owner, owner) || !_attempted || _ready || _launch.Read().ActivationAttempted
            || _typedEventMemberships.Count != 0)
            throw new InvalidOperationException("Typed Tween event borrowing requires this exact preactivation backend.");
        foreach (var source in owner.RequireActualTypedEventBorrowing(this))
        {
            var tween = Root(source);
            var field = _tweenFields["onTween"];
            var identity = Reference(source, field, nullable: true);
            adopt(tween, field, identity == 0 ? null : Root(identity));
            RequireKnown();
        }
    }

    public void BorrowDynamicTypedTweenEvents(NativeLocalTweenWorkOwner owner, long tween,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt)
    {
        _main.VerifyAccess(); RequireKnown(); ArgumentNullException.ThrowIfNull(adopt);
        if (!ReferenceEquals(_owner, owner) || !_ready || _typedEventBinding is null || !_tweens.Contains(tween))
            throw new InvalidOperationException("Dynamic typed-event borrowing requires this backend's retained Tween source.");
        var field = _tweenFields["onTween"];
        var identity = Reference(tween, field, nullable: true);
        if (_typedEventTarget is not { } target || field.FieldClass != target.DeclaringClass
            || identity != 0 && ObjectClass(Pointer(identity)) != target.DeclaringClass)
            throw new UnsupportedSignatureException("Dynamic onTween is not the exact admitted closed Single event class.");
        var eventRoot = identity == 0 ? null : Root(identity);
        if (eventRoot is not null) ReadSingleEventGraphCore(eventRoot);
        adopt(Root(tween), field, eventRoot);
        RequireKnown();
    }

    public void BindTypedTweenEvents(NativeLocalTweenWorkOwner owner, int processCapacity,
        Func<NativeOperationFrame, IDisposable?> capture,
        Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> complete,
        Action<NativeOperationFrame, string> fault)
    {
        _main.VerifyAccess(); RequireKnown();
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(complete); ArgumentNullException.ThrowIfNull(fault);
        if (!ReferenceEquals(_owner, owner) || !_attempted || _ready || _typedEventBinding is not null
            || _typedEventMemberships.Count != 0 || processCapacity < 0 || processCapacity > _tweens.Count)
            throw new InvalidOperationException("Typed Tween event binding crossed its preactivation process source set.");
        var memberships = owner.RequireTypedEventMemberships(this, processCapacity);
        if (memberships.Count > processCapacity)
            throw new InvalidOperationException("Typed Tween memberships exceed the complete checked nullable source set.");
        _typedEventMemberships.AddRange(memberships);
        var actualClass = _tweenFields["onTween"].FieldClass;
        foreach (var membership in _typedEventMemberships)
        {
            membership.RequireFor(owner, this);
            var cls = ObjectClass(Pointer(membership.Event));
            if (cls == IntPtr.Zero || membership.Field.TypeName != "UnityEngine.Events.UnityEvent<System.Single>"
                || membership.Field.FieldClass != cls
                || membership.Field.Offset != 0x98 || (membership.Field.Flags & (0x10u | 0x40u)) != 0
                || actualClass != cls)
                throw new UnsupportedSignatureException("Tween onTween membership is not the one exact closed Single event class.");
        }
        var target = Native(() => _api.ResolveObservedInstanceSingle1(_module, actualClass, 0xbc0ff0));
        ExactHookBroker.Binding Binding(Func<NativeOperationFrame, IDisposable?> actualCapture,
            Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> actualComplete,
            Action<NativeOperationFrame, string> actualFault)
            => _broker.BindNativeOperationObservation(target, NativeOperationKind.Single1,
                actualCapture, actualComplete, actualFault);
        _typedEventTarget = target;
        IDisposable? ValidatedCapture(NativeOperationFrame frame)
        {
            if (!owner.HasTypedEventIdentity(frame.Self))
            {
                owner.ObserveForeignTypedChildInvocation();
                return null;
            }
            var bridge = _singleEventBridge ?? throw new InvalidOperationException("Single native source is not bound.");
            var subscription = _singleEventSubscription ?? throw new InvalidOperationException("Single run subscription is not bound.");
            var physical = TypedEventBinding.CaptureSingleSubscriberParent(frame, bridge.Identity.Epoch);
            var loan = bridge.BorrowParent(subscription, physical);
            try
            {
                var membership = owner.MatchTypedEvent(this, frame, out var parentOperation)
                    ?? throw new InvalidOperationException("A captured Single event lost this run's exact rooted membership.");
                _ = Native(() => _api.ObserveRootedSingleEventInvocation(_module, membership.EventRoot, target, frame, parentOperation));
                var context = capture(frame) ?? throw new InvalidOperationException("An owned Single event has no cleanup context.");
                return new NativeTweenSingleEventScope<NativeTweenSingleEventBridge.Delivery>(context, frame, loan);
            }
            catch { loan.CaptureFault("Single parent Capture did not confirm its cleanup context; responsibility remains retained."); throw; }
        }
        _typedEventBinding = Binding(ValidatedCapture, (context, frame, flags) => {
            if (context is not NativeTweenSingleEventScope<NativeTweenSingleEventBridge.Delivery> actual)
                throw new InvalidOperationException("Single completion has no actual captured subscriber scope.");
            actual.Complete(frame, flags, complete);
        }, fault);
        BindSingleExecutingSource(owner);
        foreach (var membership in _typedEventMemberships)
            ReadSingleEventGraphCore(membership.EventRoot);
        RequireKnown();
    }

    private ExactHookBroker.Binding TypedEventBinding => _typedEventBinding
        ?? throw new InvalidOperationException("Typed Tween event transport is not bound.");

    internal ExactHookBroker.ManagedBoundaryActivity ReadTypedEventActivity() => ReadTypedEventActivity(null);

    private ExactHookBroker.ManagedBoundaryActivity ReadTypedEventActivity(NativeTweenSingleEventBridge.GraphRead? verification)
    {
        var operation = _typedEventBinding?.ReadActivity() ?? new(0, true);
        var single = _singleEventBridge is { } bridge
            ? verification is null ? bridge.ReadActivity() : verification.ReadActivity(bridge)
            : new(0, _typedEventBinding is null);
        var scope = _singleEventSubscription?.ReadActivity() ?? new(0, _typedEventBinding is null);
        return new(Math.Max(operation.InFlight, Math.Max(single.InFlight, scope.InFlight)), operation.Known && single.Known && scope.Known);
    }

    private NativeSingleEventGraph ReadSingleEventGraphCore(NativeRuntime.RootHandle eventRoot)
    {
        RequireKnown();
        var target = _typedEventTarget ?? throw new InvalidOperationException("Typed Single event target is not resolved.");
        var graph = Native(() => _api.ReadSingleEventGraph(eventRoot, target, pointer => Root(AdoptPublishedInput(pointer))));
        var invokables = graph.PersistentCalls.Items
            .Concat(graph.RuntimeCalls.Items)
            .Concat(graph.ExecutingCalls.Items)
            .Where(id => id != 0);
        _listeners.RegisterTypedEventWriter(graph.Event, graph.Calls, graph.PersistentGroup, invokables);
        return graph;
    }

    NativeSingleEventGraph INativeTweenTypedEventGraphSource.ReadTypedEventGraph(NativeLocalTweenWorkOwner owner,
        NativeRuntime.RootHandle eventRoot)
    {
        Check(); ArgumentNullException.ThrowIfNull(eventRoot);
        if (!ReferenceEquals(_owner, owner) || eventRoot.Target == IntPtr.Zero
            || (!_typedEventMemberships.Any(membership => membership.Event == eventRoot.Target.ToInt64())
                && !owner.HasTypedEventIdentity(eventRoot.Target)))
            throw new InvalidOperationException("Typed Single event graph read requires this exact retained owner and event root.");
        return ReadSingleEventGraphCore(eventRoot);
    }

    // Non-generic UnityEventBase / InvokableCallList / PersistentCallGroup / InvokableCall
    // mutations targeting this specific onTween graph are tracked by NativeLocalUnityEventLedger's
    // typed-event writer registry. Generic UnityEvent<Single> / InvokableCall<Single> add/remove
    // methods remain unhooked by UnityEventOperationProfile until generic writer hooks (M3B/M3C),
    // which is why Pre==Mid==Post==Dispose SingleEventGraph equality is also enforced.
    long INativeTweenTypedEventGraphSource.ReadTypedEventWriterGeneration(NativeLocalTweenWorkOwner owner,
        NativeRuntime.RootHandle eventRoot)
    {
        Check(); ArgumentNullException.ThrowIfNull(eventRoot);
        if (!ReferenceEquals(_owner, owner) || eventRoot.Target == IntPtr.Zero
            || (!_typedEventMemberships.Any(membership => membership.Event == eventRoot.Target.ToInt64())
                && !owner.HasTypedEventIdentity(eventRoot.Target)))
            throw new InvalidOperationException("Typed Single writer-generation read requires this exact retained owner and tracked onTween root.");
        return _listeners.ReadTypedEventWriterGeneration(eventRoot.Target.ToInt64());
    }

    ExactHookBroker.ManagedBoundaryActivity INativeTweenTypedEventGraphSource.ReadTypedEventActivity()
    {
        _main.VerifyAccess(); RequireRetirementRootsReadable();
        return ReadTypedEventActivity();
    }
}
