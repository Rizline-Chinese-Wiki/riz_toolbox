using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenDynamicSourceLease(NativeRuntime.RootHandle Root,
    NativeTweenAdmissionLease Lease);

internal interface INativeLocalTweenDynamicSourceBackend
{
    NativeTweenDynamicSourceLease AdoptDynamicSource(NativeLocalTweenWorkOwner owner, long tween);
}

internal sealed record NativeTweenCurrentManagerSourceRoots(NativeRuntime.RootHandle Manager, NativeRuntime.RootHandle Tween);
internal interface INativeLocalTweenDynamicManagerSourceBackend
{
    NativeTweenCurrentManagerSourceRoots BorrowCurrentManagerSourceRoots(NativeLocalTweenWorkOwner owner, NativeOperationFrame frame);
}

internal interface INativeLocalTweenDynamicChildEventBackend
{
    void BorrowDynamicTweenChildEvents(NativeLocalTweenWorkOwner owner, long tween,
        Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle> adopt);
}

internal sealed partial class NativeLocalTweenWorkOwner : INativeTweenSourceOperationOwner
{
    private const int MaximumSources = 4096;
    private readonly HashSet<long> _promotedDynamicSources = new();
    private readonly Dictionary<long, NativeRuntime.RootHandle> _dynamicSourceRoots = new();
    private bool _promotingDynamicSource;
    private OperationContext? _promotingDynamicContext;
    private OperationContext? _promotingManagerContext;
    private NativeTweenCurrentManagerSourceRoots? _promotingManagerRoots;

    internal bool OwnsTweenSource(long identity) => Volatile.Read(ref _sources).Contains(identity);

    // Kept at the idle refresh boundary as an explicit invariant: graph
    // membership supplies diagnostic roots, never a late source promotion.
    private void VerifyDynamicSourceRoots()
    {
        foreach (var source in _promotedDynamicSources)
            if (!_dynamicSourceRoots.TryGetValue(source, out var root) || root.Target.ToInt64() != source)
                throw new InvalidOperationException("An adopted dynamic Tween lost its exact first-call root.");
    }

    private NativeLocalUnityEventLedger.TweenDynamicSource? CaptureDynamicListenerSource(
        NativeTweenOperation operation, NativeOperationFrame frame)
    {
        var tween = SourceTween(operation, frame);
        if (!_main.IsMainThread || tween == 0 || _sources.Contains(tween) || _sealRequested || _admissionSealed
            || !frame.IsValid || _top?.ForeignMask == true) return null;
        var listeners = _listeners ?? _childEventListeners;
        return listeners?.TryCaptureDynamicTweenSource(this, _launch, frame);
    }

    private OperationContext? CaptureForeignTweenMask(NativeTweenOperation operation, NativeOperationFrame frame)
    {
        if (!_ready || !frame.IsValid || frame.Kind != TweenOperationProfile.Get(operation).Kind
            || SourceTween(operation, frame) == 0 || _top is null
                && (operation == NativeTweenOperation.Play || !_managers.Contains(frame.Self.ToInt64()))) return null;
        var mask = new OperationContext(this, operation, frame, 0) { ForeignMask = true };
        _top = mask; return mask;
    }

    internal bool IsControlledDynamicListenerSource(NativeLocalUnityEventLedger listeners)
        => _backend is not NativeLocalTweenBackend && _launch is null && _listeners is null
            && _childEventsAdopted && ReferenceEquals(_childEventListeners, listeners) && !Unknown;

    private static long SourceTween(NativeTweenOperation operation, NativeOperationFrame frame) => operation switch {
        NativeTweenOperation.Play => frame.Self.ToInt64(),
        NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween => frame.Arg0.ToInt64(),
        _ => 0
    };

