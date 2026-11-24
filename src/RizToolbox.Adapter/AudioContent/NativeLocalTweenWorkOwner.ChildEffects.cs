using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenWorkOwner
{
    private bool ReadActualChildEffectsEnded(InlineSourceEvent actual)
    {
        if (Unknown || actual.AfterSequence == 0 || actual.InvokeOwner is not { } listeners) return false;
        var source = _registeredChildSources.SingleOrDefault(pair => ReferenceEquals(pair.Value.Actual, actual)).Key;
        return source is not null && listeners.ReadActualTweenChildEffects(this, source);
    }
    internal bool HasReturnedActualChildSource(NativeLocalUnityEventLedger listeners, InlineEventSource source,
        ChildEventMembership membership, long receiver, long generation)
    {
        _main.VerifyAccess();
        return !Unknown && _registeredChildSources.TryGetValue(source, out var retained)
            && ReferenceEquals(retained.Membership, membership) && membership.IsFor(this, listeners)
            && ReferenceEquals(retained.Actual.InvokeOwner, listeners) && retained.Actual.InvokeGeneration == generation
            && retained.Actual.Before.Container.ToInt64() == receiver && retained.Actual.Before.Tween.ToInt64() == membership.Tween
            && retained.Actual.AfterSequence > retained.Actual.Before.EventSequence;
    }

    // Controlled integration still crosses a reader-issued source pair and an
    // actual broker Invoke context. It cannot attach to a production owner or
    // issue any production child/root-free authority.
    internal InlineEventSource? AssociateControlledChildInvokeForTests(NativeLocalUnityEventLedger listeners,
        NativeLocalUnityEventLedger.InvokeContext context, NativeVoidFrame frame)
    {
        VerifyTweenSourceOwner();
        listeners.RequireControlledRegisteredTweenChildContextForTests(this, context, frame);
        if (_backend is NativeLocalTweenBackend || _launch is not null || _listeners is not null
            || !ReferenceEquals(_childEventListeners, listeners) || _sourceJournal is null || _sourceJournal.Production
            || _top is not { Captured: true, Completed: false, Disposed: false } current || !_operations.Contains(current))
            throw new InvalidOperationException("Controlled child association cannot supply production authority or a copied parent context.");
        PollSourceJournal();
        var matches = _inlineSourceEvents.Where(value => ReferenceEquals(value.Operation.Context, current)
            && value.Before.Container == frame.Self && value.Before.RelayReturnPc.ToInt64() + 5 == frame.Caller.ToInt64()
            && value.Before.ThreadId == NativeEngineInlinePlatform.CurrentOsThreadId && value.AfterSequence == 0
            && value.Operation.PairOrder.LastOrDefault() == value.Before.Transaction && value.Operation.Pairs.ContainsKey(value.Before.Transaction)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || matches[0].InvokeOwner is not null) throw new InvalidOperationException("Controlled actual source CALL has ambiguous or reused child association.");
        var actual = matches[0]; actual.InvokeOwner = listeners; actual.InvokeContext = context; actual.InvokeGeneration = context._generation;
        return new(_inlineSourceIssuer, this, actual);
    }
}
