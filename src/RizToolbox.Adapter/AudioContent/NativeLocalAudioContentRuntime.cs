using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>One module-owned composition for the actual protection host. Native
/// initialization is gated by production startup admission. The existing public
/// RequestPlay dispatches here; this adds no startup permission or consumer-end authority.</summary>
internal sealed class NativeLocalAudioContentRuntime : ILocalRunBoundaryHandler,
    ILocalRunTerminationCommands, ILocalRunOwnedResultCommandTarget
{
    private const long RetryPreparationIntervalMilliseconds = 1000;
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly string _dobby;
    private readonly LocalRunProtectionHost _host;
    private readonly Func<IOwnedUiService>? _ui;
    private readonly Func<LocalRunLaunchCommitState, LocalRunModification>? _trackModification;
    private NativeLocalRunTerminationBoundaryState? _bridge;
    private NativeControllerAwakeLedger? _awake;
    private ExactHookBroker? _broker;
    private NativeLocalRunResultSink? _sink;
    private LocalRunTerminationHandler? _handler;
    private Run? _run;
    private NativeLocalPlayLaunchSource? _pendingSource;
    private NativeLocalPlayRetrySourceRefresh? _retrySourceRefresh;
    private Run? _pendingRetryRun;
    private long _nextRetryPreparation;
    private bool _installationRequested, _initializationAttempted, _installed, _unknown, _stopping, _busy, _stopReleaseAttempted, _observersReleased;
    private bool _playbackConflict;
    internal string? Failure { get; private set; }
    internal bool Installed { get { _main.VerifyAccess(); return _installed && !_unknown; } }
    internal bool Unknown { get { _main.VerifyAccess(); return _unknown; } }
    internal bool InstallationPending
    { get { _main.VerifyAccess(); return _installationRequested && !_initializationAttempted && !_unknown && !_stopping && !_busy && !_playbackConflict; } }
    internal bool RetainsResultUi
    { get { _main.VerifyAccess(); return _run is not null || (_sink?.HasPendingPresentations ?? false); } }
    internal void ObserveRunModification()
    {
        _main.VerifyAccess();
        if (!_busy && Installed) _run?.Coordinator.ObserveRunModification();
    }
    private sealed class Run
    {
        internal NativeLocalRunLaunchCoordinator Coordinator = null!;
        internal LocalRunSessionRegistry.SessionHandle? Session;
        internal LocalPlayBundleInputs? Inputs;
        internal readonly NativeRuntime.RootHandle?[] Roots = new NativeRuntime.RootHandle?[3];
        internal readonly bool[] FreeAttempted = new bool[3];
        internal LocalRunHostResultAction? Requested;
        internal bool InputsFreeAttempted;
    }

    internal NativeLocalAudioContentRuntime(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, IntPtr module, string dobby, LocalRunProtectionHost host,
        Func<IOwnedUiService>? ui, Func<LocalRunLaunchCommitState, LocalRunModification>? trackModification = null)
    {
        main.VerifyAccess();
        (_api, _main, _log, _profile, _module, _dobby, _host, _ui) = (api, main, log, profile, module, dobby, host, ui);
        _trackModification = trackModification;
        host.RetainComposition(this);
    }

    internal bool TryInstall()
    {
        _main.VerifyAccess(); if (_installed) return !_unknown;
        if (_unknown || _stopping) return false;
        _playbackConflict = false;
        var protection = _host.Read();
        if (!protection.AllGatesInstalled || protection.StopRequested || protection.Stopped || protection.CleanupUnknown)
        { Failure = protection.Failure ?? "The formal LOCAL protection composition is not ready."; return false; }
        // This precedes UI resolution, native metadata, root creation and hooks.
        // The owned fixture acquisition path is intentionally not accepted here.
        if (!NativeEngineRuntimeCoordinator.TryAcquire(_log, out _, out var status, out var reason))
        { Failure = $"LOCAL runtime awaits production engine admission ({status}): {reason}"; return false; }
        if (_ui is null) { Failure = "The formal adapter supplied no shared host UI owner."; return false; }
        if (_initializationAttempted) throw new InvalidOperationException("A partial LOCAL composition cannot be initialized twice.");
        _initializationAttempted = true;
        try
        {
            _broker ??= new(_dobby, _main, _log);
            try { _bridge = NativeLocalRunTerminationBoundaryState.Create(_api, _main, _profile, _module, _broker, _host); }
            catch (NativeLocalRunTerminationBoundaryStateException error) { _bridge = error.Responsibility; throw; }
            _awake = NativeControllerAwakeLedger.Bind(_api, _main, _profile, _module, _broker, _bridge.ReadFrameForAwake);
            _sink = new(_main, _host.Sessions, _ui, new NativeLocalRunResultCommands(_main, _host.Sessions, this), _log);
            _handler = new(_main, _host.Sessions, _bridge, _sink, this);
            _host.InstallHandler(this); _installed = true; Failure = null; return true;
        }
        catch (ExactHookBroker.LocalProgressPlayConflictException error) when (_bridge is null)
        {
            // The reservation refuses before the native termination owner or any hook is created.
            _initializationAttempted = false; _playbackConflict = true; Failure = error.Message; return false;
        }
        catch (Exception error) { Fail(error); throw; }
    }

    internal Result RequestPlay(LocalPlayRequest request, CancellationToken token)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(request);
        if (token.IsCancellationRequested) return Result.Fail(new(ErrorCode.Cancelled, "Play cancelled."));
        if (_stopping || _unknown) return Result.Fail(new(ErrorCode.CapabilityUnavailable,
            Failure ?? "The retained LOCAL composition no longer admits new runs.", AudioContentCapabilityIds.LocalCatalogSelection));
        // CPU-only charts are valid catalog entries. Reject an unplayable selection
        // before it can claim progress callbacks or schedule a later installation.
        LocalNativeMetadataDescriptor descriptor; int judgeIndex;
        try { (descriptor, judgeIndex) = LocalChartPlaybackMetadataValidation.Require(request); }
        catch (InvalidDataException error) { return Result.Fail(new(ErrorCode.ContentInvalid, error.Message, AudioContentCapabilityIds.LocalCatalogSelection)); }
        if (_busy) return Result.Fail(new(ErrorCode.Conflict, "An earlier LOCAL run still owns this host."));
        if (CheckEmptyPrefabSources(token) is { } refusal) return refusal;
        _installationRequested = true;
        bool installed;
        try { installed = TryInstall(); }
        catch (Exception error) when (_unknown)
        {
            return Result.Fail(new(ErrorCode.InternalError,
                Failure ?? LocalRunFailureDiagnostics.Describe("LOCAL composition responsibility retained without retry", error),
                AudioContentCapabilityIds.LocalCatalogSelection));
        }
        if (!installed) return Result.Fail(new(_playbackConflict ? ErrorCode.Conflict : ErrorCode.CapabilityUnavailable,
            Failure ?? "Production LOCAL startup admission is unavailable.", AudioContentCapabilityIds.LocalCatalogSelection));
        try { _ = _ui!(); }
        catch (InvalidOperationException error) { return Result.Fail(new(ErrorCode.NotReady, error.Message, UiInputCapabilityIds.Windows)); }
        if (_run is not null || _busy) return Result.Fail(new(ErrorCode.Conflict, "An earlier LOCAL run still owns this host."));
        if (_pendingSource is not null) return Result.Fail(new(ErrorCode.CapabilityUnavailable, "An earlier native launch source still owns unconfirmed responsibility."));
        // This owner precedes every native read/root. It cannot grant startup
        // permission; TryInstall and the coordinator each require production admission.
        var source = _pendingSource = new NativeLocalPlayLaunchSource(_api, _main, _profile, _module);
        Result outcome;
        try
        {
            source.Capture(judgeIndex); source.VerifyStable();
            if (token.IsCancellationRequested) outcome = Result.Fail(new(ErrorCode.Cancelled, "Play cancelled before native launch."));
            else outcome = TryBegin(request, descriptor, source, token);
        }
        catch (NativeLocalPlaySourceNotReadyException error) when (!_unknown && !source.CleanupUnknown)
        { outcome = Result.Fail(SourceNotReady(error)); }
        catch (UnsupportedSignatureException error)
        { outcome = Result.Fail(new(ErrorCode.UnsupportedSignature, error.Message, AudioContentCapabilityIds.LocalCatalogSelection)); }
        catch (Exception error)
        { Fail(error); outcome = Result.Fail(new(ErrorCode.InternalError, Failure!, AudioContentCapabilityIds.LocalCatalogSelection)); }
        try { source.Dispose(); _pendingSource = null; }
        catch (Exception error)
        { Fail(error); outcome = Result.Fail(new(ErrorCode.InternalError, Failure!, AudioContentCapabilityIds.LocalCatalogSelection)); }
        return outcome;
    }

    private Result? CheckEmptyPrefabSources(CancellationToken token)
    {
        if (_run is not null || _pendingSource is not null || _ui is null
            || !NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown
            || _host.Read() is not { AllGatesInstalled: true, StopRequested: false, Stopped: false, CleanupUnknown: false, UnretiredSessions: 0 })
            return null;

        _busy = true;
        try
        {
            // Discovery uses the pinned read-only card sources. A launch still
            // needs production engine admission and a fresh complete prefab scan.
            var observation = new NativeControllerPrefabResolver(_api, _main, _profile, _module).CheckEmptySources();
            if (observation.Status == ControllerPrefabResolutionStatus.CleanupUnknown)
            {
                Fail(new InvalidOperationException(observation.UnavailableReason));
                return Result.Fail(new(ErrorCode.InternalError, Failure!, AudioContentCapabilityIds.LocalCatalogSelection));
            }
            if (_stopping) return Result.Fail(new(ErrorCode.CapabilityUnavailable,
                "The retained LOCAL composition no longer admits new runs.", AudioContentCapabilityIds.LocalCatalogSelection));
            if (token.IsCancellationRequested) return Result.Fail(new(ErrorCode.Cancelled, "Play cancelled before native launch."));
            return observation.ConfirmedAbsenceError is { } error ? Result.Fail(error) : null;
        }
        finally { _busy = false; }
    }

    // RequestPlay reaches this host-only entry only after exact metadata/source
    // capture. Every input is independently retained before native Begin.
    private Result TryBegin(LocalPlayRequest request, LocalNativeMetadataDescriptor descriptor,
        NativeLocalPlayLaunchSource source, CancellationToken token)
    {
        _main.VerifyAccess();
        if (!Installed || _stopping || _busy || _run is not null)
            throw new InvalidOperationException("The complete idle LOCAL composition does not admit a new run.");
        _busy = true;
        try
        {
            var preparation = PrepareRun();
            if (!preparation.Success) return Result.Fail(preparation.Error!);
            var run = preparation.Value!;
            if (token.IsCancellationRequested)
            {
                run.Coordinator.ReleasePreparedPrefab();
                return Result.Fail(new(ErrorCode.Cancelled, "Play cancelled before native launch."));
            }
            _run = run;
            if (RecheckPreparedSource(source, run.Coordinator.ReleasePreparedPrefab) is { } refusal)
            {
                _run = null;
                return Result.Fail(refusal);
            }
            run.Inputs = LocalPlayBundleInputs.CaptureBorrowing(_main, request, descriptor);
            run.Roots[0] = _api.Root(source.Judge.Target);
            run.Roots[1] = _api.Root(source.Manager.Target);
            run.Roots[2] = _api.Root(source.InLevelControl.Target);
            _ = BeginOwned(run);
            return Result.Ok();
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }

    internal static OperationError? RecheckPreparedSource(NativeLocalPlayLaunchSource source, Action releasePreparedPrefab)
    {
        try { source.VerifyStable(); return null; }
        catch (NativeLocalPlaySourceNotReadyException error) when (!source.CleanupUnknown)
        {
            releasePreparedPrefab();
            return SourceNotReady(error);
        }
    }

    private static OperationError SourceNotReady(NativeLocalPlaySourceNotReadyException error)
        => new(ErrorCode.NotReady, error.Message, AudioContentCapabilityIds.LocalCatalogSelection, Retryable: true);

    private Result<Run> PrepareRun()
    {
        var coordinator = new NativeLocalRunLaunchCoordinator(_api, _main, _profile, _module, _host, _handler!,
            _bridge!, _awake!, _broker!, _log, _trackModification);
        var preparation = coordinator.PreparePrefab();
        return preparation.Success
            ? Result<Run>.Ok(new Run { Coordinator = coordinator })
            : Result<Run>.Fail(preparation.Error!);
    }

    private LocalRunSessionRegistry.SessionHandle BeginOwned(Run run)
    {
        try { return run.Session = run.Coordinator.Begin(run.Inputs!.Request, run.Inputs.Descriptor,
            run.Roots[0]!, run.Roots[1]!, run.Roots[2]!); }
        catch (NativeLocalRunLaunchException error)
        { run.Coordinator = error.Responsibility; throw; }
    }

    public Result QueueOwnedResult(LocalRunSessionRegistry.SessionHandle session,
        LocalRunCapturedResult exactResult, LocalRunHostResultAction action)
    {
        _main.VerifyAccess();
        if (!Installed || _busy || _run is not { } run || !ReferenceEquals(run.Session, session))
            return Result.Fail(new(ErrorCode.NotReady, "This result has no accepting exact LOCAL composition owner."));
        if (!Enum.IsDefined(typeof(LocalRunHostResultAction), action)) return Result.Fail(new(ErrorCode.InvalidArgument, "Unknown LOCAL result action."));
        if (_stopping && action == LocalRunHostResultAction.Retry)
            return Result.Fail(new(ErrorCode.ScopeExpired, "LOCAL admission was withdrawn; explicit cleanup Return is still available."));
        if (run.Requested is not null) return Result.Fail(new(ErrorCode.Conflict, "This exact run already has a queued return/retry."));
        _busy = true;
        try
        {
            // Existing inputs are a real catalog Retain over the exact resource
            // graph, including after provider withdrawal. Do not resolve IDs.
            _ = run.Inputs!.Request.Resources;
            run.Coordinator.QueueOwnedResultReturn(_host.Sessions, session, exactResult);
            run.Requested = action; return Result.Ok();
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }

    public void Execute(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long exactReceiver)
    {
        _main.VerifyAccess();
        if (!Installed || _busy || _run is not { } run || !ReferenceEquals(run.Session, session))
            throw new InvalidOperationException("No exact LOCAL composition owns this classified boundary.");
        var current = _host.Sessions.Read(session);
        if (exactReceiver == 0 || !ReferenceEquals(_host.Sessions.FindProtectedSession(boundary, exactReceiver), session))
            throw new InvalidOperationException("The result command lost its classified receiver/session.");
        if (boundary == LocalBoundaryKind.ResultStart)
        {
            // Stock result Start stays suppressed. Host presentation is produced
            // solely by the handler's exact cached ControllerOver capture.
            if (current.ResultSceneIdentity != exactReceiver || current.Phase is not (LocalRunSessionPhase.Committed
                or LocalRunSessionPhase.ResultCapturing or LocalRunSessionPhase.ResultReady))
                throw new InvalidOperationException("The classified result Start is stale.");
            return;
        }
        if (run.Requested is not null) return; // Once-only, including conflicting late requests.
        _busy = true;
        try
        {
            run.Coordinator.QueueOwnedBoundaryReturn(_host.Sessions, session, boundary, exactReceiver);
            run.Requested = boundary == LocalBoundaryKind.ResultBack || _stopping
                ? LocalRunHostResultAction.Return : LocalRunHostResultAction.Retry;
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }

    public void ValidateHost(LocalRunSessionRegistry registry)
    {
        _main.VerifyAccess();
        if (_unknown || _handler is null || !ReferenceEquals(registry, _host.Sessions))
            throw new InvalidOperationException("The formal LOCAL composition is incomplete or belongs to another host.");
        _handler.ValidateHost(registry);
    }
    public bool HasPendingWork
    { get { _main.VerifyAccess(); return _unknown || _pendingSource is not null || _run is not null || _pendingRetryRun is not null || (_handler?.HasPendingWork ?? false)
        || (_sink?.HasPendingPresentations ?? false) || _stopping && !_observersReleased; } }
    public void ObserveControllerTerminalClaim(LocalRunSessionRegistry.SessionHandle session, long receiver)
        => _handler!.ObserveControllerTerminalClaim(session, receiver);
    public void Handle(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver)
        => _handler!.Handle(boundary, session, receiver);
    public void Tick()
    {
        _main.VerifyAccess(); if (_busy || _unknown || !_installed) return;
        _busy = true;
        try
        {
            _handler!.Tick(); _sink!.Tick();
            if (_sink.HasUnknownCleanup) throw new InvalidOperationException("The exact host result presentation cleanup became Unknown.");
            if (_stopping && _run is null && !_handler.HasPendingWork && !_sink.HasPendingPresentations)
            { ReleaseIdleCompositionOnce(); return; }
            var run = _run;
            if (run?.Session is null) return;
            if (run.Coordinator.CommandOutcomeUnknown) throw new InvalidOperationException("The actual launch command outcome became Unknown.");
            var receipt = run.Coordinator.TryReadCompletedCommandReceipt(_host.Sessions, run.Session);
            if (receipt is null) return;
            receipt.ValidateFor(run.Coordinator, _host.Sessions, run.Session);
            if (run.Requested == LocalRunHostResultAction.Retry && !_stopping && !_host.Read().StopRequested)
            {
                // The resolver hashes the native image and scans both card sources.
                if (Environment.TickCount64 < _nextRetryPreparation) return;
                if (_pendingRetryRun is not null)
                    throw new InvalidOperationException("An earlier retry preparation still owns its inputs.");
                var preparation = PrepareRun();
                if (!preparation.Success)
                {
                    _nextRetryPreparation = Environment.TickCount64 + RetryPreparationIntervalMilliseconds;
                    return;
                }
                var next = _pendingRetryRun = preparation.Value!;
                var metadata = LocalChartPlaybackMetadataValidation.Require(run.Inputs!.Request);
                _retrySourceRefresh ??= new(_main, metadata.JudgeIndex,
                    () => _pendingSource = new NativeLocalPlayLaunchSource(_api, _main, _profile, _module),
                    _api.Root, source => {
                        if (!ReferenceEquals(_pendingSource, source))
                            throw new InvalidOperationException("Retry source cleanup lost its actual pending owner.");
                        _pendingSource = null;
                    });
                if (!_retrySourceRefresh.TryPrepare())
                {
                    next.Coordinator.ReleasePreparedPrefab();
                    _pendingRetryRun = null;
                    _nextRetryPreparation = Environment.TickCount64 + RetryPreparationIntervalMilliseconds;
                    return;
                }
                _nextRetryPreparation = 0;
                receipt.ValidateFor(run.Coordinator, _host.Sessions, run.Session);
                // The new table is retained before transfer or any old free.
                // Failures keep both transactions, never a detached local table.
                _retrySourceRefresh.TransferRootsTo(next.Roots);
                ReleaseRootsOnce(run);
                next.Inputs = run.Inputs; run.Inputs = null;
                _run = next; _pendingRetryRun = null;
                // Recheck after old-root cleanup: a callback in root retirement
                // cannot replace the menu's manager/control unnoticed.
                _retrySourceRefresh.VerifyStable();
                try { _ = BeginOwned(next); }
                finally { _retrySourceRefresh.ReleaseBorrowedSourceAfterBegin(); }
                _retrySourceRefresh = null;
            }
            else { ReleaseInputsOnce(run); _run = null; }
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }

    private static void ReleaseInputsOnce(Run run)
    {
        var errors = new List<Exception>();
        try { ReleaseRootsOnce(run); } catch (Exception error) { errors.Add(error); }
        if (run.Inputs is not null)
        {
            if (run.InputsFreeAttempted) errors.Add(new InvalidOperationException("Command input cleanup cannot be retried."));
            else
            {
                run.InputsFreeAttempted = true;
                try { run.Inputs.Dispose(); run.Inputs = null; } catch (Exception error) { errors.Add(error); }
            }
        }
        if (errors.Count != 0) throw new AggregateException("Independent command input responsibility remains Unknown.", errors);
    }
    private static void ReleaseRootsOnce(Run run)
    {
        var errors = new List<Exception>();
        for (var i = run.Roots.Length - 1; i >= 0; --i)
        {
            if (run.Roots[i] is not { } root) continue;
            if (run.FreeAttempted[i]) { errors.Add(new InvalidOperationException("A retained command root free cannot be retried.")); continue; }
            run.FreeAttempted[i] = true;
            try { root.Dispose(); run.Roots[i] = null; } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Independent command source roots remain Unknown.", errors);
    }
    internal void RequestStop()
    {
        _main.VerifyAccess(); _stopping = true;
        if (!_initializationAttempted) _observersReleased = true;
        _host.RequestStop();
    }
    private void ReleaseIdleCompositionOnce()
    {
        if (_observersReleased) return;
        if (_stopReleaseAttempted) throw new InvalidOperationException("Idle composition observer retirement is Unknown and cannot be retried.");
        _stopReleaseAttempted = true;
        _sink!.Dispose(); _awake!.Dispose(); _observersReleased = true;
        // Native bridge/gate retirement remains their actual host pump's work.
        // No configured native code or unwind registration is unloaded here.
    }
    private void Fail(Exception error)
    {
        var firstFailure = !_unknown; _unknown = true;
        if (firstFailure)
        {
            if (_run?.Session is { } session)
                _host.Sessions.FailResultObservation(session, "The LOCAL composition could not continue result observation.");
            Failure = _bridge?.Describe().Failure
                ?? LocalRunFailureDiagnostics.Describe("LOCAL composition responsibility retained without retry", error);
            try { _log.Log(LogLevel.Error, Failure, error); } catch { }
        }
        try { _host.RequestStop(); } catch { }
    }
}
