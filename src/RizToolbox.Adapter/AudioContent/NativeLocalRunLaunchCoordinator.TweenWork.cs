namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunLaunchCoordinator
{
    private NativeLocalTweenWorkOwner? _tweenWork;
    private void CaptureTweenWork()
    {
        try {
            _tweenWork = NativeLocalTweenWorkOwner.Capture(_api, _main, _profile, _module, _broker,
                _reservation!.State, _unityEvents!, this);
            _unityEvents!.AttachTweenInlineWork(_tweenWork);
        }
        catch (NativeLocalTweenWorkException error) { _tweenWork = error.Responsibility; throw; }
    }
    private void PumpTweenWork()
    {
        if (_tweenWork?.RetirementStarted == true)
        {
            if (_reservation is not null) _tweenWork.PumpTweenRetirement(_reservation.State, this);
            if (_tweenWork.Unknown) throw new InvalidOperationException("The actual Tween retirement became Unknown; outstanding holders remain retained.");
            return;
        }
        _tweenWork?.Tick();
        if (_tweenWork is not null && _reservation is not null && _session is not null
            && (_activeReturnQueued || _host.Sessions.Read(_session).ReturnRequested))
        {
            _tweenWork.RequestAdmissionSeal(_reservation.State, this);
            _tweenWork.TrySeal();
            _tweenWork.PumpActualTweenDeaths(_reservation.State, this);
            _tweenWork.PumpTweenRetirement(_reservation.State, this);
        }
        if (_tweenWork?.Unknown == true)
            throw new InvalidOperationException("The retained actual Tween source/work observation became Unknown.");
    }
    private bool TweenWorkCanRetire()
    {
        return _tweenWork is not null && !_tweenWork.Unknown && _tweenWork.CanRetireWork();
    }
    internal void RequireTweenAdmissionSealRequest(NativeLocalTweenWorkOwner work, LocalRunLaunchCommitState launch)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_tweenWork, work) || _reservation is null || !ReferenceEquals(_reservation.State, launch)
            || _session is null || _phase == NativeLocalRunLaunchPhase.Unknown)
            throw new InvalidOperationException("Tween admission seal has no exact retained launch coordinator.");
        var current = _host.Sessions.Read(_session);
        if (current.Identity != launch.Identity || current.CleanupAttempted || current.ProtectionRetired
            || current.Phase is LocalRunSessionPhase.Closed or LocalRunSessionPhase.CleanupUnknown
            || !_activeReturnQueued && !current.ReturnRequested)
            throw new InvalidOperationException("Tween admission seal requires an actual classified return request before cleanup.");
    }
}
