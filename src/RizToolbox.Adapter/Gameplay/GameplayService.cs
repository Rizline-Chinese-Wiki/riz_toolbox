using System.Diagnostics;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Fresh main-thread snapshots and experimental run-bound exclusive control access.</summary>
internal sealed partial class GameplayService : IDisposable
{
    private readonly IGameplayBackend _backend; private readonly IMainThreadDispatcher _main;
    private readonly Func<GameLifecycleSnapshot?> _lifecycle;
    private readonly Func<long, long, string, long, bool>? _matchesCurrentGameplayRun;
    private readonly Action<RunModificationInput>? _recordModification;
    private readonly ILogger? _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Func<double> _monotonicSeconds;
    private readonly GameplayClockObservation _clockObservation = new();
    private readonly List<GameplayModification> _modifications = new();
    private readonly Dictionary<GameplayControlAction, OperationError> _withdrawn = new();
    private GameplayCapture? _retained;
    private GameplayCapture? _expiredCapture;
    private ControlLease? _owner;
    private long _identity, _controllerIdentity, _generation, _revision;
    private GameplaySourceIdentity? _sourceIdentity;
    // Retained for successor detection only. Captures and value writes use their
    // own current descriptor, and never receive this last observed value.
    private NativePlaybackRateSourceDescriptor? _lastNativeDescriptor;
    private string? _observedRunId;
    private string? _observedSessionId;
    private long _observedSceneGeneration;
    private bool _disposed, _executing, _backendReleased;
    internal GameplayService(IGameplayBackend backend, IMainThreadDispatcher main, Func<GameLifecycleSnapshot?>? lifecycle = null,
        Action<RunModificationInput>? recordModification = null, Func<double>? monotonicSeconds = null,
        Func<long, long, string, long, bool>? matchesCurrentGameplayRun = null,
        Func<GameplayObservationBoundary>? controlBoundary = null, Func<object?>? controlSource = null,
        CallbackPerformanceMonitor? performance = null, ILogger? log = null)
    {
        _backend = backend; _main = main; _lifecycle = lifecycle ?? (() => null); _recordModification = recordModification;
        _matchesCurrentGameplayRun = matchesCurrentGameplayRun;
        _controlBoundary = controlBoundary; _controlSource = controlSource;
        _performance = performance;
        _log = log;
        _monotonicSeconds = monotonicSeconds ?? (() => _clock.Elapsed.TotalSeconds); _ = backend.Probe();
    }
    internal IGameplayClockService ClockForPlugin(IPluginContext context) => new ClockView(this, context);
    internal IGameplayControlService ControlsForPlugin(IPluginContext context) => new ControlsView(this, context);
    internal GameplayProbe Probe => _backend.Probe();

