using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record GameplayProbe(IReadOnlyList<GameplayControlAvailability> Operations, IReadOnlyDictionary<string, string?> Sources);
internal readonly record struct GameplayClockSourceIdentity(long Progress, long Chart, long Pause);
internal interface IGameplayBackend : IDisposable
{
    GameplayProbe Probe();
    GameplayCapture Capture();
    GameplayCapture CaptureObservation() => Capture();
    GameplayClockReading ReadDsp();
}
internal abstract class GameplayCapture : IDisposable
{
    private long _serviceGeneration;
    private string? _serviceRunId;
    internal void BindServiceContext(long generation, string? runId)
    {
        if (generation <= 0 || _serviceGeneration != 0 && (_serviceGeneration != generation || _serviceRunId != runId))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A capture cannot change its bound service generation or run.");
        _serviceGeneration = generation; _serviceRunId = runId;
    }
    protected bool MatchesServiceContext(GameplayControlRequest request)
        => _serviceGeneration > 0 && request.ControllerGeneration == _serviceGeneration && request.ObservedRunId == _serviceRunId;
    protected bool MatchesServiceRun(string runId) => _serviceGeneration > 0 && _serviceRunId == runId;
    internal abstract long Identity { get; }
    internal virtual long ControllerIdentity => Identity;
    internal virtual GameplaySourceIdentity? SourceIdentity => null;
    internal virtual NativePlaybackRateSourceDescriptor? CurrentNativeSourceDescriptor => SourceIdentity?.NativeDescriptor;
    internal virtual bool CurrentGraphMatches => true;
    internal abstract bool LoadOver { get; }
    internal abstract bool TimeReady { get; }
    internal abstract bool Playing { get; }
    internal abstract bool Paused { get; }
    internal abstract bool LevelOver { get; }
    internal abstract bool Crashed { get; }
    internal abstract bool CanDoOperation { get; }
    internal abstract OperationError? PauseReadinessError { get; }
    internal virtual IDisposable? BeginClockRead() => null;
    internal abstract GameplayClockReading ReadClock(string field);
    internal virtual GameplayClockSourceIdentity? ClockSourceIdentity => null;
    internal virtual GameplayPlaybackClockReading ReadPlaybackClock() => new(false, null,
        "The captured backend has no authority-bound native playback reader.",
        new(ErrorCode.ObservationUnavailable, "The native playback reader is unavailable.",
            GameplayCapabilityIds.Clock, "gameplay.clock/playback"));
    internal virtual void VerifyPlaybackClock() { }
    internal virtual void VerifyPlaybackClockState() { }
    internal virtual GameplayClockMappingSnapshot? ReadMapping(GameplayClockReading game) => null;
    internal virtual GameplayClockMappingSnapshot? ReadObservedClockState() => ReadMapping(ReadClock("nowTime"));
    internal virtual GameplayTimeConversion ConvertTime(GameplayTimeConversionRequest request)
        => throw new GameplayStateException(ErrorCode.OperationUnsupported, "No validated chart/progress-time conversion is adapted.");
    internal virtual GameplayPreparedControl Prepare(GameplayControlRequest request)
        => request.Action is GameplayControlAction.SetSpeed or GameplayControlAction.Seek
            ? throw new GameplayStateException(ErrorCode.OperationUnsupported, "No complete value-control transaction is adapted.")
            : new GameplayPreparedCommand(this, request.Action);
    internal abstract void Dispatch(GameplayControlAction action);
    internal abstract bool? Postcondition(GameplayControlAction action);
    public abstract void Dispose();
}

