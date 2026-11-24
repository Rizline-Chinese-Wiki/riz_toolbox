using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackClockPublication
{
    private readonly NativePlaybackSingleReturnBridge.FreshClockReceipt? _fresh;
    private readonly NativePlaybackStockClockRouter.Session? _freshSession;
    private bool _freshPrepared;

    private NativePlaybackClockPublication(NativePlaybackGetTimeHookLease hook, NativePlaybackClockState initial,
        IntPtr caller, Action verify, NativePlaybackStockClockRouter.Session session,
        NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackStockRootedSource.ReadbackReceipt baseline,
        PlaybackSourceGroupLease? group, Action? verifyOperationSource)
    {
        _hook = hook; _current = initial; _permittedNullCaller = caller; _verifySource = verify; _armed = false; _freshSession = session;
        _registered = this; _operationGroup = group; _verifyOperationSource = verifyOperationSource;
        VerifyOperationConfiguration();
        verify();
        _attempted = Authority(initial);
        _fresh = adoption.PrepareClock(session, baseline, hook, _attempted);
    }

    internal static NativePlaybackClockPublication PrepareFresh(NativePlaybackGetTimeHookLease hook,
        NativePlaybackClockState initial, IntPtr caller, Action verify, NativePlaybackStockClockRouter.Session session,
        NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackStockRootedSource.ReadbackReceipt baseline,
        PlaybackSourceGroupLease? group = null, Action? verifyOperationSource = null)
        => new(hook, initial, caller, verify, session, adoption, baseline, group, verifyOperationSource);

    internal bool TryPrepareDormant()
    {
        lock (_gate)
        {
            if (_fresh is null) return true;
            VerifyFreshPublicationOwner();
            if (_withdrawn || _armed) throw new InvalidOperationException("This Fresh Clock preparation is no longer dormant.");
            try
            {
                _verifySource();
                if (!_freshPrepared && !_fresh.TryPrepare()) return false;
                _freshPrepared = true;
                _fresh.Read(1); _verifySource();
                _owned = _attempted ?? _owned; _attempted = null;
                return true;
            }
            catch { WithdrawOwned(); throw; }
        }
    }

    internal void VerifyFreshAuthority(NativePlaybackStockClockRouter router,
        NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackClockState current)
    {
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_fresh is null || !_freshPrepared || !_armed || _withdrawn || _current != current)
                throw new InvalidOperationException("The current final Clock has no active exact Fresh publication.");
            adoption.VerifyClock(router, _fresh);
            if (!IsCurrentOperationPolicy(current)) throw new InvalidOperationException("The current Fresh Clock publication changed.");
        }
    }

    private void VerifyFreshPublicationOwner()
    {
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_registered, this))
            throw new InvalidOperationException("A copied or foreign-thread publication owns no Clock or cleanup ticket.");
        if (_fresh is not null) _freshSession!.VerifyFreshPublicationOwner(this);
    }

    internal void VerifyFreshWriterAuthority(NativePlaybackStockClockRouter router,
        NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackClockState current)
    {
        lock (_gate)
        {
            VerifyFreshPublicationOwner();
            if (_fresh is null || !_freshPrepared || !_armed || _withdrawn || _current != current)
                throw new InvalidOperationException("A fresh writer requires the registered current Clock publication.");
            // OnReturned has not issued the next rooted readback yet. The
            // actual producer execution or continuation proves this write;
            // a superseded API receipt must not be used to verify it.
            adoption.VerifyClock(router, _fresh);
            if (!NativeMatchesOperation(current)) throw new InvalidOperationException("The exact fresh writer's native Clock changed.");
        }
    }
}