    private GameplayCapture Capture(bool observation = false)
    {
        GameplayCapture? capture = null;
        string? runId;
        GameplaySourceIdentity? sourceIdentity;
        GameLifecycleSnapshot? lifecycle;
        bool graphMatches;
        long identity, controller;
        try
        {
            capture = observation ? _backend.CaptureObservation() : _backend.Capture();
            sourceIdentity = capture.SourceIdentity;
            lifecycle = _lifecycle();
            graphMatches = capture.CurrentGraphMatches;
            runId = graphMatches ? RunId(capture, lifecycle) : null;
            identity = capture.Identity;
            controller = capture.ControllerIdentity;
        }
        catch
        {
            capture?.Dispose();
            ReleaseInactiveCapture();
            throw;
        }
        if (graphMatches && (_matchesCurrentGameplayRun is null || runId is not null))
            ObserveCommandSource(identity, controller, lifecycle, runId, sourceIdentity);
        var graphChanged = _identity != identity || _controllerIdentity != controller
            || _observedRunId != runId || _observedSessionId != lifecycle?.SessionId
            || _observedSceneGeneration != (lifecycle?.SceneGeneration ?? 0) || !SameSourceGraph(_sourceIdentity, sourceIdentity);
        var nativeChanged = sourceIdentity?.NativeDescriptor is { } native && native != _lastNativeDescriptor;
        var nativeAvailabilityChanged = (_sourceIdentity?.NativeDescriptor is null)
            != (sourceIdentity?.NativeDescriptor is null);
        if (graphChanged || nativeChanged || nativeAvailabilityChanged) RevokeControlObservation();
        if (nativeAvailabilityChanged && !graphChanged && !nativeChanged)
        {
            // Optional clock observations keep their independent graph/run
            // lifetime. A control lease must still end when the descriptor
            // disappears or reappears; it cannot borrow later authority.
            _owner?.Expire(); _owner = null;
        }
        if (graphChanged || nativeChanged)
        {
            _owner?.Expire(); _owner = null;
            _identity = identity; _controllerIdentity = controller; _observedRunId = runId;
            _observedSessionId = lifecycle?.SessionId; _observedSceneGeneration = lifecycle?.SceneGeneration ?? 0;
            _generation++; _modifications.Clear();
            _clockObservation.Reset("The controller, playback source, or observed run changed.");
        }
        _sourceIdentity = sourceIdentity;
        if (graphChanged || sourceIdentity?.NativeDescriptor is not null)
            _lastNativeDescriptor = sourceIdentity?.NativeDescriptor;
        capture.BindServiceContext(_generation, _observedRunId);
        var previous = _retained; _retained = capture; previous?.Dispose();
        return capture;
    }
    private void ReleaseInactiveCapture(string reason = "The active gameplay capture became unavailable.")
    {
        _expiredCapture = null;
        RevokeControlObservation();
        _owner?.Expire(); _owner = null;
        var retained = _retained; _retained = null;
        _identity = 0; _controllerIdentity = 0; _observedRunId = null;
        _sourceIdentity = null; _lastNativeDescriptor = null;
        _clockObservation.Reset(reason);
        retained?.Dispose();
    }
    private string? RunId(GameplayCapture capture, GameLifecycleSnapshot? state)
    {
        return _controlBoundary?.Invoke().CurrentRun != false
            && state is not null && state.Phase is RunLifecyclePhase.Playing or RunLifecyclePhase.Paused
            && state.RunId is string run && (_matchesCurrentGameplayRun is null
                || _matchesCurrentGameplayRun(capture.Identity, capture.ControllerIdentity, run, state.SceneGeneration)) ? state.RunId : null;
    }
    private bool SameCapture(GameplayCapture capture, bool requireAssociation = true)
    {
        if (ReferenceEquals(capture, _expiredCapture)) return false;
        bool sameGraph;
        try
        {
            var currentNativeDescriptor = capture.CurrentNativeSourceDescriptor;
            var lifecycle = _lifecycle();
            sameGraph = capture.CurrentGraphMatches && capture.Identity == _identity
                && capture.ControllerIdentity == _controllerIdentity && SameSourceGraph(capture.SourceIdentity, _sourceIdentity)
                && (currentNativeDescriptor is null || currentNativeDescriptor == _lastNativeDescriptor)
                && _observedRunId == RunId(capture, lifecycle) && _observedSessionId == lifecycle?.SessionId
                && _observedSceneGeneration == (lifecycle?.SceneGeneration ?? 0);
        }
        catch { sameGraph = false; }
        if (!sameGraph && ReferenceEquals(capture, _retained))
        {
            // A prepared command can still need its roots for restoration and readback.
            // Keep them through command completion, then retire this exact capture.
            _expiredCapture = capture;
            RevokeControlObservation();
        }
        return sameGraph && (!requireAssociation || _matchesCurrentGameplayRun is null || _observedRunId is not null);
    }
    private static bool SameSourceGraph(GameplaySourceIdentity? first, GameplaySourceIdentity? second)
    {
        if (first is null || second is null) return first == second;
        return first.Value.Progress == second.Value.Progress && first.Value.Pause == second.Value.Pause
            && first.Value.Player == second.Value.Player && first.Value.AudioSource == second.Value.AudioSource
            && first.Value.Clip == second.Value.Clip;
    }
    private OperationError? AssociationError(GameplayCapture capture) => _matchesCurrentGameplayRun is not null && !SameCapture(capture)
        ? new(ErrorCode.TargetExpired, "The current native controller/information graph is not associated with the sampler's run and scene generation.") : null;
    private OperationError? Guard(IPluginContext context, CancellationToken cancellation, bool allowExecuting = false)
    {
        try
        {
            _main.VerifyAccess(); context.MainThread.VerifyAccess();
            if (_disposed) return new(ErrorCode.ScopeExpired, "Gameplay adapter is disposed.");
            if (_executing && !allowExecuting) return new(ErrorCode.Conflict, "Gameplay control transaction is in progress; synchronous service reentry is refused.");
            if (cancellation.IsCancellationRequested || context is IScopedPluginContext scope && scope.Lifetime.IsCancellationRequested)
                return new(ErrorCode.Cancelled, "Gameplay operation was cancelled before dispatch.");
            return null;
        }
        catch (ObjectDisposedException ex) { return new(ErrorCode.ScopeExpired, ex.Message); }
        catch (InvalidOperationException ex) { return new(ErrorCode.WrongThread, ex.Message); }
    }
    private Result<GameplayClockSnapshot> Read(IPluginContext context, CancellationToken cancellation, GameplayClockObservationPolicy? observationPolicy)
    {
        var error = Guard(context, cancellation); if (error is not null) return Result<GameplayClockSnapshot>.Fail(ClockError(error));
        var policy = observationPolicy ?? GameplayClockObservationPolicy.Default;
        if (!GameplayClockObservation.ValidPolicy(policy))
            return Result<GameplayClockSnapshot>.Fail(ClockError(new(ErrorCode.InvalidArgument, "Observation policy requires finite positive ages/gaps/minimum intervals and a finite nonnegative residual tolerance.")));
        _executing = true;
        try
        {
            var start = ReadMonotonicSeconds();
            GameplayCapture? capture = null; string? unavailable = null;
            try { capture = Capture(); }
            catch (Exception ex) { unavailable = ex.Message; _owner?.Expire(); _owner = null; }
            using var clockRead = capture?.BeginClockRead();
            GameplayClockReading Read(string field)
            {
                try
                {
                    if (capture is not null) return ValidateReading(capture.ReadClock(field));
                }
                catch (Exception ex) { return Missing("InLevel.LevelInformation." + field, ex.Message, GameplayClockUnit.RawGameUnits); }
                return Missing("InLevel.LevelInformation." + field, unavailable ?? "No active gameplay object.", GameplayClockUnit.RawGameUnits);
            }
            var game = Read("nowTime"); var real = Read("nowRealTime"); var synced = Read("nowSyncedTime");
            GameplayClockMappingSnapshot? mapping = null;
            GameplayClockSourceIdentity? sourceIdentity = null;
            var mappingSourceCaptured = false;
            try { sourceIdentity = capture?.ClockSourceIdentity; mapping = capture?.ReadMapping(game); mappingSourceCaptured = true; }
            catch (Exception ex)
            {
                mapping = new(Missing("InLevel.ProgressControl.ChartTimeToRealTime", ex.Message, GameplayClockUnit.Seconds),
                    Missing("InLevel.ProgressControl._nowTime", ex.Message, GameplayClockUnit.Seconds),
                    Missing("InLevel.ProgressControl._offset", ex.Message, GameplayClockUnit.Seconds),
                    Missing("InLevel.Chart.get_ChartDelay", ex.Message, GameplayClockUnit.Seconds), null, null,
                    "Mapping unavailable; independent clock readings remain available.");
            }
            GameplayClockReading dsp;
            try { dsp = ValidateReading(_backend.ReadDsp()); }
            catch (Exception ex) { dsp = Missing("UnityEngine.AudioSettings.dspTime", ex.Message, GameplayClockUnit.Seconds); }
            var playback = MissingPlayback(new(ErrorCode.ObservationUnavailable, unavailable ?? "No native playback sample was captured."));
            if (capture is not null)
            {
                try { playback = ValidatePlayback(capture.ReadPlaybackClock()); }
                catch (Exception ex) { playback = MissingPlayback(Error(ex)); }
                var current = false;
                try
                {
                    if (mapping is not null && (mapping.Playing is bool sampledPlaying && sampledPlaying != capture.Playing
                        || mapping.Paused is bool sampledPaused && sampledPaused != capture.Paused))
                        mapping = null;
                    current = SameCapture(capture, requireAssociation: false);
                    // Optional conversion evidence has its own lifetime. Failure to
                    // read it does not by itself change the independently verified
                    // controller/audio capture or expire its control lease.
                    if (mapping is not null && mappingSourceCaptured)
                    {
                        try { if (sourceIdentity != capture.ClockSourceIdentity) mapping = null; }
                        catch { mapping = null; }
                    }
                }
                catch { current = false; mapping = null; /* An unobservable run cannot authorize a mixed game-time sample. */ }
                if (!current)
                {
                    game = Missing(game.Source, "The captured run changed while sampling its clocks.", game.Unit);
                    real = Missing(real.Source, "The captured run changed while sampling its clocks.", real.Unit);
                    synced = Missing(synced.Source, "The captured run changed while sampling its clocks.", synced.Unit);
                    mapping = null; capture = null;
                    playback = MissingPlayback(new(ErrorCode.TargetExpired, "The captured run changed while sampling playback."));
                    ReleaseInactiveCapture("The captured run changed while its clock readings were sampled.");
                }
            }
            error = Guard(context, cancellation, allowExecuting: true); if (error is not null) return Result<GameplayClockSnapshot>.Fail(ClockError(error));
            var end = ReadMonotonicSeconds();
            if (capture is not null && playback.Available)
            {
                try { capture.VerifyPlaybackClock(); }
                catch (Exception ex) { playback = MissingPlayback(Error(ex)); }
            }
            if (capture is not null && !SameCapture(capture, requireAssociation: false))
            {
                game = Missing(game.Source, "The captured run changed before publishing its clocks.", game.Unit);
                real = Missing(real.Source, "The captured run changed before publishing its clocks.", real.Unit);
                synced = Missing(synced.Source, "The captured run changed before publishing its clocks.", synced.Unit);
                mapping = null; capture = null; sourceIdentity = null;
                playback = MissingPlayback(new(ErrorCode.TargetExpired, "The captured run changed before publishing playback."));
                ReleaseInactiveCapture("The captured run changed before publishing its clock snapshot.");
            }
            if (capture is not null && playback.Available)
            {
                // All native getters precede the whole-capture graph check.
                // The final receipt/policy fence samples no new DSP or graph
                // getter, but must observe native writes during that graph check.
                try { capture.VerifyPlaybackClockState(); }
                catch (Exception ex) { playback = MissingPlayback(Error(ex)); }
            }
            if (mapping is not null) mapping = mapping with
            {
                CaptureStartedMonotonicSeconds = start, CaptureFinishedMonotonicSeconds = end,
                AudioDspSampleSeconds = dsp.Available ? dsp.Value : null, HostMonotonicSampleSeconds = end,
                GameTimeInSeconds = ValidateReading(mapping.GameTimeInSeconds), ProgressTimeInSeconds = ValidateReading(mapping.ProgressTimeInSeconds),
                AppliedOffsetSeconds = ValidateReading(mapping.AppliedOffsetSeconds), ChartDelaySeconds = ValidateReading(mapping.ChartDelaySeconds)
            };
            var revision = ++_revision;
            var interval = _clockObservation.Observe(revision, _generation, capture is null ? null : _observedRunId, mapping, dsp, start, end, policy, sourceIdentity);
            if (mapping is not null) mapping = mapping with
            {
                ObservedInterval = interval, GameSecondsPerDspSecond = interval.GameSecondsPerDspSecond,
                GameSecondsPerHostSecond = interval.GameSecondsPerHostSecond
            };
            // Revalidate access after the final reentrant getter before publishing the snapshot.
            error = Guard(context, cancellation, allowExecuting: true); if (error is not null) return Result<GameplayClockSnapshot>.Fail(ClockError(error));
            return Result<GameplayClockSnapshot>.Ok(new(revision, _generation, capture is null ? null : _observedRunId, start, end,
                game, real, synced, dsp, ValidateReading(new(true, end, GameplayClockUnit.Seconds, "System.Diagnostics.Stopwatch monotonic elapsed")))
                { Mapping = mapping, ObservedInterval = interval, Playback = playback });
        }
        finally { EndExecution(); }
    }
    private Result<GameplayObservedClockConversion> ConvertObserved(IPluginContext context,
        GameplayObservedClockConversionRequest request, CancellationToken cancellation)
    {
        var error = Guard(context, cancellation);
        if (error is not null) return Result<GameplayObservedClockConversion>.Fail(ClockError(error));
        if (request is null || request.ControllerGeneration <= 0 || request.SegmentId <= 0 || string.IsNullOrWhiteSpace(request.ObservedRunId)
            || !double.IsFinite(request.Value) || !double.IsFinite(request.MaxAgeSeconds) || request.MaxAgeSeconds <= 0
            || !GameplayClockObservation.ObservedDomain(request.SourceDomain) || !GameplayClockObservation.ObservedDomain(request.TargetDomain))
            return Result<GameplayObservedClockConversion>.Fail(ClockError(new(ErrorCode.InvalidArgument,
                "Observed conversion requires an actual run/generation/segment, finite value, positive finite age, and explicit progress/DSP/host seconds domains; chart conversion is separate.")));
        _executing = true;
        try
        {
            var capture = Capture();
            if (request.ControllerGeneration != _generation || request.ObservedRunId != _observedRunId)
                return Result<GameplayObservedClockConversion>.Fail(ClockError(new(ErrorCode.TargetExpired, "Observed conversion targets a different run or controller generation.")));
            using var clockRead = capture.BeginClockRead();
            var sourceIdentity = capture.ClockSourceIdentity;
            var currentMapping = capture.ReadObservedClockState();
            var playing = capture.Playing; var paused = capture.Paused;
            var now = ReadMonotonicSeconds();
            error = Guard(context, cancellation, allowExecuting: true);
            if (error is not null) return Result<GameplayObservedClockConversion>.Fail(ClockError(error));
            if (!SameCapture(capture))
            {
                ReleaseInactiveCapture("The captured run changed while checking observed conversion state.");
                return Result<GameplayObservedClockConversion>.Fail(ClockError(new(ErrorCode.TargetExpired, "The run changed during observed conversion state validation.")));
            }
            if (sourceIdentity != capture.ClockSourceIdentity)
            {
                _clockObservation.Reset("The clock mapping source changed while checking observed conversion state.");
                return Result<GameplayObservedClockConversion>.Fail(ClockError(new(ErrorCode.TargetExpired, "The clock mapping source changed during observed conversion state validation.")));
            }
            return _clockObservation.Convert(request, _generation, _observedRunId, playing, paused, now, currentMapping, sourceIdentity);
        }
        catch (Exception ex)
        {
            _clockObservation.Reset("Current playback state could not be observed for conversion.");
            return Result<GameplayObservedClockConversion>.Fail(ClockError(Error(ex)));
        }
        finally { EndExecution(); }
    }
    private Result<GameplayTimeConversion> Convert(IPluginContext context, GameplayTimeConversionRequest request, CancellationToken cancellation)
    {
        var error = Guard(context, cancellation); if (error is not null) return Result<GameplayTimeConversion>.Fail(ClockError(error));
        if (request is null || !double.IsFinite(request.Value) || request.ControllerGeneration <= 0 || string.IsNullOrWhiteSpace(request.ObservedRunId)
            || request.SourceUnit is not (GameplayClockUnit.Seconds or GameplayClockUnit.ChartCoordinates)
            || request.TargetUnit is not (GameplayClockUnit.Seconds or GameplayClockUnit.ChartCoordinates))
            return Result<GameplayTimeConversion>.Fail(ClockError(new(ErrorCode.InvalidArgument, "Conversion requires a finite value, observed run/generation, and explicit progress-seconds/chart-coordinate units.")));
        _executing = true;
        try
        {
            var capture = Capture();
            if (request.ControllerGeneration != _generation || request.ObservedRunId != _observedRunId)
                return Result<GameplayTimeConversion>.Fail(ClockError(new(ErrorCode.TargetExpired, "Conversion targets a different observed run or controller generation.")));
            using var clockRead = capture.BeginClockRead();
            var conversion = capture.ConvertTime(request);
            error = Guard(context, cancellation, allowExecuting: true); if (error is not null) return Result<GameplayTimeConversion>.Fail(ClockError(error));
            if (!SameCapture(capture))
            {
                ReleaseInactiveCapture("The captured run changed during time conversion.");
                return Result<GameplayTimeConversion>.Fail(ClockError(new(ErrorCode.TargetExpired, "The captured run changed during time conversion.")));
            }
            if (!conversion.Converted.Available || conversion.Converted.Unit != request.TargetUnit || conversion.Converted.Value is not double value || !double.IsFinite(value))
                return Result<GameplayTimeConversion>.Fail(ClockError(conversion.Converted.Error ?? new(ErrorCode.ObservationUnavailable, "The adapted conversion did not return a finite value in the requested domain.")));
            return Result<GameplayTimeConversion>.Ok(conversion);
        }
        catch (Exception ex) { return Result<GameplayTimeConversion>.Fail(ClockError(Error(ex))); }
        finally { EndExecution(); }
    }
    private Result<GameplayControlSnapshot> State(IPluginContext context, CancellationToken cancellation)
    {
        var error = Guard(context, cancellation); if (error is not null) return Result<GameplayControlSnapshot>.Fail(ControlError(error));
        var publication = CurrentControlObservation();
        return publication.State is { } state ? Result<GameplayControlSnapshot>.Ok(state)
            : Result<GameplayControlSnapshot>.Fail(publication.Error!);
    }
    private Result<GameplayControlSnapshot> CaptureControlState(out IReadOnlyList<GameplayControlAvailability> operations, out OperationError? probeError)
    {
        operations = Array.Empty<GameplayControlAvailability>(); probeError = null;
        var probed = false;
        Result<GameplayControlSnapshot> result;
        try
        {
            var capture = Capture(observation: true);
            var error = AssociationError(capture);
            if (error is not null) result = Result<GameplayControlSnapshot>.Fail(ControlError(error));
            else
            {
                var state = new GameplayControlSnapshot(_generation, _observedRunId, true, capture.LoadOver, capture.TimeReady,
                    capture.Playing, capture.Paused, capture.LevelOver, capture.Crashed, capture.CanDoOperation,
                    Array.Empty<GameplayControlAvailability>(), Array.AsReadOnly(_modifications.ToArray()), _owner?.PluginId);
                probed = true;
                try { operations = Operations(); }
                catch (Exception ex) { probeError = ControlError(Error(ex)); throw; }
                var observed = Array.AsReadOnly(operations.Select(operation => ObserveReadiness(capture, state, operation)).ToArray());
                error = AssociationError(capture);
                if (error is null)
                {
                    operations = observed;
                    result = Result<GameplayControlSnapshot>.Ok(state with { Operations = observed });
                }
                else result = Result<GameplayControlSnapshot>.Fail(ControlError(error));
            }
        }
        catch (Exception ex) { _owner?.Expire(); _owner = null; result = Result<GameplayControlSnapshot>.Fail(ControlError(Error(ex))); }
        if (!probed && !_disposed)
        {
            try { operations = Operations(); }
            catch (Exception ex) { probeError = ControlError(Error(ex)); }
        }
        return result;
    }
    private Result<IGameplayControlLease> Acquire(IPluginContext context, bool allowExperimental, CancellationToken cancellation)
    {
        var error = Guard(context, cancellation); if (error is not null) return Result<IGameplayControlLease>.Fail(ControlError(error));
        if (!allowExperimental) return Result<IGameplayControlLease>.Fail(ControlError(new(ErrorCode.OperationUnsupported, "Controls require explicit allowExperimental:true; local-result and submission effects are unverified.")));
        _executing = true;
        try
        {
            var capture = Capture();
            error = AssociationError(capture); if (error is not null) return Result<IGameplayControlLease>.Fail(ControlError(error));
            if (_owner is { Active: true }) return Result<IGameplayControlLease>.Fail(ControlError(new(ErrorCode.Conflict, "Gameplay controls are owned by '" + _owner.PluginId + "'.")));
            if (!capture.LoadOver || !capture.TimeReady || capture.LevelOver || capture.Crashed || (!capture.Playing && !capture.Paused))
                return Result<IGameplayControlLease>.Fail(ControlError(new(ErrorCode.InvalidGameState, "No loaded, playback-ready, non-ended run is available for control.")));
            error = AssociationError(capture); if (error is not null) return Result<IGameplayControlLease>.Fail(ControlError(error));
            error = Guard(context, cancellation, allowExecuting: true);
            if (error is not null) return Result<IGameplayControlLease>.Fail(ControlError(error));
            var owner = new ControlLease(this, context, _generation, _observedRunId);
            _owner = owner;
            _controlObservationDirty = true;
            try { return Result<IGameplayControlLease>.Ok(context.Own<IGameplayControlLease>(owner)); }
            catch { owner.Dispose(); throw; }
        }
        catch (Exception ex) { return Result<IGameplayControlLease>.Fail(ControlError(Error(ex))); }
        finally { EndExecution(); }
    }
    private IReadOnlyList<GameplayControlAvailability> Operations() => Array.AsReadOnly(_backend.Probe().Operations.Select(operation =>
        _withdrawn.TryGetValue(operation.Action, out var error) ? operation with
        { Supported = false, Evidence = "Withdrawn for this controller generation after unconfirmed side effects.", Error = error,
            ValueUnits = Array.AsReadOnly(operation.ValueUnits.ToArray()) }
        : operation with { ValueUnits = Array.AsReadOnly(operation.ValueUnits.ToArray()) }).ToArray());

