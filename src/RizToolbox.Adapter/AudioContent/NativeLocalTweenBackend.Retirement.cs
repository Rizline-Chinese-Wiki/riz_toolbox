using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend : INativeLocalTweenRetirementBackend
{
    private bool _retirementBindingsWithdrawn, _retirementRootReleaseStarted, _retirementRootsReleased;
    private readonly HashSet<int> _retirementRootFreeAttempts = new();
    public bool TweenBackendRootsReleased { get { _main.VerifyAccess(); return _retirementRootsReleased && !CleanupUnknown; } }

    public void VerifyTweenRetirementOwner(NativeLocalTweenWorkOwner owner)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_owner, owner) || !_ready || CleanupUnknown || owner.ObservationAuthorityUnknown)
            throw new InvalidOperationException("Tween backend retirement belongs to another or Unknown retained owner.");
    }
    public NativeTweenAdmission.RetirementBatch PrepareTweenRetirement(NativeLocalTweenWorkOwner owner)
    {
        Check(); VerifyTweenRetirementOwner(owner); owner.RequireAdmissionSourceSet(_sourceClaims.Keys);
        return (_admissionAuthority ?? throw new InvalidOperationException("No native Tween admission authority.")).PrepareRetirement(owner);
    }
    public NativeTweenRetirementContext BindTweenRetirementContext(NativeLocalTweenWorkOwner owner)
    { Check(); VerifyTweenRetirementOwner(owner); return NativeTweenRetirementContext.BindProduction(owner, _main.VerifyAccess); }
    public void WithdrawTweenObservationBindings(NativeLocalTweenWorkOwner owner)
    {
        VerifyTweenRetirementOwner(owner);
        if (_retirementBindingsWithdrawn || _bindings.Count != TweenOperationProfile.All.Count || _typedEventBinding is null)
            throw new InvalidOperationException("The complete six Tween observation sinks must be withdrawn exactly once.");
        // Dispose removes only this run's sinks. The process original and other
        // owners' sinks continue, including foreign originals during the Cut.
        _retirementBindingsWithdrawn = true;
        try { RetireSingleSubscription(owner); _typedEventBinding.Dispose(); foreach (var binding in _bindings) binding.Dispose(); }
        catch { CleanupUnknown = true; throw; }
        VerifyTweenCompletionBindingsWithdrawn(owner);
    }
    public void VerifyTweenCompletionBindingsWithdrawn(NativeLocalTweenWorkOwner owner)
    {
        VerifyTweenRetirementOwner(owner);
        if (!_retirementBindingsWithdrawn || _bindings.Count != TweenOperationProfile.All.Count
            || _bindings.Any(binding => !binding.IsDisposed) || _typedEventBinding?.IsDisposed != true || !SingleSubscriptionWithdrawnAndDrained)
            throw new InvalidOperationException("This exact run still owns an enrolled Tween observation sink.");
    }
    public void ReleaseTweenBackendRoots(NativeLocalTweenWorkOwner owner, NativeTweenRetirementContext.Cut cut)
    {
        VerifyTweenCompletionBindingsWithdrawn(owner); cut.VerifyCurrent();
        if (!cut.Committed || !ReferenceEquals(cut.Owner, owner) || _retirementRootReleaseStarted || !cut.Production)
            throw new InvalidOperationException("Backend roots require their one exact committed production cut.");
        _retirementRootReleaseStarted = true;
        try
        {
            for (var slot = 0; slot < _roots.Length; ++slot)
            {
                if (_roots[slot] is not { } root) continue;
                cut.VerifyCurrent(); VerifyTweenCompletionBindingsWithdrawn(owner);
                if (!_retirementRootFreeAttempts.Add(slot)) throw new InvalidOperationException("A Tween root Free was already attempted.");
                root.Dispose(); // The recorded attempt survives an external Free exception or reentry.
                _roots[slot] = null;
                cut.VerifyCurrent();
            }
            _slots.Clear(); _sourceClaims.Clear();
            _retirementRootsReleased = true;
        }
        catch { CleanupUnknown = true; throw; }
    }
}
