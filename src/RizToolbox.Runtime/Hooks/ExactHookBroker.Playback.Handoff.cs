namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal void BindLogicalSource(NativePlaybackStockSourceHub hub)
        { lock (_gate) { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); _bridge!.BindLogicalSource(hub); } }
        internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginInitializationHandoff(NativePlaybackSingleReturnAuthority exact, IntPtr initializer)
        { lock (_gate) { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); return _bridge!.BeginInitializationHandoff(exact, initializer); } }
        internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginLogicalHandoff(NativePlaybackSingleReturnAuthority exact)
        { lock (_gate) { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); return _bridge!.BeginLogicalHandoff(exact); } }
        internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginCrossRunInitializationHandoff(NativePlaybackSingleReturnAuthority exact,
            NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer)
        { lock (_gate) { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); return _bridge!.BeginCrossRunInitializationHandoff(exact, initializer); } }
    }
    internal sealed partial class PlaybackSourceInstallGroup
    {
        private bool _logicalBound;
        internal void VerifyInitializationCapture(NativePlaybackSingleReturnBridge.LogicalHandoff handoff, long player, long source, long clip)
        { VerifyCurrent(); handoff.VerifyInitializationCapture(_hub!, player, source, clip); }
        internal void BindLogicalHandoff()
        {
            VerifyCurrent();
            if (_logicalBound) return;
            _getTime!.BindLogicalSource(_hub!); _logicalBound = true;
        }
    }
}

internal sealed partial class NativePlaybackGetTimeHookLease
{
    internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginInitializationHandoff(NativePlaybackSingleReturnAuthority exact, IntPtr initializer)
        => _owner.BeginInitializationHandoff(exact, initializer);
    internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginLogicalHandoff(NativePlaybackSingleReturnAuthority exact)
        => _owner.BeginLogicalHandoff(exact);
    internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginCrossRunInitializationHandoff(NativePlaybackSingleReturnAuthority exact,
        NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer)
        => _owner.BeginCrossRunInitializationHandoff(exact, initializer);
}
