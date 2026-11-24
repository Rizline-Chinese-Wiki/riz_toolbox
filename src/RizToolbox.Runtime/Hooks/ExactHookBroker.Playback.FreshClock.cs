namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal NativePlaybackSingleReturnBridge.FreshClockReceipt PrepareFreshClock(
            NativePlaybackSingleReturnBridge.FreshSourceReceipt source,
            NativePlaybackStockOccurrenceProducer.ExecutionReceipt baseline, NativePlaybackSingleReturnAuthority initial)
        {
            lock (_gate)
            {
                VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
                if (initial.Identity.VerifiedGetTimeMethod != _target.MethodInfo)
                    throw new UnsupportedSignatureException("The Fresh Clock publisher belongs to another GetTime MethodInfo.");
                return _bridge!.PrepareFreshClock(source, baseline, initial);
            }
        }
    }
}

internal sealed partial class NativePlaybackGetTimeHookLease
{
    internal NativePlaybackSingleReturnBridge.FreshClockReceipt PrepareFreshClock(
        NativePlaybackSingleReturnBridge.FreshSourceReceipt source,
        NativePlaybackStockOccurrenceProducer.ExecutionReceipt baseline, NativePlaybackSingleReturnAuthority initial)
        => _owner.PrepareFreshClock(source, baseline, initial);
}
