using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    // No accepted validation is retained here. This only avoids requesting a
    // first Clock when no native completion has produced a paused candidate.
    internal bool WaitsForFirstReadback(GameEventState.PlaybackRunAssociation run)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "Playback scheduling belongs to the route owner thread.");
        run.VerifyHostCurrent();
        if (!ReferenceEquals(run, _run) || _disposed || _physicalFailure is not null || _transferFailure is not null
            || _freshCapture is null || _freshAdoption is null || _freshHostEnded || _source is null
            || _sourceRoute is null || _group is not { IsActive: true } || _session is not null || _rateHost is not null
            || _initialHost is not null || _handoff is not null || _admitted is not null || InitializationPending
            || _resourceRetryCandidate is not null || _initializationRetryKind != InitializationRetryKind.None
            || _freshInitializer is not null || _freshInitializationRoots is not null || _formalSuccessor is not null
            || _directPitchCalls.Count != 0 || _retiredFreshAdoptions.Count != 0 || _retainedSources.Count != 0
            || _retainedInitializers.Count != 0 || _retainedObservedInitializers.Count != 0
            || _retainedInitializationAdmissions.Count != 0 || _unreleasedObservedGraphs.Count != 0)
            return false;
        _freshAdoption.VerifySourceRegistration(_source);
        return !_source.HasFirstPausedReadbackCandidate;
    }
}
