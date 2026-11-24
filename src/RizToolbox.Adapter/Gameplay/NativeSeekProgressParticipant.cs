using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Owns the actual progress and three LevelInformation time caches.
/// Audio, the Clock, notes and visual state are separate transaction participants.
/// The applied offset is retained, never rewritten or augmented by chart delay.</summary>
internal sealed class NativeSeekProgressParticipant : IGameplayValueParticipant
{
    private static readonly List<NativeSeekProgressParticipant> Unreleased = new();
    private readonly NativeSeekProgressParticipant _registered;
    private readonly NativeRuntime _api;
    private readonly ISeekChartNativeProfile _profile;
    private readonly NativePlaybackBinding _binding;
    private readonly long _pause;
    private readonly GameplayControlRequest _request;
    private readonly Action _verifyOrigin;
    private readonly Action _verifyWriteAuthority;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<NativeRuntime.RootHandle> _roots = new();
    private readonly bool[] _attempted = new bool[4];
    private IntPtr _informationClass;
    private Values _baseline = null!, _target = null!;
    private double _offset;
    private float _clipLength;
    private bool _busy, _disposed, _releaseAttempted, _commitEntered, _restoreEntered, _committed;
    private Exception? _cleanupFailure;
    private sealed record Values(double Progress, float Chart, float Real, float Synced);

    internal SeekTargetCoordinates Coordinates { get; private set; } = null!;
    internal float MediaSeconds { get; private set; }
    internal double CanonicalConfirmedValue => _request.Unit == GameplayControlValueUnit.GameSeconds
        ? Coordinates.ProgressSeconds : Coordinates.ChartCoordinate;
    internal double ValueTolerance => Math.Max(.00001, Math.Abs(CanonicalConfirmedValue - _request.Value!.Value));
    public string Id => "native-seek/progress-time";

    private NativeSeekProgressParticipant(NativeRuntime api, ISeekChartNativeProfile profile,
        NativePlaybackBinding binding, long pause, GameplayControlRequest request, Action verifyOrigin, Action verifyWriteAuthority)
        => (_registered, _api, _profile, _binding, _pause, _request, _verifyOrigin, _verifyWriteAuthority)
            = (this, api, profile, binding, pause, request, verifyOrigin, verifyWriteAuthority);

    internal static NativeSeekProgressParticipant Capture(NativeRuntime api, ISeekChartNativeProfile profile,
        NativePlaybackBinding binding, long pause, GameplayControlRequest request, Action verifyOrigin, Action verifyWriteAuthority)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(verifyOrigin);
        ArgumentNullException.ThrowIfNull(verifyWriteAuthority);
        if (!binding.Valid || pause == 0 || request.ControllerGeneration <= 0 || request.Action != GameplayControlAction.Seek
            || request.ObservedRunId != binding.RunId || request.Value is not double value || !double.IsFinite(value)
            || request.Unit is not (GameplayControlValueUnit.GameSeconds or GameplayControlValueUnit.ChartCoordinates))
            throw new GameplayStateException(ErrorCode.InvalidArgument, "Seek needs this exact observed run and a finite game-seconds or chart-coordinate target.");
        var owner = new NativeSeekProgressParticipant(api, profile, binding, pause, request, verifyOrigin, verifyWriteAuthority);
        try { owner.Operation(owner.Initialize); return owner; }
        catch (Exception error)
        {
            try { owner.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekProgressCleanupOwner"] = owner; error.Data["SeekProgressCleanupFailure"] = cleanup; }
            throw;
        }
    }

