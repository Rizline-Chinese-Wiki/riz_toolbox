using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

// Diagnostic input to the owner's private transfer issuer, never a completion ticket.
internal sealed record NativeTweenTypedPlayConsumer(long Invokable, long Action, long Tween, long Method,
    long Entry, long InvokeImplementation, long MethodCode);

internal interface INativeTweenTypedListenerEffectBackend
{
    NativeTweenTypedPlayConsumer? ReadTypedPlayConsumer(NativeLocalTweenWorkOwner owner, IDisposable context,
        NativeTweenSingleEventFrame frame, NativeSingleDispatchTarget target);
}

internal sealed partial class NativeLocalTweenBackend : INativeTweenTypedListenerEffectBackend
{
    NativeTweenTypedPlayConsumer? INativeTweenTypedListenerEffectBackend.ReadTypedPlayConsumer(
        NativeLocalTweenWorkOwner owner, IDisposable context, NativeTweenSingleEventFrame frame, NativeSingleDispatchTarget target)
    {
        Check(); owner.RequireTypedListenerEffectRead(this, context, frame);
        if (!ReferenceEquals(owner, _owner) || !_ready || target.Site != 1 || frame.Site != 1) return null;
        var result = NativeTweenTypedPlayConsumerReader.Read(_api, _module, frame, target,
            _targets[NativeTweenOperation.Play], _tweenClass, _delegates, _delegateTarget,
            Root, Reference, owner.OwnsTweenSource, RequireKnown);
        owner.RequireTypedListenerEffectRead(this, context, frame);
        return result;
    }
}
