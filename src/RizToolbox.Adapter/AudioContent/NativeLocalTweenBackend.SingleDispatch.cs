using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend
{
    private NativeTweenSingleEventBridge? _singleEventBridge;
    private NativeTweenSingleEventSubscriptions<NativeTweenSingleEventBridge.Delivery>.Subscription? _singleEventSubscription;

    private void BindSingleExecutingSource(NativeLocalTweenWorkOwner owner)
    {
        _singleEventBridge = NativeTweenSingleEventBridge.AdoptReady();
        _singleEventBridge.RequireProduction();
        _singleEventSubscription = _singleEventBridge.Subscribe(delivery =>
        {
            if (!TypedEventBinding.WithCurrentSingleContext(delivery, context =>
            {
                var actual = context as NativeTweenSingleEventScope<NativeTweenSingleEventBridge.Delivery>
                    ?? throw new InvalidOperationException("Single delivery has no captured scope for this exact backend subscription.");
                NativeTweenSingleDeliveryReader.Observe(owner, actual, delivery, _api, _module,
                    _typedEventTarget!, AdoptPublishedInput, Root, RequireKnown);
            })) throw new UnsupportedSignatureException("A captured Single callback lost its exact retained physical cleanup context.");
        }, reason =>
        {
            CleanupUnknown = true;
            _typedEventBinding?.SingleEventFault(reason);
            owner.ObserveTypedSourceFault(reason);
        });
    }

    private void RetireSingleSubscription(NativeLocalTweenWorkOwner owner)
    {
        if (!ReferenceEquals(_owner, owner) || _singleEventSubscription is null)
            throw new InvalidOperationException("Single subscription withdrawal requires its exact retained Tween backend.");
        _singleEventSubscription.Dispose();
    }
    private bool SingleSubscriptionWithdrawnAndDrained
        => _singleEventSubscription is { IsWithdrawn: true, IsDrained: true };
}
