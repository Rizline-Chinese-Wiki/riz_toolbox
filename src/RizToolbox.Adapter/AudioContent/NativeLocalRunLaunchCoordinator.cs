using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeLocalRunLaunchPhase { Preparing, Activating, AwaitingScene, AwaitingInitialization, Ready, Unknown,
    AwaitingControllerDestruction, ControllerDestroyed, AwaitingFactoryRetirement, FactoryRootsReleased }
internal sealed record NativeLocalRunLaunchSnapshot(NativeLocalRunLaunchPhase Phase,
    LocalRunSessionIdentity? Session, LocalRunLaunchCommitSnapshot? Commit, string? Failure,
    NativeLocalRunSceneTransitionSnapshot? SceneTransition,
    NativeLocalCutoutEventSourceSnapshot? CutoutEvents = null,
    NativeLocalActiveDestructionSnapshot? ActiveDestruction = null,
    NativeLocalCutoutCoroutineSnapshot? CutoutCoroutines = null,
    NativeLocalUnityEventLedgerSnapshot? UnityEvents = null,
    NativeCoroutineCancellationObserverSnapshot? CoroutineCancellation = null,
    NativeEngineCoroutineRuntimeSnapshot? EngineCoroutines = null,
    bool FactoryDeathObservationFrozen = false, bool FactoryRootsReleased = false,
    NativeLocalTweenWorkSnapshot? TweenWork = null);
internal sealed class NativeLocalRunLaunchException : InvalidOperationException
{
    internal NativeLocalRunLaunchCoordinator Responsibility { get; }
    internal NativeLocalRunLaunchException(NativeLocalRunLaunchCoordinator owner, Exception error)
        : base("LOCAL launch did not complete; the coordinator retains all acquired responsibility.", error) => Responsibility = owner;
}

/// <summary>Production composition of bundle, pending protection, actual inactive
/// clone, parameter setter, first activation and asynchronous Start/getter receipt.
/// No RunStarted event or consumer-end evidence is manufactured here. Callers must
/// install the complete host handler/commands before construction and supply the
/// actual current manager and judging roots during the synchronous Begin call.</summary>
internal sealed partial class NativeLocalRunLaunchCoordinator
{
    private const int MaximumOwners = 32;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalRunLaunchCoordinator> ProcessOwners = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly LocalRunProtectionHost _host;
    private readonly LocalRunTerminationHandler _handler;
    private readonly NativeLocalRunTerminationBoundaryState _bridge;
    private readonly NativeControllerAwakeLedger _awake;
    private readonly ExactHookBroker _broker;
    private readonly ILogger _log;
    private readonly Func<LocalRunLaunchCommitState, LocalRunModification>? _trackModification;
    private LocalRunModification? _modification;
    private NativeEngineRuntimeCoordinator? _engineRuntime;
    private NativeEngineCoroutineRuntimeCoordinator? _engineCoroutines;
    // All handoff storage and the process owner exist before the first native operation.
    private OwnedNativeLocalPlayBundle? _bundle;
    private OwnedNativeControllerPrefab? _prefab;
    private NativeRuntime.RootHandle? _prefabRoot, _registryController, _factoryController;
    private OwnedNativeControllerGlobals? _globals;
    private NativeControllerAwakeLedger.CaptureWindow? _window;
    private OwnedNativeInactiveController? _controller;
    private NativeInactiveControllerParameterBinding? _parameters;
    private NativeLocalRunLaunchReservation? _reservation;
    private NativeInactiveControllerActivation? _activation;
    private NativeLocalRunSceneTransition? _sceneTransition;
    private NativeLocalMenuLedger? _menu;
    private NativeLocalInitializationCoroutineTracker? _initialCoroutines;
    private NativeLocalTerminalCoroutineTracker? _terminalCoroutines;
    private NativeLocalActiveDestructionTracker? _activeDestruction;
    private NativeLocalCutoutEventSource? _cutoutEvents;
    private NativeLocalCutoutCoroutineTracker? _cutoutCoroutines;
    private NativeLocalUnityEventLedger? _unityEvents;
    private NativeCoroutineCancellationObserver? _coroutineCancellation;
    private LocalRunSessionRegistry.SessionHandle? _session;
    private LocalRunSessionRegistry.CommitClaim? _claim;
    private NativeLocalRunLaunchPhase _phase = NativeLocalRunLaunchPhase.Preparing;
    private bool _begun, _busy;
    private string? _failure;

