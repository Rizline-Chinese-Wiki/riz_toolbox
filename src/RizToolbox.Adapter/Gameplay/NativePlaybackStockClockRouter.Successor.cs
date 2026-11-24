using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    private readonly List<NativePlaybackFreshSourceCapture.Adoption> _retiredFreshAdoptions = new();
    private bool _freshHostEnded;
    private NativePlaybackInitializationTracker? _freshInitializer;
    private IDisposable? _freshInitializationRoots;
    private FormalSuccessor? _formalSuccessor;
    private GameEventState.PlaybackRunAssociation? _noPredecessorObservedRun;

    // Captures dispatch state only. It proves no current source or native
    // receipt and is never accepted by an admission or Clock method.
    internal sealed class TransitionIdleHint
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly object? _run, _session, _initialization, _freshInitializer, _observedInitialization;
        private readonly object? _handoff, _initializationHandoff, _firstCaptureAdmission;
        private readonly object? _resourceRetryFirst, _resourceRetryTransfer, _resourceRetryCandidate;
        private readonly object? _freshAdoption, _source, _sourceRoute, _admitted, _formalSuccessor, _initialHost;
        private readonly bool _sessionSupportsSuccessors, _freshHostEnded, _disposed;
        private readonly InitializationRetryKind _retryKind;
        private readonly string? _physicalFailure, _transferFailure;
        private readonly string? _runWithdrawalReason;

        internal TransitionIdleHint(NativePlaybackStockClockRouter owner)
        {
            _owner = owner;
            _run = owner._run; _session = owner._session;
            _initialization = owner._initialization; _freshInitializer = owner._freshInitializer;
            _observedInitialization = owner._observedInitialization;
            _handoff = owner._handoff; _initializationHandoff = owner._initializationHandoff;
            _firstCaptureAdmission = owner._firstCaptureAdmission;
            _resourceRetryFirst = owner._resourceRetryFirst; _resourceRetryTransfer = owner._resourceRetryTransfer;
            _resourceRetryCandidate = owner._resourceRetryCandidate;
            _freshAdoption = owner._freshAdoption; _source = owner._source; _sourceRoute = owner._sourceRoute;
            _admitted = owner._admitted; _formalSuccessor = owner._formalSuccessor; _initialHost = owner._initialHost;
            _sessionSupportsSuccessors = owner._session?.SupportsObservedSuccessors == true;
            _freshHostEnded = owner._freshHostEnded; _disposed = owner._disposed;
            _retryKind = owner._initializationRetryKind;
            _physicalFailure = owner._physicalFailure; _transferFailure = owner._transferFailure;
            _runWithdrawalReason = owner._run.WithdrawalReason;
        }

        internal bool StillCurrent(NativePlaybackStockClockRouter owner)
        {
            owner.VerifyRegisteredOwner();
            if (owner._thread != Environment.CurrentManagedThreadId) return false;
            return ReferenceEquals(_owner, owner)
                && ReferenceEquals(_run, owner._run) && ReferenceEquals(_session, owner._session)
                && ReferenceEquals(_initialization, owner._initialization)
                && ReferenceEquals(_freshInitializer, owner._freshInitializer)
                && ReferenceEquals(_observedInitialization, owner._observedInitialization)
                && ReferenceEquals(_handoff, owner._handoff)
                && ReferenceEquals(_initializationHandoff, owner._initializationHandoff)
                && ReferenceEquals(_firstCaptureAdmission, owner._firstCaptureAdmission)
                && ReferenceEquals(_resourceRetryFirst, owner._resourceRetryFirst)
                && ReferenceEquals(_resourceRetryTransfer, owner._resourceRetryTransfer)
                && ReferenceEquals(_resourceRetryCandidate, owner._resourceRetryCandidate)
                && ReferenceEquals(_freshAdoption, owner._freshAdoption)
                && ReferenceEquals(_source, owner._source) && ReferenceEquals(_sourceRoute, owner._sourceRoute)
                && ReferenceEquals(_admitted, owner._admitted)
                && ReferenceEquals(_formalSuccessor, owner._formalSuccessor)
                && ReferenceEquals(_initialHost, owner._initialHost)
                && _sessionSupportsSuccessors == (owner._session?.SupportsObservedSuccessors == true)
                && _freshHostEnded == owner._freshHostEnded && _disposed == owner._disposed
                && _retryKind == owner._initializationRetryKind
                && _physicalFailure == owner._physicalFailure && _transferFailure == owner._transferFailure
                && _runWithdrawalReason == owner._run.WithdrawalReason;
        }
    }

    internal TransitionIdleHint? CaptureUnavailableTransitionIdleHint(GameEventState.PlaybackRunAssociation run)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId) return null;
        // A retained native admission can advance inside the same object.
        // Keep the original full retry path while any such candidate exists.
        // A prior-run tracker whose host association is already withdrawn
        // cannot create the missing armed session; its physical drain still
        // runs through PumpCompleted on every host pump.
        // A withdrawn session cannot rearm. The host still pumps completions and
        // checks the run, the final initializer-return count and this dispatch
        // snapshot before skipping another admission attempt.
        var endedSameRun = ReferenceEquals(_run, run) && _session is { IsWithdrawn: true };
        var missingPredecessor = ReferenceEquals(_noPredecessorObservedRun, run) && _session is null;
        if ((!endedSameRun && !missingPredecessor) || _disposed || _group is not { IsActive: true }
            || _physicalFailure is not null
            || _transferFailure is not null
            || _handoff is not null || _observedInitialization is not null
            || _initializationHandoff is not null || _firstCaptureAdmission is not null
            || _resourceRetryFirst is not null || _resourceRetryTransfer is not null
            || _resourceRetryCandidate is not null || _freshInitializer is not null
            || _freshInitializationRoots is not null || _initialHost is not null
            || (_initialization is not null && _run.WithdrawalReason is null))
            return null;
        return new(this);
    }

    internal void FreshHostWithdrawn(NativePlaybackFreshSourceCapture.Adoption adoption, string reason)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_freshAdoption, adoption))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A foreign capture cannot end this route's observed host run.");
        _freshHostEnded = true;
        _session?.Withdraw(reason);
    }

    internal void VerifyFreshInitializer(NativePlaybackFreshSourceCapture.Adoption adoption,
        NativePlaybackInitializationTracker initializer)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_freshAdoption, adoption)
            || !ReferenceEquals(_initialization, initializer) || !ReferenceEquals(_freshInitializer, initializer)
            || _session is not { SupportsObservedSuccessors: true })
            throw new GameplayStateException(ErrorCode.OperationUnsupported,
                "An actual Start can succeed Fresh only after its first Clock has armed.");
    }

    // Retirement ends the old borrow. It grants no successor authority: the
    // latter still needs its exact native transfer, capture and source-open.
    private void RetireFreshPredecessor(NativePlaybackSingleReturnBridge.LogicalHandoff native)
    {
        if (_freshAdoption is not { } fresh) return;
        var info = native.Read(2);
        if (info.OldEpoch != checked((ulong)_binding.SourceGeneration))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The transfer did not consume this fresh predecessor epoch.");
        _retiredFreshAdoptions.Add(fresh); _freshAdoption = null;
        _freshInitializer = null;
        var hold = _freshInitializationRoots; _freshInitializationRoots = null;
        hold?.Dispose();
    }

    private void RegisterFormalSuccessor(NativePlaybackSingleReturnBridge.LogicalHandoff native,
        NativePlaybackStockRootedSource source)
    {
        if (_freshCapture is null) return; // Historical fixture route has its own admission.
        if (!ReferenceEquals(_source, source) || !ReferenceEquals(_admitted, native))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A foreign source cannot inherit the retained formal lineage.");
        var info = native.Read(3);
        if (info.NewEpoch != checked((ulong)_binding.SourceGeneration))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The source-open epoch differs from the actual successor graph.");
        _formalSuccessor = new(this, native, source, _run, _binding);
    }

    internal bool SupportsObservedSuccessors
    {
        get
        {
            VerifyRegisteredOwner();
            if (_thread != Environment.CurrentManagedThreadId) return false;
            return !_disposed && _physicalFailure is null && (_session?.SupportsObservedSuccessors == true
                || _handoff is not null || _initializationHandoff is not null || _observedInitialization is not null);
        }
    }

    internal Session? GetOrPrepareObservedSession(GameEventState.PlaybackRunAssociation run,
        NativePlaybackGetTimeCallProfile? callProfile)
    {
        Open(); _noPredecessorObservedRun = null;
        run.VerifyHostCurrent(); _group!.VerifyCurrent();
        if (_observedInitialization is { HasNoNativeReceipt: true } unconsumed && !unconsumed.MatchesRun(run))
        {
            _observedInitialization = null;
            _retainedObservedInitializers.Add(unconsumed); unconsumed.Dispose();
        }
        if (_observedInitialization is { } observed)
        {
            if (!observed.TryComplete(run)) return null;
        }
        else if (_handoff is { } pending)
        {
            // A source-open Busy retains the admitted run and source object.
            // Retry that same ticket even after _run has become the new run.
            if (!pending.TryAdmitObserved(run)) return null;
        }
        else if (!ReferenceEquals(run, _run))
        {
            if (run.RunId == _run.RunId)
                throw new GameplayStateException(ErrorCode.OperationUnsupported,
                    "A recovered observer association without a new run or actual Start cannot replace the old source.");
            if (_initializationHandoff is not null || _firstCaptureAdmission is not null
                || _resourceRetryFirst is not null || _resourceRetryTransfer is not null)
                throw new GameplayStateException(ErrorCode.OperationUnsupported,
                    "A new host run arrived while the previous actual initializer still owns this source gate.");
            if (_initialization is not null || !HasOrdinarySuccessorBoundary())
            {
                if (!TryObservedInitialization(run)) return null;
            }
            else
            {
                var predecessor = _session ?? throw new GameplayStateException(ErrorCode.OperationUnsupported,
                    "A source that never armed a Clock has no retained native predecessor to transfer.");
                BeginHandoff(predecessor);
                if (!_handoff!.TryAdmitObserved(run)) return null;
            }
        }
        PumpCompleted();
        if (InitializationPending || _handoff is not null) return null;
        if (_session is { } current)
        {
            if (current.IsWithdrawn)
                throw new GameplayStateException(ErrorCode.OperationUnsupported,
                    "The ended source needs a different host-issued run or a completed actual Start before it can be replaced.");
            return current;
        }
        return PreparePausedClock(callProfile);
    }

    private bool HasOrdinarySuccessorBoundary()
    {
        _group!.VerifyCurrent();
        try { _group.RequireFreshAdmission(); return true; }
        catch (UnsupportedSignatureException) when (_group.ReadActivity().Known && _group.GetTimeLease.ReadActivity().Known)
        {
            // This is a rejection selector only. It does not grant admission:
            // the native joint handoff still validates every actual transition.
            return false;
        }
    }

    private sealed class FormalSuccessor
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly NativePlaybackSingleReturnBridge.LogicalHandoff _native;
        private readonly NativePlaybackStockRootedSource _source;
        private readonly GameEventState.PlaybackRunAssociation _run;
        private readonly NativePlaybackBinding _binding;
        internal FormalSuccessor(NativePlaybackStockClockRouter owner, NativePlaybackSingleReturnBridge.LogicalHandoff native,
            NativePlaybackStockRootedSource source, GameEventState.PlaybackRunAssociation run, NativePlaybackBinding binding)
            => (_owner, _native, _source, _run, _binding) = (owner, native, source, run, binding);
        internal void Verify()
        {
            _owner.Open();
            VerifyOperationIdentity();
            _source.Current?.VerifyCurrent();
        }
        internal void VerifyOperationIdentity()
        {
            _owner.OpenOperation();
            if (!ReferenceEquals(_owner._formalSuccessor, this) || !ReferenceEquals(_owner._admitted, _native)
                || !ReferenceEquals(_owner._source, _source) || !ReferenceEquals(_owner._run, _run)
                || !ReferenceEquals(_owner._binding, _binding))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The private formal successor is no longer registered.");
            _run.VerifyCurrent(_binding);
            if (_native.Read(5).NewEpoch != checked((ulong)_binding.SourceGeneration))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The actual armed transfer no longer owns this successor epoch.");
        }
    }

    internal sealed partial class Session
    {
        internal bool IsWithdrawn => _withdrawn;
        internal bool SupportsObservedSuccessors => ReferenceEquals(_owner._session, this) && _armed
            && _publication is { SupportsObservedSuccessors: true };
        internal void VerifyFormalClockAuthority()
        {
            Registered();
            // This method is the installation/adoption audit. Running rate
            // and Clock operations use the explicit identity/policy path.
            _owner._group!.VerifyCurrent();
            if (_owner._freshAdoption is not null) { VerifyFreshClockAuthority(); return; }
            if (!_armed || _finalClock is null || _publication is null || _owner._formalSuccessor is not { } successor)
                throw new GameplayStateException(ErrorCode.NotReady, "No exact typed transfer owns this formal Clock.");
            successor.Verify();
            if (!_publication.IsCurrent(_finalClock.Capture()))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The transferred Clock publication changed.");
            Registered();
        }
        private void VerifyFormalClockOperationIdentity()
        {
            VerifyRateOperationIdentity();
            if (_owner._freshAdoption is not null) VerifyFreshClockOperationIdentity();
            else if (_owner._formalSuccessor is { } successor) successor.VerifyOperationIdentity();
            else throw new GameplayStateException(ErrorCode.NotReady,
                "No registered Fresh adoption or exact typed transfer owns this formal playback source.");
            VerifyRateOperationIdentity();
        }
        internal void VerifyFormalRateSourceAuthority()
        {
            VerifyFormalClockAuthority();
            if (_owner._freshAdoption is { } fresh) fresh.VerifyRateSource(this);
            else
            {
                _owner._formalSuccessor!.Verify();
                _owner._group!.RequireFreshAdmission();
                _owner.Current().VerifyCurrent();
            }
        }
    }
}
