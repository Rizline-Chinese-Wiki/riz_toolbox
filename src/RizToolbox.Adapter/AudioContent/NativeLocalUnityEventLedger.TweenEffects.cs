using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal interface INativeTweenDynamicListenerSourceBackend
{
    ExactHookBroker.NativeVoidPhysicalIdentity? BorrowCurrentDynamicListenerIdentity(
        NativeLocalUnityEventLedger owner, NativeLocalUnityEventLedger.InvokeContext context);
}

internal sealed partial class NativeLocalUnityEventLedger
{
    private readonly object _tweenSourceIssuer = new();
    internal sealed class TweenDynamicSource
    {
        private readonly object _issuer;
        private readonly NativeLocalUnityEventLedger _owner;
        private readonly NativeLocalTweenWorkOwner _work;
        private readonly InvokeContext _context;
        private readonly DispatchRecord _dispatch;
        private readonly NativeOperationFrame _frame;
        private readonly ExactHookBroker.NativeVoidPhysicalIdentity _physical;
        internal TweenDynamicSource(object issuer, NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner work,
            InvokeContext context, DispatchRecord dispatch, NativeOperationFrame frame, ExactHookBroker.NativeVoidPhysicalIdentity physical)
            => (_issuer, _owner, _work, _context, _dispatch, _frame, _physical) = (issuer, owner, work, context, dispatch, frame, physical);
        internal bool IsCurrentFor(NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner work, NativeOperationFrame frame)
            => ReferenceEquals(_issuer, owner._tweenSourceIssuer) && ReferenceEquals(_owner, owner) && ReferenceEquals(_work, work)
                && ReferenceEquals(owner._current, _context) && _context._native && _context._active && !_context._after
                && _context._record is { } row && row.Generation == _context._generation && owner.PhysicalTop == row.Event
                && owner.IsOwnedDynamicListenerContext(work, _context)
                && _physical.IsCurrentFor(_context)
                && row.Dispatches.Contains(_dispatch) && _dispatch.Captured && !_dispatch.Completed && _dispatch.Observation is not null
                && _frame.Equals(frame) && frame.IsValid && (owner._launch is { } launch
                    ? work.IsFor(launch, owner) : work.IsControlledDynamicListenerSource(owner));
    }

    // A manager is shared with foreign game UI. Only the exact still-open
    // listener dispatch belonging to this launch may lend its dynamic calls.
    internal TweenDynamicSource? TryCaptureDynamicTweenSource(NativeLocalTweenWorkOwner work,
        LocalRunLaunchCommitState? launch, NativeOperationFrame frame)
    {
        _main.VerifyAccess(); Guard();
        if (!ReferenceEquals(_launch, launch) || !frame.IsValid || (launch is not null
                ? !work.IsFor(launch, this) : _backend is NativeLocalUnityEventBackend || !work.IsControlledDynamicListenerSource(this)))
            throw new InvalidOperationException("Dynamic Tween source association crossed its actual listener launch or native frame.");
        if (_current is not { _native: true, _active: true, _after: false, _record: { } row } context
            || context._generation != row.Generation || PhysicalTop != row.Event || !IsOwnedDynamicListenerContext(work, context)) return null;
        var dispatch = row.Dispatches.LastOrDefault(value => value.Captured && !value.Completed);
        if (dispatch?.Observation is null) return null;
        var physical = (_backend as INativeTweenDynamicListenerSourceBackend)?.BorrowCurrentDynamicListenerIdentity(this, context);
        if (physical?.IsCurrentFor(context) != true) return null;
        return new(_tweenSourceIssuer, this, work, context, dispatch, frame, physical);
    }

    private bool IsOwnedDynamicListenerContext(NativeLocalTweenWorkOwner work, InvokeContext context)
        => context._record is { } row && (row.Slot < (_triggerEventCountBeforeTweenChildren ?? _count)
            || _tweenInlineSources.Any(source => source.IsFor(work, this, context)));