    internal NativeLocalRunLaunchCoordinator(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, LocalRunProtectionHost host,
        LocalRunTerminationHandler handler, NativeLocalRunTerminationBoundaryState bridge,
        NativeControllerAwakeLedger awake, ExactHookBroker broker, ILogger log,
        Func<LocalRunLaunchCommitState, LocalRunModification>? trackModification = null)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(bridge); ArgumentNullException.ThrowIfNull(awake);
        ArgumentNullException.ThrowIfNull(broker); _broker = broker;
        ArgumentNullException.ThrowIfNull(log); _log = log;
        _trackModification = trackModification;
        if (!handler.UsesBoundaryState(bridge))
            throw new InvalidOperationException("Launch participants must use the same exact boundary bridge.");
        handler.ValidateHost(host.Sessions);
        (_api, _main, _profile, _module, _host, _handler, _bridge, _awake) =
            (api, main, profile, module, host, handler, bridge, awake);
    }

    // Complete discovery before registering a session or allocating the play bundle.
    internal Result PreparePrefab()
    {
        _main.VerifyAccess();
        if (_busy || _begun || _prefab is not null || _phase != NativeLocalRunLaunchPhase.Preparing)
            throw new InvalidOperationException("A launch coordinator requires an unused preparation.");
        RequireIdleHost();
        if (!NativeEngineRuntimeCoordinator.TryAcquire(_log, out _engineRuntime, out var engineStatus, out var engineReason))
            throw new UnsupportedSignatureException($"Engine coroutine observation {engineStatus}: {engineReason}");
        lock (Gate)
        {
            if (ProcessOwners.Count >= MaximumOwners || ProcessOwners.Any(owner => ReferenceEquals(owner._host, _host)))
                throw new InvalidOperationException("An earlier launch still owns this host or process launch capacity is exhausted.");
            ProcessOwners.Add(this);
        }
        _busy = true;
        try
        {
            var resolution = new NativeControllerPrefabResolver(_api, _main, _profile, _module).Resolve();
            return CompletePrefabPreparation(resolution, prefab => _prefab = prefab, ReleasePreparationSlot);
        }
        catch (Exception error) { Fail(error); throw new NativeLocalRunLaunchException(this, error); }
        finally { _busy = false; }
    }

    internal static Result CompletePrefabPreparation(ControllerPrefabResolution resolution,
        Action<OwnedNativeControllerPrefab> retain, Action releasePreparation)
    {
        if (resolution.Status == ControllerPrefabResolutionStatus.Ready && resolution.Prefab is { } prefab)
        {
            retain(prefab);
            return Result.Ok();
        }
        if (resolution.ConfirmedAbsenceError is { } error)
        {
            releasePreparation();
            return Result.Fail(error);
        }
        throw new InvalidOperationException(resolution.UnavailableReason);
    }

    internal void ReleasePreparedPrefab()
    {
        _main.VerifyAccess();
        if (_busy || _begun || _prefab is null || _phase != NativeLocalRunLaunchPhase.Preparing)
            throw new InvalidOperationException("Only an unused prefab preparation can be released.");
        _busy = true;
        try
        {
            _prefab.Dispose();
            _prefab = null;
            ReleasePreparationSlot();
        }
        catch (Exception error) { Fail(error); throw new NativeLocalRunLaunchException(this, error); }
        finally { _busy = false; }
    }

    private void ReleasePreparationSlot()
    {
        lock (Gate)
            if (!ProcessOwners.Remove(this))
                throw new InvalidOperationException("Prefab preparation lost its exact process owner.");
    }

    private void RequireIdleHost()
    {
        _handler.ValidateHost(_host.Sessions);
        var status = _host.Read();
        var activity = _host.ReadNativeBoundaryActivity();
        if (!status.AllGatesInstalled || !status.AcceptingNewRuns || status.CleanupUnknown
            || status.UnretiredSessions != 0 || !_bridge.IsReadyForHost(_host.Sessions)
            || !activity.Known || activity.InFlight != 0)
            throw new InvalidOperationException("A fresh launch requires the complete idle host and no older unretired run.");
    }

    /// <summary>Borrowed inputs are used only synchronously; each native owner
    /// retains its independent roots. A coordinator accepts exactly one run,
    /// including failed attempts. Retry requires a new complete run transaction.</summary>
    internal LocalRunSessionRegistry.SessionHandle Begin(LocalPlayRequest request,
        LocalNativeMetadataDescriptor descriptor, NativeRuntime.RootHandle judgeSource,
        NativeRuntime.RootHandle gameManager, NativeRuntime.RootHandle inLevelControl)
    {
        _main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(judgeSource); ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(inLevelControl);
        if (_busy || _begun || _prefab is null || _engineRuntime is null || _phase != NativeLocalRunLaunchPhase.Preparing)
            throw new InvalidOperationException("A launch coordinator requires its exact prepared prefab and accepts one launch.");
        _begun = true; _busy = true;
        try
        {
            RequireIdleHost();
            _prefabRoot = _api.Root(_prefab.BorrowPrefabPointer);
            // The later explicit Return command owns this independent manager
            // root even after the Begin caller's borrowed root has gone away.
            _activeReturnManager = _api.Root(gameManager.Target);
            // The factory forks CPU leases and independently roots actual resources.
            try { _bundle = new NativeLocalPlayBundleFactory(_api, _main, _profile, _module).CreateBorrowing(request, descriptor, judgeSource); }
            catch (NativeLocalPlayBundleCreationException error) { _bundle = error.Responsibility; throw; }
            var bundle = _bundle.Read();
            _session = _host.RegisterPrepared(new(descriptor.LocalId, descriptor.Revision, descriptor.ChartId),
                _bundle.MetadataIdentity, _bundle, request.ResultObservation);
            _claim = _host.Sessions.BeginCommit(_session);
            try { _reservation = NativeLocalRunLaunchReservation.Reserve(_api, _main, _profile, _module,
                _host, _handler, _bridge, _bundle, _claim); }
            catch (NativeLocalRunLaunchReservationException error) { _reservation = error.Responsibility; throw; }
            _modification = _trackModification?.Invoke(_reservation.State);

            _globals = OwnedNativeControllerGlobals.Capture(_api, _main, _profile, _module, gameManager, inLevelControl);
            _window = _awake.BeginCaptureWindow("LOCAL owned launch");
            _prefab.VerifyStillCurrent();
            _reservation.NoteNativeLaunchAttempt();
            try { _controller = new NativeInactiveControllerFactory(_api, _main, _profile, _module).CreateBorrowing(_prefabRoot, _window); }
            catch (NativeInactiveControllerCreationException error) { _controller = error.Responsibility; throw; }
            var inactive = _controller.Read();
            _registryController = _controller.RetainController();
            _factoryController = _controller.RetainController();
            _host.Sessions.BindPendingController(_claim, inactive.Controller, _registryController);
            // Reservation adopts its independent factory root before any later action.
            _reservation.AttachPendingController(_factoryController, _registryController);
            _globals.BindActiveConsumerLaunch(_reservation.State);
            try { _parameters = NativeInactiveControllerParameterBinding.Bind(_api, _main, _profile, _module,
                _host.Sessions, _claim, _bundle, _controller); }
            catch (NativeInactiveControllerParameterBindingException error) { _parameters = error.Responsibility; throw; }
            _reservation.State.AttachParameterBinding(_parameters);
            if (_parameters.SetterBaseline.Parameters != bundle.ParametersIdentity)
                throw new InvalidOperationException("The bound setter receipt differs from the prepared native bundle.");
            try { _sceneTransition = NativeLocalRunSceneTransition.Capture(_api, _main, _profile, _module, gameManager, _reservation.State); }
            catch (NativeLocalRunSceneTransitionException error) { _sceneTransition = error.Responsibility; throw; }
            try { _terminalCoroutines = NativeLocalTerminalCoroutineTracker.Capture(_api, _main, _profile, _module, _broker, _reservation.State); }
            catch (NativeLocalTerminalCoroutineException error) { _terminalCoroutines = error.Responsibility; throw; }
            _reservation.AttachTerminalCoroutines(_terminalCoroutines);
            // These owners capture the actual clone and its event source before
            // activation can synchronously dispatch work from Awake or a callback.
            try { _cutoutEvents = NativeLocalCutoutEventSource.Capture(_api, _main, _profile, _module, _broker, _reservation.State, _factoryController!); }
            catch (NativeLocalCutoutEventSourceException error) { _cutoutEvents = error.Responsibility; throw; }
            try { _cutoutCoroutines = NativeLocalCutoutCoroutineTracker.Capture(_api, _main, _profile, _module, _broker, _reservation.State, _cutoutEvents); }
            catch (NativeLocalCutoutCoroutineException error) { _cutoutCoroutines = error.Responsibility; throw; }
            try { _unityEvents = NativeLocalUnityEventLedger.Capture(_api, _main, _profile, _module, _broker, _reservation.State, _cutoutEvents); }
            catch (NativeLocalUnityEventLedgerException error) { _unityEvents = error.Responsibility; throw; }
            CaptureTweenWork();
            try { _activeDestruction = NativeLocalActiveDestructionTracker.Capture(_api, _main, _profile, _module, _host, _bridge,
                _reservation.State, _controller, _terminalCoroutines, _cutoutEvents, _cutoutCoroutines, _unityEvents); }
            catch (NativeLocalActiveDestructionException error) { _activeDestruction = error.Responsibility; throw; }
            try { _initialCoroutines = NativeLocalInitializationCoroutineTracker.Capture(_api, _main, _profile, _module, _broker, _reservation.State); }
            catch (NativeLocalInitializationCoroutineException error) { _initialCoroutines = error.Responsibility; throw; }
            try { _coroutineCancellation = NativeCoroutineCancellationObserver.Capture(_api, _main, _profile, _module, _broker,
                _reservation.State, _initialCoroutines, _terminalCoroutines, _cutoutCoroutines); }
            catch (NativeCoroutineCancellationObserver.CaptureException error) { _coroutineCancellation = error.Responsibility; throw; }
            try { _engineCoroutines = NativeEngineCoroutineRuntimeCoordinator.Capture(_api, _main, _reservation.State,
                _factoryController!, _initialCoroutines, _terminalCoroutines, _cutoutCoroutines, _engineRuntime!,
                _activeDestruction!, _cutoutEvents!, _coroutineCancellation!); }
            catch (NativeEngineCoroutineRuntimeCoordinator.CaptureException error) { _engineCoroutines = error.Responsibility; throw; }
            try { _menu = NativeLocalMenuLedger.Capture(_api, _main, _profile, _module, _broker, gameManager, _reservation.State, _initialCoroutines); }
            catch (NativeLocalMenuException error) { _menu = error.Responsibility; throw; }
            _activation = _controller.CreateActivation(_reservation.State, _parameters, _window, _globals);
            _phase = NativeLocalRunLaunchPhase.Activating;
            _menu.BeginLaunchOnce();
            _activation.ActivateOnce();
            _sceneTransition.StartLevelOnce(_activation);
            _phase = NativeLocalRunLaunchPhase.AwaitingScene;
            // Start is Unity-driven. Do not call it or the getter a second time.
            return _session;
        }
        catch (Exception error) { Fail(error); throw new NativeLocalRunLaunchException(this, error); }
        finally { _busy = false; }
    }

    internal NativeLocalRunLaunchSnapshot Read()
    {
        _main.VerifyAccess();
        return new(_phase, _session is null ? null : _host.Sessions.Read(_session).Identity,
            _reservation?.State.Read(), _failure, _sceneTransition?.Read(), _cutoutEvents?.Read(), _activeDestruction?.Read(), _cutoutCoroutines?.Read(), _unityEvents?.Read(),
            _coroutineCancellation?.Read(), _engineCoroutines?.Read(),
            _factoryRetirementSeal is not null, _factoryRootsReleased, _tweenWork?.Read());
    }

    internal void ObserveRunModification()
    {
        _main.VerifyAccess();
        if (!_busy && _phase == NativeLocalRunLaunchPhase.Ready) _modification?.Observe();
    }

    /// <summary>Called by the retained host frame pump after its native bridge.
    /// Only the actual fully verified getter/Start graph can unlock shared ready.</summary>
    private void Tick()
    {
        _main.VerifyAccess();
        // A frozen death source must never be pumped again. The registry may
        // have withdrawn its mappings since the prior frame, so the old
        // protected-source callbacks are also invalid after that handoff.
        if (_factoryRetirementSeal is not null)
        {
            if (!_busy && _phase != NativeLocalRunLaunchPhase.Unknown)
            {
                _busy = true;
                try { PumpTweenWork(); TickActiveFactoryRetirement(); }
                catch (Exception error) { Fail(error); }
                finally { _busy = false; }
            }
            return;
        }
        if (_busy || _phase is not (NativeLocalRunLaunchPhase.AwaitingScene or NativeLocalRunLaunchPhase.AwaitingInitialization
            or NativeLocalRunLaunchPhase.Ready or NativeLocalRunLaunchPhase.AwaitingControllerDestruction or NativeLocalRunLaunchPhase.ControllerDestroyed)) return;
        _busy = true;
        try
        {
            PumpTweenWork();
            if (_session is not null && _host.Sessions.Read(_session).ProtectionRetired)
                throw new InvalidOperationException("Native protection retired before the exact factory death observation was frozen.");
            _engineCoroutines?.RefreshTrackedIdentities();
            if (_engineCoroutines is null || _engineCoroutines.Unknown)
                throw new InvalidOperationException("Engine/tracker observation is unavailable or Unknown.");
            var retiring = _phase is NativeLocalRunLaunchPhase.Ready or NativeLocalRunLaunchPhase.AwaitingControllerDestruction
                or NativeLocalRunLaunchPhase.ControllerDestroyed;
            // This observation also runs before initial readiness: one missing
            // receiver must not hide its independent source behind a ready gate.
            var controllerDestroyed = _engineCoroutines.PumpIndependentReceiverDeaths();
            if (_engineCoroutines.Unknown) throw new InvalidOperationException("Independent receiver death observation became Unknown.");
            if (retiring)
            {
                if (_phase == NativeLocalRunLaunchPhase.Ready
                    && (!_engineCoroutines.Read().Receivers!.MayCommit || _terminalCoroutines!.Read().CutoutNaturallyEnded))
                    _phase = NativeLocalRunLaunchPhase.AwaitingControllerDestruction;
                if (controllerDestroyed) _phase = NativeLocalRunLaunchPhase.ControllerDestroyed;
            }
            // A Stop return is not an engine cancellation-completed receipt.
            // Keep even naturally ended observer roots while an owned Stop is
            // pending, and block readiness until a future verified end contract.
            if (retiring) TryObserveActiveSourceClosure(controllerDestroyed);
            if (_factoryRetirementSeal is not null) return; // The next pump uses only the frozen-source retirement path.
            if (_coroutineCancellation is null || _coroutineCancellation.MustRetainCoroutineResources())
            {
                if (_coroutineCancellation?.Unknown == true) throw new InvalidOperationException("Coroutine stop observation is unknown.");
                return;
            }
            if (retiring)
            {
                if (_engineCoroutines.AwaitingIndependentReceiverDeath) return;
                // Only independent completed observer roots retire here. Producers,
                // controller/resources and the full run protection remain owned.
                if (!_unityEvents!.TryConfirmNativeIdle()) return;
                if (!_cutoutCoroutines!.TryConfirmNativeIdle()) return;
                // An ended managed iterator cannot withdraw the provider while
                // an observed engine generation still depends on its identity.
                // Inline cleanup without a final Release receipt stays pending.
                _cutoutCoroutines.RecycleNaturallyEnded();
                _terminalCoroutines!.RecycleNaturallyEnded();
                if (!_initialCoroutines!.Read().Closed) _initialCoroutines.TryReleaseAfterEnded();
                if (_engineCoroutines.Unknown) throw new InvalidOperationException("Engine observer retirement verification became Unknown.");
                if (_cutoutEvents!.Read().Unknown) throw new InvalidOperationException("Cutout event-source observation became unknown during retirement pumping.");
                if (!_cutoutEvents.TryConfirmNativeIdle()) return;
                if (!_cutoutCoroutines.TryConfirmNativeIdle()) return;
                if (!_unityEvents.TryConfirmNativeIdle()) return;
                if (controllerDestroyed)
                {
                    _activeDestruction!.RequestStagingDestroyOnce();
                    TryFreezeActiveFactoryRetirement();
                }
                // A destroyed clone does not seal EventTrigger listeners or the
                // run's other consumers. Keep the graph and protection installed.
                return;
            }
            if (!_engineCoroutines.VerifyReceiversReadyForCommit()) return;
            var state = _reservation!.State;
            if (state.Read().Phase == LocalRunLaunchCommitPhase.Unknown || state.Read().EarlyTerminal)
                throw new InvalidOperationException("The actual launch lost its pending initialization authority during transition.");
            if (!_sceneTransition!.Tick()) return;
            if (_menu!.Read().Unknown) throw new InvalidOperationException("Menu handoff lost its continuous observation authority.");
            if (!_menu.Read().LaunchSceneCompleted) _menu.CompleteInitialScene(_sceneTransition);
            if (!_menu.TryVerifyCurrent()) return;
            _phase = NativeLocalRunLaunchPhase.AwaitingInitialization;
            if (TryCommitInitialized(_main, _host, _handler, _bridge, state,
                () => _activation!.ReadAfterSceneTransition(_sceneTransition), _initialCoroutines!.TryConfirmEnded,
                () => !_coroutineCancellation.MustRetainCoroutineResources(), _engineCoroutines.VerifyReceiversReadyForCommit))
                _phase = NativeLocalRunLaunchPhase.Ready;
        }
        catch (Exception error) { Fail(error); }
        finally { _busy = false; }
    }

    // The same commit pump is exercised with controlled native graph/activation
    // observations in regression tests. Production supplies the post-transition
    // activation check only after its actual scene operation completed.
    internal static bool TryCommitInitialized(IMainThreadDispatcher main, LocalRunProtectionHost host,
        LocalRunTerminationHandler handler, NativeLocalRunTerminationBoundaryState bridge,
        LocalRunLaunchCommitState state, Action verifyActivation, Func<bool> verifyInitialCoroutines,
        Func<bool>? verifyCancellationIdle = null, Func<bool>? verifyReceiversReady = null)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(verifyActivation); ArgumentNullException.ThrowIfNull(verifyInitialCoroutines);
        if (!ReferenceEquals(state.Registry, host.Sessions) || !handler.UsesBoundaryState(bridge))
            throw new InvalidOperationException("Commit pumping requires the same exact launch host and graph participants.");
        handler.ValidateHost(host.Sessions); state.VerifyPendingOwnership(host.Sessions);
        var value = state.Read();
        if (value.EarlyTerminal || value.Phase == LocalRunLaunchCommitPhase.Unknown)
            throw new InvalidOperationException(value.Failure ?? "A terminal call arrived before launch readiness.");
        var status = host.Read();
        if (!status.AllGatesInstalled || status.CleanupUnknown || status.StopRequested || status.Stopped)
            throw new InvalidOperationException("Native launch protection became unavailable before commit.");
        bool ObservationClear() => !state.CoroutineCancellationPending && (verifyCancellationIdle?.Invoke() ?? true)
            && (verifyReceiversReady?.Invoke() ?? true) && !state.CoroutineCancellationPending;
        if (!ObservationClear()) return false;
        verifyActivation();
        if (!ObservationClear()) return false;
        value = state.Read();
        if (value.EarlyTerminal || value.Phase == LocalRunLaunchCommitPhase.Unknown)
            throw new InvalidOperationException("The launch changed while verifying actual activation.");
        if (value.Phase == LocalRunLaunchCommitPhase.ControllerBound) return false;
        if (!verifyInitialCoroutines() || !ObservationClear()) return false;
        value = state.Read();
        if (value.EarlyTerminal || value.Phase == LocalRunLaunchCommitPhase.Unknown)
            throw new InvalidOperationException("Initialization authority changed while verifying actual coroutine completion.");
        if (!bridge.VerifyInitializedGraph(state) || !ObservationClear()) return false;
        state.PrepareCommit();
        state.CommitPrepared(); handler.CompletePending(state);
        return true;
    }

    private void Fail(Exception error)
    {
        _phase = NativeLocalRunLaunchPhase.Unknown;
        _failure = error.GetType().Name + ": " + error.Message;
        if (_failure.Length > 768) _failure = _failure[..768];
        if (_session is { } session)
            _host.Sessions.FailResultObservation(session, "The LOCAL launch could not continue to an exact result.");
        _reservation?.State.MarkUnknown(_failure);
        // Active consumers, partial publication and exceptional native ownership
        // must remain protected. This is not an inactive destroy/close shortcut.
    }

    internal static void TickForHost(LocalRunProtectionHost host)
    {
        NativeLocalRunLaunchCoordinator[] owners;
        lock (Gate) owners = ProcessOwners.Where(owner => ReferenceEquals(owner._host, host)).ToArray();
        foreach (var owner in owners) if (owner._main.IsMainThread) owner.Tick();
    }
    internal static bool HasPendingWorkForHost(LocalRunProtectionHost host)
    { lock (Gate) return ProcessOwners.Any(owner => ReferenceEquals(owner._host, host)); }
    internal static bool HasUnknownWorkForHost(LocalRunProtectionHost host)
    { lock (Gate) return ProcessOwners.Any(owner => ReferenceEquals(owner._host, host)
        && (owner._phase == NativeLocalRunLaunchPhase.Unknown || !owner._main.IsMainThread || owner._menu?.Read().Unknown == true || owner._initialCoroutines?.Unknown == true
            || owner._terminalCoroutines?.Unknown == true || owner._activeDestruction?.Read().Unknown == true
            || owner._cutoutEvents?.Read().Unknown == true || owner._cutoutCoroutines?.Unknown == true || owner._unityEvents?.Read().Unknown == true
            || owner._coroutineCancellation?.Unknown == true || owner._engineCoroutines?.Unknown == true
            || owner._tweenWork?.Unknown == true)); }
}
