using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    private NativePlaybackInitializationTracker? _initialization;
    private readonly List<NativePlaybackInitializationTracker> _retainedInitializers = new();
    private InitializationHandoff? _initializationHandoff;
    private FirstCaptureAdmission? _firstCaptureAdmission;
    private NativePlaybackSingleReturnBridge.InitialCapture? _resourceRetryFirst;
    private NativePlaybackSingleReturnBridge.LogicalHandoff? _resourceRetryTransfer;
    private NativePlaybackStockRootedSource? _resourceRetryCandidate;
    // The typed reason selects an existing native coordinator; it grants no
    // source authority and cannot turn the separate 112-byte pitch wire into
    // a time interruption. Retained roots still follow the joint drain path.
    private enum InitializationRetryKind { None, Resource, Backing, LowerPitch, AudioInterruption }
    private InitializationRetryKind _initializationRetryKind;
    private readonly List<object> _retainedInitializationAdmissions = new();
    internal bool InitializationPending => _initialization is not null || _initializationHandoff is not null || _firstCaptureAdmission is not null
        || _resourceRetryFirst is not null || _resourceRetryTransfer is not null || _observedInitialization is not null;

    private bool RouteInitializationBefore(NativePlaybackSourceObservation observation)
    {
        if (observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext && _run.WithdrawalReason is not null)
        {
            // The old host association cannot authorize a same-run tracker.
            // Native still retains its real blocked initializer; a later host
            // observation selects an explicitly supported transfer protocol.
            observation.DisposeOperation(); return true;
        }
        if (observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext && _initialization is { } interrupted
            && (interrupted.AudioInterruptionCaptureInvalidated || interrupted.ResourceCaptureInvalidated
                || interrupted.BackingCaptureInvalidated || interrupted.LowerPitchCaptureInvalidated)
            && observation.ParentSequence == 0 && observation.Depth == 1 && _group!.ReadSourceOperationHealth().InFlight == 1)
            RetainInterruptedInitializationCapture();
        if (observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext && _initialization is null)
        {
            if (_disposed) { observation.DisposeOperation(); return true; }
            _initialization = _initialHost is { } host
                ? new(_api, _group!, _run, host) : new(_api, _group!, _run, _binding, _source?.InitializationWatchOwner);
            if (_freshAdoption is { } fresh)
            {
                // Authenticate and retain the actual Start before ending the
                // old fresh graph. The resource watch still belongs to capture.
                if (!_initialization.ObserveBefore(observation))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The actual initializer did not claim its native Before receipt.");
                _freshInitializer = _initialization;
                _freshInitializationRoots = fresh.RetainForInitialization(this, _initialization);
                _source?.WithdrawForInitialization();
                return true;
            }
            // Claim Start only once, outside the retired stock producer. Its
            // native Before already closed Fresh; withdraw the old logical
            // Clock/source before the original reaches any field store.
            _source?.WithdrawForInitialization();
        }
        return _initialization?.ObserveBefore(observation) == true;
    }

    private void PumpInitialization()
    {
        if (_observedInitialization is not null) return; // Its actual resource ticket remains held through source-open.
        if (!_disposed && _run.WithdrawalReason is not null) return; // Wait for the actual post-Poll successor association.
        if (!_disposed && _initialization is { } pending) DeferInvalidatedInitialization(pending);
        if (_initialization is not null)
        {
            if (_disposed) _initialization.Dispose();
            else if (_transferFailure is null) TryCompleteInitialization();
        }
        foreach (var retained in _retainedInitializers.ToArray())
        {
            retained.Dispose();
            if (!retained.RootsRetained) _retainedInitializers.Remove(retained);
        }
        if (_disposed && _initialHost is { } host && host.TryReleaseStopped(this, _group)) _initialHost = null;
    }

    private bool DeferInvalidatedInitialization(NativePlaybackInitializationTracker initializer)
    {
        if (!ReferenceEquals(_initialization, initializer)
            || !(initializer.AudioInterruptionCaptureInvalidated || initializer.ResourceCaptureInvalidated
                || initializer.BackingCaptureInvalidated || initializer.LowerPitchCaptureInvalidated)) return false;
        if (_group!.ReadActivity() is { Known: true, InFlight: 0 }) RetainInterruptedInitializationCapture();
        return true;
    }

    private void RetainInterruptedInitializationCapture()
    {
        var prior = _initialization!;
        // Audio is a distinct Scheduling interruption, authenticated first.
        // A failed/pending audio coordinator must never fall through to one
        // of the unrelated resource, backing or pitch replacement actions.
        _initializationRetryKind = prior.AudioInterruptionCaptureInvalidated ? InitializationRetryKind.AudioInterruption
            : prior.LowerPitchCaptureInvalidated ? InitializationRetryKind.LowerPitch
            : prior.BackingCaptureInvalidated ? InitializationRetryKind.Backing
            : prior.ResourceCaptureInvalidated ? InitializationRetryKind.Resource
            : throw new InvalidOperationException("Only an actual typed interruption may retry initialization.");
        if (_firstCaptureAdmission is { } first)
        {
            _retainedInitializationAdmissions.Add(first);
            _resourceRetryFirst = first.NativeForResourceRetry;
            if (first.CandidateForResourceRetry is { } candidate) _resourceRetryCandidate = candidate;
            _firstCaptureAdmission = null;
        }
        if (_initializationHandoff is { } transfer)
        {
            _retainedInitializationAdmissions.Add(transfer);
            _resourceRetryTransfer = transfer.NativeForResourceRetry;
            if (transfer.CandidateForResourceRetry is { } candidate) _resourceRetryCandidate = candidate;
            _session = transfer.PredecessorForResourceRetry;
            _initializationHandoff = null;
        }
        if (_initializationRetryKind == InitializationRetryKind.AudioInterruption
            && _resourceRetryFirst is null && _resourceRetryTransfer is null)
            throw new UnsupportedSignatureException("An audio interruption lost its exact retained native first-capture or handoff receipt.");
        // Keep a pending candidate's seven roots and three pending watches
        // until the replacement watches are staged atomically. Both original
        // entry gates must drain; late old retirement must not clear the new
        // pending graph or release the old physical callback responsibility.
        prior.Dispose(); if (prior.RootsRetained) _retainedInitializers.Add(prior);
        _initialization = null;
    }

    private void ReleaseResourceRetryCandidate(NativePlaybackStockRootedSource successor)
    {
        if (_resourceRetryCandidate is { } prior && !ReferenceEquals(prior, successor))
        {
            prior.Dispose(); prior.PumpCompleted();
            if (!prior.RootsReleased) _retainedSources.Add(prior);
            _resourceRetryCandidate = null;
        }
        // A new seven-root candidate has now replaced the pending native
        // watch. Until this point its old admission and candidate stayed held.
        _retainedInitializationAdmissions.Clear();
    }

    /// <summary>Owner-thread continuation after the native parent really exited.
    /// Uses only the private retained completion and the same host association;
    /// no caller can supply a replacement graph, revision or completion flag.</summary>
    internal bool TryCompleteInitialization()
    {
        Open(); _group!.VerifyCurrent();
        if (_observedInitialization is not null || _run.WithdrawalReason is not null) return false;
        if (_initialization is not { } initializer) return false;
        if (DeferInvalidatedInitialization(initializer)) return false;
        if (_handoff is not null)
            throw new GameplayStateException(ErrorCode.Conflict, "A cross-run handoff already owns this physical route.");
        if (_firstCaptureAdmission is { } first) return first.TryAdmit();
        if (_initializationHandoff is null)
        {
            NativePlaybackInitializationTracker.Completion completion;
            try { completion = initializer.CaptureCompleted(); }
            catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady) { return false; }
            if (_resourceRetryFirst is { } retryFirst)
            {
                var capture = _initializationRetryKind switch
                {
                    InitializationRetryKind.AudioInterruption => retryFirst.RetryAudioInterruptedInitialization(completion.NativeIdentityFor(_group)),
                    InitializationRetryKind.LowerPitch => retryFirst.RetryLowerPitchInitialization(completion.NativeIdentityFor(_group)),
                    InitializationRetryKind.Backing => retryFirst.RetryBackingInitialization(completion.NativeIdentityFor(_group)),
                    InitializationRetryKind.Resource => retryFirst.RetryResourceInitialization(completion.NativeIdentityFor(_group)),
                    _ => throw new UnsupportedSignatureException("The retained first capture has no exact typed retry reason.")
                };
                // Native result 8 projects to null. Keep the same opaque prior,
                // fresh completion and retained candidate for the next drain.
                if (capture is null) return false;
                completion.RetainFirstCapture(_group, capture);
                _firstCaptureAdmission = FirstCaptureAdmission.Issue(this, initializer, completion, capture, _initialHost!);
                _resourceRetryFirst = null;
                _initializationRetryKind = InitializationRetryKind.None;
                return _firstCaptureAdmission.TryAdmit();
            }
            if (_resourceRetryTransfer is { } retryTransfer)
            {
                // Derive the graph while completion is still an unconsumed
                // phase-four candidate. Native retry consumes it into phase six.
                var retryGraph = completion.DescribeSuccessor(_binding);
                var retryNative = _initializationRetryKind switch
                {
                    InitializationRetryKind.AudioInterruption => retryTransfer.RetryAudioInterruptedInitialization(completion.NativeIdentityFor(_group)),
                    InitializationRetryKind.LowerPitch => retryTransfer.RetryLowerPitchInitialization(completion.NativeIdentityFor(_group)),
                    InitializationRetryKind.Backing => retryTransfer.RetryBackingInitialization(completion.NativeIdentityFor(_group)),
                    InitializationRetryKind.Resource => retryTransfer.RetryResourceInitialization(completion.NativeIdentityFor(_group)),
                    _ => throw new UnsupportedSignatureException("The retained handoff has no exact typed retry reason.")
                };
                // Null preserves the predecessor's exact session/policy and
                // pending candidate; only native can mint a replacement object.
                if (retryNative is null) return false;
                completion.RetainTransfer(_group, retryNative);
                _initializationHandoff = InitializationHandoff.Issue(this, initializer, completion, retryNative, retryGraph);
                _resourceRetryTransfer = null;
                _initializationRetryKind = InitializationRetryKind.None;
                return _initializationHandoff.TryAdmit();
            }
            if (_initializationRetryKind == InitializationRetryKind.AudioInterruption)
                throw new UnsupportedSignatureException("An audio retry cannot fall back to an ordinary initializer admission.");
            if (_session is not { } predecessor)
            {
                if (_initialHost is null || _source is not null) return false;
                var capture = _group.BeginInitialCapture(completion.NativeIdentityFor(_group));
                try
                {
                    completion.RetainFirstCapture(_group, capture);
                    _firstCaptureAdmission = FirstCaptureAdmission.Issue(this, initializer, completion, capture, _initialHost);
                }
                catch (Exception error)
                {
                    _transferFailure = "The consumed first initializer capture failed closed: " + error.Message; throw;
                }
                return _firstCaptureAdmission.TryAdmit();
            }
            var graph = completion.DescribeSuccessor(_binding);
            var native = predecessor.BeginOwnInitializationHandoff(initializer, completion);
            try
            {
                completion.RetainTransfer(_group, native);
                _initializationHandoff = InitializationHandoff.Issue(this, initializer, completion, native, graph);
                predecessor.Withdraw("The exact initializer entered its dedicated native same-run transfer.");
            }
            catch (Exception error)
            {
                _transferFailure = "The consumed initializer transfer failed closed: " + error.Message;
                throw;
            }
        }
        return _initializationHandoff.TryAdmit();
    }

    private sealed class FirstCaptureAdmission
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly NativePlaybackInitializationTracker _initializer;
        private readonly NativePlaybackInitializationTracker.Completion _completion;
        private readonly NativePlaybackSingleReturnBridge.InitialCapture _native;
        private readonly NativePlaybackInitializationHost _host;
        private readonly NativePlaybackStockRootedSource? _predecessorCandidate;
        private NativePlaybackStockRootedSource? _successor;
        private NativePlaybackInitializationTracker.CapturePermit? _permit;
        private bool _consumed;
        internal NativePlaybackSingleReturnBridge.InitialCapture NativeForResourceRetry => _native;
        internal NativePlaybackStockRootedSource? CandidateForResourceRetry => _successor;
        private FirstCaptureAdmission(NativePlaybackStockClockRouter owner, NativePlaybackInitializationTracker initializer,
            NativePlaybackInitializationTracker.Completion completion, NativePlaybackSingleReturnBridge.InitialCapture native,
            NativePlaybackInitializationHost host)
        {
            (_owner, _initializer, _completion, _native, _host) = (owner, initializer, completion, native, host);
            _predecessorCandidate = owner._resourceRetryCandidate;
        }
        internal static FirstCaptureAdmission Issue(NativePlaybackStockClockRouter owner, NativePlaybackInitializationTracker initializer,
            NativePlaybackInitializationTracker.Completion completion, NativePlaybackSingleReturnBridge.InitialCapture native,
            NativePlaybackInitializationHost host) => new(owner, initializer, completion, native, host);
        private void Registered()
        {
            _owner.Open(); _owner._group!.VerifyCurrent(); _host.VerifyCurrent(_owner._api, _owner._run);
            if (_consumed || !ReferenceEquals(_owner._firstCaptureAdmission, this) || !ReferenceEquals(_owner._initialHost, _host)
                || !ReferenceEquals(_owner._initialization, _initializer) || _owner._session is not null
                || (_successor is null
                    ? !ReferenceEquals(_owner._source, _predecessorCandidate)
                        || !ReferenceEquals(_owner._resourceRetryCandidate, _predecessorCandidate)
                    : !ReferenceEquals(_owner._source, _successor)))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied or consumed first capture has no exact route ownership.");
        }
        internal bool TryAdmit()
        {
            if (_owner.DeferInvalidatedInitialization(_initializer)) return false;
            Registered();
            if (_successor is null && !_native.TryAdmit()) return false;
            try
            {
                if (_successor is null)
                {
                    var binding = _completion.DescribeInitial(_owner._group!, _native);
                    var route = new object();
                    _permit = _completion.GrantFirstCapture(_owner._group!, _native);
                    _successor = NativePlaybackStockRootedSource.CaptureInitialized(_owner._api, binding, _host.Origin,
                        _owner._run, _owner._group!, _permit,
                        receipt => { if (ReferenceEquals(_owner._sourceRoute, route)) _owner._session?.Observe(receipt); },
                        reason => { if (ReferenceEquals(_owner._sourceRoute, route)) _owner._session?.Withdraw("The exact first source withdrew: " + reason); });
                    _owner.ReleaseResourceRetryCandidate(_successor);
                    _owner._binding = binding; _owner._origin = _host.Origin; _owner._source = _successor; _owner._sourceRoute = route;
                    _owner._admitted = null; _owner._callProfile = null;
                }
                if (_owner.DeferInvalidatedInitialization(_initializer)) return false;
                if (!_native.TryOpenSource())
                {
                    _owner.DeferInvalidatedInitialization(_initializer);
                    return false;
                }
                _successor.VerifyPlayerWatchActivated();
                _successor.CompleteInitializationCapture(_permit!);
                _initializer.Dispose();
                if (_initializer.RootsRetained) _owner._retainedInitializers.Add(_initializer);
                _host.ReleaseAfterSourceOpen(_owner, _successor, _native);
                _consumed = true; _owner._initialization = null; _owner._firstCaptureAdmission = null; _owner._initialHost = null;
                return true;
            }
            catch (Exception error)
            {
                if (_owner.DeferInvalidatedInitialization(_initializer)) return false;
                _consumed = true; _owner._sourceRoute = null;
                _owner._transferFailure = "The admitted first initialized graph failed closed: " + error.Message;
                if (_successor is not null && !ReferenceEquals(_owner._source, _successor)) _owner._retainedSources.Add(_successor);
                throw;
            }
        }
    }

    internal sealed partial class Session
    {
        internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginOwnInitializationHandoff(
            NativePlaybackInitializationTracker initializer, NativePlaybackInitializationTracker.Completion completion)
        {
            _owner.Open();
            if (!ReferenceEquals(_owner._session, this) || !_armed || _finalClock is null || _publication is null
                || _owner._handoff is not null || _owner._initializationHandoff is not null
                || !ReferenceEquals(_owner._initialization, initializer) || !ReferenceEquals(initializer.CaptureCompleted(), completion))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only this registered formerly armed session and exact retained initializer own same-run handoff.");
            _owner._group!.VerifyCurrent();
            return _publication.BeginInitializationHandoff(completion.NativeIdentityFor(_owner._group));
        }
    }

    private sealed class InitializationHandoff
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly NativePlaybackInitializationTracker _initializer;
        private readonly NativePlaybackInitializationTracker.Completion _completion;
        private readonly NativePlaybackSingleReturnBridge.LogicalHandoff _native;
        private readonly NativePlaybackBinding _graph;
        private readonly Session _predecessor;
        private NativePlaybackStockRootedSource? _successor;
        private NativePlaybackInitializationTracker.CapturePermit? _permit;
        private bool _consumed;
        internal NativePlaybackSingleReturnBridge.LogicalHandoff NativeForResourceRetry => _native;
        internal NativePlaybackStockRootedSource? CandidateForResourceRetry => _successor;
        internal Session PredecessorForResourceRetry => _predecessor;
        private InitializationHandoff(NativePlaybackStockClockRouter owner, NativePlaybackInitializationTracker initializer,
            NativePlaybackInitializationTracker.Completion completion, NativePlaybackSingleReturnBridge.LogicalHandoff native,
            NativePlaybackBinding graph)
            => (_owner, _initializer, _completion, _native, _graph, _predecessor) = (owner, initializer, completion, native, graph, owner._session!);
        internal static InitializationHandoff Issue(NativePlaybackStockClockRouter owner, NativePlaybackInitializationTracker initializer,
            NativePlaybackInitializationTracker.Completion completion, NativePlaybackSingleReturnBridge.LogicalHandoff native,
            NativePlaybackBinding graph) => new(owner, initializer, completion, native, graph);
        private void Registered()
        {
            _owner.Open(); _owner._group!.VerifyCurrent();
            if (_consumed || !ReferenceEquals(_owner._initializationHandoff, this) || !ReferenceEquals(_owner._initialization, _initializer)
                || (_successor is null ? !ReferenceEquals(_owner._session, _predecessor)
                    : !ReferenceEquals(_owner._source, _successor) || _owner._session is not null))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied, consumed or superseded initialization handoff has no route ownership.");
            _completion.VerifyTransferred(_owner._group, _native);
        }
        internal bool TryAdmit()
        {
            if (_owner.DeferInvalidatedInitialization(_initializer)) return false;
            Registered();
            if (_successor is null && !_native.TryAdmit()) return false;
            try
            {
                if (_successor is null)
                {
                    var info = _native.Read(2);
                    if (info.NewEpoch > long.MaxValue) throw new InvalidOperationException("Initialized source generation overflow.");
                    var binding = _graph with { SourceGeneration = checked((long)info.NewEpoch) };
                    _owner._run.VerifyCurrent(binding);
                    var origin = _owner._origin;
                    var route = new object();
                    _permit = _completion.GrantCapture(_owner._group!, _native, binding);
                    _successor = NativePlaybackStockRootedSource.CaptureInitialized(_owner._api, binding, origin, _owner._run,
                        _owner._group!, _permit,
                        receipt => { if (ReferenceEquals(_owner._sourceRoute, route)) _owner._session?.Observe(receipt); },
                        reason => { if (ReferenceEquals(_owner._sourceRoute, route)) _owner._session?.Withdraw("The exact initialized source withdrew: " + reason); });
                    _owner.ReleaseResourceRetryCandidate(_successor);
                    // Admission drained the pre-cut borrowers, but another raw
                    // GetTime original can enter while API capture stages the
                    // new watch. Keep the predecessor's exact roots until that
                    // real borrower drains; source-open remains natively gated.
                    var priorSource = _owner._source!;
                    _owner.RetireFreshPredecessor(_native);
                    priorSource.Dispose(); priorSource.PumpCompleted();
                    if (!priorSource.RootsReleased && !_owner._retainedSources.Contains(priorSource))
                        _owner._retainedSources.Add(priorSource);
                    _owner._logicalObservations.Clear();
                    _owner._session = null; _owner._binding = binding; _owner._origin = origin;
                    _owner._admitted = _native; _owner._callProfile = null;
                    _owner._source = _successor; _owner._sourceRoute = route;
                }
                if (_owner.DeferInvalidatedInitialization(_initializer)) return false;
                if (!_native.TryOpenSource())
                {
                    _owner.DeferInvalidatedInitialization(_initializer);
                    return false;
                }
                _successor.VerifyPlayerWatchActivated();
                _successor.CompleteInitializationCapture(_permit!);
                _initializer.Dispose();
                if (_initializer.RootsRetained) _owner._retainedInitializers.Add(_initializer);
                _owner.RegisterFormalSuccessor(_native, _successor);
                _consumed = true; _owner._initialization = null; _owner._initializationHandoff = null;
                return true;
            }
            catch (Exception error)
            {
                if (_owner.DeferInvalidatedInitialization(_initializer)) return false;
                _consumed = true; _owner._sourceRoute = null;
                _owner._transferFailure = "The admitted initializer successor failed closed: " + error.Message;
                if (_successor is not null && !ReferenceEquals(_owner._source, _successor)) _owner._retainedSources.Add(_successor);
                throw;
            }
        }
    }
}