    /// <summary>Adopts during the real native Before observer, after the exact
    /// backend has retained its published arguments and before any original
    /// instruction can run. Idle graphs and copied journal rows cannot enter.</summary>
    private void AdoptDynamicSourceBefore(OperationContext context)
    {
        var tween = SourceTween(context.Operation, context.Frame);
        if (tween == 0 || _sources.Contains(tween)) return;
        if (_backend is not INativeLocalTweenDynamicSourceBackend sourceBackend
            || _sealRequested || _admissionSealed || !_ready || _promotingDynamicSource
            || !ReferenceEquals(_top, context) || !context.Captured || context.Before is null
            || context.Completed || context.Disposed || !HasDynamicSourceOrigin(context)
            || _backend is NativeLocalTweenBackend && _launch?.Read().ActivationAttempted != true)
            throw new InvalidOperationException("Dynamic Tween adoption has no exact current owned native Before source.");
        if (_sources.Length >= MaximumSources)
            throw new InvalidOperationException("Dynamic Tween source responsibility exhausted its fixed complete bound.");
        if (!context.Before.Tweens.Any(row => row.Object == tween))
            throw new InvalidOperationException("The actual dynamic Tween input is missing from its retained Before graph.");
        _promotingDynamicSource = true; _promotingDynamicContext = context;
        try
        {
            var adopted = sourceBackend.AdoptDynamicSource(this, tween);
            if (adopted.Root.Target.ToInt64() != tween || !ReferenceEquals(adopted.Root, adopted.Lease.Root)
                || adopted.Lease.Object.ToInt64() != tween || adopted.Lease.Unknown || !adopted.Lease.RootRecorded
                || !_promotedDynamicSources.Add(tween))
                throw new InvalidOperationException("Dynamic Tween promotion did not return its exact already-retained graph root/lease.");
            _dynamicSourceRoots.Add(tween, adopted.Root);
            // Publish the same retained root responsibility before child/death
            // reads can throw. Any partial join permanently remains Unknown.
            Volatile.Write(ref _sources, _sources.Append(tween).ToArray());
            if (_sourceJournal is not null && _sourceRootSet is not null)
                _sourceJournal.PromoteCurrentOperationTweenRoot(_sourceRootSet, adopted.Root, context.Frame);
            else if (_backend is NativeLocalTweenBackend)
                throw new InvalidOperationException("Dynamic Tween has no actual retained production source journal.");
            if (_backend is not INativeLocalTweenDeathBackend deathBackend)
                throw new InvalidOperationException("Dynamic Tween production owner has no native death backend.");
            var deathSource = new TweenDeathSource(_tweenDeathIssuer, this, deathBackend, adopted.Root, adopted.Lease, tween);
            var tracker = new NativeLocalTweenDeathTracker(_main, deathSource);
            if (!_tweenDeaths.TryAdd(tween, (deathSource, tracker)))
                throw new InvalidOperationException("Dynamic Tween death ownership was already registered.");
            tracker.CaptureLiveBaseline();
            AdoptDynamicChildEvents(tween, adopted.Root);
            if (_typedEventsAdopted) AdoptDynamicTypedEvent(tween, adopted.Root);
            Known();
        }
        finally { _promotingDynamicContext = null; _promotingDynamicSource = false; }
    }

    private bool HasCurrentDynamicListenerSource(OperationContext context)
        => (_listeners ?? _childEventListeners) is { } listeners
            && context.DynamicSource?.IsCurrentFor(listeners, this, context.Frame) == true;

    private bool HasDynamicSourceOrigin(OperationContext context)
        => HasCurrentDynamicListenerSource(context) || HasOwnedTweenParent(context.Parent);

    private void VerifyDynamicSourcePhysicalParent(OperationContext context)
    {
        if (_backend is NativeLocalTweenBackend && !HasCurrentDynamicListenerSource(context))
            ExactHookBroker.VerifyCurrentOperationParent(context.Frame, context.Parent?.Frame);
    }