    private Result<GameplayControlReceipt> ExecuteReserved(ControlCommand command, ControlLease lease,
        GameplayControlRequest request, CancellationToken cancellation)
    {
        var action = request.Action;
        var valued = action is GameplayControlAction.SetSpeed or GameplayControlAction.Seek;
        GameplayPreparedControl? prepared = null;
        try
        {
            GameplayControlAvailability? available;
            using (MeasureOperation(action, "probe"))
                available = Operations().FirstOrDefault(x => x.Action == action);
            if (available?.Supported != true) return Result<GameplayControlReceipt>.Fail(ControlError(available?.Error ?? new(ErrorCode.OperationUnsupported, "This control action is not adapted."), action));
            if (valued && !available.ValueUnits.Contains(request.Unit))
                return Result<GameplayControlReceipt>.Fail(ControlError(new(ErrorCode.OperationUnsupported, "This control's backend does not adapt the requested unit."), action));
            var error = Guard(lease.Context, cancellation, allowExecuting: true) ?? ValidateControlOwner(lease, request);
            if (error is not null) return Result<GameplayControlReceipt>.Fail(ControlError(error, action));
            GameplayCapture capture;
            using (MeasureOperation(action, "capture")) capture = Capture();
            error = AssociationError(capture) ?? ValidateControlOwner(lease, request);
            if (error is not null) return Result<GameplayControlReceipt>.Fail(ControlError(error, action));
            var stateError = Preconditions(capture, action);
            if (stateError is not null) return Result<GameplayControlReceipt>.Fail(ControlError(stateError, action));
            error = Guard(lease.Context, cancellation, allowExecuting: true) ?? ValidateControlOwner(lease, request);
            if (error is not null) return Result<GameplayControlReceipt>.Fail(ControlError(error, action));
            var stamp = ReadMonotonicSeconds();
            GameLifecycleSnapshot? origin = null;
            try { origin = _lifecycle(); } catch { /* Audit context cannot change the native command's exception semantics. */ }
            // Preparation must only capture/validate state and retain prerequisites. No audio/game mutation is permitted here.
            using (MeasureOperation(action, "prepare"))
                prepared = capture.Prepare(request) ?? throw new GameplayStateException(ErrorCode.OperationUnsupported, "The backend supplied no prepared transaction.");
            if (valued && (!prepared.CanRestore || !double.IsFinite(prepared.ValueTolerance) || prepared.ValueTolerance < 0))
                return Result<GameplayControlReceipt>.Fail(ControlError(new(ErrorCode.OperationUnsupported, "The value transaction has no complete restoration/readback contract."), action));
            error = Guard(lease.Context, cancellation, allowExecuting: true);
            if (error is not null) return Result<GameplayControlReceipt>.Fail(ControlError(error, action));
            if (!lease.Active || !ReferenceEquals(_owner, lease) || !SameCapture(capture))
            {
                lease.Dispose();
                return Result<GameplayControlReceipt>.Fail(ControlError(new(ErrorCode.TargetExpired, "Ownership or the captured run changed during preparation; nothing was committed."), action));
            }
            stateError = Preconditions(capture, action);
            if (stateError is not null) return Result<GameplayControlReceipt>.Fail(ControlError(stateError, action));
            if (!SameCapture(capture))
            {
                lease.Dispose();
                return Result<GameplayControlReceipt>.Fail(ControlError(new(ErrorCode.TargetExpired, "The exact run association changed before commit; nothing was committed."), action));
            }
            error = Guard(lease.Context, cancellation, allowExecuting: true) ?? ValidateControlOwner(lease, request);
            if (error is not null) return Result<GameplayControlReceipt>.Fail(ControlError(error, action));

            GameplayModification Modification(bool returned, GameplayControlTransactionState state, string outcome,
                GameplayControlConfirmation? confirmation = null) => new(lease.PluginId, action, _generation, lease.ObservedRunId, stamp, returned,
                    outcome + " Request value=" + (request.Value?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "none") + " unit=" + request.Unit + ".")
                { Request = request, TransactionState = state, ConfirmedValue = confirmation?.Value, ConfirmedUnit = confirmation?.Unit ?? GameplayControlValueUnit.None };

            Result<GameplayControlReceipt> FailedCommit(Exception failure, bool returned)
            {
                var recovery = new GameplayControlRestoration(false, "No verified restoration is available for this command.");
                if (valued)
                {
                    try
                    {
                        using (MeasureOperation(action, "restore")) prepared.Restore();
                        using (MeasureOperation(action, "restore-readback")) recovery = prepared.ConfirmRestored();
                    }
                    catch (Exception ex) { recovery = new(false, "Restoration threw: " + ex.Message); }
                }
                command.Restored = recovery.OriginalStateRestored;
                var state = recovery.OriginalStateRestored ? GameplayControlTransactionState.Restored : GameplayControlTransactionState.SideEffectsUnknown;
                var detail = recovery.OriginalStateRestored
                    ? "Value transaction failed; all prepared original game/audio state was read back as restored. "
                    : "Control failed; partial side effects are unknown. ";
                detail += failure.Message + " " + recovery.Evidence;
                AddModification(Modification(returned, state, detail), origin);
                var failureError = ControlError(Error(failure) with { Message = detail }, action);
                lease.Dispose();
                return Result<GameplayControlReceipt>.Fail(failureError);
            }

            _clockObservation.Reset("A gameplay control transaction is being committed; new actual samples are required.");
            command.WriteStarted = true;
            try { using (MeasureOperation(action, "commit")) prepared.Commit(); }
            catch (Exception ex) { return FailedCommit(ex, returned: false); }
            bool OwnsControl() => lease.Active && ReferenceEquals(_owner, lease);
            // The graph read itself can reenter scope teardown.
            bool CapturedRunIsCurrent() => OwnsControl() && SameCapture(capture) && OwnsControl();
            GameplayControlConfirmation confirmation;
            if (_disposed || !lease.Active)
            {
                if (valued) return FailedCommit(new GameplayStateException(ErrorCode.TargetExpired, "Ownership ended during value commit; confirmation was not attempted."), returned: true);
                confirmation = new(null, "Ownership ended during dispatch; no immediate readback was attempted.");
            }
            else
            {
                try
                {
                    if (valued && !CapturedRunIsCurrent()) throw new GameplayStateException(ErrorCode.TargetExpired, "The captured run changed during value commit.");
                    using (MeasureOperation(action, "confirm")) confirmation = prepared.Confirm();
                    if (valued && !CapturedRunIsCurrent()) throw new GameplayStateException(ErrorCode.TargetExpired, "The captured run changed during value confirmation.");
                }
                catch (Exception ex)
                {
                    if (valued) return FailedCommit(ex, returned: true);
                    confirmation = new(null, "Immediate postcondition unavailable: " + ex.Message);
                }
            }
            if (valued && (confirmation.Observed != true || confirmation.Unit != request.Unit
                || confirmation.Value is not double actual || !double.IsFinite(actual)
                || Math.Abs(actual - request.Value!.Value) > prepared.ValueTolerance))
                return FailedCommit(new GameplayStateException(ErrorCode.InvalidGameState, "The actual committed value/unit was not confirmed within the prepared tolerance. " + confirmation.Evidence), returned: true);
            var modification = Modification(true, valued ? GameplayControlTransactionState.Confirmed : GameplayControlTransactionState.Dispatched,
                valued ? "Prepared value transaction committed and its actual value/unit were confirmed; result/submission behavior remains unverified."
                    : "Verified runtime_invoke returned; full gameplay/result/submission behavior is unverified.", confirmation);
            if (_matchesCurrentGameplayRun is not null && !SameCapture(capture))
            {
                AddModification(modification with { ObservedRunId = null,
                    Outcome = modification.Outcome + " Exact run association ended during dispatch; no run-bound receipt is issued." }, null);
                lease.Dispose();
                return Result<GameplayControlReceipt>.Fail(ControlError(new(ErrorCode.TargetExpired,
                    "Control returned after its exact run association ended; dispatch evidence is unassociated and no run-bound receipt is issued."), action));
            }
            AddModification(modification, origin);
            if (action is GameplayControlAction.Retry or GameplayControlAction.Skip) lease.Dispose();
            return Result<GameplayControlReceipt>.Ok(new(modification, confirmation.Observed, confirmation.Evidence));
        }
        catch (Exception ex) { lease.Dispose(); return Result<GameplayControlReceipt>.Fail(ControlError(Error(ex), action)); }
        finally
        {
            try
            {
                if (prepared is not null)
                    using (MeasureOperation(action, "release")) prepared.Dispose();
            }
            catch { /* Preparation cleanup cannot replace commit/restoration evidence. */ }
        }
    }
    private OperationError? ValidateRequest(GameplayControlRequest request)
    {
        if (request is null || !Enum.IsDefined(typeof(GameplayControlAction), request.Action) || request.ControllerGeneration <= 0)
            return ControlError(new(ErrorCode.InvalidArgument, "A known action and positive controller generation are required."));
        var valid = request.Action switch
        {
            GameplayControlAction.SetSpeed => request.Value is double speed && double.IsFinite(speed) && speed > 0 && request.Unit == GameplayControlValueUnit.PlaybackRateMultiplier,
            GameplayControlAction.Seek => request.Value is double position && double.IsFinite(position) && request.Unit is GameplayControlValueUnit.GameSeconds or GameplayControlValueUnit.ChartCoordinates,
            _ => request.Value is null && request.Unit == GameplayControlValueUnit.None
        };
        return valid ? null : ControlError(new(ErrorCode.InvalidArgument, "SetSpeed needs a positive finite playback-rate multiplier; Seek needs a finite value with a game-seconds/chart-coordinate unit; commands take no value."), request.Action);
    }
    private void AddModification(GameplayModification modification, GameLifecycleSnapshot? origin)
    {
        _controlObservationDirty = true;
        if (_matchesCurrentGameplayRun is not null)
        {
            var associated = false;
            try { associated = _retained is not null && SameCapture(_retained) && modification.ObservedRunId == _observedRunId; }
            catch { /* Unobservable association cannot attach dispatch evidence to a run. */ }
            if (!associated) { modification = modification with { ObservedRunId = null }; origin = null; }
        }
        if (_modifications.Count == 256) _modifications.RemoveAt(0); _modifications.Add(modification);
        try
        {
            _recordModification?.Invoke(new(modification.Owner, "riz.gameplay.controls", modification.Action.ToString(),
                "gameplay.controller/" + modification.ControllerGeneration, RunModificationCategory.GameplayControl,
                modification.TransactionState switch
                {
                    GameplayControlTransactionState.Restored => RunModificationOutcome.Restored,
                    GameplayControlTransactionState.SideEffectsUnknown => RunModificationOutcome.InvocationFailedSideEffectsUnknown,
                    _ => RunModificationOutcome.Applied
                },
                modification.Outcome, modification.ObservedRunId, origin?.SessionId, origin?.Revision,
                modification.LocalResultBehaviorVerified, modification.SubmissionBehaviorVerified));
        }
        catch { /* Diagnostics must not turn a returned native call into a failure or replace an original invocation error. */ }
    }
    private static readonly Result ReadyInObservedState = Result.Ok();

