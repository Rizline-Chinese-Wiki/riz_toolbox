using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

internal sealed record ObservedRunCancellationToken(string SessionId, string RunId, long SceneGeneration, long Revision, long Information);

/// <summary>One main-thread lifecycle state shared by scene and run observers. Initial data is a baseline, not history.</summary>
internal sealed partial class GameEventState : IGameLifecycleService
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<Subscription> _subscriptions = new();
    private readonly Queue<GameLifecycleChange> _pendingChanges = new();
    private bool _publishing;
    private bool _hasScene, _hasResults, _openRun, _controllerPresent, _over, _startedForGraph;
    private string _scene = "";
    private long _lastResult, _information, _controller;
    private bool? _paused;
    private bool _scenesAvailable, _runsAvailable, _pauseAvailable;
    private string? _scenesReason, _runsReason, _pauseReason;
    private Action<GameLifecycleChange>? _publisher;
    private object? _publisherIdentity;
    internal string SessionId { get; } = Guid.NewGuid().ToString("N");
    internal long Revision { get; private set; }
    internal long SceneGeneration { get; private set; }
    internal string? RunId { get; private set; }
    internal string? ChartId { get; private set; }
    internal RunLifecyclePhase Phase { get; private set; }
    internal bool IsPlaying => Phase == RunLifecyclePhase.Playing;
    internal ResultOriginRegistry ResultOrigins { get; } = new();

    internal void BindPublisher(object identity, Action<GameLifecycleChange> publisher)
    {
        Verify();
        if (_publisherIdentity is not null && !ReferenceEquals(_publisherIdentity, identity)) throw new InvalidOperationException("Shared lifecycle state must use one host event bus.");
        if (_publisherIdentity is null) { _publisherIdentity = identity; _publisher = publisher; }
    }

    internal void SetAvailability(bool? scenes = null, bool? runs = null, bool? pause = null, string? reason = null)
    {
        Verify(); bool changed = false; var previousPhase = Phase;
        if (scenes is not null && (_scenesAvailable != scenes.Value || (!scenes.Value && _scenesReason != reason)))
        { _scenesAvailable = scenes.Value; _scenesReason = scenes.Value ? null : reason; changed = true; }
        if (runs is not null && (_runsAvailable != runs.Value || (!runs.Value && _runsReason != reason)))
        { _runsAvailable = runs.Value; _runsReason = runs.Value ? null : reason; changed = true; }
        if (pause is not null && (_pauseAvailable != pause.Value || (!pause.Value && _pauseReason != reason)))
        {
            _pauseAvailable = pause.Value; _pauseReason = pause.Value ? null : reason;
            if (!pause.Value) { _paused = null; if (_openRun && Phase == RunLifecyclePhase.Paused) Phase = RunLifecyclePhase.ObservationLost; }
            changed = true;
        }
        if (changed) Emit(GameLifecycleChangeKind.ObservationChanged, previousPhase, "Observer availability changed.");
    }

    internal GameSceneChanged? Scene(string current)
    {
        Verify();
        if (!_hasScene)
        {
            _hasScene = true; _scene = current; SceneGeneration = 1;
            Emit(GameLifecycleChangeKind.SnapshotChanged, Phase, "Initial active scene baseline; no historical scene event.");
            return null;
        }
        if (current == _scene) return null;
        string previous = _scene; _scene = current; SceneGeneration++;
        // Scene exit is not evidence of cancellation: result UI can live in another scene.
        var result = new GameSceneChanged(previous, current) { SessionId = SessionId, Revision = Revision + 1, SceneGeneration = SceneGeneration };
        Emit(GameLifecycleChangeKind.SceneChanged, Phase, "Active scene name changed.");
        return result;
    }

    /// <param name="playbackStarted">A verified playback-progress latch, never levelBegan alone.</param>
    internal RunStarted? Controller(long identity, bool playbackStarted, bool over, string? chartId, long controllerIdentity = 0)
    {
        Verify();
        using var playbackMutation = BeginPlaybackRunMutation();
        if (identity == 0) { ControllerUnavailable(); return null; }
        _playbackRunObservedScene = SceneGeneration;
        var differentController = _controller != 0 && controllerIdentity != 0 && _controller != controllerIdentity;
        var newGraph = _information != identity || differentController;
        if (!_controllerPresent || _controller != controllerIdentity || _information != identity)
            WithdrawPlaybackRunAssociation("The observed gameplay controller/information association changed.");
        _controllerPresent = true; _controller = controllerIdentity;
        if (newGraph)
        {
            var previous = Phase;
            _information = identity; _startedForGraph = false; _openRun = false; _over = over;
            RunId = null; ChartId = chartId; _paused = null;
            Phase = over ? RunLifecyclePhase.AwaitingResult : RunLifecyclePhase.Loading;
            if (!playbackStarted || over) Emit(GameLifecycleChangeKind.SnapshotChanged, previous,
                "New live controller/information graph; no start until actual playback is observed.");
        }
        _over = over;
        if (over)
        {
            if (Phase != RunLifecyclePhase.Finished && Phase != RunLifecyclePhase.AwaitingResult)
            { var previous = Phase; Phase = RunLifecyclePhase.AwaitingResult; Emit(GameLifecycleChangeKind.SnapshotChanged, previous, "levelOver observed; awaiting actual result to determine termination reason."); }
            return null;
        }
        if (_startedForGraph)
        {
            if (_openRun && Phase == RunLifecyclePhase.ObservationLost && playbackStarted)
            { var previous = Phase; Phase = _paused == true ? RunLifecyclePhase.Paused : RunLifecyclePhase.Playing; Emit(GameLifecycleChangeKind.ObservationChanged, previous, "The same rooted controller and gameplay information became observable again."); }
            return null;
        }
        if (!playbackStarted) return null;
        _startedForGraph = true; _openRun = true; RunId = Guid.NewGuid().ToString("N"); ChartId = chartId;
        var before = Phase; Phase = RunLifecyclePhase.Playing;
        var started = new RunStarted(RunId, ChartId) { SessionId = SessionId, Revision = Revision + 1, SceneGeneration = SceneGeneration };
        Emit(GameLifecycleChangeKind.RunStarted, before, "ProgressControl.isPlaying && timeReady observed for this controller/information graph.");
        return started;
    }

    internal void ObservePause(bool? paused)
    {
        Verify();
        if (!_openRun || _over || Phase == RunLifecyclePhase.Finished) return;
        if (paused is null)
        {
            if (_paused is null) return;
            _paused = null; Emit(GameLifecycleChangeKind.ObservationChanged, Phase, "Pause flag is unavailable; pause/resume is not inferred from playback stopping.");
            return;
        }
        bool? previousPause = _paused; _paused = paused;
        if (previousPause == paused) return;
        var previousPhase = Phase;
        Phase = paused.Value ? RunLifecyclePhase.Paused : RunLifecyclePhase.Playing;
        Emit(previousPause is null ? GameLifecycleChangeKind.SnapshotChanged : paused.Value ? GameLifecycleChangeKind.Paused : GameLifecycleChangeKind.Resumed,
            previousPhase, "PauseUIControl._pausing Boolean sampled from the active controller's own pauseUIControl.");
    }

    internal void ControllerUnavailable()
    {
        Verify(); _controllerPresent = false;
        _playbackRunObservedScene = 0;
        WithdrawPlaybackRunAssociation("The observed gameplay controller became unavailable.");
        _paused = null; _pauseAvailable = false; _pauseReason = "The active controller's pause object cannot currently be sampled.";
        if (!_openRun || _over || Phase == RunLifecyclePhase.Finished || Phase == RunLifecyclePhase.ObservationLost) return;
        var previous = Phase; Phase = RunLifecyclePhase.ObservationLost;
        Emit(GameLifecycleChangeKind.ObservationChanged, previous, "Active controller is unavailable; cancellation is not observable from disappearance alone.");
    }

    internal bool ObserveResult(long identity)
    {
        Verify();
        if (!_hasResults) { _hasResults = true; _lastResult = identity; return false; }
        if (identity == 0 || identity == _lastResult) return false;
        _lastResult = identity;
        return true;
    }

    internal ObservedResultOrigin? CaptureResultOrigin(long information, long controller)
    {
        Verify();
        return ResultOrigins.Active && _openRun && RunId is not null && information != 0 && information == _information
            && controller != 0 && controller == _controller
            ? new(SessionId, RunId, information, controller, Revision, SceneGeneration, ResultOrigins.Generation) : null;
    }

    internal void ResultOriginAvailabilityChanged()
    {
        Verify(); Emit(GameLifecycleChangeKind.ObservationChanged, Phase,
            ResultOrigins.Active ? "Original-result origin observer became available." : ResultOrigins.UnavailableReason);
    }

    internal static RunEndReason CapturedEndReason(bool crashed, bool skipped)
        => crashed ? RunEndReason.Failed : skipped ? RunEndReason.Skipped : RunEndReason.Completed;

    internal RunFinished Finish(RunFinished result, ObservedResultOrigin? origin = null, string? originReason = null)
    {
        Verify();
        bool associated = MatchesTerminalResult(result, origin);
        var reason = CapturedEndReason(result.Crashed, result.Skipped);
        var completed = result with { RunId = associated ? RunId : null, AssociatedWithObservedStart = associated,
            SessionId = SessionId, SceneGeneration = SceneGeneration, EndReason = reason, Revision = Revision + 1 };
        var previous = Phase;
        if (associated) { _openRun = false; _paused = null; Phase = RunLifecyclePhase.Finished; }
        Emit(associated ? GameLifecycleChangeKind.RunEnded : GameLifecycleChangeKind.ResultObserved, previous, associated
            ? $"Actual InsideLevelResult matched its rooted ControllerOver entry origin; capturedRevision={origin!.Revision}; sourceGeneration={origin.SourceGeneration}."
            : "Actual InsideLevelResult has no matching current-run origin; RunId remains null. " + (originReason ?? "No original-return provenance was supplied."),
            reason, completed);
        return completed;
    }

    internal RunFinished? FinishTerminalReturn()
    {
        Verify();
        // ControllerOver can return and the controller can leave between two polls.
        // The registry requires the terminal copy from that exact run in either case.
        if (_controllerPresent && !_over) return null;
        if (CaptureResultOrigin(_information, _controller) is not { } current) return null;
        return ResultOrigins.FinishTerminalReturn(current, ChartId,
            (snapshot, origin) => MatchesTerminalResult(snapshot, origin) ? Finish(snapshot, origin) : null);
    }

    private bool MatchesTerminalResult(RunFinished result, ObservedResultOrigin? origin)
        => _openRun && RunId is not null && (_over || !_controllerPresent)
            && ResultOrigins.Active && origin is not null && origin.SourceGeneration == ResultOrigins.Generation
            && origin.SessionId == SessionId && origin.RunId == RunId && origin.Information == _information
            && origin.Controller != 0 && origin.Controller == _controller
            && (ChartId is null || result.ChartId is null || ChartId == result.ChartId);

    /// <summary>Only a separately verified direct cancellation target may call this. Neither scene exit nor _terminated does.</summary>
    internal ObservedRunCancellationToken? CaptureCancellationToken(long information)
    {
        Verify();
        return _openRun && !_over && RunId is not null && information != 0 && information == _information
            ? new(SessionId, RunId, SceneGeneration, Revision, information) : null;
    }
    /// <summary>Correlates a freshly rooted native graph with the observed run.
    /// A snapshot's chart name alone cannot associate a replacement controller
    /// during the sampler's interval. No native address enters the public SDK.</summary>
    internal bool MatchesCurrentGameplayRun(long information, long controller, string runId, long sceneGeneration)
    {
        Verify();
        return _runsAvailable && _controllerPresent && _openRun && !_over
            && Phase is RunLifecyclePhase.Playing or RunLifecyclePhase.Paused
            && information != 0 && information == _information && controller != 0 && controller == _controller
            && !string.IsNullOrWhiteSpace(runId) && runId == RunId && sceneGeneration == SceneGeneration;
    }
    // Judgement provenance may outlive the levelOver edge within the final
    // original invocation. Capture the still-open observed run without applying
    // the cancellation API's !_over precondition.
    internal Judgements.ObservedJudgementRun? CaptureJudgementRun(long information, long controller)
    {
        Verify();
        return _openRun && RunId is not null && information != 0 && information == _information
            && controller != 0 && controller == _controller
            ? new(SessionId, RunId, SceneGeneration, Revision, information, controller) : null;
    }
    internal bool AbortFromVerifiedCallback(ObservedRunCancellationToken token, string evidence)
        => EndFromVerifiedCallback(token, RunEndReason.Aborted, evidence);

    /// <summary>Direct native boundary evidence may close an observed run without inventing a result.</summary>
    internal bool EndFromVerifiedCallback(ObservedRunCancellationToken token, RunEndReason reason, string evidence)
    {
        Verify();
        if (reason is not (RunEndReason.Aborted or RunEndReason.Skipped) || token is null ||
            string.IsNullOrWhiteSpace(evidence) || CaptureCancellationToken(token.Information) != token) return false;
        CloseFromVerifiedCallback(reason, evidence); return true;
    }
    internal void AbortFromVerifiedCallback(string evidence)
    {
        Verify(); if (!_openRun || _over || string.IsNullOrWhiteSpace(evidence)) return;
        CloseFromVerifiedCallback(RunEndReason.Aborted, evidence);
    }
    private void CloseFromVerifiedCallback(RunEndReason reason, string evidence)
    {
        var previous = Phase; _openRun = false; _paused = null; Phase = RunLifecyclePhase.Finished;
        Emit(GameLifecycleChangeKind.RunEnded, previous, evidence, reason, source: LifecycleObservationSource.VerifiedCallback);
    }

    internal string? CompleteRunId() => RunId;
    internal GameStateSnapshot Snapshot() => new(_scene, RunId, ChartId, IsPlaying)
    { SessionId = SessionId, Revision = Revision, SceneGeneration = SceneGeneration, Paused = _paused };

    public Result<GameLifecycleSnapshot> GetSnapshot()
    {
        try { Verify(); return Result<GameLifecycleSnapshot>.Ok(LifecycleSnapshot()); }
        catch (Exception ex) { return Result<GameLifecycleSnapshot>.Fail(new(ErrorCode.WrongThread, ex.Message)); }
    }
    internal GameLifecycleSnapshot LifecycleSnapshot()
    {
        Verify(); var limitations = new List<OperationError>();
        if (!_scenesAvailable) limitations.Add(new(ErrorCode.ObservationUnavailable, _scenesReason ?? "Scene observer is not ready.", CapabilityId: GameCapabilityIds.Scenes));
        if (!_runsAvailable) limitations.Add(new(ErrorCode.ObservationUnavailable, _runsReason ?? "Gameplay observer is not ready.", CapabilityId: GameCapabilityIds.Runs));
        if (!_pauseAvailable || _paused is null) limitations.Add(new(ErrorCode.ObservationUnavailable, _pauseReason ?? "Pause state has no current validated sample.", TargetId: "PauseUIControl._pausing"));
        limitations.Add(new(ErrorCode.ObservationUnavailable, "Sampled disappearance never proves a run end. Accepted Retry, Back and Skip have separate optional native boundary capabilities.", TargetId: "run.abort"));
        if (!ResultOrigins.Active) limitations.Add(new(ErrorCode.ObservationUnavailable, ResultOrigins.UnavailableReason,
            CapabilityId: "riz.lifecycle.result-origin.observe", TargetId: "run.result-origin"));
        return new(SessionId, Revision, SceneGeneration, _scene, RunId, ChartId, Phase, _paused, _scenesAvailable, _runsAvailable,
            _pauseAvailable && _paused is not null, Array.AsReadOnly(limitations.ToArray()));
    }

    public Result<IGameLifecycleSubscription> Subscribe(Action<GameLifecycleChange> handler)
    {
        try
        {
            Verify(); ArgumentNullException.ThrowIfNull(handler);
            var subscription = new Subscription(this, handler, LifecycleSnapshot()); _subscriptions.Add(subscription);
            return Result<IGameLifecycleSubscription>.Ok(subscription);
        }
        catch (Exception ex) { return Result<IGameLifecycleSubscription>.Fail(new(ex is ArgumentNullException ? ErrorCode.InvalidArgument : ErrorCode.WrongThread, ex.Message)); }
    }

    internal IGameLifecycleService ForPlugin(IPluginContext context)
    {
        Verify(); var view = new ScopedView(this, context); return context.Own(view);
    }

    private void Emit(GameLifecycleChangeKind kind, RunLifecyclePhase previous, string evidence, RunEndReason? reason = null,
        RunFinished? result = null, LifecycleObservationSource source = LifecycleObservationSource.SampledGameState)
    {
        // Revoke before any host/plugin listener can use the new public snapshot.
        RevalidatePlaybackRunAssociation();
        Revision++;
        var change = new GameLifecycleChange(kind, previous, LifecycleSnapshot(), new(DateTimeOffset.UtcNow, source,
            source == LifecycleObservationSource.SampledGameState ? 250 : 0, evidence), reason, result);
        _pendingChanges.Enqueue(change);
        if (_publishing) return;
        _publishing = true;
        try
        {
            while (_pendingChanges.TryDequeue(out var next))
            {
                _publisher?.Invoke(next);
                foreach (var subscription in _subscriptions.ToArray())
                    if (subscription.Active && next.Snapshot.Revision > subscription.InitialSnapshot.Revision)
                        try { subscription.Handler(next); } catch { subscription.Dispose(); }
            }
        }
        finally { _publishing = false; }
    }
    private void Verify()
    { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Lifecycle state must be used on the verified main thread."); }

    private sealed class Subscription : IGameLifecycleSubscription
    {
        private GameEventState? _owner; internal Action<GameLifecycleChange> Handler { get; }
        internal bool Active => _owner is not null;
        public GameLifecycleSnapshot InitialSnapshot { get; }
        internal Subscription(GameEventState owner, Action<GameLifecycleChange> handler, GameLifecycleSnapshot initial)
            => (_owner, Handler, InitialSnapshot) = (owner, handler, initial);
        public void Dispose() { var owner = _owner; if (owner is null) return; owner.Verify(); _owner = null; owner._subscriptions.Remove(this); }
    }
    private sealed class ScopedView : IGameLifecycleService, IDisposable
    {
        private readonly GameEventState _state; private readonly IPluginContext _context; private bool _disposed;
        internal ScopedView(GameEventState state, IPluginContext context) => (_state, _context) = (state, context);
        private void Verify()
        { _state.Verify(); _context.MainThread.VerifyAccess(); if (_disposed || _context is IPluginScope { IsActive: false }) throw new ObjectDisposedException("Lifecycle activation has ended."); }
        public Result<GameLifecycleSnapshot> GetSnapshot()
        {
            try { Verify(); return Result<GameLifecycleSnapshot>.Ok(_state.LifecycleSnapshot()); }
            catch (Exception ex) { return Result<GameLifecycleSnapshot>.Fail(new(ex is ObjectDisposedException ? ErrorCode.ScopeExpired : ErrorCode.WrongThread, ex.Message)); }
        }
        public Result<IGameLifecycleSubscription> Subscribe(Action<GameLifecycleChange> handler)
        {
            try
            {
                Verify(); ArgumentNullException.ThrowIfNull(handler);
                // Owned EventHub delivery preserves fault attribution to this subscriber. No state mutation can interleave on this thread.
                var initial = _state.LifecycleSnapshot();
                var events = _context.Events.Subscribe<GameLifecycleChange>(change =>
                { if (!_disposed && change.Snapshot.SessionId == initial.SessionId && change.Snapshot.Revision > initial.Revision) handler(change); });
                return Result<IGameLifecycleSubscription>.Ok(_context.Own(new OwnedSubscription(initial, events)));
            }
            catch (Exception ex) { return Result<IGameLifecycleSubscription>.Fail(new(ex is ObjectDisposedException ? ErrorCode.ScopeExpired : ex is ArgumentNullException ? ErrorCode.InvalidArgument : ErrorCode.WrongThread, ex.Message)); }
        }
        public void Dispose() => _disposed = true;
    }
    private sealed class OwnedSubscription : IGameLifecycleSubscription
    {
        private IDisposable? _events; public GameLifecycleSnapshot InitialSnapshot { get; }
        internal OwnedSubscription(GameLifecycleSnapshot initial, IDisposable events) => (InitialSnapshot, _events) = (initial, events);
        public void Dispose() => Interlocked.Exchange(ref _events, null)?.Dispose();
    }
}