    internal NativeOperationFrame RequireCurrentDynamicSourceAdoption(NativeLocalTweenBackend backend, long tween)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(backend, _backend) || _promotingDynamicContext is not { } context
            || !_promotingDynamicSource || !ReferenceEquals(context, _top) || !_reading
            || !context.Captured || context.Completed || context.Disposed || context.Before is null
            || SourceTween(context.Operation, context.Frame) != tween || !HasDynamicSourceOrigin(context)
            || !context.Before.Tweens.Any(row => row.Object == tween) || _sealRequested || _admissionSealed)
            throw new InvalidOperationException("The dynamic source backend lost its exact retained native Before adoption scope.");
        VerifyDynamicSourcePhysicalParent(context);
        return context.Frame;
    }

    public void VerifyTweenSourceOperationPromotion(NativeOperationFrame frame, NativeRuntime.RootHandle root)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(root);
        if (_promotingDynamicContext is not { } context || !_promotingDynamicSource
            || !ReferenceEquals(context, _top) || !_reading || !context.Captured || context.Completed || context.Disposed
            || context.Before is null || !SameFrame(context.Frame, frame) || !HasDynamicSourceOrigin(context)
            || !_dynamicSourceRoots.TryGetValue(SourceTween(context.Operation, frame), out var retained)
            || !ReferenceEquals(root, retained) || root.Target.ToInt64() != SourceTween(context.Operation, frame)
            || !context.Before.Tweens.Any(row => row.Object == root.Target.ToInt64()) || _sealRequested || _admissionSealed)
            throw new InvalidOperationException("Source journal promotion requires this exact private current native Before/root receipt.");
        VerifyDynamicSourcePhysicalParent(context);
    }

    private void AdoptCurrentManagerSourceBefore(OperationContext context)
    {
        if (context.Operation is not (NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween)
            || !_sources.Contains(context.Frame.Arg0.ToInt64())) return;
        if (_sourceRootSet?.TryGetAnchorKind(context.Frame.Self, out var role) == true && role == NativeTweenSourceObjectKind.Manager) return;
        if (_sealRequested || _admissionSealed)
            throw new InvalidOperationException("An unowned manager source cannot extend the sealed source responsibility set.");
        if (_backend is not INativeLocalTweenDynamicManagerSourceBackend source)
        {
            if (_backend is NativeLocalTweenBackend) throw new InvalidOperationException("The actual manager source has no rooted current-input borrowing backend.");
            return;
        }
        _promotingManagerContext = context;
        try
        {
            _promotingManagerRoots = source.BorrowCurrentManagerSourceRoots(this, context.Frame);
            VerifyTweenSourceOperationManagerPromotion(context.Frame, _promotingManagerRoots.Manager, _promotingManagerRoots.Tween);
            if (_sourceJournal is not null && _sourceRootSet is not null)
            {
                _sourceJournal.PromoteCurrentOperationManagerRoot(_sourceRootSet, _promotingManagerRoots.Manager, _promotingManagerRoots.Tween, context.Frame);
                RegisterJournalManagers();
            }
            else if (_backend is NativeLocalTweenBackend)
                throw new InvalidOperationException("Actual manager source promotion has no retained production source journal.");
        }
        finally { _promotingManagerRoots = null; _promotingManagerContext = null; }
    }

    internal void RequireCurrentManagerSourceBorrowing(NativeLocalTweenBackend backend, NativeOperationFrame frame)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(backend, _backend) || _promotingManagerContext is not { } context
            || !ReferenceEquals(context, _top) || !_reading || !context.Captured || context.Completed || context.Disposed
            || context.Before is null || !SameFrame(context.Frame, frame)
            || context.Operation is not (NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween)
            || !_sources.Contains(frame.Arg0.ToInt64()) || !context.Before.Tweens.Any(row => row.Object == frame.Arg0.ToInt64())
            || !context.Before.Managers.Any(row => row.Object == frame.Self.ToInt64()))
            throw new InvalidOperationException("Manager source borrowing has no exact retained owned-argument Before context.");
    }

    public void VerifyTweenSourceOperationManagerPromotion(NativeOperationFrame frame, NativeRuntime.RootHandle managerRoot, NativeRuntime.RootHandle tweenRoot)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(managerRoot); ArgumentNullException.ThrowIfNull(tweenRoot);
        if (_promotingManagerContext is not { } context || _promotingManagerRoots is not { } roots
            || !ReferenceEquals(context, _top) || !_reading || !context.Captured || context.Completed || context.Disposed
            || context.Before is null || !SameFrame(context.Frame, frame) || !ReferenceEquals(roots.Manager, managerRoot)
            || !ReferenceEquals(roots.Tween, tweenRoot) || managerRoot.Target != frame.Self || tweenRoot.Target != frame.Arg0
            || context.Operation is not (NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween)
            || !_sources.Contains(frame.Arg0.ToInt64()) || !context.Before.Tweens.Any(row => row.Object == frame.Arg0.ToInt64())
            || !context.Before.Managers.Any(row => row.Object == frame.Self.ToInt64()))
            throw new InvalidOperationException("Manager ownership promotion lost its private current owned-input graph/root scope.");
    }

    private bool IsCurrentDynamicSourceBaseline(NativeRuntime.RootHandle root, long tween)
        => _promotingDynamicSource && _promotingDynamicContext is { } context && ReferenceEquals(context, _top)
            && context.Captured && !context.Completed && !context.Disposed && context.Before is not null
            && SourceTween(context.Operation, context.Frame) == tween && HasDynamicSourceOrigin(context)
            && context.Before.Tweens.Any(row => row.Object == tween)
            && _dynamicSourceRoots.TryGetValue(tween, out var retained) && ReferenceEquals(root, retained);

    internal void RequireCurrentDynamicSourceJoin(NativeLocalUnityEventLedger listeners)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(listeners, _listeners ?? _childEventListeners) || _promotingDynamicContext is not { } context
            || !_dynamicSourceRoots.TryGetValue(SourceTween(context.Operation, context.Frame), out var root)
            || !IsCurrentDynamicSourceBaseline(root, SourceTween(context.Operation, context.Frame)))
            throw new InvalidOperationException("Dynamic child registration has no exact retained current source Before join.");
    }

    private bool HasOwnedTweenParent() => HasOwnedTweenParent(_top);

    private bool HasOwnedTweenParent(OperationContext? parent)
    {
        if (!_main.IsMainThread || parent is null || parent.ForeignMask || !parent.Captured || parent.Completed || parent.Disposed) return false;
        var sources = Volatile.Read(ref _sources);
        return parent.Operation switch {
            NativeTweenOperation.Play => sources.Contains(parent.Frame.Self.ToInt64()),
            NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween
                => sources.Contains(parent.Frame.Arg0.ToInt64()),
            _ => false
        };
    }

    private void AdoptDynamicChildEvents(long tween, NativeRuntime.RootHandle tweenRoot)
    {
        if (_childEventListeners is null) return;
        if (_backend is not INativeLocalTweenDynamicChildEventBackend childSource)
            throw new InvalidOperationException("Production Tween owner has no dynamic child event source.");
        _childEventListeners.RequireDynamicTweenChildAdoption(this, _launch);
        _borrowingChildEvents = true;
        try
        {
            childSource.BorrowDynamicTweenChildEvents(this, tween, (root, field, eventRoot) => {
                Known(); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(eventRoot);
                var role = ChildEventRole(field);
                var receiver = eventRoot.Target.ToInt64();
                if (!ReferenceEquals(root, tweenRoot) || root.Target.ToInt64() != tween || receiver == 0
                    || _childEventMemberships.Any(value => value.Tween == tween && value.Role == role))
                    throw new InvalidOperationException("Dynamic Tween child borrowing repeated a field or changed its exact source root.");
                var membership = new ChildEventMembership(_childEventIssuer, this, _childEventListeners, root, field,
                    eventRoot, tween, receiver, role);
                _childEventMemberships.Add(membership);
                _childEventListeners.AdoptDynamicTweenChildEvent(membership);
                Known();
            });
        }
        finally { _borrowingChildEvents = false; }
    }

    internal void AdoptControlledDynamicTweenChildEventsForTests(NativeLocalUnityEventLedger listeners,
        long tween, NativeRuntime.RootHandle tweenRoot)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(listeners); ArgumentNullException.ThrowIfNull(tweenRoot);
        if (_backend is NativeLocalTweenBackend || _launch is not null || _listeners is not null
            || !ReferenceEquals(_childEventListeners, listeners) || !_childEventsAdopted
            || tweenRoot.Target.ToInt64() != tween || _sources.Contains(tween)
            || _backend is not INativeLocalTweenDynamicChildEventBackend childSource)
            throw new InvalidOperationException("Controlled dynamic child adoption requires the exact initialized nonproduction owner/listener pair.");
        Volatile.Write(ref _sources, _sources.Append(tween).ToArray());
        _borrowingChildEvents = true;
        try
        {
            listeners.RequireControlledDynamicTweenChildAdoption(this);
            childSource.BorrowDynamicTweenChildEvents(this, tween, (root, field, eventRoot) => {
                Known(); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(eventRoot);
                var role = ChildEventRole(field);
                var receiver = eventRoot.Target.ToInt64();
                if (!ReferenceEquals(root, tweenRoot) || root.Target.ToInt64() != tween || receiver == 0
                    || _childEventMemberships.Any(value => value.Tween == tween && value.Role == role))
                    throw new InvalidOperationException("Controlled dynamic child borrowing changed its source or repeated a role.");
                var membership = new ChildEventMembership(_childEventIssuer, this, listeners, root, field,
                    eventRoot, tween, receiver, role);
                _childEventMemberships.Add(membership);
                listeners.AdoptControlledDynamicTweenChildEvent(membership);
                Known();
            });
        }
        catch (Exception error) { Fault("Controlled dynamic child adoption: " + error.Message); throw; }
        finally { _borrowingChildEvents = false; }
    }
}
