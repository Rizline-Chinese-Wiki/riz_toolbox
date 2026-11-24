using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackClockPublication
{
    internal bool SupportsObservedSuccessors => _fresh is { } fresh
        ? fresh.SupportsCrossRunHandoff && fresh.SupportsInitializationHandoff
        : _hook is not null && (_bootstrap is not null || _transfer is not null);
    private readonly NativePlaybackSingleReturnBridge.LogicalHandoff? _transfer;
    private NativePlaybackClockPublication(NativePlaybackGetTimeHookLease hook, NativePlaybackClockState initial,
        IntPtr caller, Action verify, NativePlaybackSingleReturnBridge.LogicalHandoff transfer,
        PlaybackSourceGroupLease? group, Action? verifyOperationSource)
    {
        _hook = hook; _current = initial; _permittedNullCaller = caller; _verifySource = verify; _transfer = transfer; _armed = false;
        _registered = this; _operationGroup = group; _verifyOperationSource = verifyOperationSource;
        VerifyOperationConfiguration();
        if (initial.Revision != transfer.AdmittedRevision)
            throw new InvalidOperationException("Only the registered native admitted baseline can initialize a successor publication.");
        try
        {
            verify(); _attempted = Authority(initial); transfer.Prepare(_attempted);
            verify(); transfer.Read(4); _owned = _attempted; _attempted = null;
        }
        catch { WithdrawOwned(); throw; }
    }
    internal static NativePlaybackClockPublication PrepareTransferred(NativePlaybackGetTimeHookLease hook,
        NativePlaybackClockState initial, IntPtr caller, Action verify, NativePlaybackSingleReturnBridge.LogicalHandoff transfer,
        PlaybackSourceGroupLease? group = null, Action? verifyOperationSource = null)
        => new(hook, initial, caller, verify, transfer, group, verifyOperationSource);

    internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginInitializationHandoff(IntPtr initializer)
    {
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_fresh is not null) return _fresh.BeginInitializationHandoff(initializer);
            var exact = _owned ?? throw new InvalidOperationException("No retained exact publication owns initialization handoff.");
            if (!OwnsPublication(exact) || _hook is null)
                throw new InvalidOperationException("The predecessor publication is no longer owned by its exact hook.");
            return _hook.BeginInitializationHandoff(exact, initializer);
        }
    }
    internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginHandoff()
    {
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_fresh is not null) return _fresh.BeginHandoff();
            var exact = _owned ?? throw new InvalidOperationException("No exact retained publication owns a logical handoff.");
            if (!OwnsPublication(exact) || _hook is null)
                throw new InvalidOperationException("The exact predecessor publication is no longer this hook's owner.");
            return _hook.BeginLogicalHandoff(exact);
        }
    }

    internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginCrossRunInitializationHandoff(
        NativePlaybackStockSourceHub.CompletedInitializationReceipt initializer)
    {
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_fresh is not null) return _fresh.BeginCrossRunInitializationHandoff(initializer);
            var exact = _owned ?? throw new InvalidOperationException("No retained publication owns the observed initializer handoff.");
            if (!OwnsPublication(exact) || _hook is null)
                throw new InvalidOperationException("The observed initializer belongs to another physical publication owner.");
            return _hook.BeginCrossRunInitializationHandoff(exact, initializer);
        }
    }
}