    private GameplayControlAvailability ObserveReadiness(GameplayCapture capture, GameplayControlSnapshot state,
        GameplayControlAvailability operation)
    {
        if (!operation.Supported) return operation;
        OperationError? error;
        try { error = Preconditions(capture, operation.Action, state); }
        catch (Exception ex) { error = Error(ex); }
        return operation with { StateReadiness = error is null ? ReadyInObservedState : Result.Fail(ControlError(error, operation.Action)) };
    }

    private OperationError? Preconditions(GameplayCapture capture, GameplayControlAction action,
        GameplayControlSnapshot? sampled = null)
    {
        if (action is GameplayControlAction.SetSpeed or GameplayControlAction.Seek && string.IsNullOrWhiteSpace(_observedRunId))
            return new(ErrorCode.InvalidGameState, "Value controls require a real observed run identity.");
        // A publication has already read these fields once for every plugin. A command omits
        // the sample and re-reads each required field at both execution boundaries.
        if (!(sampled?.LoadOver ?? capture.LoadOver) || !(sampled?.TimeReady ?? capture.TimeReady)
            || (sampled?.LevelOver ?? capture.LevelOver) || (sampled?.Crashed ?? capture.Crashed))
            return new(ErrorCode.InvalidGameState, "The run is not ready, is over, or has crashed.");
        if (action is GameplayControlAction.Resume or GameplayControlAction.Retry)
            return PauseMenuReady() ? null : new(ErrorCode.InvalidGameState, "Resume/Retry requires the live paused, non-ending pause UI.");
        if (action is GameplayControlAction.SetSpeed or GameplayControlAction.Seek)
            return PauseMenuReady() ? null : new(ErrorCode.InvalidGameState, "Value controls require the live paused, non-ending pause UI so all audio/game state can be prepared before mutation.");
        if (action == GameplayControlAction.Pause && capture.PauseReadinessError is { } pauseError) return pauseError;
        return (sampled?.Playing ?? capture.Playing) && !(sampled?.Paused ?? capture.Paused)
            ? null : new(ErrorCode.InvalidGameState, "Pause/Skip requires actual playing and an unpaused UI.");

        bool PauseMenuReady() => (sampled?.Paused ?? capture.Paused) && (sampled?.CanDoOperation ?? capture.CanDoOperation);
    }
    private static OperationError Error(Exception ex) => new(ex switch
    {
        GameplayStateException state => state.Code,
        RizToolbox.Runtime.UnsupportedSignatureException => ErrorCode.UnsupportedSignature,
        RizToolbox.Runtime.TargetExpiredException => ErrorCode.TargetExpired,
        NotSupportedException => ErrorCode.OperationUnsupported,
        ObjectDisposedException => ErrorCode.TargetExpired,
        _ => ErrorCode.InternalError
    }, ex.Message);
    private OperationError ControlError(OperationError error, GameplayControlAction? action = null) => error with
    { CapabilityId = GameplayCapabilityIds.Controls, TargetId = "gameplay.controller/" + _generation + (action is null ? "" : "/" + action) };
    private static OperationError ClockError(OperationError error) => error with
    { CapabilityId = GameplayCapabilityIds.Clock, TargetId = error.TargetId ?? "gameplay.clock" };
    private static GameplayPlaybackClockReading MissingPlayback(OperationError error)
        => new(false, null, error.Message, ClockError(error with { TargetId = "gameplay.clock/playback" }));
    private static GameplayPlaybackClockReading ValidatePlayback(GameplayPlaybackClockReading reading)
    {
        if (!reading.Available) return MissingPlayback(reading.Error
            ?? new(ErrorCode.ObservationUnavailable, reading.Evidence));
        if (reading.Error is not null || reading.Sample is not { } sample || sample.ClockRevision <= 0
            || !Enum.IsDefined(typeof(GameplayPlaybackPhase), sample.Phase) || !double.IsFinite(sample.MediaSeconds)
            || sample.MediaSeconds != (float)sample.MediaSeconds || !double.IsFinite(sample.DspSeconds) || sample.DspSeconds < 0
            || !NativePlaybackClock.ValidRate(sample.PlaybackRate) || sample.PlaybackRate != (float)sample.PlaybackRate)
            return MissingPlayback(new(ErrorCode.ObservationUnavailable, "The native playback sample is incomplete or outside its finite typed domain."));
        return reading;
    }
    private static GameplayClockReading ValidateReading(GameplayClockReading reading)
        => reading.Available && reading.Value is double value && double.IsFinite(value) ? reading
            : reading with { Available = false, Value = null, Error = ClockError(reading.Error
                ?? new(ErrorCode.ObservationUnavailable, "The sampled clock value is absent or nonfinite.", TargetId: reading.Source)) };
    private static GameplayClockReading Missing(string source, string reason, GameplayClockUnit unit)
        => new(false, null, unit, source, new(ErrorCode.ObservationUnavailable, reason, GameplayCapabilityIds.Clock, source));
    private double ReadMonotonicSeconds()
    {
        try { return _monotonicSeconds(); }
        catch { return double.NaN; }
    }
    private void EndExecution()
    {
        if (!_disposed && _expiredCapture is { } expired && ReferenceEquals(expired, _retained))
        {
            try { ReleaseInactiveCapture(); }
            catch (Exception error) { _log?.TryLog(LogLevel.Error, "Expired gameplay capture cleanup failed.", error); }
        }
        _executing = false;
        if (!_disposed) return;
        // A reentrant Dispose defers backend release until after the current
        // operation settles. A release fault cannot replace that outcome.
        try { ReleaseBackend(); }
        catch (Exception error) { _log?.TryLog(LogLevel.Error, "Deferred gameplay backend cleanup failed.", error); }
    }
    private void ReleaseBackend()
    {
        if (_backendReleased) return; _backendReleased = true;
        try { _retained?.Dispose(); } finally { _retained = null; _backend.Dispose(); }
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        _owner?.Expire(); _owner = null;
        _clockObservation.Reset("The gameplay adapter was disposed.");
        DisposeControlSubscriptions();
        if (!_executing) ReleaseBackend();
    }
    private sealed class ClockView : IGameplayClockService
    {
        private readonly GameplayService _owner; private readonly IPluginContext _context;
        public ClockView(GameplayService owner, IPluginContext context) { _owner = owner; _context = context; }
        public Result<GameplayClockSnapshot> Read(CancellationToken cancellationToken = default, GameplayClockObservationPolicy? observationPolicy = null)
            => _owner.Read(_context, cancellationToken, observationPolicy);
        public Result<GameplayTimeConversion> Convert(GameplayTimeConversionRequest request, CancellationToken cancellationToken = default)
            => _owner.Convert(_context, request, cancellationToken);
        public Result<GameplayObservedClockConversion> ConvertObserved(GameplayObservedClockConversionRequest request, CancellationToken cancellationToken = default)
            => _owner.ConvertObserved(_context, request, cancellationToken);
    }
    private sealed class ControlsView : IGameplayControlService
    {
        private readonly GameplayService _owner; private readonly IPluginContext _context;
        public ControlsView(GameplayService owner, IPluginContext context) { _owner = owner; _context = context; }
        public Result<GameplayControlSnapshot> GetState(CancellationToken cancellationToken = default) => _owner.State(_context, cancellationToken);
        public Result<IGameplayControlSubscription> Subscribe(Action<GameplayControlObservationSnapshot> handler,
            CancellationToken cancellationToken = default) => _owner.SubscribeControls(_context, handler, cancellationToken);
        public Result<IGameplayControlLease> Acquire(bool allowExperimental = false, CancellationToken cancellationToken = default)
            => _owner.Acquire(_context, allowExperimental, cancellationToken);
    }
    private sealed class ControlLease : IGameplayControlLease
    {
        private readonly GameplayService _owner; internal readonly IPluginContext Context;
        internal bool Active { get; private set; } = true;
        internal string PluginId => Context.PluginId;
        public long ControllerGeneration { get; }
        public string? ObservedRunId { get; }
        internal ControlLease(GameplayService owner, IPluginContext context, long generation, string? runId)
        { _owner = owner; Context = context; ControllerGeneration = generation; ObservedRunId = runId; }
        public Result<GameplayControlReceipt> Execute(GameplayControlAction action, CancellationToken cancellationToken = default)
            => _owner.Execute(this, new(action, ControllerGeneration, ObservedRunId), cancellationToken);
        public Result<GameplayControlReceipt> Execute(GameplayControlRequest request, CancellationToken cancellationToken = default)
            => _owner.Execute(this, request, cancellationToken);
        public Result<IGameplayControlCommand> Submit(GameplayControlRequest request, CancellationToken cancellationToken = default)
            => _owner.Submit(this, request, cancellationToken);
        internal void Expire()
        {
            Active = false;
            _owner.CancelQueuedControlCommand(this);
        }
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (!Active) return;
            Expire();
            if (!ReferenceEquals(_owner._owner, this)) return;
            _owner._owner = null;
            _owner._controlObservationDirty = true;
        }
    }
}