    internal sealed class TweenPlaySource
    {
        private readonly object _issuer;
        private readonly NativeLocalUnityEventLedger _owner;
        private readonly DispatchRecord _dispatch;
        internal NativeTweenPlaySourceSnapshot Snapshot { get; }
        internal TweenPlaySource(object issuer, NativeLocalUnityEventLedger owner, DispatchRecord dispatch,
            NativeTweenPlaySourceSnapshot snapshot)
            => (_issuer, _owner, _dispatch, Snapshot) = (issuer, owner, dispatch, snapshot);
        internal bool IsFor(NativeLocalUnityEventLedger owner)
            => ReferenceEquals(_owner, owner) && ReferenceEquals(_issuer, owner._tweenSourceIssuer)
                && _dispatch.Frame.ChildSequence == Snapshot.ChildSequence && _dispatch.Captured;
    }
    // This private dispatch association describes the issuer of actual Play.
    // It cannot certify arbitrary leaf effects or close any listener writer.
    internal TweenPlaySource? TryCaptureTweenPlaySource(NativeLocalTweenWorkOwner work,
        LocalRunLaunchCommitState launch, long target, IntPtr method)
    {
        _main.VerifyAccess(); Guard();
        if (!ReferenceEquals(_launch, launch) || !work.IsFor(launch, this))
            throw new InvalidOperationException("Tween source association crossed its exact production launch/listener owner.");
        if (method == IntPtr.Zero) return null;
        if (_current is not { _native: true, _active: true, _after: false, _record: { } row } context
            || context._generation != row.Generation || PhysicalTop != row.Event) return null;
        var dispatch = row.Dispatches.LastOrDefault(value => value.Captured && !value.Completed);
        if (dispatch?.Observation is not { } observation) return null;
        var matches = observation.Invokable.Leaves.Where(leaf => !leaf.Opaque && leaf.Target == target
            && leaf.MethodInfo == method.ToInt64()).ToArray();
        if (matches.Length == 0) return null;
        return new(_tweenSourceIssuer, this, dispatch, new(row.Slot, row.Event, row.Generation,
            dispatch.Frame.ParentSequence, dispatch.Frame.ChildSequence, observation.Invokable.Object, target,
            matches[0].MethodInfo, matches.Length != 1));
    }
    internal void BorrowPersistentTargets(LocalRunLaunchCommitState launch, IntPtr exactTargetClass,
        Action<int, NativeUnityEventPersistent, NativeRuntime.RootHandle> adopt)
    {
        _main.VerifyAccess(); Guard(); _authority.VerifyBeforeCapture(); ArgumentNullException.ThrowIfNull(adopt);
        if (!ReferenceEquals(_launch, launch) || launch.Read().ActivationAttempted || exactTargetClass == IntPtr.Zero
            || _backend is not NativeLocalUnityEventBackend backend)
            throw new InvalidOperationException("Only this exact preactivation production listener owner can lend actual candidate roots.");
        backend.BorrowPersistentTargets(this, launch, exactTargetClass, adopt); Known();
    }
}

internal sealed partial class NativeLocalUnityEventBackend : INativeTweenDynamicListenerSourceBackend
{
    ExactHookBroker.NativeVoidPhysicalIdentity? INativeTweenDynamicListenerSourceBackend.BorrowCurrentDynamicListenerIdentity(
        NativeLocalUnityEventLedger owner, NativeLocalUnityEventLedger.InvokeContext context)
    {
        Check();
        if (!ReferenceEquals(owner, _owner) || _invokeBinding is null
            || !_invokeBinding.TryGetCurrentNativeVoidContext<NativeLocalUnityEventLedger.InvokeContext>(new(context._receiver), out var retained, out var identity)
            || !ReferenceEquals(context, retained) || identity?.IsCurrentFor(context) != true) return null;
        return identity;
    }
    internal void BorrowPersistentTargets(NativeLocalUnityEventLedger owner, LocalRunLaunchCommitState launch,
        IntPtr exactClass, Action<int, NativeUnityEventPersistent, NativeRuntime.RootHandle> adopt)
    {
        Check();
        if (!ReferenceEquals(_owner, owner) || !ReferenceEquals(_launch, launch) || launch.Read().ActivationAttempted)
            throw new InvalidOperationException("Actual persistent target borrowing crossed its retained preactivation owner.");
        // Additional Tween child events are distinct sources. Scanning their
        // candidates here would recursively turn listener targets into new
        // admission sources merely because child membership was registered.
        var triggerEvents = owner.ReadOriginalTriggerEventIdentities().ToHashSet();
        var triggerSlots = Enumerable.Range(0, _eventCount).Where(slot => triggerEvents.Contains(_events[slot])).ToArray();
        foreach (var slot in triggerSlots)
        {
            var graph = ReadGraph(slot);
            foreach (var candidate in graph.Candidates.Where(row => row.Target != 0 && row.TargetClass == exactClass.ToInt64()))
            {
                var pointer = Pointer(candidate.Target);
                if (ObjectClass(pointer) != exactClass) throw new InvalidOperationException("Borrowed persistent target changed its exact actual class.");
                var root = _roots[_rootSlots[candidate.Target]]!;
                adopt(slot, candidate, root); RequireObservationKnown();
            }
        }
    }
}
