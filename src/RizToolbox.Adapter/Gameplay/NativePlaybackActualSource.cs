using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Actual NativeRuntime access to one independently rooted graph. This is an accessor,
/// not a return/schedule/writer authority. Equal-value foreign writes require a complete observer.</summary>
internal sealed class NativePlaybackActualSource : IDisposable
{
    private readonly NativeRuntime _api;
    private readonly NativePlaybackBinding _binding;
    private readonly long _pause;
    private readonly NativePlaybackFreshSourceCapture.Adoption? _freshRoots;
    private readonly NativePlaybackStockRootedSource? _freshBorrowSource;
    private readonly NativePlaybackActualSource _registeredActual;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<NativeRuntime.RootHandle> _roots = new();
    private readonly PlaybackFreshReadScopes _calls = new();
    private readonly IntPtr _controllerClass, _informationClass, _progressClass, _pauseClass,
        _playerClass, _sourceClass, _clipClass, _unityObjectClass, _dspClass;
    private readonly IntPtr _pitchMethod, _clipMethod;
    private bool _disposed;
    private ExceptionDispatchInfo? _rootReleaseFailure;

    internal NativePlaybackActualSource(NativeRuntime api, NativePlaybackBinding binding, long pause)
        : this(api, binding, pause, null, null) { }
    internal NativePlaybackActualSource(NativeRuntime api, NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackStockRootedSource source)
        : this(api, adoption.ReadConstructionIdentity(source, api), adoption, source) { }
    private NativePlaybackActualSource(NativeRuntime api,
        (NativePlaybackBinding Binding, NativePlaybackRateOrigin Origin) identity,
        NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackStockRootedSource source)
        : this(api, identity.Binding, identity.Origin.Pause, adoption, source) { }
    private NativePlaybackActualSource(NativeRuntime api, NativePlaybackBinding binding, long pause,
        NativePlaybackFreshSourceCapture.Adoption? adoption, NativePlaybackStockRootedSource? source)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (!binding.Valid || pause == 0) throw new GameplayStateException(ErrorCode.InvalidArgument,
            "The native playback accessor requires a complete immutable graph and exact pause receiver.");
        (_api, _binding, _pause, _freshRoots) = (api, binding, pause, adoption);
        (_freshBorrowSource, _registeredActual) = (source, this);
        _controllerClass = api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        _informationClass = api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        _progressClass = api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl");
        _pauseClass = api.Class("Assembly-CSharp.dll", "", "PauseUIControl");
        _playerClass = api.Class("Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer");
        _sourceClass = api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
        _clipClass = api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
        _unityObjectClass = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _dspClass = api.Class("Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core.Services", "DspTimeService");
        try
        {
            if (adoption is not null) _roots.AddRange(adoption.BorrowRoots(source!, this));
            else foreach (var row in new[] { (binding.Controller, _controllerClass), (binding.Information, _informationClass),
                (binding.Progress, _progressClass), (pause, _pauseClass), (binding.Player, _playerClass),
                (binding.AudioSource, _sourceClass), (binding.Clip, _clipClass) })
                _roots.Add(api.RetainExactObject(new(row.Item1), row.Item2));
            RequireField(_controllerClass, "LevelInformation", "InLevel.LevelInformation");
            RequireField(_controllerClass, "pauseUIControl", "PauseUIControl");
            RequireField(_informationClass, "progressControl", "InLevel.ProgressControl");
            RequireField(_progressClass, "levelInformation", "InLevel.LevelInformation");
            RequireField(_progressClass, "<MusicPlayer>k__BackingField", "Pigeon.MUGToolkit.Core.MUGMusicPlayer");
            RequireField(_progressClass, "audioSource", "UnityEngine.AudioSource");
            foreach (var name in new[] { "levelOver", "crashed", "loadOver" }) RequireField(_informationClass, name, "System.Boolean");
            foreach (var name in new[] { "isPlaying", "prepareToPlay", "timeReady" }) RequireField(_progressClass, name, "System.Boolean");
            RequireField(_pauseClass, "_pausing", "System.Boolean");
            RequireField(_progressClass, "_nowTime", "System.Double"); RequireField(_progressClass, "_offset", "System.Double");
            RequireField(_informationClass, "nowTime", "System.Single");
            // Pinned instructions read the nullable tag at player+18 and its double at +20.
            // This projection does not add a general Nullable<T> runtime_invoke ABI.
            VerifyPlayerLayout();
            _pitchMethod = RequireMethod(_sourceClass, "set_pitch", "System.Void", false, "System.Single");
            _clipMethod = RequireMethod(_sourceClass, "set_clip", "System.Void", false, "UnityEngine.AudioClip");
            RequireMethod(_sourceClass, "get_clip", "UnityEngine.AudioClip");
            RequireMethod(_sourceClass, "get_pitch", "System.Single"); RequireMethod(_sourceClass, "get_time", "System.Single");
            RequireMethod(_sourceClass, "set_time", "System.Void", false, "System.Single");
            RequireMethod(_sourceClass, "PlayScheduled", "System.Void", false, "System.Double");
            RequireMethod(_sourceClass, "Pause", "System.Void"); RequireMethod(_sourceClass, "Stop", "System.Void");
            RequireMethod(_sourceClass, "get_isPlaying", "System.Boolean");
            RequireMethod(_sourceClass, "get_isActiveAndEnabled", "System.Boolean");
            RequireMethod(_controllerClass, "get_isActiveAndEnabled", "System.Boolean");
            RequireMethod(_clipClass, "get_length", "System.Single");
            RequireMethod(_dspClass, "get_DspTime", "System.Double", true);
            if (RequireMethod(_playerClass, "GetTime", "System.Single").ToInt64() != binding.GetTimeMethod)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The exact player GetTime MethodInfo changed.");
            VerifyGraph();
        }
        catch { try { ReleaseRoots(); } catch { } throw; }
    }

    internal void VerifyFreshBorrowCandidate(NativePlaybackFreshSourceCapture.Adoption adoption,
        NativePlaybackStockRootedSource source, NativeRuntime api)
    {
        VerifyThread();
        if (!ReferenceEquals(_registeredActual, this) || !ReferenceEquals(_freshRoots, adoption)
            || !ReferenceEquals(_freshBorrowSource, source) || !ReferenceEquals(_api, api) || _disposed || _roots.Count != 0)
            throw new GameplayStateException(ErrorCode.TargetExpired, "Only this exact constructing accessor may borrow the fresh roots.");
    }
    internal void VerifyGraph()
    {
        using var call = EnterCall();
        if (_freshRoots is { } fresh)
        {
            // EnterAccessor performs the complete current capture graph,
            // claimed ticket, metadata and active/clip checks. The accessor
            // borrowed those exact handles and immutable identity; prove that
            // projection here instead of recursively repeating the same graph
            // through ReadActive. No sample or native action occurs between.
            using var borrow = fresh.EnterAccessor(this);
            Open();
            fresh.VerifyAccessorProjection(this, _api, _binding, _pause, _roots);
            fresh.VerifyAccessorOpen(this);
            Open();
            return;
        }
        Open();
        var expected = new[] { _binding.Controller, _binding.Information, _binding.Progress, _pause,
            _binding.Player, _binding.AudioSource, _binding.Clip };
        var classes = new[] { _controllerClass, _informationClass, _progressClass, _pauseClass,
            _playerClass, _sourceClass, _clipClass };
        for (var i = 0; i < expected.Length; i++)
            if (_roots[i].Target.ToInt64() != expected[i] || _api.ObjectClass(_roots[i].Target) != classes[i])
                throw new GameplayStateException(ErrorCode.TargetExpired, "A playback root or its exact class was replaced.");
        if (_api.Read<IntPtr>(Controller, "LevelInformation") != Information
            || _api.Read<IntPtr>(Controller, "pauseUIControl") != Pause
            || _api.Read<IntPtr>(Information, "progressControl") != Progress
            || _api.Read<IntPtr>(Progress, "levelInformation") != Information
            || _api.Read<IntPtr>(Progress, "<MusicPlayer>k__BackingField") != Player
            || _api.Read<IntPtr>(Progress, "audioSource") != Source
            || _api.Read<IntPtr>(Player, "audioSource") != Source
            || CheckedReturn(_api.Call(Source, "get_clip")) != Clip
            || _api.Method(_playerClass, "GetTime", 0).ToInt64() != _binding.GetTimeMethod)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The exact playback controller/progress/player/source/clip graph changed.");
        if (!CheckedValue<bool>(_api.Call(Controller, "get_isActiveAndEnabled")) || !ReadActive()
            || !CheckedValue<bool>(_api.CallStatic(_unityObjectClass, "op_Implicit", NativeRuntime.Arg.Ref(Clip))))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The actual playback receiver, source, or clip is no longer alive and active.");
        _freshRoots?.VerifyAccessorOpen(this);
        Open();
    }

    // The fifth handle is the independently retained player in the existing
    // seven-root graph. A constructor watch must borrow this exact handle;
    // allocating another handle would hide a source lifetime mismatch.
    internal NativeRuntime.RootHandle ProgressRoot
    {
        get { Open(); return _roots[2]; }
    }
    internal NativeRuntime.RootHandle PlayerRoot
    {
        get { Open(); return _roots[4]; }
    }

    // The sixth existing root owns the AudioSource independently of player.
    internal NativeRuntime.RootHandle AudioSourceRoot
    {
        get { Open(); return _roots[5]; }
    }
    internal NativeRuntime.RootHandle ClipRoot
    {
        get { Open(); return _roots[6]; }
    }

    internal NativePlaybackRateSample Read(long authorityRevision, long schedulingRevision)
    {
        using var call = EnterCall();
        // The first and final VerifyGraph below keep the complete fresh checks.
        // This outer pin only retains roots across all nested getter scopes.
        using var borrow = _freshRoots?.PinAccessorLifetime(this);
        VerifyGraph(); VerifyPlayerLayout();
        var origin = ReadOrigin();
        var sample = new NativePlaybackRateSample(_binding, authorityRevision, schedulingRevision,
            _api.Read<bool>(Pause, "_pausing"), ReadPlaying(), _api.Read<bool>(Progress, "isPlaying"),
            _api.Read<bool>(Progress, "prepareToPlay"), _api.Read<bool>(Progress, "timeReady") && _api.Read<bool>(Information, "loadOver"),
            _api.Read<bool>(Information, "levelOver"), _api.Read<bool>(Information, "crashed"), ReadDsp(), ReadMedia(),
            CheckedValue<float>(_api.Call(Clip, "get_length")), CheckedValue<float>(_api.Call(Source, "get_pitch")),
            _api.Read<double>(Progress, "_nowTime"), _api.Read<float>(Information, "nowTime"),
            _api.Read<double>(Progress, "_offset"), origin.HasValue, origin.Value);
        VerifyGraph();
        if (origin != ReadOrigin()) throw new GameplayStateException(ErrorCode.Conflict, "The stock nullable origin changed during the actual source read.");
        _freshRoots?.VerifyAccessorOpen(this);
        Open();
        return sample;
    }

    internal double VerifyPausedPitchRecoveryBaseline(NativePlaybackRateSample baseline)
    {
        using var call = EnterCall();
        using var borrow = _freshRoots?.PinAccessorLifetime(this);
        VerifyGraph(); VerifyPlayerLayout();
        var origin = ReadOrigin();
        var paused = _api.Read<bool>(Pause, "_pausing");
        var playing = ReadPlaying();
        var progressPlaying = _api.Read<bool>(Progress, "isPlaying");
        var preparing = _api.Read<bool>(Progress, "prepareToPlay");
        var ready = _api.Read<bool>(Progress, "timeReady") && _api.Read<bool>(Information, "loadOver");
        var over = _api.Read<bool>(Information, "levelOver");
        var crashed = _api.Read<bool>(Information, "crashed");
        var dsp = ReadDsp();
        var media = ReadMedia();
        var length = CheckedValue<float>(_api.Call(Clip, "get_length"));
        var progress = _api.Read<double>(Progress, "_nowTime");
        var chart = _api.Read<float>(Information, "nowTime");
        var offset = _api.Read<double>(Progress, "_offset");
        VerifyGraph();
        if (baseline.Binding != _binding || !paused || playing || progressPlaying || preparing || !ready || over || crashed
            || paused != baseline.Paused || playing != baseline.AudioPlaying || progressPlaying != baseline.ProgressPlaying
            || preparing != baseline.Preparing || ready != baseline.TimeReady || over != baseline.LevelOver || crashed != baseline.Crashed
            || !double.IsFinite(dsp) || dsp < baseline.DspSeconds
            || media != baseline.AudioPositionSeconds || length != baseline.ClipLengthSeconds
            || progress != baseline.ProgressSeconds || chart != baseline.ChartPosition || offset != baseline.AppliedOffsetSeconds
            || origin.HasValue != baseline.HasStockOrigin || origin.Value != baseline.StockOriginSeconds || origin != ReadOrigin())
            throw new GameplayStateException(ErrorCode.Conflict,
                "The actual paused recovery source differs from the retained non-pitch baseline.");
        _freshRoots?.VerifyAccessorOpen(this); Open();
        return dsp;
    }

    internal double VerifyPausedTimeRecoveryBaseline(NativePlaybackRateSample baseline)
    {
        using var call = EnterCall();
        using var borrow = _freshRoots?.PinAccessorLifetime(this);
        VerifyGraph(); VerifyPlayerLayout();
        var origin = ReadOrigin();
        var paused = _api.Read<bool>(Pause, "_pausing");
        var playing = ReadPlaying();
        var progressPlaying = _api.Read<bool>(Progress, "isPlaying");
        var preparing = _api.Read<bool>(Progress, "prepareToPlay");
        var ready = _api.Read<bool>(Progress, "timeReady") && _api.Read<bool>(Information, "loadOver");
        var over = _api.Read<bool>(Information, "levelOver");
        var crashed = _api.Read<bool>(Information, "crashed");
        var dsp = ReadDsp();
        // Media is the unconfirmed field. Its real native continuation owns the
        // inverse; never use a guessed or previously cached media getter here.
        var pitch = CheckedValue<float>(_api.Call(Source, "get_pitch"));
        var length = CheckedValue<float>(_api.Call(Clip, "get_length"));
        var progress = _api.Read<double>(Progress, "_nowTime");
        var chart = _api.Read<float>(Information, "nowTime");
        var offset = _api.Read<double>(Progress, "_offset");
        VerifyGraph();
        if (baseline.Binding != _binding || !paused || playing || progressPlaying || preparing || !ready || over || crashed
            || paused != baseline.Paused || playing != baseline.AudioPlaying || progressPlaying != baseline.ProgressPlaying
            || preparing != baseline.Preparing || ready != baseline.TimeReady || over != baseline.LevelOver || crashed != baseline.Crashed
            || !double.IsFinite(dsp) || dsp < baseline.DspSeconds
            || pitch != baseline.Pitch || length != baseline.ClipLengthSeconds
            || progress != baseline.ProgressSeconds || chart != baseline.ChartPosition || offset != baseline.AppliedOffsetSeconds
            || origin.HasValue != baseline.HasStockOrigin || origin.Value != baseline.StockOriginSeconds || origin != ReadOrigin())
            throw new GameplayStateException(ErrorCode.Conflict,
                "The actual paused recovery source differs from the retained non-media baseline.");
        _freshRoots?.VerifyAccessorOpen(this); Open();
        return dsp;
    }

    internal bool ReadActive()
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); var result = CheckedValue<bool>(_api.Call(Source, "get_isActiveAndEnabled")); _freshRoots?.VerifyAccessorOpen(this); Open(); return result; }
    internal bool ReadPlaying()
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); var result = CheckedValue<bool>(_api.Call(Source, "get_isPlaying")); _freshRoots?.VerifyAccessorOpen(this); Open(); return result; }
    internal double ReadDsp()
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); var result = CheckedValue<double>(_api.CallStatic(_dspClass, "get_DspTime")); _freshRoots?.VerifyAccessorOpen(this); Open(); return result; }
    internal float ReadMedia()
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); var result = CheckedValue<float>(_api.Call(Source, "get_time")); _freshRoots?.VerifyAccessorOpen(this); Open(); return result; }
    internal void InvokeAudioPause()
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); _api.Call(Source, "Pause"); _freshRoots?.VerifyAccessorOpen(this); Open(); }
    internal void InvokeStop()
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); _api.Call(Source, "Stop"); _freshRoots?.VerifyAccessorOpen(this); Open(); }
    internal void InvokeTime(float seconds)
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); _api.Call(Source, "set_time", NativeRuntime.Arg.Value(seconds)); _freshRoots?.VerifyAccessorOpen(this); Open(); }
    internal void InvokeSchedule(double dspSeconds)
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); _api.Call(Source, "PlayScheduled", NativeRuntime.Arg.Value(dspSeconds)); _freshRoots?.VerifyAccessorOpen(this); Open(); }
    internal void InvokePitch(float pitch)
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); _api.Invoke(_pitchMethod, Source, NativeRuntime.Arg.Value(pitch)); _freshRoots?.VerifyAccessorOpen(this); Open(); }
    internal void InvokeClip(IntPtr clip)
    { using var call = EnterCall(); using var borrow = _freshRoots?.EnterAccessor(this); _api.Invoke(_clipMethod, Source, NativeRuntime.Arg.Ref(clip)); _freshRoots?.VerifyAccessorOpen(this); Open(); }

    // A call may synchronously close this accessor. Keep its roots through
    // return, but stop this traversal before unboxing or invoking another getter.
    private IntPtr CheckedReturn(IntPtr result) { _freshRoots?.VerifyAccessorOpen(this); Open(); return result; }
    private T CheckedValue<T>(IntPtr result) where T : struct
    { var value = _api.Unbox<T>(CheckedReturn(result)); _freshRoots?.VerifyAccessorOpen(this); Open(); return value; }

    private (bool HasValue, double Value) ReadOrigin()
    {
        var address = IntPtr.Add(Player, 0x18);
        HookTargetValidation.VerifyReadable(address, 16);
        var tag = Marshal.ReadByte(address);
        if (tag > 1) throw new UnsupportedSignatureException("The stock nullable origin tag is not a Boolean.");
        var value = tag == 0 ? 0 : BitConverter.Int64BitsToDouble(Marshal.ReadInt64(address, 8));
        if (!double.IsFinite(value)) throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual stock DSP origin is nonfinite.");
        return (tag != 0, value);
    }
    private void VerifyPlayerLayout()
    {
        _api.RequireInstanceFieldLayout(_playerClass, "audioSource", "UnityEngine.AudioSource", 0x10);
        _api.RequireInstanceFieldLayout(_playerClass, "startPlayTime", "System.Nullable<System.Double>", 0x18);
    }
    private void RequireField(IntPtr cls, string name, string type)
    {
        if (_api.DescribeFieldType(cls, name) != type) throw new UnsupportedSignatureException("Playback field type differs: " + name);
    }
    private IntPtr RequireMethod(IntPtr cls, string name, string result, bool isStatic = false, params string[] parameters)
    {
        var method = _api.Method(cls, name, parameters.Length, isStatic, parameters);
        if (_api.DescribeReturnType(method) != result) throw new UnsupportedSignatureException("Playback method return differs: " + name);
        return method;
    }
    private IntPtr Controller => _roots[0].Target;
    private IntPtr Information => _roots[1].Target;
    private IntPtr Progress => _roots[2].Target;
    private IntPtr Pause => _roots[3].Target;
    private IntPtr Player => _roots[4].Target;
    private IntPtr Source => _roots[5].Target;
    private IntPtr Clip => _roots[6].Target;
    private void VerifyThread()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "The actual rooted playback accessor is owner-thread bound.");
    }
    private void VerifyOwner()
    {
        VerifyThread();
        if (!ReferenceEquals(_registeredActual, this))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A copied playback accessor owns no roots or call responsibility.");
    }
    private void Open() { VerifyOwner(); _freshRoots?.VerifyAccessorRegistration(this); if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackActualSource)); }

    // This scope retains roots only. Every getter/action still verifies its
    // current graph/registration; a reentrant Dispose closes new use at once
    // and releases owned roots only after the actual call has returned.
    private IDisposable EnterCall()
    {
        Open();
        return new ActualCall(this, _calls.Enter());
    }
    private sealed class ActualCall : IDisposable
    {
        private readonly NativePlaybackActualSource _owner;
        private readonly IDisposable _scope;
        private readonly ActualCall _registered;
        private bool _closed;
        internal ActualCall(NativePlaybackActualSource owner, IDisposable scope)
            => (_owner, _scope, _registered) = (owner, scope, this);
        public void Dispose()
        {
            _owner.VerifyOwner();
            if (!ReferenceEquals(_registered, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied accessor call cannot release retained roots.");
            if (_closed) return;
            _closed = true;
            _scope.Dispose();
            if (_owner._disposed && _owner._calls.Active == 0) _owner.ReleaseRoots();
        }
    }
    private void ReleaseRoots()
    {
        VerifyOwner(); // Reject before any handle attempt or list mutation so owner-thread retry remains possible.
        _rootReleaseFailure?.Throw();
        if (_calls.Active != 0) return;
        if (_freshRoots is { } adoption) { adoption.ReleaseAccessor(this); _roots.Clear(); return; }
        Exception? first = null;
        foreach (var root in _roots.AsEnumerable().Reverse().ToArray())
        {
            try { root.Dispose(); _roots.Remove(root); }
            catch (Exception error) { first ??= error; }
        }
        // RootHandle consumes its id before native free. A failed attempt may
        // already have freed it, so keep the failed record and never replay the
        // attempt or turn a later no-op Dispose into successful retirement.
        if (first is not null) { _rootReleaseFailure = ExceptionDispatchInfo.Capture(first); _rootReleaseFailure.Throw(); }
    }
    public void Dispose()
    {
        VerifyOwner(); _freshRoots?.VerifyAccessorRegistration(this);
        if (_disposed) { _rootReleaseFailure?.Throw(); return; }
        _disposed = true; _calls.Close(); ReleaseRoots();
    }
}
