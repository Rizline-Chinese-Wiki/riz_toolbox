namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunLaunchCoordinator
{
    private NativeLocalActiveDestructionTracker.FactoryRetirementSeal? _factoryRetirementSeal;
    private NativeLocalActiveSourceClosureSeal? _activeSourceClosure;
    private NativeLocalActiveConsumersEndedVerifier? _activeConsumersVerifier;
    private object? _activeReturnIssuer;
    private LocalRunRetirementRequest? _activeReturnRequest;
    private NativeLocalActiveConsumerAssessment? _activeConsumerAssessment;
    private bool _factoryRootsReleased;

    // Captures only an already observed death fact. It neither closes the
    // registry nor supplies the missing all-consumers-ended verifier.
    private void TryFreezeActiveFactoryRetirement()
    {
        _main.VerifyAccess();
        if (_factoryRetirementSeal is not null || _activeDestruction is null || _controller is null
            || _reservation is null || _engineCoroutines is null || _coroutineCancellation is null)
            return;
        var death = _activeDestruction.Read();
        var engine = _engineCoroutines.Read();
        if (death.Unknown || !death.FiveObjectsDestroyed || death.Closed || engine.Unknown
            || !engine.NativeKnown || engine.NativeInFlight != 0 || engine.Receivers is not { Unknown: false } receivers
            || receivers.Receivers.Count != 2
            || receivers.Receivers.Any(row => row.Phase != NativeEngineCoroutineReceiverPhase.ConfirmedDead)
            || _coroutineCancellation.Unknown)
            return;
        var seal = _activeDestruction.FreezeForActiveFactoryRetirement(_reservation.State, _controller);
        if (seal is null) return;
        // No callback is allowed between adoption and stopping the old pump.
        // The next frame takes TickActiveFactoryRetirement before any source read.
        _factoryRetirementSeal = seal;
        _phase = NativeLocalRunLaunchPhase.AwaitingFactoryRetirement;
    }

    // Runs before the ordinary cancellation-retention early return. Sampling
    // an already independently dead source retains every root; it cannot Close
    // or manufacture cancellation completion. Never reread a frozen death source.
    private void TryObserveActiveSourceClosure(bool controllerDestroyed)
    {
        _main.VerifyAccess();
        PumpRequestedActiveReturn();
        if (!controllerDestroyed || _activeDestruction is null || _engineCoroutines is null
            || _reservation is null || _controller is null || _coroutineCancellation is null) return;
        if (_factoryRetirementSeal is null)
        {
            _activeDestruction.RequestStagingDestroyOnce();
            TryFreezeActiveFactoryRetirement();
        }
        if (_factoryRetirementSeal is null) return;
        _activeSourceClosure ??= _factoryRetirementSeal.TryAcquireSourceClosure(_reservation.State, _controller, _engineCoroutines);
        if (_activeSourceClosure is not null)
            _engineCoroutines.TryConfirmCancellationCompletion(_activeSourceClosure);
    }

    private void TickActiveFactoryRetirement()
    {
        _main.VerifyAccess();
        if (_factoryRootsReleased) { TickActiveOwnerRetirement(); return; }
        var current = _host.Sessions.Read(_session!);
        if (current.Phase == LocalRunSessionPhase.CleanupUnknown)
            throw new InvalidOperationException("The exact native consumer close became Unknown before factory retirement.");
        if (current.Phase != LocalRunSessionPhase.Closed)
        {
            // Freezing death observations does not freeze engine completion or
            // naturally ended iterator retirement. Keep consuming those facts
            // while this exact run is still protected, without touching the
            // frozen destruction source again.
            if (current.ProtectionRetired || current.CleanupAttempted)
                throw new InvalidOperationException("An incomplete native close withdrew protection before factory retirement.");
            _engineCoroutines!.RefreshTrackedIdentities();
            if (_engineCoroutines.Unknown || _coroutineCancellation!.Unknown)
                throw new InvalidOperationException("The retained engine or cancellation observation became Unknown.");
            TryObserveActiveSourceClosure(controllerDestroyed: true);
            if (_coroutineCancellation.MustRetainCoroutineResources()) return;
            _cutoutCoroutines!.RecycleNaturallyEnded();
            _terminalCoroutines!.RecycleNaturallyEnded();
            if (!_initialCoroutines!.Read().Closed) _initialCoroutines.TryReleaseAfterEnded();
            if (!current.ReturnRequested) return;
            var request = NativeLocalActiveConsumersEndedVerifier.Request(current);
            if (_activeReturnRequest is null)
            {
                _activeReturnRequest = request; _activeReturnIssuer = new object();
                _activeConsumersVerifier = new(this, _host, _session!, _activeReturnIssuer, request);
            }
            else if (_activeReturnRequest != request)
                throw new InvalidOperationException("Active return changed its exact session, controller or result generation.");
            if (!_host.TryRetireActive(_session!, _activeConsumersVerifier!, out var closed) || !closed) return;
        }
        // The native bridge owns independent roots and must withdraw its exact
        // progress/initialization classification before this factory free pass.
        if (!_bridge.TryRetire(_session!)) return;
        if (!ActiveFactoryRetirementDrained()) return;
        if (!_controller!.TryCompleteActiveDestroy(_reservation!.State, _factoryRetirementSeal!, this)) return;
        _parameters!.Dispose();
        if (!_controller.IsClosed || _controller.CleanupUnknown || !_parameters.IsClosed || _parameters.CleanupUnknown)
            throw new InvalidOperationException("The original active factory or parameter roots did not finish independent cleanup.");
        if (!TryRetireActiveObserverOwners()) return;
        if (_activeSourceClosure is null
            || !_engineCoroutines!.TryReleaseAfterActiveClose(_reservation.State, _controller, _factoryRetirementSeal!, _activeSourceClosure, this)) return;
        if (!_activeDestruction!.TryReleaseFrozenObservationRootsAfterClose(_reservation.State, _controller, _factoryRetirementSeal!, this)) return;
        if (!ReleaseActiveReturnObserversAfterClose()) return;
        _factoryRootsReleased = true;
        _phase = NativeLocalRunLaunchPhase.FactoryRootsReleased;
        // Other independently retained owners still prevent process-owner removal.
        // Only their real final retirement can make this entire launch complete.
    }

    internal void RequireActiveConsumerIssuer(LocalRunProtectionHost host,
        LocalRunSessionRegistry.SessionHandle session, object issuer, LocalRunRetirementRequest request)
    {
        if (_main is null) throw new InvalidOperationException("A constructed coordinator label has no retained active consumer issuer.");
        _main.VerifyAccess();
        if (!_busy || !ReferenceEquals(_host, host) || !ReferenceEquals(_session, session)
            || !ReferenceEquals(_activeReturnIssuer, issuer) || _activeReturnRequest != request
            || _factoryRetirementSeal is null || _reservation is null || _controller is null
            || _phase != NativeLocalRunLaunchPhase.AwaitingFactoryRetirement)
            throw new InvalidOperationException("Only this retained coordinator's actual active return can issue its consumer verifier.");
        _factoryRetirementSeal.ValidateFor(_reservation.State, _controller);
        var current = host.Sessions.Read(session);
        if (!NativeLocalActiveConsumersEndedVerifier.MatchesProtectedReturn(request, current))
            throw new InvalidOperationException("The exact active return is no longer protected or has a different result generation.");
    }
    internal bool IsIssuedActiveConsumersVerifier(NativeLocalActiveConsumersEndedVerifier verifier,
        LocalRunProtectionHost host, LocalRunSessionRegistry.SessionHandle session, object issuer,
        LocalRunRetirementRequest request)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_activeConsumersVerifier, verifier)) return false;
        RequireActiveConsumerIssuer(host, session, issuer, request); return true;
    }

    internal NativeLocalActiveConsumerAssessment AssessActiveConsumers(NativeLocalActiveConsumersEndedVerifier verifier,
        LocalRunSessionRegistry.SessionHandle session, LocalRunRetirementRequest request)
    {
        if (!ReferenceEquals(_activeConsumersVerifier, verifier) || _activeReturnIssuer is null)
            throw new InvalidOperationException("A constructed or foreign verifier has no active consumer authority.");
        RequireActiveConsumerIssuer(_host, session, _activeReturnIssuer, request);
        var checks = new List<NativeLocalActiveConsumerCheck>();
        void Check(NativeLocalActiveConsumerObligation kind, bool known, bool complete, string detail)
            => checks.Add(new(kind, !known ? NativeLocalActiveConsumerProofState.Unknown
                : complete ? NativeLocalActiveConsumerProofState.Proven : NativeLocalActiveConsumerProofState.Pending, detail));
        var current = _host.Sessions.Read(session); var launch = _reservation!.State;
        Check(NativeLocalActiveConsumerObligation.ReturnRequest, true,
            current.ReturnRequested && NativeLocalActiveConsumersEndedVerifier.Request(current) == request,
            "Same retained session and controller/result/return identity.");
        Check(NativeLocalActiveConsumerObligation.ProviderWithdrawal, true, current.ProviderWithdrawn,
            "The registry's actual provider must be withdrawn before active Close.");
        _factoryRetirementSeal!.ValidateFor(launch, _controller!);
        Check(NativeLocalActiveConsumerObligation.FactoryDeathSeal, true,
            _factoryRetirementSeal.Session == current.Identity && _factoryRetirementSeal.Identity.Controller == current.ControllerIdentity,
            "Five independent death observations retain this source's issued immutable factory seal.");
        var engine = _engineCoroutines!.Read(); var receivers = engine.Receivers;
        Check(NativeLocalActiveConsumerObligation.ReceiverDeathSources, !engine.Unknown && receivers?.Unknown == false,
            receivers is not null && receivers.Receivers.Count == 2 && receivers.Receivers.Select(row => row.Role).Distinct().Count() == 2
                && receivers.Receivers.All(row => row.Phase == NativeEngineCoroutineReceiverPhase.ConfirmedDead
                    && row.ZeroObserved && row.IndependentDeathObserved && row.CachedNativeOwner == 0),
            "Controller and EventTrigger require distinct retained identities, cached-owner zero and independent death sources.");
        Check(NativeLocalActiveConsumerObligation.TrackerGenerations, !engine.Unknown && engine.Identities?.Unknown == false,
            _engineCoroutines.MayRetireTrackedObserverRoots(),
            "Actual provider/tracker identities must match final native node and Start generations.");
        engine = _engineCoroutines.Read();
        Check(NativeLocalActiveConsumerObligation.InlineCompletion, engine.Inline?.Unknown == false,
            engine.Inline?.Outstanding == 0 && engine.Identities?.Nodes.Nodes.All(row => !row.InlineCleanupUnproven) == true,
            "Every observed inline Start branch must have its actual complete/dispose and final cleanup receipt.");
        Check(NativeLocalActiveConsumerObligation.SchedulerRows, engine.Scheduler?.Unknown == false,
            engine.Scheduler is { Outstanding: 0 } scheduler && scheduler.Records.All(row => row.State == NativeEngineSchedulerRowState.Retired),
            "Every actual M registration generation is retired; idle alone does not close future producers.");
        Check(NativeLocalActiveConsumerObligation.WaitPublications, engine.Wait?.Unknown == false,
            engine.Wait is { Outstanding: 0 } wait && wait.Records.All(row => row.State == NativeEngineWaitState.Retired),
            "Every actual N publication generation is retired, with no dispatch/callback/cleanup duty.");
        var cancellation = _coroutineCancellation!.Read();
        Check(NativeLocalActiveConsumerObligation.CancellationRequests, !cancellation.Unknown && cancellation.NativeKnown,
            !cancellation.Ledger.Pending && cancellation.Ledger.Outstanding == 0
                && (!cancellation.Ledger.HasObservedRequests || cancellation.Ledger.EngineCancelled),
            "Owned Stop requests require the actual issued completion receipt; Stop return cannot substitute.");
        Check(NativeLocalActiveConsumerObligation.NativeActivity, !engine.Unknown && engine.NativeKnown,
            engine.NativeInFlight == 0, "Actual full engine activity must remain known and drained.");
        var boundaries = _host.ReadNativeBoundaryActivity();
        Check(NativeLocalActiveConsumerObligation.HostActivity, boundaries.Known, boundaries.InFlight == 0,
            "Actual host native boundaries must be drained before classification withdrawal.");
        var events = _unityEvents!.Read();
        Check(NativeLocalActiveConsumerObligation.UnityEventConsumers, !events.Unknown,
            events.NativeInFlight == 0 && events.Invocations == events.ReturnedInvocations
                && events.InvokableDispatches == events.ReturnedDispatches && events.ObservedMutations == events.ReturnedMutations
                && _unityEvents.TryConfirmNativeIdle() && ListenerEffectsAccounted(events),
            "Actual UnityEvent calls/mutations must complete; arbitrary listener dispatch effects require their own exact completion owner and are not proved by scene unloading.");
        var cutout = _cutoutEvents!.Read();
        Check(NativeLocalActiveConsumerObligation.CutoutDispatch, !cutout.Unknown && cutout.Session == current.Identity,
            cutout.Source is { Alive: false } && cutout.InFlight == 0 && cutout.Entered == cutout.Exited
                && _cutoutEvents.TryConfirmNativeIdle(),
            "Exact retained cutout source is independently dead and all its physical dispatches returned.");
        foreach (var kind in new[] { NativeLocalActiveConsumerObligation.FutureNodeSource,
            NativeLocalActiveConsumerObligation.FutureSchedulerSource, NativeLocalActiveConsumerObligation.FutureWaitSource })
            checks.Add(ReadActiveClosure(_activeSourceClosure, launch, kind));
        checks.Add(ReadActiveClosure(_sceneTransition, launch, NativeLocalActiveConsumerObligation.AsyncConsumers));
        checks.Add(ReadActiveClosure(_globals, launch, NativeLocalActiveConsumerObligation.GlobalReferences));
        checks.Add(ReadActiveClosure(_handler, launch, NativeLocalActiveConsumerObligation.SceneResultConsumers));
        _activeConsumerAssessment = NativeLocalActiveConsumersEndedVerifier.Assess(checks);
        // The actual globals/scene inspections above may execute native
        // queries. Do not combine their later results with an earlier idle
        // engine snapshot: recheck every retained physical source at the end.
        engine = _engineCoroutines.Read(); cancellation = _coroutineCancellation.Read();
        boundaries = _host.ReadNativeBoundaryActivity();
        if (engine.Unknown || !engine.NativeKnown || engine.NativeInFlight != 0
            || cancellation.Unknown || !cancellation.NativeKnown || cancellation.NativeInFlight != 0
            || cancellation.Ledger.Pending || cancellation.Ledger.Outstanding != 0
            || !boundaries.Known || boundaries.InFlight != 0
            || _activeSourceClosure?.CoversEngineCancellationSources(_engineCoroutines) != true)
            return _activeConsumerAssessment = NativeLocalActiveConsumersEndedVerifier.Assess(checks.Select(row =>
                row.Obligation == NativeLocalActiveConsumerObligation.NativeActivity
                    ? row with { State = NativeLocalActiveConsumerProofState.Pending, Detail = "Actual physical activity or source closure changed during native consumer verification." } : row));
        RequireActiveConsumerIssuer(_host, session, _activeReturnIssuer!, request);
        return _activeConsumerAssessment;
    }
    private static NativeLocalActiveConsumerCheck ReadActiveClosure(object? exactOwner, LocalRunLaunchCommitState launch,
        NativeLocalActiveConsumerObligation obligation)
        => exactOwner is INativeLocalActiveConsumerClosureSource source
            ? source.ReadActiveConsumerClosure(launch, obligation)
            : new(obligation, NativeLocalActiveConsumerProofState.Missing, "The retained exact owner has not issued this source's closure facts.");

    private bool ListenerEffectsAccounted(NativeLocalUnityEventLedgerSnapshot events)
    {
        // Complete-empty past invocations do not seal future direct,
        // serialized, generic or externally retained listener writers.
        if (events.Unknown || !TweenWorkCanRetire()) return false;
        var proof = ReadActiveClosure(_unityEvents, _reservation!.State, NativeLocalActiveConsumerObligation.UnityEventConsumers);
        return proof.Obligation == NativeLocalActiveConsumerObligation.UnityEventConsumers
            && proof.State == NativeLocalActiveConsumerProofState.Proven;
    }

    // A zero observed child-dispatch count is not a proof of an empty actual
    // executing list. The bounded snapshot currently retains one invocation
    // generation per event, so repeated Invoke also needs a separate effects
    // owner rather than borrowing its last generation's empty list.
    internal static bool ListenerEffectsIndependentlyEmpty(NativeLocalUnityEventLedgerSnapshot events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Unknown || events.NativeInFlight != 0 || events.AbnormalDispatches != 0
            || events.Invocations < 0 || events.ReturnedInvocations != events.Invocations
            || events.InvokableDispatches != 0 || events.ReturnedDispatches != 0
            || events.ObservedMutations != events.ReturnedMutations) return false;
        if (events.EventSnapshots.Any(row => row.Entered < 0 || row.Exited != row.Entered || row.InFlight != 0
            || row.Dispatches is { Count: > 0 })) return false;
        if (events.Invocations == 0) return events.EventSnapshots.All(row => row.Entered == 0 && !row.NativeInvokeObserved);
        if (events.InlineExecutionUnobserved || !events.DispatchPathVerified
            || events.Events != events.EventSnapshots.Count || events.ObservedMutations != 0
            || events.EventSnapshots.Sum(row => row.Entered) != events.Invocations) return false;
        foreach (var row in events.EventSnapshots.Where(row => row.Entered != 0))
        {
            if (row.Entered != 1 || !row.Returned || !row.NativeInvokeObserved || row.InvokeCaller == 0 || row.InvokeMethod == 0
                || row.Generation <= 0 || row.MutationGeneration != 0 || row.Mutations is { Count: > 0 }
                || row.Dispatches is null || row.Inline is not { } actual || actual.NativeToken == 0
                || actual.ParentCaller != row.InvokeCaller || actual.MidReturnPc == 0
                || actual.Graph.Event != row.Event || actual.Graph.Calls != row.Initial.Calls
                || actual.Executing.Object == 0 || actual.Executing.Rows.Count != 0
                || actual.Graph.ExecutingCalls.Object != actual.Executing.Object
                || actual.Graph.ExecutingCalls.Version != actual.Executing.Version
                || actual.Graph.ExecutingCalls.Rows.Count != 0) return false;
        }
        return true;
    }

    private bool ActiveFactoryRetirementDrained()
    {
        if (_engineCoroutines is null || _coroutineCancellation is null
            || _engineCoroutines.Unknown || _coroutineCancellation.Unknown
            || _coroutineCancellation.MustRetainCoroutineResources()) return false;
        var engine = _engineCoroutines.Read();
        return !engine.Unknown && engine.NativeKnown && engine.NativeInFlight == 0
            && engine.Receivers is { Unknown: false, AwaitingIndependentDeath: false } receivers
            && receivers.Receivers.Count == 2
            && receivers.Receivers.All(row => row.Phase == NativeEngineCoroutineReceiverPhase.ConfirmedDead)
            && _engineCoroutines.MayRetireTrackedObserverRoots();
    }

    // Invoked before and after EACH independent factory root free. Identity,
    // frozen-source adoption and exact close facts cannot be supplied by a bool
    // parameter or an external caller-created receipt.
    internal void VerifyActiveControllerFactoryRetirement(OwnedNativeInactiveController exactOwner,
        NativeLocalActiveDestructionTracker.FactoryRetirementSeal seal)
    {
        _main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(exactOwner); ArgumentNullException.ThrowIfNull(seal);
        if (!_busy || _phase is not (NativeLocalRunLaunchPhase.AwaitingFactoryRetirement or NativeLocalRunLaunchPhase.FactoryRootsReleased)
            || _wholeLaunchRetired || !ReferenceEquals(_controller, exactOwner)
            || !ReferenceEquals(_factoryRetirementSeal, seal) || _reservation is null || _session is null
            || _reservation.State.Read().Phase == LocalRunLaunchCommitPhase.Unknown)
            throw new InvalidOperationException("Active factory cleanup requires its adopted frozen source in the actual retirement pump.");
        seal.ValidateFor(_reservation.State, exactOwner);
        var state = _reservation.State.Read(); var current = _host.Sessions.Read(_session);
        var graph = _bridge.Describe(); var boundaries = _host.ReadNativeBoundaryActivity();
        if (current.Identity != state.Identity || current.ControllerIdentity != state.Controller
            || current.Phase != LocalRunSessionPhase.Closed || !current.ReturnRequested || !current.ProtectionRetired
            || !current.CleanupAttempted || current.OutstandingOwnershipRecords != 0
            || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence)
            || _host.Sessions.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, state.Controller) is not null
            || current.ResultSceneIdentity != 0 && (
                _host.Sessions.FindProtectedSession(LocalBoundaryKind.ResultStart, current.ResultSceneIdentity) is not null
                || _host.Sessions.FindProtectedSession(LocalBoundaryKind.ResultBack, current.ResultSceneIdentity) is not null)
            || graph.CleanupUnknown || graph.Records != 0 || graph.PublishedMappings != 0
            || !boundaries.Known || boundaries.InFlight != 0 || !ActiveFactoryRetirementDrained())
            throw new InvalidOperationException("The exact consumer close, withdrawn mappings or native drain changed during active factory cleanup.");
    }
}