/// <summary>Reads actual active NormalMode objects. No native ABI delegate is used to execute control commands.</summary>
internal sealed partial class GameplayNativeBackend : IGameplayBackend
{
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main;
    private readonly INativePlaybackRateProvider? _rateProvider;
    private readonly Func<NativeRuntime.RootHandle?>? _observedController;
    private readonly IntPtr _resources, _controller, _information, _unityObject;
    private IntPtr _audio, _progress, _pause, _time, _touchBlocker, _windowControl;
    private IntPtr _playerClass, _sourceClass, _clipClass;
    private bool _sourceIdentityMetadata;
    private GameplayProbe? _probe;
    private string? _rateMetadataError;
    private readonly GameplayClockProfileCache<GameplayClockNativeProfile, NativePlaybackGetTimeHookLease> _clockProfiles;
    private bool _disposed;
    internal GameplayNativeBackend(NativeRuntime api, IMainThreadDispatcher main, INativePlaybackRateProvider? rateProvider = null,
        Func<NativeRuntime.RootHandle?>? observedController = null)
    {
        _api = api; _main = main; _rateProvider = rateProvider; _observedController = observedController;
        _resources = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Resources");
        _unityObject = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _controller = api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        _information = api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        _clockProfiles = new(
            () => (_rateProvider as INativePlaybackGetTimeHookProvider)?.CaptureGetTimeHook(),
            hook => new GameplayClockNativeProfile(_api, _information, hook),
            (profile, hook) => profile.AttachGetTimeHook(hook), profile => profile.VerifyOperation());
    }
    public GameplayProbe Probe()
        => CurrentProbe(MetadataProbe());
    private GameplayProbe MetadataProbe()
    {
        _main.VerifyAccess();
        if (_probe is not null) return _probe;
        RequireField(_controller, "LevelInformation", "InLevel.LevelInformation");
        _api.Method(_resources, "FindObjectsOfTypeAll", 1, true, new[] { "System.Type" });
        var sources = new Dictionary<string, string?>(StringComparer.Ordinal)
        { ["clockMapping"] = "Clock mapping requires a current capture and exact native profile verification." };
        foreach (var field in new[] { "nowTime", "nowRealTime", "nowSyncedTime" })
        {
            try { RequireField(_information, field, "System.Single"); sources[field] = null; }
            catch (Exception ex) { sources[field] = ex.Message; }
        }
        try
        {
            _audio = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSettings");
            RequireMethod(_audio, "get_dspTime", "System.Double", true); sources["dspTime"] = null;
        }
        catch (Exception ex) { sources["dspTime"] = ex.Message; }
        string? preconditions = null;
        try
        {
            _progress = _api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl");
            _pause = _api.Class("Assembly-CSharp.dll", "", "PauseUIControl");
            RequireField(_controller, "pauseUIControl", "PauseUIControl"); RequireField(_information, "progressControl", "InLevel.ProgressControl");
            foreach (var field in new[] { "loadOver", "levelOver", "crashed" }) RequireField(_information, field, "System.Boolean");
            foreach (var field in new[] { "timeReady", "isPlaying" }) RequireField(_progress, field, "System.Boolean");
            RequireField(_pause, "_pausing", "System.Boolean"); RequireMethod(_pause, "get_CanDoOperation", "System.Boolean");
        }
        catch (Exception ex) { preconditions = ex.Message; }
        try
        {
            if (preconditions is not null) throw new UnsupportedSignatureException(preconditions);
            RequireField(_progress, "levelInformation", "InLevel.LevelInformation");
            RequireMethod(_controller, "get_isActiveAndEnabled", "System.Boolean");
        }
        catch (Exception ex) { _rateMetadataError = ex.Message; }
        try
        {
            _playerClass = _api.Class("Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer");
            _sourceClass = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
            _clipClass = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
            RequireField(_progress, "<MusicPlayer>k__BackingField", "Pigeon.MUGToolkit.Core.MUGMusicPlayer");
            RequireField(_progress, "audioSource", "UnityEngine.AudioSource");
            RequireField(_playerClass, "audioSource", "UnityEngine.AudioSource");
            RequireMethod(_sourceClass, "get_clip", "UnityEngine.AudioClip");
            _sourceIdentityMetadata = true; sources["controlSourceIdentity"] = null;
        }
        catch (Exception ex)
        {
            sources["controlSourceIdentity"] = ex.Message;
            if (_rateProvider is INativePlaybackRateSourceDescriptorProvider) _rateMetadataError ??= ex.Message;
        }
        var operations = new List<GameplayControlAvailability>();
        foreach (var action in Enum.GetValues<GameplayControlAction>())
        {
            if (action is GameplayControlAction.SetSpeed or GameplayControlAction.Seek)
            {
                var reason = action == GameplayControlAction.SetSpeed
                    ? "No synchronized playback-rate transaction is proven for MUGMusicPlayer/DspTimeService and progress. LevelInformation.speed is not a playback-rate setter."
                    : "No complete audio schedule, progress, note/judge/hold/cursor reset and restoration transaction is proven for arbitrary positioning.";
                operations.Add(new(action, false, reason, new(ErrorCode.OperationUnsupported, reason, GameplayCapabilityIds.Controls, "gameplay.controller/" + action)));
                continue;
            }
            try
            {
                if (preconditions is not null) throw new NotSupportedException(preconditions);
                RequireMethod(action is GameplayControlAction.Pause or GameplayControlAction.Resume ? _pause : _controller, action.ToString(), "System.Void");
                if (action == GameplayControlAction.Pause)
                {
                    RequireMethod(_controller, "Pause", "System.Void");
                    foreach (var field in new[] { "_over", "_terminated" }) RequireField(_pause, field, "System.Boolean");
                    RequireField(_pause, "_pauseTime", "System.Single"); RequireField(_progress, "prepareToPlay", "System.Boolean");
                    _time = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
                    _touchBlocker = _api.Class("Assembly-CSharp.dll", "", "TouchBlocker");
                    _windowControl = _api.Class("Assembly-CSharp.dll", "", "WindowControl");
                    RequireMethod(_time, "get_timeSinceLevelLoad", "System.Single", true);
                    RequireMethod(_touchBlocker, "get_Blocked", "System.Boolean", true);
                    RequireMethod(_windowControl, "get_HasActiveWindow", "System.Boolean", true);
                }
                operations.Add(new(action, true, "Exact instance void() metadata; actual dispatch and full flow remain experimental."));
            }
            catch (Exception ex) { operations.Add(new(action, false, "Control metadata/preconditions unavailable.", new(ErrorCode.OperationUnsupported, ex.Message))); }
        }
        _probe = new(Array.AsReadOnly(operations.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(sources));
        return _probe;
    }
    private GameplayProbe CurrentProbe(GameplayProbe probe)
    {
        // Capability metadata does not consume GetTime authority. Only an
        // actual clock or value-control request refreshes its physical owner.
        return probe with
        {
            Operations = Array.AsReadOnly(probe.Operations.Select(operation => operation.Action == GameplayControlAction.SetSpeed
                ? _rateMetadataError is string error
                    ? new(GameplayControlAction.SetSpeed, false, "Native rate-control capture metadata is unavailable.",
                        new(ErrorCode.UnsupportedSignature, error, GameplayCapabilityIds.Controls, "gameplay.controller/SetSpeed"))
                    : NativePlaybackRateProviderAdoption.Availability(_rateProvider, observationOnly: true)
                : operation.Action == GameplayControlAction.Seek ? CurrentSeekAvailability() : operation).ToArray())
        };
    }
    private GameplayClockNativeProfile? RefreshClockProfile(out string? error)
    {
        error = _clockProfiles.Refresh();
        var profile = error is null ? _clockProfiles.Current : null;
        if (profile is not null) _progress = profile.ProgressClass;
        return profile;
    }
    private GameplayClockNativeProfile? RefreshReadClockProfile(out string? error)
    {
        if (_rateProvider is not NativePlaybackStockRateHost host)
            return RefreshClockProfile(out error);
        try
        {
            var profile = host.CaptureReadClockProfile(_api, _information);
            _progress = profile.ProgressClass;
            error = null;
            return profile;
        }
        catch (Exception failure) { error = failure.Message; return null; }
    }
    public GameplayClockReading ReadDsp()
    {
        _main.VerifyAccess(); var probe = MetadataProbe();
        if (probe.Sources["dspTime"] is string error) return Missing("UnityEngine.AudioSettings.dspTime", error, GameplayClockUnit.Seconds);
        try
        {
            var value = _api.Unbox<double>(_api.CallStatic(_audio, "get_dspTime"));
            return double.IsFinite(value) ? new(true, value, GameplayClockUnit.Seconds, "UnityEngine.AudioSettings.dspTime")
                : Missing("UnityEngine.AudioSettings.dspTime", "DSP value is not finite.", GameplayClockUnit.Seconds);
        }
        catch (Exception ex) { return Missing("UnityEngine.AudioSettings.dspTime", ex.Message, GameplayClockUnit.Seconds); }
    }
    public GameplayCapture Capture()
    {
        _main.VerifyAccess(); if (_disposed) throw new ObjectDisposedException(nameof(GameplayNativeBackend)); MetadataProbe();
        var candidates = new List<NativeRuntime.RootHandle>();
        try
        {
            var array = _api.CallStatic(_resources, "FindObjectsOfTypeAll", A.Ref(_api.Type(_controller)));
            using var arrayRoot = _api.Root(array);
            foreach (var value in _api.ArrayItems(arrayRoot.Target))
            {
                if (value == IntPtr.Zero) continue;
                var root = _api.Root(value);
                try
                {
                    if (!Alive(root.Target) || !_api.Unbox<bool>(_api.Call(root.Target, "get_isActiveAndEnabled"))) continue;
                    candidates.Add(root); root = null!;
                }
                finally { root?.Dispose(); }
            }
            if (candidates.Count == 0) throw new GameplayStateException(ErrorCode.TargetMissing, "No active NormalMode controller.");
            if (candidates.Count > 1) throw new GameplayStateException(ErrorCode.AmbiguousTarget, "More than one active NormalMode controller; control refused.");
            var controller = candidates[0]; candidates.Clear();
            return CaptureController(controller);
        }
        finally { foreach (var candidate in candidates) candidate.Dispose(); }
    }

    public GameplayCapture CaptureObservation()
    {
        if (_observedController is null) return Capture();
        _main.VerifyAccess();
        if (_disposed) throw new ObjectDisposedException(nameof(GameplayNativeBackend));
        MetadataProbe();
        var controller = _observedController()
            ?? throw new GameplayStateException(ErrorCode.NotReady, "The lifecycle sampler has no current gameplay controller.");
        try
        {
            if (!Alive(controller.Target) || !_api.Unbox<bool>(_api.Call(controller.Target, "get_isActiveAndEnabled")))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The sampled gameplay controller is no longer active.");
        }
        catch { controller.Dispose(); throw; }
        return CaptureController(controller);
    }

    private GameplayCapture CaptureController(NativeRuntime.RootHandle controller)
    {
        NativeRuntime.RootHandle? information = null, progress = null, pause = null;
        try
        {
            var pointer = _api.Read<IntPtr>(controller.Target, "LevelInformation");
            if (!Alive(pointer)) throw new GameplayStateException(ErrorCode.NotReady, "Current LevelInformation is absent.");
            information = _api.Root(pointer);
            // Controls can be unavailable while independently verified clock fields remain readable.
            try
            {
                var p = _api.Read<IntPtr>(information.Target, "progressControl"); if (Alive(p)) progress = _api.Root(p);
                p = _api.Read<IntPtr>(controller.Target, "pauseUIControl"); if (Alive(p)) pause = _api.Root(p);
            }
            catch (Exception ex) when (ex is MissingFieldException or UnsupportedSignatureException or TargetExpiredException) { }
            return new CaptureValue(this, controller, information, progress, pause);
        }
        catch { controller.Dispose(); information?.Dispose(); progress?.Dispose(); pause?.Dispose(); throw; }
    }

    private bool Alive(IntPtr value) => value != IntPtr.Zero && _api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(value)));
    private NativePlaybackRateSourceDescriptor? CaptureSourceDescriptor()
    {
        try
        {
            // Controller snapshots need an immutable identity projection, not
            // a new native audio/Clock transaction on every frame. The actual
            // value consumers still acquire and verify their own participants.
            if (_rateProvider is INativePlaybackRateSourceDescriptorObservation observation)
                return observation.ObserveSourceDescriptor();
            return (_rateProvider as INativePlaybackRateSourceDescriptorProvider)?.CaptureSourceDescriptor();
        }
        // Optional native availability cannot invalidate independently rooted
        // game fields. A present descriptor still detects real source changes.
        // Value writes and playback reads obtain fresh authority separately.
        catch (Exception)
        { return null; }
    }
    private void RequireField(IntPtr cls, string field, string expected)
    { if (_api.DescribeFieldType(cls, field) != expected) throw new UnsupportedSignatureException(field + " differs from " + expected); }
    private void RequireMethod(IntPtr cls, string name, string expected, bool isStatic = false)
    { if (_api.DescribeReturnType(_api.Method(cls, name, 0, isStatic, Array.Empty<string>())) != expected) throw new UnsupportedSignatureException(name + " differs from " + expected); }
    private static GameplayClockReading Missing(string source, string reason, GameplayClockUnit unit = GameplayClockUnit.RawGameUnits)
        => new(false, null, unit, source, new(ErrorCode.ObservationUnavailable, reason, GameplayCapabilityIds.Clock, source));
    public void Dispose() { _main.VerifyAccess(); _disposed = true; }

    private sealed partial class CaptureValue : GameplayCapture
    {
        private readonly GameplayNativeBackend _owner;
        private readonly NativeRuntime.RootHandle _controller, _information;
        private readonly NativeRuntime.RootHandle? _progress, _pause;
        private readonly NativeRuntime.RootHandle? _player, _audioSource, _clip;
        private readonly GameplaySourceIdentity? _sourceIdentity;
        private readonly GameplayClockProfileReads<GameplayClockNativeProfile> _clockReads;
        private INativePlaybackClockReadProvider? _playbackReader;
        private NativePlaybackClockReadout? _playbackReadout;
        private bool _disposed;
        internal CaptureValue(GameplayNativeBackend owner, NativeRuntime.RootHandle controller, NativeRuntime.RootHandle information,
            NativeRuntime.RootHandle? progress, NativeRuntime.RootHandle? pause)
        {
            _owner = owner; _controller = controller; _information = information; _progress = progress; _pause = pause;
            _clockReads = new(() => { var profile = owner.RefreshReadClockProfile(out var error); return (profile, error); });
            try
            {
                if (owner._sourceIdentityMetadata && progress is not null)
                {
                    var api = owner._api;
                    var player = api.Read<IntPtr>(progress.Target, "<MusicPlayer>k__BackingField");
                    if (player != IntPtr.Zero) _player = api.RetainExactObject(player, owner._playerClass);
                    var source = api.Read<IntPtr>(progress.Target, "audioSource");
                    if (source != IntPtr.Zero) _audioSource = api.RetainExactObject(source, owner._sourceClass);
                    var clip = source == IntPtr.Zero ? IntPtr.Zero : api.Call(source, "get_clip");
                    if (clip != IntPtr.Zero) _clip = api.RetainExactObject(clip, owner._clipClass);
                    _sourceIdentity = new(progress.Target.ToInt64(), pause?.Target.ToInt64() ?? 0,
                        player.ToInt64(), source.ToInt64(), clip.ToInt64(), owner.CaptureSourceDescriptor());
                }
            }
            catch
            {
                try { _clip?.Dispose(); } finally { try { _audioSource?.Dispose(); } finally { _player?.Dispose(); } }
                throw;
            }
        }
        internal override long Identity => _information.Target.ToInt64();
        internal override long ControllerIdentity => _controller.Target.ToInt64();
        internal override GameplaySourceIdentity? SourceIdentity => _sourceIdentity;
        internal override NativePlaybackRateSourceDescriptor? CurrentNativeSourceDescriptor => _owner.CaptureSourceDescriptor();
        internal override bool CurrentGraphMatches => !_disposed && _owner.Alive(_controller.Target) && _owner.Alive(_information.Target)
            && _owner._api.Read<IntPtr>(_controller.Target, "LevelInformation") == _information.Target
            && (_progress is null || _owner._api.Read<IntPtr>(_information.Target, "progressControl") == _progress.Target
                && _owner._api.Read<IntPtr>(_progress.Target, "levelInformation") == _information.Target)
            && (_pause is null || _owner._api.Read<IntPtr>(_controller.Target, "pauseUIControl") == _pause.Target)
            && CurrentSourceMatches();
        private bool CurrentSourceMatches()
        {
            if (_sourceIdentity is not { } identity) return true;
            var api = _owner._api;
            return api.Read<IntPtr>(_progress!.Target, "<MusicPlayer>k__BackingField").ToInt64() == identity.Player
                && api.Read<IntPtr>(_progress.Target, "audioSource").ToInt64() == identity.AudioSource
                && (_player is null || api.Read<IntPtr>(_player.Target, "audioSource").ToInt64() == identity.AudioSource)
                && (_audioSource is null || api.Call(_audioSource.Target, "get_clip").ToInt64() == identity.Clip);
        }
        internal override bool LoadOver => _owner._api.Read<bool>(_information.Target, "loadOver");
        internal override bool LevelOver => _owner._api.Read<bool>(_information.Target, "levelOver");
        internal override bool Crashed => _owner._api.Read<bool>(_information.Target, "crashed");
        internal override bool TimeReady => _progress is not null && _owner._api.Read<bool>(_progress.Target, "timeReady");
        internal override bool Playing => _progress is not null && _owner._api.Read<bool>(_progress.Target, "isPlaying");
        internal override bool Paused => _pause is not null && _owner._api.Read<bool>(_pause.Target, "_pausing");
        internal override bool CanDoOperation => _pause is not null && _owner._api.Unbox<bool>(_owner._api.Call(_pause.Target, "get_CanDoOperation"));
        internal override OperationError? PauseReadinessError
        {
            get
            {
                var api = _owner._api;
                if (_pause is null || _progress is null || !_owner.Alive(_pause.Target) || !api.Unbox<bool>(api.Call(_pause.Target, "get_isActiveAndEnabled")))
                    return Refused("The live pause UI is unavailable.");
                if (api.Read<bool>(_pause.Target, "_over") || api.Read<bool>(_pause.Target, "_terminated"))
                    return Refused("The pause UI is ending or terminated.");
                var countdown = api.Read<float>(_pause.Target, "_pauseTime");
                var sceneTime = api.Unbox<float>(api.CallStatic(_owner._time, "get_timeSinceLevelLoad"));
                if (!float.IsFinite(countdown) || !float.IsFinite(sceneTime))
                    return Refused("The game reported a nonfinite pause timer or scene time.");
                if (countdown > 0)
                    return Refused("The game's pause countdown is still running.");
                if (sceneTime < 1.8f)
                    return Refused("The initial 1.8 seconds of scene time have not elapsed.");
                if (api.Read<bool>(_progress.Target, "prepareToPlay"))
                    return Refused("The game's progress controller is still preparing playback.");
                if (api.Unbox<bool>(api.CallStatic(_owner._touchBlocker, "get_Blocked")) || api.Unbox<bool>(api.CallStatic(_owner._windowControl, "get_HasActiveWindow")))
                    return Refused("The game's touch blocker or active window currently owns input.");
                return null;
            }
        }
        private static OperationError Refused(string reason) => new(ErrorCode.InvalidGameState, reason);
        internal override IDisposable BeginClockRead()
        {
            _owner._main.VerifyAccess();
            if (_owner._disposed || _disposed) throw new ObjectDisposedException(nameof(CaptureValue));
            return _clockReads.Begin();
        }
        internal override GameplayClockReading ReadClock(string field)
        {
            var source = "InLevel.LevelInformation." + field;
            if (_owner.MetadataProbe().Sources[field] is string error) return Missing(source, error);
            try
            {
                var value = _owner._api.Read<float>(_information.Target, field);
                if (!float.IsFinite(value)) return Missing(source, "Game clock value is not finite.");
                var unit = GameplayClockUnit.RawGameUnits; string? evidence;
                try
                {
                    if (_clockReads.Read(out evidence) is not null)
                    {
                        unit = field == "nowTime" ? GameplayClockUnit.ChartCoordinates : GameplayClockUnit.Seconds;
                        evidence = GameplayClockNativeProfile.Evidence;
                    }
                }
                catch (Exception ex) { evidence = "Unit profile unavailable: " + ex.Message; }
                return new(true, value, unit, source) { UnitEvidence = evidence };
            }
            catch (Exception ex) { return Missing(source, ex.Message); }
        }
        private GameplayClockNativeProfile RequireMapping(bool independent = false)
        {
            // Each explicit read consumer selects one freshly verified profile.
            // Control preparation and calls outside a read scope refresh afresh.
            string? error;
            var profile = (independent ? _owner.RefreshClockProfile(out error) : _clockReads.Read(out error))
                ?? throw new GameplayStateException(ErrorCode.OperationUnsupported,
                error ?? "No fixed clock semantics profile is available.");
            return RequireMapping(profile);
        }
        private GameplayClockNativeProfile RequireMapping(GameplayClockNativeProfile profile)
        {
            if (_progress is null || _owner._api.Read<IntPtr>(_controller.Target, "LevelInformation") != _information.Target
                || _owner._api.Read<IntPtr>(_information.Target, "progressControl") != _progress.Target
                || _owner._api.Read<IntPtr>(_progress.Target, "levelInformation") != _information.Target)
                throw new GameplayStateException(ErrorCode.NotReady, "The actual progress object does not belong to this captured LevelInformation.");
            return profile;
        }
        internal override GameplayClockSourceIdentity? ClockSourceIdentity
        {
            get
            {
                var profile = RequireMapping();
                var chart = _owner._api.Read<IntPtr>(_information.Target, "chart");
                if (chart == IntPtr.Zero || !_owner._api.ExactHookReceiverMatches(chart, profile.ChartClass))
                    throw new GameplayStateException(ErrorCode.NotReady, "The current exact chart source is unavailable.");
                if (_owner._api.Read<IntPtr>(_controller.Target, "pauseUIControl") != (_pause?.Target ?? IntPtr.Zero))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The captured pause state source was replaced.");
                return new(_progress!.Target.ToInt64(), chart.ToInt64(), _pause?.Target.ToInt64() ?? 0);
            }
        }
        internal override GameplayPlaybackClockReading ReadPlaybackClock()
        {
            _playbackReader = null; _playbackReadout = null;
            if (_owner._rateProvider is not INativePlaybackClockReadProvider reader) return base.ReadPlaybackClock();
            VerifyPlaybackCapture();
            // This reader supplies and verifies its own current native source
            // ticket. Refreshing the unrelated chart semantics here discarded
            // its result and repeated the full GetTime/group traversal.
            var descriptor = _sourceIdentity?.NativeDescriptor ?? throw new GameplayStateException(ErrorCode.NotReady,
                "The captured graph has no current native playback source descriptor.");
            var binding = descriptor.Binding;
            var identity = _sourceIdentity!.Value;
            if (!binding.Valid || !MatchesServiceRun(binding.RunId) || binding.Controller != ControllerIdentity
                || binding.Information != Identity || binding.Progress != identity.Progress || descriptor.Pause != identity.Pause
                || binding.Player != identity.Player || binding.AudioSource != identity.AudioSource || binding.Clip != identity.Clip)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The native playback descriptor differs from the current service capture.");
            var origin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration,
                binding.Controller, binding.Information, binding.Progress, descriptor.Pause);
            var readout = reader.ReadPlaybackClock(origin);
            if (readout.State.Binding != binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The playback reader returned another exact source binding.");
            _playbackReader = reader; _playbackReadout = readout;
            VerifyPlaybackClock();
            var phase = readout.State.Phase switch
            {
                NativePlaybackPhase.Paused => GameplayPlaybackPhase.Paused,
                NativePlaybackPhase.Playing => GameplayPlaybackPhase.Playing,
                NativePlaybackPhase.Scheduled => GameplayPlaybackPhase.Scheduled,
                _ => throw new GameplayStateException(ErrorCode.TargetExpired, "The native playback Clock is withdrawn.")
            };
            return new(true, new(readout.State.Revision, phase, readout.DspSeconds, readout.MediaSeconds, readout.State.Rate),
                "Current registered native playback policy, actual rooted DspTimeService sample and canonical Single media return domain. Controlled provider scope remains experimental.");
        }
        internal override void VerifyPlaybackClock()
        {
            if (_playbackReadout is not { } readout || _playbackReader is not { } reader)
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "This capture has no current native playback sample.");
            VerifyPlaybackCapture();
            reader.VerifyPlaybackClock(readout);
            VerifyPlaybackCapture();
            VerifyPlaybackClockState();
        }
        internal override void VerifyPlaybackClockState()
        {
            if (_playbackReadout is not { } readout)
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "This capture has no native playback sample.");
            if (_playbackReader is not { } reader || !ReferenceEquals(reader, _owner._rateProvider))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The captured playback reader ownership changed.");
            reader.VerifyPlaybackClockState(readout);
            if (!readout.Clock.HasExactReadState(readout.State))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The playback revision changed while validating the service capture.");
        }
        private void VerifyPlaybackCapture()
        {
            _owner._main.VerifyAccess();
            if (_owner._disposed || _disposed || _progress is null || _pause is null || !CurrentGraphMatches
                || !_owner.Alive(_progress.Target) || !_owner.Alive(_pause.Target)
                || !_owner._api.Unbox<bool>(_owner._api.Call(_controller.Target, "get_isActiveAndEnabled")))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The native playback capture or its roots are no longer current.");
        }
        internal override GameplayClockMappingSnapshot? ReadObservedClockState()
            => ReadMapping(Missing("Observed conversion validation", "Only current source state is being validated.", GameplayClockUnit.ChartCoordinates));
        private GameplayClockReading ConvertValue(double value, GameplayClockUnit source, GameplayClockUnit target,
            GameplayClockNativeProfile? profile = null)
        {
            profile = profile is null ? RequireMapping() : RequireMapping(profile);
            if (source == target) return new(true, value, target, "Identity in the same game-time domain") { UnitEvidence = GameplayClockNativeProfile.Evidence };
            // Calling the actual converter remains an independent native code
            // consumer even when its surrounding field reads share a profile.
            profile.VerifyOperation();
            var method = source == GameplayClockUnit.ChartCoordinates ? "ChartTimeToRealTime" : "RealTimeToChartTime";
            var converted = _owner._api.Unbox<float>(_owner._api.CallTyped(_progress!.Target, method, new[] { "System.Double" }, A.Value(value)));
            profile.VerifyOperation();
            if (!float.IsFinite(converted)) throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual chart conversion returned a nonfinite value.");
            return new(true, converted, target, "InLevel.ProgressControl." + method + "(System.Double)") { UnitEvidence = GameplayClockNativeProfile.Evidence };
        }
        internal override GameplayTimeConversion ConvertTime(GameplayTimeConversionRequest request)
            => new(request, ConvertValue(request.Value, request.SourceUnit, request.TargetUnit), GameplayClockNativeProfile.Evidence);
        internal override GameplayClockMappingSnapshot ReadMapping(GameplayClockReading game)
        {
            var profile = RequireMapping(); var api = _owner._api;
            var chart = api.Read<IntPtr>(_information.Target, "chart");
            if (chart == IntPtr.Zero || !api.ExactHookReceiverMatches(chart, profile.ChartClass))
                throw new GameplayStateException(ErrorCode.NotReady, "This captured run has no exact chart object.");
            using var chartRoot = api.Root(chart);
            var progress = api.Read<double>(_progress!.Target, "_nowTime");
            var offset = api.Read<double>(_progress.Target, "_offset");
            var chartDelay = api.Read<int>(chartRoot.Target, "chartDelayMs") / 1000d;
            var normalized = game is { Available: true, Value: double value, Unit: GameplayClockUnit.ChartCoordinates }
                ? ConvertValue(value, GameplayClockUnit.ChartCoordinates, GameplayClockUnit.Seconds, profile)
                : Missing("InLevel.ProgressControl.ChartTimeToRealTime", "No verified chart-coordinate sample is available.", GameplayClockUnit.Seconds);
            GameplayClockReading Seconds(double value, string source) => double.IsFinite(value)
                ? new(true, value, GameplayClockUnit.Seconds, source) { UnitEvidence = GameplayClockNativeProfile.Evidence }
                : Missing(source, "The captured value is not finite.", GameplayClockUnit.Seconds);
            return new(normalized, Seconds(progress, "InLevel.ProgressControl._nowTime"),
                offset >= -99d ? Seconds(offset, "InLevel.ProgressControl._offset (subtracted from MusicPlayer.GetTime)")
                    : Missing("InLevel.ProgressControl._offset", "The native offset initialization sentinel is still present.", GameplayClockUnit.Seconds),
                Seconds(chartDelay, "InLevel.Chart.chartDelayMs / 1000"), Playing, _pause is null ? null : Paused,
                GameplayClockNativeProfile.Evidence + " Paused/nonplaying progress is held; after levelOver the producer can advance by DeltaTime. MusicPlayer.GetTime may correct its DSP origin or use AudioSource.time; it is not invoked by these reads. Chart delay is reported separately and is not added to conversion again. DSP/host are sample correlations only; rate, latency and extrapolation remain unknown.")
                { BuildFingerprint = GameplayClockNativeProfile.Fingerprint };
        }
        internal override void Dispatch(GameplayControlAction action)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CaptureValue));
            if (action is GameplayControlAction.Pause or GameplayControlAction.Resume)
            {
                if (_pause is null) throw new GameplayStateException(ErrorCode.NotReady, "Pause UI is absent.");
                if (action == GameplayControlAction.Pause)
                {
                    // The game's Update path stops progress before opening PauseUI.
                    // PauseUI.Pause alone changes UI flags without stopping the chart.
                    _owner._api.Call(_controller.Target, "Pause");
                    if (Playing) throw new GameplayStateException(ErrorCode.InvalidGameState, "Original controller Pause returned without stopping progress; pause UI was not opened.");
                }
                _owner._api.Call(_pause.Target, action.ToString());
            }
            else _owner._api.Call(_controller.Target, action.ToString());
        }
        internal override bool? Postcondition(GameplayControlAction action) => action switch
        { GameplayControlAction.Pause => Paused && !Playing, GameplayControlAction.Resume => !Paused && Playing, _ => null };
        internal override GameplayPreparedControl Prepare(GameplayControlRequest request)
        {
            if (request.Action == GameplayControlAction.Seek) return PrepareSeek(request);
            if (request.Action != GameplayControlAction.SetSpeed) return base.Prepare(request);
            var provider = _owner._rateProvider ?? throw new GameplayStateException(ErrorCode.OperationUnsupported,
                "No complete native playback-rate provider is installed.");
            // Mapping is optional for an independently verified rate provider,
            // but this attempt must still observe a late or withdrawn hook.
            _owner.RefreshClockProfile(out _);
            void VerifyCapture()
            {
                _owner._main.VerifyAccess();
                if (_owner._disposed || _disposed || !CurrentGraphMatches || _progress is null || _pause is null
                    || !_owner.Alive(_progress.Target) || !_owner.Alive(_pause.Target)
                    || !_owner._api.Unbox<bool>(_owner._api.Call(_controller.Target, "get_isActiveAndEnabled")))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The rooted native gameplay controller/information/progress/pause capture is no longer current.");
                // Optional read availability has an independent lifetime. A value
                // write still requires the exact current descriptor captured here.
                if (provider is INativePlaybackRateSourceDescriptorProvider)
                {
                    var captured = _sourceIdentity?.NativeDescriptor;
                    var current = _owner.CaptureSourceDescriptor();
                    // A new capture can legitimately await native provider
                    // readiness. It never held descriptor authority to expire.
                    // A previously present or newly changed descriptor still
                    // invalidates this exact capture before provider entry.
                    if (captured is null && current is null)
                        throw new GameplayStateException(ErrorCode.NotReady,
                            "No current native source descriptor belongs to this captured playback graph.");
                    if (captured is null || captured != current)
                        throw new GameplayStateException(ErrorCode.TargetExpired,
                            "The exact native source descriptor for this value write is unavailable or changed.");
                }
            }
            VerifyCapture();
            var nativeRequest = request;
            if (provider is INativePlaybackRateSourceDescriptorProvider)
            {
                if (!MatchesServiceContext(request))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The rate request differs from this capture's service generation or run.");
                var descriptor = _sourceIdentity?.NativeDescriptor ?? throw new GameplayStateException(ErrorCode.NotReady,
                    "No current native source descriptor belongs to this captured playback graph.");
                var binding = descriptor.Binding;
                if (!binding.Valid || binding.RunId != request.ObservedRunId || binding.Controller != ControllerIdentity
                    || binding.Information != Identity || binding.Progress != _sourceIdentity!.Value.Progress
                    || descriptor.Pause != _sourceIdentity.Value.Pause || binding.Player != _sourceIdentity.Value.Player
                    || binding.AudioSource != _sourceIdentity.Value.AudioSource || binding.Clip != _sourceIdentity.Value.Clip)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The captured native source descriptor differs from this service request or rooted playback graph.");
                VerifyCapture();
                // Only the private capture maps its bound SDK request to the independently
                // verified native controller generation. Service receipts retain request.
                nativeRequest = request with { ControllerGeneration = binding.ControllerGeneration };
            }
            var origin = new NativePlaybackRateOrigin(nativeRequest.ObservedRunId ?? "", nativeRequest.ControllerGeneration,
                _controller.Target.ToInt64(), _information.Target.ToInt64(), _progress!.Target.ToInt64(), _pause!.Target.ToInt64());
            return NativePlaybackRateProviderAdoption.Prepare(provider, origin, nativeRequest, VerifyCapture);
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { _clip?.Dispose(); } finally { try { _audioSource?.Dispose(); } finally { try { _player?.Dispose(); } finally {
                try { _pause?.Dispose(); } finally { try { _progress?.Dispose(); } finally { try { _information.Dispose(); } finally { _controller.Dispose(); } } }
            } } }
        }
    }
}
internal sealed class GameplayStateException : InvalidOperationException
{
    internal ErrorCode Code { get; }
    internal GameplayStateException(ErrorCode code, string message) : base(message) => Code = code;
}
