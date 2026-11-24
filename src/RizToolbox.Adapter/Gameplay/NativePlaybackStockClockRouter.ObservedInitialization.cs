using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    private ObservedInitialization? _observedInitialization;
    private readonly List<ObservedInitialization> _retainedObservedInitializers = new();

    private bool TryObservedInitialization(GameEventState.PlaybackRunAssociation run)
    {
        if (_observedInitialization is null)
        {
            if (_session is not { SupportsObservedSuccessors: true } predecessor
                || _initializationHandoff is not null || _firstCaptureAdmission is not null
                || _resourceRetryFirst is not null || _resourceRetryTransfer is not null)
            {
                // A host observation alone cannot mint an armed predecessor.
                // Remember only which exact dispatch failed; this is not a
                // native receipt, source lease or successful validation.
                _noPredecessorObservedRun = run;
                throw new GameplayStateException(ErrorCode.OperationUnsupported,
                    "This transition has no unconsumed armed predecessor for an observed initializer.");
            }
            _observedInitialization = new(this, run, predecessor, _initialization);
        }
        return _observedInitialization.TryComplete(run);
    }

    // A late host association can consume a real completed Start only when the
    // original native record and its Before-bound resource ticket still exist.
    // These four host roots do not stage a watch or create an initializer.
    internal sealed class ObservedInitialization : IDisposable
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly GameEventState.PlaybackRunAssociation _run;
        private readonly Session _predecessor;
        private readonly NativePlaybackInitializationTracker? _retainedTracker;
        private readonly ObservedInitialization _registered;
        private NativePlaybackInitializationHost? _host;
        private NativePlaybackStockSourceHub.CompletedInitializationReceipt? _completion;
        private NativePlaybackStockSourceHub.InitializationInformation _completed;
        private NativePlaybackSingleReturnBridge.LogicalHandoff? _native;
        private NativePlaybackInitializationTracker.CapturePermit? _permit;
        private NativePlaybackStockRootedSource? _source;
        private NativePlaybackBinding? _binding;
        private bool _sourceBuilt, _closed, _opened, _releaseFailed;
        private int _active;

        internal ObservedInitialization(NativePlaybackStockClockRouter owner, GameEventState.PlaybackRunAssociation run,
            Session predecessor, NativePlaybackInitializationTracker? retainedTracker)
            => (_owner, _run, _predecessor, _retainedTracker, _registered) = (owner, run, predecessor, retainedTracker, this);

        internal bool RootsRetained => _host?.RootsRetained ?? false;
        internal bool HasNoNativeReceipt => _completion is null && _native is null;
        internal bool MatchesRun(GameEventState.PlaybackRunAssociation run) => ReferenceEquals(_run, run);
        private void Registered()
        {
            _owner.VerifyRegisteredOwner();
            if (_owner._thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_registered, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied observed initializer owns no roots or native completion.");
        }
        private void Open()
        {
            Registered();
            if (_closed || _owner._disposed || !ReferenceEquals(_owner._observedInitialization, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The actual observed initializer no longer owns its pending route.");
        }
        private void VerifyHost()
        {
            Open(); _run.VerifyHostCurrent();
            _host!.VerifyCurrent(_owner._api, _run);
            Open();
        }

        internal bool TryComplete(GameEventState.PlaybackRunAssociation run)
        {
            Open();
            if (!ReferenceEquals(_run, run))
                throw new GameplayStateException(ErrorCode.OperationUnsupported,
                    "A different host observation cannot replace this retained initializer candidate.");
            ++_active;
            try
            {
                _host ??= NativePlaybackInitializationHost.Capture(_owner._api, _run);
                VerifyHost();
                if (_completion is null)
                {
                    var diagnostic = _owner._group!.ReadInitialization();
                    if (diagnostic.Progress != unchecked((ulong)_host.Progress) || diagnostic.Blocked != 1)
                        throw new GameplayStateException(ErrorCode.OperationUnsupported,
                            "No watched actual Start belongs to this observed Progress; a post-observation capture cannot invent its missing Before receipt.");
                    _completion = _host.TryCaptureCompletedInitializer(_owner._group);
                    if (_completion is null) return false;
                    _completed = _completion.Read();
                    VerifyHost();
                }
                if (_native is null)
                {
                    _native = _predecessor.BeginOwnObservedInitialization(this, _completion);
                    _predecessor.Withdraw("The actual completed initializer entered its registered cross-run transfer.");
                }
                if (!_sourceBuilt)
                {
                    if (_native.Read().Phase == 1 && !_native.TryAdmit()) return false;
                    _completion.VerifyTransferred(_native);
                    var admission = _native.Read(2);
                    _binding = new(_run.RunId, _run.SceneGeneration, _run.Epoch, checked((long)admission.NewEpoch),
                        _run.Controller, _run.Information, _host.Progress, checked((long)_completed.Player),
                        checked((long)_completed.Source), checked((long)_completed.Clip), _host.GetTimeMethod);
                    _run.VerifyCurrent(_binding);
                    _permit = NativePlaybackInitializationTracker.CapturePermit.IssueObserved(this, _owner._group!, _native, _binding);
                    var route = new object();
                    var source = NativePlaybackStockRootedSource.CaptureInitialized(_owner._api, _binding, _host.Origin,
                        _run, _owner._group!, _permit,
                        receipt => { if (ReferenceEquals(_owner._sourceRoute, route)) _owner._session?.Observe(receipt); },
                        reason => { if (ReferenceEquals(_owner._sourceRoute, route)) _owner._session?.Withdraw("The observed successor withdrew: " + reason); });
                    if (!ReferenceEquals(_source, source))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "The initialized source did not claim its one observed permit.");
                    _sourceBuilt = true; VerifyHost();
                    var prior = _owner._source!;
                    _owner.RetireFreshPredecessor(_native);
                    prior.Dispose(); prior.PumpCompleted();
                    if (!prior.RootsReleased && !_owner._retainedSources.Contains(prior)) _owner._retainedSources.Add(prior);
                    _predecessor.Withdraw("The completed actual initializer transferred to the observed successor run.");
                    _owner._logicalObservations.Clear(); _owner._session = null;
                    _owner._binding = _binding; _owner._origin = _host.Origin; _owner._run = _run;
                    _owner._admitted = _native; _owner._callProfile = null;
                    _owner._source = source; _owner._sourceRoute = route;
                }
                VerifyHost();
                if (!_native.TryOpenSource()) return false;
                _source!.VerifyPlayerWatchActivated();
                _source.CompleteInitializationCapture(_permit!);
                _owner.RegisterFormalSuccessor(_native, _source);
                _opened = true;
                if (_retainedTracker is not null)
                {
                    _retainedTracker.Dispose();
                    if (_retainedTracker.RootsRetained) _owner._retainedInitializers.Add(_retainedTracker);
                    if (ReferenceEquals(_owner._initialization, _retainedTracker)) _owner._initialization = null;
                }
                _owner._observedInitialization = null;
                _owner._retainedObservedInitializers.Add(this);
                Dispose(); return true;
            }
            catch (GameplayStateException error) when (error.Code == ErrorCode.OperationUnsupported && _native is null)
            { throw; }
            catch (Exception error)
            {
                _owner._transferFailure = "The observed initialized successor failed closed: " + error.Message;
                Dispose(); throw;
            }
            finally
            {
                --_active;
                if (_closed) TryReleaseRoots();
            }
        }

        internal void RegisterSource(NativePlaybackInitializationTracker.CapturePermit permit, NativePlaybackStockRootedSource source,
            PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            Open();
            if (_source is not null || !ReferenceEquals(_permit, permit) || !ReferenceEquals(_owner._group, group) || _binding != binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The observed initializer permits one exact rooted source.");
            _source = source;
        }
        private void VerifyPermitIdentity(NativePlaybackInitializationTracker.CapturePermit permit, NativePlaybackStockRootedSource source,
            PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            Open();
            if (!ReferenceEquals(_permit, permit) || !ReferenceEquals(_source, source)
                || !ReferenceEquals(_owner._group, group) || _binding != binding || _native is null || _completion is null)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The initialized graph lost its original observed permit.");
            VerifyHost(); _run.VerifyCurrent(binding);
        }
        internal void VerifyPermit(NativePlaybackInitializationTracker.CapturePermit permit, NativePlaybackStockRootedSource source,
            PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            VerifyPermitIdentity(permit, source, group, binding);
            _completion!.VerifyTransferred(_native!);
            group.VerifyInitializationCapture(_native!, binding.Player, binding.AudioSource, binding.Clip);
            Open();
        }
        internal void VerifyOpened(NativePlaybackInitializationTracker.CapturePermit permit, NativePlaybackStockRootedSource source,
            PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            VerifyPermitIdentity(permit, source, group, binding);
            _completion!.VerifyOpened(_native!); group.RequireFreshAdmission(); Open();
        }
        private void TryReleaseRoots()
        {
            if (!_closed || _active != 0 || _releaseFailed || _host is null || !_host.RootsRetained) return;
            try
            {
                var source = _owner._group!.ReadActivity(); var getTime = _owner._group.GetTimeLease.ReadActivity();
                if (!source.Known || source.InFlight != 0 || !getTime.Known || getTime.InFlight != 0) return;
                _host.Dispose();
            }
            catch { _releaseFailed = true; } // Preserve actual roots and unknown free outcome.
        }
        public void Dispose()
        {
            Registered(); _closed = true;
            if (_sourceBuilt && !_opened && !ReferenceEquals(_owner._source, _source))
            {
                _source!.Dispose();
                if (!_source.RootsReleased && !_owner._retainedSources.Contains(_source)) _owner._retainedSources.Add(_source);
            }
            TryReleaseRoots();
        }
    }

    internal sealed partial class Session
    {
        internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginOwnObservedInitialization(ObservedInitialization observed,
            NativePlaybackStockSourceHub.CompletedInitializationReceipt completion)
        {
            _owner.Open();
            if (!ReferenceEquals(_owner._observedInitialization, observed) || !ReferenceEquals(_owner._session, this)
                || !_armed || _publication is null || _finalClock is null || _owner._handoff is not null)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the retained armed predecessor can consume this observed initializer.");
            return _publication.BeginCrossRunInitializationHandoff(completion);
        }
    }
}
