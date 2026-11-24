namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeEngineCoroutineRuntimeCoordinator
{
    private readonly object _activeSourceClosureIssuer = new();
    private NativeLocalActiveSourceClosureSeal? _activeSourceClosure;
    private OwnedNativeInactiveController? _activeSourceFactory;
    private NativeLocalActiveDestructionTracker.FactoryRetirementSeal? _activeSourceDeath;
    private NativeCoroutineCancellationIdentityProvider[]? _activeSourceProviders;
    private bool _issuingActiveSourceClosure, _activeCloseBusy, _activeCloseAttempted, _activeCloseCompleted;
    private readonly bool[] _activeRootFreeAttempted = new bool[2];

    internal bool ActiveObservationRootsReleased
    { get { _main.VerifyAccess(); return _activeCloseCompleted && !Unknown && _roots.All(root => root is null); } }

    internal NativeLocalActiveSourceClosureSeal? TryAcquireActiveSourceClosure(NativeLocalActiveDestructionTracker exactSource,
        LocalRunLaunchCommitState exactLaunch, OwnedNativeInactiveController exactFactory,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal death)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(exactSource); ArgumentNullException.ThrowIfNull(exactFactory);
        death.ValidateFor(exactLaunch, exactFactory);
        if (!ReferenceEquals(exactSource, _activeDestruction) || !ReferenceEquals(exactLaunch, _launch)
            || !death.Production || !exactFactory.IsProductionActiveFactory)
            throw new InvalidOperationException("Production source closure requires this coordinator's exact retained death source and factory.");
        if (_activeSourceClosure is { } previous) { previous.ValidateFor(exactLaunch, exactFactory); return previous; }
        if (!ReceiverSourcesClosed() || _providers is null || _providers.Length != 3 || _activeCloseAttempted) return null;
        _activeSourceFactory = exactFactory; _activeSourceDeath = death; _activeSourceProviders = _providers.ToArray();
        _issuingActiveSourceClosure = true;
        try { _activeSourceClosure = new(this, _activeSourceClosureIssuer, exactLaunch, exactFactory, death); }
        finally { _issuingActiveSourceClosure = false; }
        return _activeSourceClosure;
    }

    internal bool IsIssuingActiveSourceClosure(object issuer, LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController factory, NativeLocalActiveDestructionTracker.FactoryRetirementSeal death)
        => _issuingActiveSourceClosure && ReferenceEquals(issuer, _activeSourceClosureIssuer)
            && ReferenceEquals(launch, _launch) && ReferenceEquals(factory, _activeSourceFactory)
            && ReferenceEquals(death, _activeSourceDeath) && _activeSourceClosure is null && ReceiverSourcesClosed();

    internal void RequireIssuedActiveSourceClosure(NativeLocalActiveSourceClosureSeal seal, LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController factory, NativeLocalActiveDestructionTracker.FactoryRetirementSeal death)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(seal, _activeSourceClosure) || !ReferenceEquals(launch, _launch)
            || !ReferenceEquals(factory, _activeSourceFactory) || !ReferenceEquals(death, _activeSourceDeath)
            || _providers is null || _activeSourceProviders is null || _providers.Length != _activeSourceProviders.Length
            || _providers.Where((provider, index) => !ReferenceEquals(provider, _activeSourceProviders[index])).Any()
            || !ReceiverSourcesClosed())
            throw new InvalidOperationException("Engine source closure lost its exact issuer, provider owners, receiver generations or known state.");
    }

    private bool ReceiverSourcesClosed()
    {
        _main.VerifyAccess();
        if (!_installed || Unknown || _receivers is null || _pairs.Length != 2 || _identities is null
            || _inline is null || _scheduler is null || _wait is null) return false;
        var receivers = _receivers.Read();
        if (receivers.Unknown || receivers.AwaitingIndependentDeath || receivers.Receivers.Count != 2) return false;
        for (var index = 0; index < 2; ++index)
        {
            var row = receivers.Receivers[index];
            if ((int)row.Role != index || row.Retained != _pairs[index] || row.CachedNativeOwner != 0
                || !row.ZeroObserved || !row.IndependentDeathObserved || row.Phase != NativeEngineCoroutineReceiverPhase.ConfirmedDead)
                return false;
        }
        // These facts come from two independent retained sources and actual
        // cached-owner observations. CaptureFrame.RequireNewOwnedEntry rejects
        // any later owned entry, making such a violation permanently Unknown.
        return true;
    }

    internal bool ReadSourceClosureProof(NativeLocalActiveSourceClosureSeal seal, NativeLocalActiveConsumerObligation obligation)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(seal, _activeSourceClosure) || !ReceiverSourcesClosed()) return false;
        if (obligation == NativeLocalActiveConsumerObligation.FutureNodeSource) return true;
        if (obligation is not NativeLocalActiveConsumerObligation.FutureSchedulerSource
            and not NativeLocalActiveConsumerObligation.FutureWaitSource) return false;
        var activity = _runtime.ReadActivity(); var identities = _identities!.Read(); var inline = _inline!.Read();
        var scheduler = _scheduler!.Read(); var wait = _wait!.Read();
        if (!activity.Known || activity.InFlight != 0 || identities.Unknown || identities.Outstanding != 0 || identities.Untracked != 0
            || identities.Nodes.Unknown || identities.Nodes.Outstanding != 0 || identities.Nodes.Untracked != 0
            || inline.Unknown || inline.Outstanding != 0 || scheduler.Unknown || scheduler.Pending || scheduler.Outstanding != 0
            || wait.Unknown || wait.Pending || wait.Outstanding != 0 || identities.Nodes.Nodes.Count != identities.Identities.Count)
            return false;
        foreach (var selected in identities.Identities)
        {
            if (selected.Node == 0 || selected.EngineGeneration <= 0 || selected.StartSequence == 0
                || selected.StartSequence > (ulong)long.MaxValue || !selected.NativeStartReturned || !selected.StartDisposed
                || !NativeEngineCoroutineObserverRetirement.HasRetainedSource(identities, selected)
                || !_pairs.Any(pair => pair.NativeOwner == selected.NativeOwner && pair.ManagedReceiver == selected.ManagedReceiver)) return false;
            var nodes = identities.Nodes.Nodes.Where(node => node.Node == selected.Node && node.Generation == selected.EngineGeneration
                && node.StartSequence == checked((long)selected.StartSequence) && node.NativeOwner == selected.NativeOwner
                && node.Enumerator == selected.Enumerator).ToArray();
            if (nodes.Length != 1 || !nodes[0].FinalCleanupObserved || !nodes[0].StartCompleted
                || nodes[0].OpenCalls != 0 || nodes[0].ActiveExecutions != 0 || nodes[0].InlineCleanupUnproven
                || !_scheduler.MayRetireNodeGeneration(selected) || !_wait.MayRetireNodeGeneration(selected)) return false;
        }
        return ReceiverSourcesClosed();
    }

    internal bool TryReleaseAfterActiveClose(LocalRunLaunchCommitState launch, OwnedNativeInactiveController factory,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal death, NativeLocalActiveSourceClosureSeal sources,
        NativeLocalRunLaunchCoordinator authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority); ArgumentNullException.ThrowIfNull(sources);
        if (_activeCloseBusy) throw new InvalidOperationException("Engine observation retirement cannot reenter.");
        sources.ValidateFor(launch, factory);
        if (!ReferenceEquals(sources, _activeSourceClosure) || !ReferenceEquals(death, _activeSourceDeath))
            throw new InvalidOperationException("Engine release requires its one exact retained source closure.");
        if (_activeCloseCompleted) return true;
        if (_activeCloseAttempted || Unknown) return false;
        void Verify()
        {
            sources.ValidateFor(launch, factory);
            factory.VerifyActiveRootsClosedForObservationRetirement(launch, death);
            if (!sources.CoversEngineCancellationSources(this))
                throw new InvalidOperationException("Engine source closure or native side duties changed during independent root retirement.");
            authority.VerifyActiveControllerFactoryRetirement(factory, death);
        }
        Verify(); _activeCloseAttempted = _activeCloseBusy = true;
        try
        {
            // Withdraw only this owner's logical memberships. The permanent
            // physical native owners survive; withdrawal itself proves no drain.
            _subscription?.Dispose(); _subscription = null; Verify();
            _schedulerBinding?.Dispose(); _schedulerBinding = null; Verify();
            _waitBinding?.Dispose(); _waitBinding = null; Verify();
            for (var index = _roots.Length - 1; index >= 0; --index)
            {
                Verify();
                if (_roots[index] is not { } root || _activeRootFreeAttempted[index])
                    throw new InvalidOperationException("The exact engine receiver root is absent or its one free was already attempted.");
                _activeRootFreeAttempted[index] = true; root.Dispose(); _roots[index] = null;
                Verify();
            }
            if (_roots.Any(root => root is not null)) throw new InvalidOperationException("Independent engine receiver roots remain retained.");
            _activeCloseCompleted = true;
            lock (Gate) Owners.Remove(this);
            return true;
        }
        catch (Exception error) { Fault("Engine observation retirement is Unknown: " + error.GetType().Name); throw; }
        finally { _activeCloseBusy = false; }
    }
}
