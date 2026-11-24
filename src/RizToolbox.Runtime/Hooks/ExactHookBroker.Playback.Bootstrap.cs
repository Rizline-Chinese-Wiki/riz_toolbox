namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal NativePlaybackSingleReturnBridge.DormantBootstrap PrepareDormant(NativePlaybackSingleReturnAuthority initial)
        {
            lock (_gate)
            {
                VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
                if (initial.Identity.VerifiedGetTimeMethod != _target.MethodInfo)
                    throw new UnsupportedSignatureException("The dormant publisher belongs to another GetTime MethodInfo.");
                var receipt = _bridge!.PrepareDormant(initial);
                try { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); return receipt; }
                catch { try { receipt.Cancel(); } catch { } throw; }
            }
        }
    }
}
