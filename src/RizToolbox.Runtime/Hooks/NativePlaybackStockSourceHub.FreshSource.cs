namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    internal void RequireFreshSourceProtocol()
    {
        VerifyThread(); _image.Verify();
        if (Export<VersionNative>("RtPlaybackFreshSourceVersion")() != 1
            || Export<VersionNative>("RtPlaybackFreshSourceDiagnosticVersion")() != 2)
            throw new UnsupportedSignatureException("The source helper cannot issue a joint native fresh-source identity.");
        _ = System.Runtime.InteropServices.NativeLibrary.GetExport(_library, "RtPlaybackSourceCoordinateFresh");
        _ = System.Runtime.InteropServices.NativeLibrary.GetExport(_library, "RtPlaybackSourceCoordinateFreshWithDiagnostic");
    }
    internal PlayerWatch StageFreshSource(object owner, NativeRuntime api, NativeRuntime.RootHandle player,
        NativeRuntime.RootHandle source, NativeRuntime.RootHandle progress, NativeRuntime.RootHandle clip)
    {
        RequireFreshSourceProtocol(); RequireFreshAdmission();
        PrepareUnityBackingWatch(api, source, clip);
        var watch = StagePlayerWatch(owner, api, player, source)
            ?? throw new UnsupportedSignatureException("Fresh capture requires an actual constructor watch.");
        StageProgressWatch(owner, api, progress, watch);
        StageUnityBackingWatch(owner, api, source, clip, watch);
        return watch; // Pending only. The joint native issuer performs activation.
    }
}

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal void RequireFreshSourceProtocol() { lock (_gate) _bridge!.RequireFreshSourceProtocol(); }
        internal NativePlaybackSingleReturnBridge.FreshSourceReceipt PrepareFreshSource(
            NativePlaybackStockSourceHub source, NativePlaybackStockSourceHub.PlayerWatch watch)
        { lock (_gate) return _bridge!.PrepareFreshSource(source, watch); }
    }
    internal sealed partial class PlaybackSourceInstallGroup
    {
        private object? _stockFreshOwner;
        private NativePlaybackSingleReturnBridge.FreshSourceReceipt? _stockFreshReceipt;
        internal void RequireStockFreshSource(NativeRuntime.NativePlaybackStockTargets stock)
        {
            VerifyStockIdentity(stock);
            if (OwnedFixture || _foundation is null || FoundationEpoch == 0 || !_targets.HasUnityBacking
                || !_targets.HasLowerTime || !_targets.HasEnginePitch || !_targets.HasProgressResource || _targets.SourceRoleCount != 22)
                throw new UnsupportedSignatureException("Formal fresh capture requires this exact stock group and its retained foundation membership.");
            _getTime!.RequireFreshSourceProtocol(); _hub!.RequireFreshSourceProtocol();
        }
        internal NativePlaybackSingleReturnBridge.FreshSourceReceipt StageStockFreshSource(object owner, NativeRuntime api,
            NativeRuntime.NativePlaybackStockTargets stock, NativeRuntime.RootHandle player, NativeRuntime.RootHandle source,
            NativeRuntime.RootHandle progress, NativeRuntime.RootHandle clip)
        {
            ArgumentNullException.ThrowIfNull(owner); RequireStockFreshSource(stock);
            if (_stockFreshOwner is not null) throw new UnsupportedSignatureException("A stock physical group admits one initial fresh candidate, never a replacement retry graph.");
            _stockFreshOwner = owner; // Retain before any native stage can fail.
            var watch = _hub!.StageFreshSource(owner, api, player, source, progress, clip);
            return _stockFreshReceipt = _getTime!.PrepareFreshSource(_hub, watch);
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal void RequireStockFreshSource(NativeRuntime.NativePlaybackStockTargets stock) => _owner.RequireStockFreshSource(stock);
    internal NativePlaybackSingleReturnBridge.FreshSourceReceipt StageStockFreshSource(object owner, NativeRuntime api,
        NativeRuntime.NativePlaybackStockTargets stock, NativeRuntime.RootHandle player, NativeRuntime.RootHandle source,
        NativeRuntime.RootHandle progress, NativeRuntime.RootHandle clip)
        => _owner.StageStockFreshSource(owner, api, stock, player, source, progress, clip);
}
