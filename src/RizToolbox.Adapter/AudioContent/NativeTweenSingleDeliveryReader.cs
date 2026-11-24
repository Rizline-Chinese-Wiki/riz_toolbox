using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

// Only a live native Delivery can enter this reader. The physical broker associates its
// retained scope before entry; the work owner then rechecks its exact current typed parent.
internal static class NativeTweenSingleDeliveryReader
{
    internal static void Observe(NativeLocalTweenWorkOwner owner,
        NativeTweenSingleEventScope<NativeTweenSingleEventBridge.Delivery> scope,
        NativeTweenSingleEventBridge.Delivery delivery, NativeRuntime api, IntPtr module,
        VerifiedHookTarget eventTarget, Func<IntPtr, long> adoptPublishedInput,
        Func<long, NativeRuntime.RootHandle> root, Action requireKnown)
    {
        delivery.VerifyCurrent();
        var frame = delivery.Observation.Frame;
        if (frame.Stage is NativeTweenSingleEventStage.Returned or NativeTweenSingleEventStage.Abnormal)
        {
            // Completion only consumes retained Before roots, including after Unknown.
            owner.CompleteTypedDispatch(scope.Inner, frame); return;
        }
        var batch = NativeTweenSingleEventRootBatch.Capture(frame, adoptPublishedInput);
        requireKnown(); delivery.VerifyCurrent();
        NativeRuntime.RootHandle Publish(IntPtr pointer) => root(adoptPublishedInput(pointer));
        var executing = api.ReadSingleExecutingList(root(batch.Event), root(batch.List), eventTarget, Publish);
        requireKnown(); delivery.VerifyCurrent();
        if (frame.Stage == NativeTweenSingleEventStage.Mid)
            owner.CaptureTypedExecutingList(scope.Inner, frame, executing);
        else
        {
            var target = api.ReadSingleDispatch(delivery.Observation, root(batch.Invokable),
                batch.ArgumentArray == 0 ? null : root(batch.ArgumentArray), Publish, module);
            requireKnown(); delivery.VerifyCurrent();
            owner.CaptureTypedDispatch(scope.Inner, frame, executing, target);
        }
    }
}
