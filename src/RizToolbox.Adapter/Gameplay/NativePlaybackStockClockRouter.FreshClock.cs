using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    internal void VerifyFreshClockCandidate(Session session, NativePlaybackFreshSourceCapture.Adoption adoption,
        NativePlaybackStockRootedSource.ReadbackReceipt baseline, NativePlaybackGetTimeHookLease hook)
    {
        Open();
        if (!ReferenceEquals(_freshAdoption, adoption) || !ReferenceEquals(_session, session)
            || !ReferenceEquals(_group!.GetTimeLease, hook) || !ReferenceEquals(Current(), baseline))
            throw new GameplayStateException(ErrorCode.TargetExpired, "Only the registered adopted source session may prepare its original Fresh Clock.");
        adoption.VerifyRouter(this);
    }

    internal sealed partial class Session
    {
        internal void VerifyFreshPublicationOwner(NativePlaybackClockPublication publication)
        {
            if (_owner._thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_owner._registeredOwner, _owner)
                || !ReferenceEquals(_owner._session, this) || !ReferenceEquals(_publication, publication))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied or foreign Fresh publication cannot use this session's ticket.");
        }

        internal void VerifyFreshClockAuthority()
        {
            Registered();
            if (!_armed || _finalClock is null || _publication is null || _owner._freshAdoption is not { } adoption)
                throw new GameplayStateException(ErrorCode.NotReady, "The exact Fresh source has no armed Clock session.");
            // Current policy is checked, including legitimate Pause/Resume
            // successors; the initial Pause is only the one-time admission cut.
            _publication.VerifyFreshAuthority(_owner, adoption, _finalClock.Capture());
            Registered();
        }

        private void VerifyFreshClockOperationIdentity()
        {
            VerifyRateOperationIdentity();
            var adoption = _owner._freshAdoption ?? throw new GameplayStateException(ErrorCode.TargetExpired,
                "The original Fresh source is no longer registered.");
            var source = _owner._source ?? throw new GameplayStateException(ErrorCode.TargetExpired,
                "The Fresh source's registered rooted consumer ended.");
            adoption.VerifyOperationIdentity(_owner, source, _owner._api, _owner._run,
                _owner._group!, _initial.Binding, _owner._origin);
        }

        internal Session? FreshWriterSession()
        {
            Registered();
            return _armed ? this : null;
        }

        internal void VerifyFreshWriterAuthority(NativePlaybackFreshSourceCapture.Adoption adoption,
            NativePlaybackStockRootedSource source)
        {
            VerifyFreshClockOperationIdentity();
            if (!_armed || _finalClock is null || _publication is null || !ReferenceEquals(_owner._freshAdoption, adoption)
                || !ReferenceEquals(_owner._source, source))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The writer is not this armed Fresh session's original source.");
            _publication.VerifyFreshWriterAuthority(_owner, adoption, _finalClock.Capture());
            Registered();
        }
    }

    internal void VerifyFreshRateSession(Session session, NativePlaybackFreshSourceCapture.Adoption adoption)
    {
        OpenOperation();
        if (!ReferenceEquals(_session, session) || !ReferenceEquals(_freshAdoption, adoption))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The short rate transaction belongs to another Fresh session.");
    }

    internal Session? FreshWriterSession(NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackStockRootedSource source)
    {
        OpenOperation();
        if (!ReferenceEquals(_freshAdoption, adoption) || !ReferenceEquals(_source, source))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The writer belongs to another adopted graph.");
        return _session?.FreshWriterSession();
    }
}