    private void Initialize()
    {
        Origin(); _profile.VerifyCurrent(); Local();
        Retain(_binding.Controller, "Assembly-CSharp.dll", "", "NormalModeInLevelController");
        Retain(_binding.Information, "Assembly-CSharp.dll", "InLevel", "LevelInformation");
        Retain(_binding.Progress, "Assembly-CSharp.dll", "InLevel", "ProgressControl");
        Retain(_pause, "Assembly-CSharp.dll", "", "PauseUIControl");
        Retain(_binding.Player, "Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer");
        Retain(_binding.AudioSource, "UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
        Retain(_binding.Clip, "UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
        Retain(_api.Read<IntPtr>(Pointer(1), "chart").ToInt64(), "Assembly-CSharp.dll", "InLevel", "Chart");
        _informationClass = _api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        _api.RequireInstanceFieldLayout(_profile.ProgressClass, "_nowTime", "System.Double", 0x50);
        foreach (var (name, offset) in new[] { ("nowTime", 0xb0), ("nowRealTime", 0xb4), ("nowSyncedTime", 0xb8) })
            _api.RequireInstanceFieldLayout(_informationClass, name, "System.Single", offset);
        _offset = _api.Read<double>(Pointer(2), "_offset");
        _clipLength = _api.Unbox<float>(_api.Call(Pointer(6), "get_length"));
        if (!double.IsFinite(_offset) || _offset < -99d || !float.IsFinite(_clipLength) || _clipLength <= 0)
            throw new GameplayStateException(ErrorCode.NotReady, "Seek needs an initialized applied offset and a finite positive clip length.");
        VerifyGraph();
        _baseline = ReadValues();
        var progress = _request.Unit == GameplayControlValueUnit.GameSeconds ? _request.Value!.Value
            : Convert("ChartTimeToRealTime", _request.Value!.Value);
        var media = progress + _offset;
        if (!double.IsFinite(media) || media < 0 || media > _clipLength || !float.IsFinite((float)media))
            throw new GameplayStateException(ErrorCode.InvalidArgument, "The requested progress position maps outside the actual audio clip.");
        MediaSeconds = (float)media;
        if (MediaSeconds < 0 || MediaSeconds > _clipLength)
            throw new GameplayStateException(ErrorCode.InvalidArgument, "The native Single media position is outside the actual audio clip.");
        progress = (double)MediaSeconds - _offset;
        var chart = Convert("RealTimeToChartTime", progress);
        if (!double.IsFinite(progress) || !float.IsFinite((float)progress))
            throw new GameplayStateException(ErrorCode.InvalidArgument, "The canonical progress target exceeds the native cache domain.");
        Coordinates = new(progress, (float)progress, chart);
        _target = new(progress, chart, (float)progress, (float)progress);
        VerifyGraph(); RequireValues(_baseline);
    }

    private void Retain(long pointer, string image, string ns, string type)
    {
        Local();
        var root = _api.RetainExactObject(new(pointer), _api.Class(image, ns, type));
        _roots.Add(root); Local();
    }
    private IntPtr Pointer(int index) => _roots[index].Target;
    private float Convert(string method, double value)
    {
        Local(); var result = _api.Unbox<float>(_api.CallTyped(Pointer(2), method, new[] { "System.Double" }, A.Value(value))); Local();
        if (!float.IsFinite(result)) throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual chart conversion returned a nonfinite position.");
        return result;
    }
    private void VerifyGraph()
    {
        Origin(); _profile.VerifyCurrent(); Local();
        if (_roots.Count != 8 || _api.Read<IntPtr>(Pointer(0), "LevelInformation") != Pointer(1)
            || _api.Read<IntPtr>(Pointer(0), "pauseUIControl") != Pointer(3)
            || _api.Read<IntPtr>(Pointer(1), "progressControl") != Pointer(2)
            || _api.Read<IntPtr>(Pointer(2), "levelInformation") != Pointer(1)
            || _api.Read<IntPtr>(Pointer(1), "chart") != Pointer(7)
            || _api.Read<IntPtr>(Pointer(2), "<MusicPlayer>k__BackingField") != Pointer(4)
            || _api.Read<IntPtr>(Pointer(2), "audioSource") != Pointer(5)
            || _api.Read<IntPtr>(Pointer(4), "audioSource") != Pointer(5)
            || _api.Call(Pointer(5), "get_clip") != Pointer(6))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The retained Seek progress/chart/audio graph was replaced.");
        if (!_api.Unbox<bool>(_api.Call(Pointer(0), "get_isActiveAndEnabled"))
            || !_api.Read<bool>(Pointer(3), "_pausing") || !_api.Unbox<bool>(_api.Call(Pointer(3), "get_CanDoOperation"))
            || !_api.Read<bool>(Pointer(1), "loadOver") || !_api.Read<bool>(Pointer(2), "timeReady")
            || _api.Read<bool>(Pointer(1), "levelOver") || _api.Read<bool>(Pointer(1), "crashed")
            || _api.Read<bool>(Pointer(2), "isPlaying") || _api.Read<bool>(Pointer(2), "prepareToPlay")
            || _api.Unbox<bool>(_api.Call(Pointer(5), "get_isPlaying")))
            throw new GameplayStateException(ErrorCode.InvalidGameState, "Seek requires the same ready, paused, non-ending song and inactive audio schedule.");
        if (!Same(_api.Read<double>(Pointer(2), "_offset"), _offset)
            || !Same(_api.Unbox<float>(_api.Call(Pointer(6), "get_length")), _clipLength))
            throw new GameplayStateException(ErrorCode.Conflict, "The applied offset or actual clip length changed during Seek.");
        Origin();
    }
    private Values ReadValues()
    {
        Local();
        var value = new Values(_api.Read<double>(Pointer(2), "_nowTime"), _api.Read<float>(Pointer(1), "nowTime"),
            _api.Read<float>(Pointer(1), "nowRealTime"), _api.Read<float>(Pointer(1), "nowSyncedTime"));
        Local();
        if (!double.IsFinite(value.Progress) || !float.IsFinite(value.Chart) || !float.IsFinite(value.Real) || !float.IsFinite(value.Synced))
            throw new GameplayStateException(ErrorCode.ObservationUnavailable, "A retained progress cache is nonfinite.");
        return value;
    }
    private static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    private static bool Same(Values a, Values b) => Same(a.Progress, b.Progress) && Same(a.Chart, b.Chart) && Same(a.Real, b.Real) && Same(a.Synced, b.Synced);
    private static double At(Values value, int slot) => slot switch { 0 => value.Progress, 1 => value.Chart, 2 => value.Real, 3 => value.Synced, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    private bool SlotMatches(Values actual, Values expected, int slot)
        => slot == 0 ? Same(actual.Progress, expected.Progress) : Same((float)At(actual, slot), (float)At(expected, slot));
    private void RequireValues(Values expected)
    {
        if (!Same(ReadValues(), expected))
            throw new GameplayStateException(ErrorCode.Conflict, "The progress cache no longer matches this transaction's exact expected values.");
    }
    private void Write(int slot, Values value, Action attempted)
    {
        Local();
        var name = slot switch { 0 => "_nowTime", 1 => "nowTime", 2 => "nowRealTime", 3 => "nowSyncedTime", _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
        var argument = slot == 0 ? A.Value(value.Progress) : A.Value((float)At(value, slot));
        _api.WriteExactGuarded(_roots[slot == 0 ? 2 : 1], slot == 0 ? _profile.ProgressClass : _informationClass,
            name, slot == 0 ? "System.Double" : "System.Single", argument, () =>
            {
                // Metadata/export/root lookups can withdraw the bound source
                // after VerifyGraph. Check the same source/Clock operation at
                // the actual store boundary, without another graph getter.
                Local(); _verifyWriteAuthority(); Local(); attempted();
            });
        Local();
    }

    public void VerifyPrepared() => Operation(() => { VerifyGraph(); RequireValues(_baseline); });
    public void Commit() => Operation(() =>
    {
        if (_commitEntered || _restoreEntered) throw new InvalidOperationException("Progress Seek commit is once-only and precedes restoration.");
        _commitEntered = true; VerifyGraph(); RequireValues(_baseline);
        var expected = _baseline;
        for (var slot = 0; slot < 4; slot++)
        {
            VerifyGraph(); RequireValues(expected); var index = slot;
            Write(slot, _target, () => _attempted[index] = true);
            expected = slot switch { 0 => expected with { Progress = _target.Progress }, 1 => expected with { Chart = _target.Chart },
                2 => expected with { Real = _target.Real }, _ => expected with { Synced = _target.Synced } };
            RequireValues(expected);
        }
        VerifyGraph(); RequireValues(_target); _committed = true;
    });
    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_committed || _restoreEntered) throw new InvalidOperationException("Seek progress must commit before confirmation.");
        VerifyGraph(); var actual = ReadValues();
        var confirmed = _request.Unit == GameplayControlValueUnit.GameSeconds ? actual.Progress : actual.Chart;
        return new GameplayControlConfirmation(Same(actual, _target), "Independent readback of the retained progress and all three published game-time caches.", confirmed, _request.Unit);
    });
    public void Restore() => Restore(static () => { });
    internal void Restore(Action verifyRestorationWrite) => Operation(() =>
    {
        ArgumentNullException.ThrowIfNull(verifyRestorationWrite);
        if (_restoreEntered) throw new InvalidOperationException("Progress Seek restoration is once-only.");
        _restoreEntered = true; var errors = new List<Exception>();
        for (var slot = 3; slot >= 0; slot--)
        {
            if (!_attempted[slot]) continue;
            try
            {
                VerifyGraph(); var actual = ReadValues();
                if (SlotMatches(actual, _baseline, slot)) continue;
                if (!SlotMatches(actual, _target, slot))
                    throw new GameplayStateException(ErrorCode.Conflict, "A later writer owns a progress cache; restoration refused.");
                // Write invokes this prerequisite after metadata/root lookups,
                // immediately before the actual inverse field store.
                Write(slot, _baseline, verifyRestorationWrite);
                if (!SlotMatches(ReadValues(), _baseline, slot)) throw new IOException("A restored progress cache did not read back.");
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("One or more progress cache restorations failed.", errors);
    });
    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        VerifyGraph(); return new GameplayControlRestoration(Same(ReadValues(), _baseline), "Independent readback of the complete original progress caches and unchanged applied offset.");
    });
    private void Owner()
    {
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_registered, this))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A foreign progress participant owns no Seek roots or field writes.");
    }
    private void Local() { Owner(); if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekProgressParticipant)); }
    private void Origin() { Local(); _verifyOrigin(); Local(); }
    private void Operation(Action action) => Operation(() => { action(); return 0; });
    private T Operation<T>(Func<T> action)
    {
        Local(); if (_busy) throw new GameplayStateException(ErrorCode.Conflict, "Progress Seek cannot reenter its retained operation.");
        _busy = true; Exception? failure = null;
        try { return action(); }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            _busy = false;
            if (_disposed)
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) throw;
                    failure.Data["SeekProgressCleanupOwner"] = this;
                    failure.Data["SeekProgressCleanupFailure"] = cleanup;
                }
        }
    }
    public void Dispose()
    {
        Owner(); _disposed = true; if (!_busy) Release();
    }
    private void Release()
    {
        if (_releaseAttempted) { if (_cleanupFailure is not null) throw _cleanupFailure; return; }
        _releaseAttempted = true; var errors = new List<Exception>();
        foreach (var root in _roots.AsEnumerable().Reverse())
            try { root.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count == 0) return;
        Unreleased.Add(this);
        _cleanupFailure = new AggregateException("Seek progress retains unknown root-release outcomes without retry.", errors);
        _cleanupFailure.Data["SeekProgressCleanupOwner"] = this;
        throw _cleanupFailure;
    }
}
