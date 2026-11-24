using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal interface INativeTweenChildEventBackend
{
    long AdoptBorrowedTweenChildEventRoot(NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner.ChildEventMembership membership);
    void RetainTweenChildEventSlot(NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner.ChildEventMembership membership, int slot, long identity);
}

internal sealed partial class NativeLocalUnityEventBackend : INativeTweenChildEventBackend
{
    private readonly Dictionary<NativeLocalTweenWorkOwner.ChildEventMembership, int?> _tweenChildRootResponsibilities = new();

    long INativeTweenChildEventBackend.AdoptBorrowedTweenChildEventRoot(NativeLocalUnityEventLedger owner,
        NativeLocalTweenWorkOwner.ChildEventMembership membership)
    {
        Check(); membership.RequireFor(owner);
        if (!ReferenceEquals(_owner, owner) || !membership.Production)
            throw new InvalidOperationException("Actual child roots require this exact retained listener backend.");
        if (_launch.Read().ActivationAttempted) membership.Owner.RequireCurrentDynamicSourceJoin(owner);
        try
        {
            if (!_tweenChildRootResponsibilities.TryAdd(membership, null))
                throw new InvalidOperationException("The same child borrower was adopted twice.");
            var borrowed = membership.BorrowEventRoot(owner);
            // The lender already holds this object. Hold records this backend's
            // independent root before checking its class or reading its graph.
            return Hold(Native(() => borrowed.Target), _eventClass);
        }
        catch { CleanupUnknown = true; throw; }
    }
    void INativeTweenChildEventBackend.RetainTweenChildEventSlot(NativeLocalUnityEventLedger owner,
        NativeLocalTweenWorkOwner.ChildEventMembership membership, int slot, long identity)
    {
        Check(); membership.RequireFor(owner);
        try
        {
            if (!ReferenceEquals(_owner, owner) || !_tweenChildRootResponsibilities.TryGetValue(membership, out var prior)
                || prior is not null || slot < 0 || slot >= MaximumEvents || identity != membership.Event
                || !_rootSlots.ContainsKey(identity) || _events[slot] != 0 && _events[slot] != identity)
                throw new InvalidOperationException("Child event slot crossed its independent retained root or reservation.");
            _events[slot] = identity; _eventCount = Math.Max(_eventCount, slot + 1);
            _tweenChildRootResponsibilities[membership] = slot;
        }
        catch { CleanupUnknown = true; throw; }
    }
}
