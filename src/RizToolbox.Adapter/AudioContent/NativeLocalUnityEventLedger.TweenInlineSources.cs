using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalUnityEventLedger
{
    private NativeLocalTweenWorkOwner? _tweenInlineWork;
    private long _tweenInlineMaskGeneration;
    private readonly List<NativeLocalTweenWorkOwner.InlineEventSource> _tweenInlineSources = new();

    internal void WithdrawTweenInlineWork(NativeLocalTweenWorkOwner work, NativeTweenRetirementContext.Cut cut)
    {
        _main.VerifyAccess(); cut.VerifyCurrent();
        if (!cut.Committed || !ReferenceEquals(cut.Owner, work) || !work.RetiredRootsUnavailable || Unknown
            || !ReferenceEquals(_tweenInlineWork, work) || _current is not null || _physicalDepth != 0)
            throw new InvalidOperationException("Inline Tween observation can withdraw only at its same-owner committed native cut.");
        _tweenInlineWork = null;
    }

    internal void AttachTweenInlineWork(NativeLocalTweenWorkOwner work)
    {
        _main.VerifyAccess(); Guard(); ArgumentNullException.ThrowIfNull(work);
        if (_tweenInlineWork is not null && !ReferenceEquals(_tweenInlineWork, work)
            || _launch is null || !work.IsFor(_launch, this))
            throw new InvalidOperationException("Tween inline attachment crossed the actual retained launch/listener owner.");
        work.RequireActualTweenInlineListenerOwner(this); _tweenInlineWork = work;
    }
    internal bool ShouldObserveTweenInlineInvoke(long receiver)
    { _main.VerifyAccess(); return !Unknown && _tweenInlineWork?.ShouldObserveActualInlineInvoke(receiver) == true; }
    internal void ObserveActualTweenInlineEntry(InvokeContext context, NativeVoidFrame frame)
    {
        if (_tweenInlineWork is not { } work) return;
        if (!RequireActualTweenInlineContext(work, context, frame)) return;
        // Unregistered child events use the ledger's actual physical mask. Give
        // each such real native entry a unique generation before binding it;
        // mask reuse must not make an old source token match a later Invoke.
        if (context._record is null) context._generation = checked(++_tweenInlineMaskGeneration);
        var source = CaptureActualTweenInlineSource(work, context, frame);
        if (source is not null) _tweenInlineSources.Add(source);
    }
    // Root connects this only after CaptureNativeInvoke has established its
    // actual current physical context. A copied event/list snapshot cannot enter.
    internal NativeLocalTweenWorkOwner.InlineEventSource? CaptureActualTweenInlineSource(
        NativeLocalTweenWorkOwner work, InvokeContext context, NativeVoidFrame frame)
    {
        if (!RequireActualTweenInlineContext(work, context, frame)) return null;
        var binding = work.AssociateActualInlineInvoke(this, context, frame);
        if (binding is not null && !binding.IsFor(work, this, context))
            throw new InvalidOperationException("The inline source association crossed its exact Tween/event owners.");
        return binding;
    }
    internal bool RequireActualTweenInlineContext(NativeLocalTweenWorkOwner work, InvokeContext context, NativeVoidFrame frame)
    {
        _main.VerifyAccess(); Guard(); ArgumentNullException.ThrowIfNull(work); ArgumentNullException.ThrowIfNull(context);
        if (!frame.IsValid || !ReferenceEquals(_current, context) || !context._native || !context._active || context._after
            || PhysicalTop != frame.Self.ToInt64() || context._receiver != frame.Self.ToInt64()
            || context._nativeCaller != frame.Caller.ToInt64() || context._nativeMethod != frame.Method.ToInt64())
            throw new InvalidOperationException("Inline Tween association requires the actual current native UnityEvent Invoke generation.");
        return _launch is not null && work.IsFor(_launch, this);
    }
}
