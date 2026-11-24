using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter;

/// <summary>Main-thread sampling of actual Unity scenes, live gameplay flags and new result objects.</summary>
internal sealed class GameEventObserver : IGameStateService, IGameLifecycleService, IDisposable
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly EventHub _events;
    private readonly ExtensionPointRegistry _extensions;
    private readonly ILogger _log;
    private readonly IntPtr _sceneManager, _scene, _resources, _controllerType, _resultType;
    private readonly GameEventState _state;
    private NativeRuntime.RootHandle? _lastResultRoot;
    private NativeRuntime.RootHandle? _controllerRoot;
    private NativeRuntime.RootHandle? _informationRoot;
    private IntPtr _lastResultPointer;
    private bool _sceneBaselineLogged;
    private readonly PlaybackStartLatch _playback = new();
    private string? _lastPlaybackSample;
    private readonly BoundedObservationSamples _candidateSamples = new(128);
    private bool _pauseSymbolsAvailable, _disposed;
    private readonly bool _observeScenes, _observeRuns;
    private (long Identity, bool HasInformation, bool Began, bool Over, string? ChartId)? _lastControllerSample;
    internal bool ScenesAvailable { get; private set; }
    internal bool RunsAvailable { get; private set; }
    internal RunFinished? CurrentResult { get; private set; }
    private IntPtr _copiedResultPointer;

    internal GameEventObserver(NativeRuntime api, AdapterBuildProfile profile, IMainThreadDispatcher main,
        EventHub events, ExtensionPointRegistry extensions, ILogger log,
        bool observeScenes = true, bool observeRuns = true, GameEventState? state = null)
    {
        _api = api; _main = main; _events = events; _extensions = extensions; _log = log; _state = state ?? new();
        _observeScenes = observeScenes; _observeRuns = observeRuns;
        _state.BindPublisher(events, change =>
        {
            events.Publish(change);
            var message = $"Observed lifecycle {change.Kind}: session={change.Snapshot.SessionId}; revision={change.Snapshot.Revision}; sceneGeneration={change.Snapshot.SceneGeneration}; phase={change.Snapshot.Phase}; run={change.RelatedRunId ?? "<unassociated>"}; evidence={change.Observation.Evidence}";
            LogObservation("game.lifecycle", message, change.RelatedRunId, change.Snapshot.SceneGeneration);
        });
        IntPtr C(string key) { var type = profile.Types[key]; return api.Class(type.Assembly, type.Namespace, type.Name); }
        void Register<T>(ExtensionPoint<T> point, string description)
        {
            var existing = extensions.Describe(point.Id);
            if (existing.Success)
            {
                if (existing.Value!.Version != point.Version || existing.Value.PayloadType != typeof(T) || existing.Value.Phase != ExtensionPointPhase.After)
                    throw new InvalidDataException($"Conflicting observer extension point: {point.Id}");
                return;
            }
            var added = extensions.Register(point, ExtensionPointPhase.After, description);
            if (!added.Success) throw new AdapterOperationException(added.Error!);
        }
        if (observeScenes) try
        {
            _sceneManager = C("sceneManager"); _scene = C("scene");
            api.Method(_sceneManager, "GetActiveScene", 0, true); api.Method(_scene, "GetNameInternal", 1, true);
            Register(GameExtensionPoints.SceneChanged, "Active Unity scene name changed (sampled every 250 ms).");
            ScenesAvailable = true;
            _state.SetAvailability(scenes: true);
        }
        catch (Exception ex) { _state.SetAvailability(scenes: false, reason: ex.Message); log.TryLog(LogLevel.Warning, "Scene observation unavailable.", ex); }
        if (observeRuns) try
        {
            if (_scene == IntPtr.Zero) _scene = C("scene");
            _resources = C("resources"); _controllerType = C("normalController"); _resultType = C("resultController");
            api.Method(_resources, "FindObjectsOfTypeAll", 1, true, new[] { "System.Type" });
            api.Field(_controllerType, "LevelInformation"); api.Field(_controllerType, "LevelParams"); api.Field(_resultType, "levelOverInfo");
            RequireField(api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation"), "progressControl", "InLevel.ProgressControl");
            var progress = api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl");
            RequireField(progress, "isPlaying", "System.Boolean"); RequireField(progress, "timeReady", "System.Boolean");
            Register(GameExtensionPoints.RunStarted, "Actual ProgressControl.isPlaying && timeReady latch observed for a new LevelInformation identity (250 ms sampling).");
            Register(GameExtensionPoints.RunFinished, "New actual InsideLevelResult observed from the result screen or its verified terminal return (250 ms sampling).");
            RunsAvailable = true;
            _state.SetAvailability(runs: true);
            try
            {
                RequireField(_controllerType, "pauseUIControl", "PauseUIControl");
                RequireField(api.Class("Assembly-CSharp.dll", "", "PauseUIControl"), "_pausing", "System.Boolean");
                _pauseSymbolsAvailable = true;
                _state.SetAvailability(pause: false, reason: "Pause symbols verified; waiting for the active controller's pause UI object.");
            }
            catch (Exception ex)
            {
                _state.SetAvailability(pause: false, reason: "Pause metadata is unavailable: " + ex.Message);
                log.TryLog(LogLevel.Warning, "Pause observation unavailable; run start/result observation remains active.", ex);
            }
        }
        catch (Exception ex) { _state.SetAvailability(runs: false, pause: false, reason: ex.Message); log.TryLog(LogLevel.Warning, "Run observation unavailable.", ex); }

        void RequireField(IntPtr type, string name, string expected)
        { var actual = api.DescribeFieldType(type, name); if (actual != expected) throw new InvalidDataException($"{name} expected {expected}; metadata reports {actual}."); }
    }

    private void Publish<T>(ExtensionPoint<T> point, T payload)
    {
        _events.Publish(payload); _extensions.Publish(point, payload);
        var message = $"Observed game event {point.Id}: {payload}";
        var runId = payload switch { RunStarted started => started.RunId, RunFinished finished => finished.RunId, _ => null };
        LogObservation(point.Id, message, runId, _state.SceneGeneration);
    }

    private void LogObservation(string eventName, string message, string? runId, long sceneGeneration)
    {
        if (_log is not StructuredDiagnosticLogger structured)
        {
            _log.TryLog(LogLevel.Info, message);
            return;
        }
        try { structured.Write(LogLevel.Info, eventName, message, new DiagnosticContext(RunId: runId, SceneGeneration: sceneGeneration)); }
        catch (Exception) { }
    }

    internal void Poll()
    {
        _main.VerifyAccess();
        if (_disposed) return;
        if (ScenesAvailable)
        {
            try
            {
                var scene = _api.CallStatic(_sceneManager, "GetActiveScene");
                var name = _api.Text(_api.CallStatic(_scene, "GetNameInternal", A.Value(_api.Unbox<int>(scene))));
                if (!_sceneBaselineLogged) { _sceneBaselineLogged = true; _log.TryLog(LogLevel.Info, $"Game scene baseline: {name}."); }
                var change = _state.Scene(name);
                if (change is not null) Publish(GameExtensionPoints.SceneChanged, change);
            }
            catch (Exception ex) { ScenesAvailable = false; _state.SetAvailability(scenes: false, reason: ex.Message); _log.TryLog(LogLevel.Warning, "Scene observation stopped.", ex); }
        }
        if (!RunsAvailable) return;
        try
        {
            using var controllerRoot = Active(_controllerType);
            var controller = controllerRoot?.Target ?? IntPtr.Zero;
            if (controller != IntPtr.Zero)
            {
                var info = _api.Read<IntPtr>(controller, "LevelInformation");
                if (info != IntPtr.Zero)
                {
                    if (_informationRoot is null || _informationRoot.Target != info)
                    { var root = _api.Root(info); _informationRoot?.Dispose(); _informationRoot = root; }
                    info = _informationRoot.Target;
                    var parameters = _api.Read<IntPtr>(controller, "LevelParams");
                    using var parametersRoot = parameters == IntPtr.Zero ? null : _api.Root(parameters);
                    var chartId = parameters == IntPtr.Zero ? null : ChartId(_api, _api.Read<IntPtr>(parameters, "metadata"));
                    var began = _api.Read<bool>(info, "levelBegan");
                    var over = _api.Read<bool>(info, "levelOver");
                    // In this build levelBegan is not a persistent playback flag. Observe
                    // the actual progress controller instead of inferring start from scene entry.
                    var progress = _api.Read<IntPtr>(info, "progressControl");
                    using var progressRoot = progress == IntPtr.Zero ? null : _api.Root(progress);
                    _playback.Observe(info.ToInt64(), controller.ToInt64(), false, false);
                    if (progress != IntPtr.Zero)
                    {
                        var playing = _api.Read<bool>(progress, "isPlaying");
                        var ready = _api.Read<bool>(progress, "timeReady");
                        _playback.Observe(info.ToInt64(), controller.ToInt64(), playing, ready);
                        var sample = $"info={info.ToInt64():x}; playing={playing}; ready={ready}; began={began}; over={over}";
                        if (_lastPlaybackSample != sample)
                        { _lastPlaybackSample = sample; _log.TryLog(LogLevel.Debug, "Playback sample: " + sample); }
                    }
                    // levelBegan is diagnostic only. It does not independently prove playback in this build.
                    TraceController(controller, true, _playback.Started, over, chartId);
                    RememberController(controller);
                    var start = _state.Controller(info.ToInt64(), _playback.Started, over, chartId, controller.ToInt64());
                    if (start is not null) Publish(GameExtensionPoints.RunStarted, start);
                    ObservePause(controller);
                }
                else { TraceController(controller, false, false, false, null); _state.ControllerUnavailable(); }
            }
            else { TraceController(IntPtr.Zero, false, false, false, null); _state.ControllerUnavailable(); }
            using var screenRoot = Active(_resultType);
            var screen = screenRoot?.Target ?? IntPtr.Zero;
            var result = screen == IntPtr.Zero ? IntPtr.Zero : _api.Read<IntPtr>(screen, "levelOverInfo");
            if (_lastResultRoot is not null) _lastResultPointer = _lastResultRoot.Target;
            if (result != IntPtr.Zero && result != _lastResultPointer)
            {
                var root = _api.Root(result); _lastResultRoot?.Dispose(); _lastResultRoot = root; _lastResultPointer = result;
            }
            bool observed = _state.ObserveResult(result.ToInt64());
            if (result == IntPtr.Zero) { CurrentResult = null; _copiedResultPointer = IntPtr.Zero; }
            else if (observed || result != _copiedResultPointer)
            {
                var published = _state.ResultOrigins.ReadPublishedResult(result.ToInt64());
                var copied = published ?? CopyActualResult(_api, _lastResultRoot!);
                bool duplicate = false;
                string? originReason = null;
                var origin = observed ? _state.ResultOrigins.Consume(result.ToInt64(), out duplicate, out originReason) : null;
                CurrentResult = observed && !duplicate ? _state.Finish(copied, origin, originReason) : published ?? (copied with {
                    SessionId = _state.SessionId, Revision = _state.Revision, SceneGeneration = _state.SceneGeneration,
                    AssociatedWithObservedStart = false });
                _copiedResultPointer = result;
                if (observed && !duplicate) Publish(GameExtensionPoints.RunFinished, CurrentResult);
                if (observed) _log.TryLog(LogLevel.Debug, $"Result-origin sample: associated={CurrentResult.AssociatedWithObservedStart}; duplicate={duplicate}; sourceRun={origin?.RunId ?? "<unknown>"}; reason={originReason}");
            }
            if (_state.FinishTerminalReturn() is { } returned) Publish(GameExtensionPoints.RunFinished, returned);
        }
        catch (Exception ex) { RunsAvailable = false; CurrentResult = null; RememberController(IntPtr.Zero); _state.ControllerUnavailable(); _state.SetAvailability(runs: false, pause: false, reason: ex.Message); _log.TryLog(LogLevel.Warning, "Run observation stopped.", ex); }
    }

    private void RememberController(IntPtr controller)
    {
        if (controller != IntPtr.Zero && _controllerRoot?.Target == controller) return;
        var previous = _controllerRoot;
        _controllerRoot = controller == IntPtr.Zero ? null : _api.Root(controller);
        previous?.Dispose();
    }

    // Retain the last controller across a sampling gap to prevent address
    // reuse. Consumers can acquire an independent root only for a current run.
    internal NativeRuntime.RootHandle? AcquireObservedGameplayController()
    {
        _main.VerifyAccess();
        if (_disposed || !RunsAvailable || _controllerRoot is null) return null;
        var boundary = _state.GameplayObservationBoundary();
        var controller = _controllerRoot.Target;
        return boundary.CurrentRun && controller != IntPtr.Zero && controller.ToInt64() == boundary.Controller
            ? _api.Root(controller) : null;
    }

    private void ObservePause(IntPtr controller)
    {
        if (!_pauseSymbolsAvailable) { _state.ObservePause(null); return; }
        try
        {
            var pause = _api.Read<IntPtr>(controller, "pauseUIControl");
            if (pause == IntPtr.Zero)
            { _state.SetAvailability(pause: false, reason: "The active controller has no pause UI object yet."); _state.ObservePause(null); return; }
            using var root = _api.Root(pause);
            bool paused = _api.Read<bool>(root.Target, "_pausing");
            _state.SetAvailability(pause: true);
            _state.ObservePause(paused);
        }
        catch (Exception ex)
        {
            _state.SetAvailability(pause: false, reason: "Pause sample is unavailable: " + ex.Message);
            _state.ObservePause(null);
        }
    }

    private void TraceController(IntPtr controller, bool hasInformation, bool began, bool over, string? chartId)
    {
        var sample = (controller.ToInt64(), hasInformation, began, over, chartId);
        if (_lastControllerSample == sample) return;
        _lastControllerSample = sample;
        _log.TryLog(LogLevel.Debug, $"Run controller sample: identity={controller.ToInt64():x}; info={hasInformation}; began={began}; over={over}; chart={chartId ?? "<unknown>"}.");
    }

    private NativeRuntime.RootHandle? Active(IntPtr type)
    {
        var array = _api.CallStatic(_resources, "FindObjectsOfTypeAll", A.Ref(_api.Type(type)));
        NativeRuntime.RootHandle? selected = null;
        try
        {
            foreach (var component in _api.ArrayItems(array))
            {
                if (component == IntPtr.Zero) continue;
                using var componentRoot = _api.Root(component);
                if (!_api.Unbox<bool>(_api.Call(componentRoot.Target, "get_isActiveAndEnabled"))) continue;
                var gameObject = _api.Call(componentRoot.Target, "get_gameObject");
                using var objectRoot = _api.Root(gameObject);
                var sceneHandle = _api.Unbox<int>(_api.Call(objectRoot.Target, "get_scene"));
                var valid = _api.Unbox<bool>(_api.CallStatic(_scene, "IsValidInternal", A.Value(sceneHandle)));
                var loaded = valid && _api.Unbox<bool>(_api.CallStatic(_scene, "GetIsLoadedInternal", A.Value(sceneHandle)));
                var description = $"name={_api.Text(_api.Call(objectRoot.Target, "get_name"))}; scene={sceneHandle}; valid={valid}; loaded={loaded}";
                if (_candidateSamples.Changed(component, description)) _log.TryLog(LogLevel.Debug, "Run object candidate: " + description);
                if (!loaded) continue;
                if (selected is not null) throw new InvalidOperationException("Multiple loaded active gameplay objects matched; refusing ambiguous observation.");
                selected = _api.Root(componentRoot.Target);
            }
            var result = selected; selected = null; return result;
        }
        finally { selected?.Dispose(); }
    }
    internal static RunFinished CopyActualResult(NativeRuntime api, NativeRuntime.RootHandle root)
    {
        var result = root.Target;
        return new(null, ChartId(api, api.Read<IntPtr>(result, "metadata")),
            api.Unbox<int>(api.Call(result, "get_Score")), api.Unbox<float>(api.Call(result, "get_RawCompleteRate")),
            api.Read<int>(result, "perfect"), api.Read<int>(result, "maxPerfect"),
            api.Read<int>(result, "bad"), api.Read<int>(result, "miss"),
            api.Read<bool>(result, "crashed"), api.Read<bool>(result, "skip"));
    }

    private static string? ChartId(NativeRuntime api, IntPtr metadata)
    {
        if (metadata == IntPtr.Zero) return null;
        using var metadataRoot = api.Root(metadata);
        try
        {
            var difference = api.Read<IntPtr>(metadataRoot.Target, "chartInfo");
            using var differenceRoot = difference == IntPtr.Zero ? null : api.Root(difference);
            var chart = differenceRoot is null ? IntPtr.Zero : api.Read<IntPtr>(differenceRoot.Target, "chartInfo");
            using var chartRoot = chart == IntPtr.Zero ? null : api.Root(chart);
            return chartRoot is null ? null : api.ReadString(chartRoot.Target, "id");
        }
        catch (MissingFieldException) { return null; }
    }
    public OperationResult<GameStateSnapshot> GetSnapshot() { _main.VerifyAccess(); return OperationResult<GameStateSnapshot>.Ok(_state.Snapshot()); }
    Result<GameLifecycleSnapshot> IGameLifecycleService.GetSnapshot() => _state.GetSnapshot();
    public Result<IGameLifecycleSubscription> Subscribe(Action<GameLifecycleChange> handler) => _state.Subscribe(handler);
    internal IGameLifecycleService ForPlugin(IPluginContext context) => _state.ForPlugin(context);
    internal void AbortFromVerifiedCallback(string evidence) => _state.AbortFromVerifiedCallback(evidence);
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        RememberController(IntPtr.Zero);
        _lastResultRoot?.Dispose(); _lastResultRoot = null; _informationRoot?.Dispose(); _informationRoot = null;
        _candidateSamples.Clear();
        CurrentResult = null; _copiedResultPointer = IntPtr.Zero;
        if (_observeScenes) _state.SetAvailability(scenes: false, reason: "Scene observer module was withdrawn.");
        if (_observeRuns) _state.SetAvailability(runs: false, pause: false, reason: "Run observer module was withdrawn.");
    }
}

/// <summary>The only start evidence is actual playback plus a ready timing source for this exact controller/information pair.</summary>
internal sealed class PlaybackStartLatch
{
    private (long Information, long Controller) _graph;
    internal bool Started { get; private set; }
    internal void Observe(long information, long controller, bool isPlaying, bool timeReady)
    {
        var graph = (information, controller);
        if (_graph != graph) { _graph = graph; Started = false; }
        if (information != 0 && controller != 0 && isPlaying && timeReady) Started = true;
    }
}

internal sealed class BoundedObservationSamples
{
    private readonly int _limit;
    private readonly Dictionary<IntPtr, string> _samples = new();
    private readonly Queue<IntPtr> _order = new();
    internal int Count => _samples.Count;
    internal BoundedObservationSamples(int limit) { if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit)); _limit = limit; }
    internal bool Changed(IntPtr key, string value)
    {
        if (_samples.TryGetValue(key, out var previous)) { if (previous == value) return false; _samples[key] = value; return true; }
        while (_samples.Count >= _limit) _samples.Remove(_order.Dequeue());
        _samples.Add(key, value); _order.Enqueue(key); return true;
    }
    internal void Clear() { _samples.Clear(); _order.Clear(); }
}
