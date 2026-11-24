using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeTweenInlineEventSnapshot(long Observation, ulong Transaction, ulong BeforeSequence,
    ulong AfterSequence, NativeTweenSourceSite Site, long Event, long Tween, long? Occurrence,
    bool NativeCallReturned, bool InvokeAssociated, bool Production, bool EffectsEnded);

internal sealed partial class NativeLocalTweenWorkOwner
{
    private readonly object _inlineSourceIssuer = new();
    private readonly List<InlineSourceEvent> _inlineSourceEvents = new();

    internal void RequireActualTweenInlineListenerOwner(NativeLocalUnityEventLedger ledger)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(ledger);
        if (!ReferenceEquals(_listeners, ledger) || _launch is null || !ledger.IsFor(_launch)
            || _sourceJournal is null || !_sourceJournal.Production)
            throw new InvalidOperationException("Tween inline observation requires this actual production journal and its retained launch/listener owners.");
    }
    internal bool ShouldObserveActualInlineInvoke(long receiver)
    {
        _main.VerifyAccess();
        if (receiver == 0 || Unknown || _sourceJournal is null || _listeners is null
            || _top is not { Captured: true, Completed: false, Disposed: false } current || !_operations.Contains(current)) return false;
        PollSourceJournal();
        var sources = _inlineSourceEvents.Where(value => ReferenceEquals(value.Operation.Context, current)
            && value.Before.Container.ToInt64() == receiver && value.AfterSequence == 0 && value.InvokeOwner is null
            && value.Before.ThreadId == NativeEngineInlinePlatform.CurrentOsThreadId
            && value.Operation.PairOrder.LastOrDefault() == value.Before.Transaction
            && value.Operation.Pairs.ContainsKey(value.Before.Transaction)).ToArray();
        if (sources.Length > 1)
        { Fault("An actual Tween inline receiver has multiple private current source CALLs."); throw new InvalidOperationException("Ambiguous actual inline source receiver."); }
        return sources.Length == 1;
    }

    private sealed class InlineSourceEvent
    {
        private readonly NativeLocalTweenWorkOwner _owner;
        internal readonly SourceOperation Operation;
        internal readonly NativeTweenSourceRecord Before;
        internal readonly SourceOccurrence? Occurrence;
        internal ulong AfterSequence;
        internal NativeLocalUnityEventLedger? InvokeOwner;
        internal NativeLocalUnityEventLedger.InvokeContext? InvokeContext;
        internal long InvokeGeneration;
        internal InlineSourceEvent(NativeLocalTweenWorkOwner owner, SourceOperation operation, NativeTweenSourceRecord before, SourceOccurrence? occurrence)
            => (_owner, Operation, Before, Occurrence) = (owner, operation, before, occurrence);
        internal NativeTweenInlineEventSnapshot Snapshot() => new(Operation.Context.Observation, Before.Transaction,
            Before.EventSequence, AfterSequence, Before.Site, Before.Container.ToInt64(), Before.Tween.ToInt64(),
            Occurrence?.Id, AfterSequence != 0, InvokeOwner is not null, Operation.Production, _owner.ReadActualChildEffectsEnded(this));
    }
    // Only the parent Tween owner issues this binding while an actual source
    // pair and this exact UnityEvent Invoke context are both physically open.
    // It remains an issuer association; it has no effects-completed method.
    internal sealed class InlineEventSource
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly InlineSourceEvent _source;
        internal InlineEventSource(object issuer, NativeLocalTweenWorkOwner owner, object source)
        {
            if (!ReferenceEquals(issuer, owner._inlineSourceIssuer) || source is not InlineSourceEvent actual
                || !owner._inlineSourceEvents.Contains(actual))
                throw new InvalidOperationException("Inline source binding requires its parent's private retained issuer.");
            (_issuer, _owner, _source) = (issuer, owner, actual);
        }
        internal NativeTweenInlineEventSnapshot Snapshot => _source.Snapshot();
        internal bool IsFor(NativeLocalTweenWorkOwner owner, NativeLocalUnityEventLedger ledger,
            NativeLocalUnityEventLedger.InvokeContext context)
            => ReferenceEquals(_issuer, owner._inlineSourceIssuer) && ReferenceEquals(_owner, owner)
                && ReferenceEquals(_source.InvokeOwner, ledger) && ReferenceEquals(_source.InvokeContext, context)
                && _source.InvokeGeneration == context._generation;
    }
    private void BeginInlineSourceEvent(SourceOperation operation, NativeTweenSourceRecord record, SourceOccurrence? occurrence)
    {
        if (record.Container == IntPtr.Zero || _sourceRootSet?.HasRetainedObject(record.Container.ToInt64()) != true
            || record.Tween == IntPtr.Zero || !_sources.Contains(record.Tween.ToInt64()))
            throw new InvalidOperationException("Inline event source has no actual pre-rooted owned Tween and event receiver.");
        if (_inlineSourceEvents.Any(value => value.Before.Transaction == record.Transaction && value.Before.Epoch == record.Epoch))
            throw new InvalidOperationException("Inline source call repeated its private transaction identity.");
        _inlineSourceEvents.Add(new(this, operation, record, occurrence));
    }
    private void CompleteInlineSourceEvent(SourceOperation operation, NativeTweenSourceRecord after)
    {
        var value = _inlineSourceEvents.SingleOrDefault(value => ReferenceEquals(value.Operation, operation)
            && value.Before.Transaction == after.Transaction);
        if (value is null || value.AfterSequence != 0 || value.Before.Container != after.Container || value.Before.Tween != after.Tween)
            throw new InvalidOperationException("Inline event source After crossed its actual private call transaction.");
        if (value.InvokeOwner is not null && (value.InvokeContext is not { _after: true, _active: false } context
            || context._generation != value.InvokeGeneration))
            throw new InvalidOperationException("The actual inline UnityEvent Invoke has not completed and disposed its precise generation.");
        value.AfterSequence = after.EventSequence;
    }
    internal InlineEventSource? AssociateActualInlineInvoke(NativeLocalUnityEventLedger ledger,
        NativeLocalUnityEventLedger.InvokeContext context, NativeVoidFrame frame)
    {
        VerifyTweenSourceOwner(); ArgumentNullException.ThrowIfNull(ledger); ArgumentNullException.ThrowIfNull(context);
        if (_listeners is null || !ReferenceEquals(_listeners, ledger) || _sourceJournal is null) return null;
        if (!ledger.RequireActualTweenInlineContext(this, context, frame)) return null;
        if (_top is not { Disposed: false, Completed: false } actualParent || !_operations.Contains(actualParent)) return null;
        PollSourceJournal();
        var candidates = _inlineSourceEvents.Where(value => ReferenceEquals(value.Operation.Context, actualParent)
            && value.AfterSequence == 0 && value.Before.Container == frame.Self
            // The original five-byte CALL is replayed directly after the Pre
            // preserving-entry return. Its real child caller lives in relay
            // code; SourcePc remains the original publication-bound site.
            && value.Before.RelayReturnPc.ToInt64() + 5 == frame.Caller.ToInt64()
            && value.Before.ThreadId == NativeEngineInlinePlatform.CurrentOsThreadId
            && value.Operation.PairOrder.LastOrDefault() == value.Before.Transaction
            && value.Operation.Pairs.ContainsKey(value.Before.Transaction)).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1) throw new InvalidOperationException("Actual Invoke has multiple uncompleted inline source issuers.");
        var value = candidates[0];
        if (value.InvokeOwner is not null) throw new InvalidOperationException("Inline source call was associated with a second Invoke generation.");
        value.InvokeOwner = ledger; value.InvokeContext = context; value.InvokeGeneration = context._generation;
        return new(_inlineSourceIssuer, this, value);
    }
    private IReadOnlyList<NativeTweenInlineEventSnapshot> ReadInlineSourceEvents()
        => Array.AsReadOnly(_inlineSourceEvents.Select(value => value.Snapshot()).ToArray());
}
