using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

// One original run owns the dependent game-state stages. In particular a later
// stage is never verified against the target planner's pre-mutation snapshot.
internal sealed partial class NativeSeekGameStatePreparation : IGameplayValueParticipant
{
    private readonly NativeSeekGameStatePreparation _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NativeRuntime _api;
    private readonly GameEventState _state;
    private readonly GameEventState.PlaybackRunAssociation _run;
    private readonly JudgementModule.SeekBackendAccess _judgement;
    private readonly Profiles _profiles;
    private readonly Func<JudgementRunCapture, NativeSeekTargetStatePlan, ISeekTargetNativeProfile,
        NativeJudgementBackend.SeekHoldEpoch> _prepareEpoch;
    private readonly List<IDisposable> _owners = new();
    private readonly List<IGameplayValueParticipant> _values = new();
    private JudgementRunCapture _capture = null!;
    private NativeSeekTargetStatePlan _target = null!;
    private NativeSeekChartPlan _chart = null!;
    private NativeRuntime.RootHandle _information = null!;
    private NativeExactReadField _speedField = null!;
    private float _speed;
    private bool _busy, _disposed, _entered, _restoring, _committed, _restored;
    private int _attemptedValues;
    private readonly List<Exception> _cleanup = new();
    private readonly List<IDisposable> _failedOwners = new();
    internal SeekTargetCoordinates Coordinates { get; }
    public string Id => "native.seek.game-state";

    private NativeSeekGameStatePreparation(NativeRuntime api, GameEventState state,
        GameEventState.PlaybackRunAssociation run, GameplayClockNativeProfile clock,
        SeekTargetCoordinates coordinates, JudgementModule.SeekBackendAccess judgement)
    {
        _registered = this; _api = api; _state = state; _run = run;
        _judgement = judgement; Coordinates = coordinates;
        Current(); _profiles = GetProfiles(api, clock, judgement); Current();
        _prepareEpoch = (capture, target, profile) => judgement.Backend.PrepareSeekHoldEpoch(judgement, capture, target, profile);
    }

    // Offline composition supplies controlled profiles to the same capture,
    // commit and inverse. The game entry still constructs every fixed profile
    // and obtains the epoch from its original installed backend.
    private NativeSeekGameStatePreparation(NativeRuntime api, GameEventState state,
        GameEventState.PlaybackRunAssociation run, SeekTargetCoordinates coordinates,
        JudgementModule.SeekBackendAccess judgement, Profiles profiles,
        Func<JudgementRunCapture, NativeSeekTargetStatePlan, ISeekTargetNativeProfile,
            NativeJudgementBackend.SeekHoldEpoch> prepareEpoch)
    {
        _registered = this; _api = api; _state = state; _run = run;
        _judgement = judgement; Coordinates = coordinates; _profiles = profiles; _prepareEpoch = prepareEpoch;
        Current(); profiles.Verify(); Current();
        if (!ReferenceEquals(profiles.Api, api) || !ReferenceEquals(profiles.Start, judgement.StartCoroutineBinding)
            || !ReferenceEquals(profiles.Move, judgement.HoldMoveBinding))
            throw new InvalidOperationException("The composed profiles belong to another runtime or installed Hold observers.");
    }

    internal static NativeSeekGameStatePreparation Prepare(NativeRuntime api, GameEventState state,
        GameEventState.PlaybackRunAssociation run, SeekTargetCoordinates coordinates,
        JudgementModule.SeekBackendAccess judgement, Profiles profiles,
        Func<JudgementRunCapture, NativeSeekTargetStatePlan, ISeekTargetNativeProfile,
            NativeJudgementBackend.SeekHoldEpoch> prepareEpoch)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(run); ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(judgement); ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(prepareEpoch);
        var value = new NativeSeekGameStatePreparation(api, state, run, coordinates, judgement, profiles, prepareEpoch);
        try { value.Capture(); return value; }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekGameStateCleanupOwner"] = value; error.Data["SeekGameStateCleanupFailure"] = cleanup; }
            throw;
        }
    }

    internal static NativeSeekGameStatePreparation Prepare(NativeRuntime api, GameEventState state,
        GameEventState.PlaybackRunAssociation run, GameplayClockNativeProfile clock,
        SeekTargetCoordinates coordinates, JudgementModule.SeekBackendAccess judgement)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(run); ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(coordinates); ArgumentNullException.ThrowIfNull(judgement);
        var value = new NativeSeekGameStatePreparation(api, state, run, clock, coordinates, judgement);
        try { value.Capture(); return value; }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekGameStateCleanupOwner"] = value; error.Data["SeekGameStateCleanupFailure"] = cleanup; }
            throw;
        }
    }

    private T Own<T>(T value) where T : IDisposable
    {
        _owners.Add(value); Current(); return value;
    }
    private T Value<T>(T value) where T : IGameplayValueParticipant
    { Own(value); _values.Add(value); return value; }

    private void Registered()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "The prepared game state belongs to its owner thread.");
        if (!ReferenceEquals(_registered, this))
            throw new InvalidOperationException("A copied game-state owner cannot use the original retained graph.");
    }
    private void Current()
    {
        Registered();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekGameStatePreparation));
        _judgement.VerifyCurrent(); _run.VerifyHostCurrent();
        if (!ReferenceEquals(_state.CaptureCurrentPlaybackRun(), _run))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The original host run changed during Seek preparation.");
    }
    private T Operation<T>(Func<T> action)
    {
        Registered();
        if (_busy) throw new InvalidOperationException("The prepared game state cannot synchronously reenter.");
        _busy = true; Exception? failure = null;
        try { Current(); var result = action(); Current(); return result; }
        catch (Exception error)
        {
            failure = error;
            try { RevokeAllocationAcknowledgement(); }
            catch (Exception revoke) { error.Data["SeekGameStateAcknowledgementFailure"] = revoke; }
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposed)
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) throw;
                    failure.Data["SeekGameStateCleanupOwner"] = this;
                    failure.Data["SeekGameStateCleanupFailure"] = cleanup;
                }
        }
    }

    private void Capture() => Operation(() =>
    {
        _capture = Own(_judgement.Backend.CaptureRun()
            ?? throw new GameplayStateException(ErrorCode.NotReady, "The actual judgement run is not available."));
        if (_capture.Run.RunId != _run.RunId || _capture.Run.SceneGeneration != _run.SceneGeneration
            || _capture.Run.Information != _run.Information || _capture.Run.Controller != _run.Controller)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The retained judgement run differs from the typed playback run.");
        Current();
        _information = Own(_api.RetainExactObject(new IntPtr(_run.Information), _profiles.Target.InformationClass));
        _speedField = _api.InspectExactDeclaredReadField(_profiles.Target.InformationClass, "speed", "System.Single", false, 0x98);
        _speed = _api.ReadExactField<float>(_information, _speedField);
        if (!float.IsFinite(_speed) || _speed <= 0)
            throw new GameplayStateException(ErrorCode.NotReady, "The actual chart movement speed is not finite and positive.");
        Current();
        _target = Own(_capture.CaptureSeekTargetStatePlan(_profiles.Target,
            new(Coordinates.ProgressSeconds), new(SeekCrossingHoldPolicy.ContinuationRequired)));
        if (_target.Coordinates != Coordinates)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The independent native conversion differs from the canonical Seek target.");
        if (_target.Coverage.MissingLocations.Count != 0)
            throw new GameplayStateException(ErrorCode.NotReady, "The complete target contains chart notes without actual retained runtime instances.");
        if (_target.RequiredWork.Any(row => row.Kind == SeekTargetRequiredWorkKind.RuntimeTimeMismatch))
            throw new GameplayStateException(ErrorCode.NotReady, "Runtime note timing differs from the native chart conversion before target queue projection.");
        _chart = Own(_capture.CaptureSeekPlan(new(Coordinates.ProgressSeconds), _profiles.Clock));
        _holdEpoch = Own(_prepareEpoch(_capture, _target, _profiles.Target)
            ?? throw new InvalidOperationException("The original backend epoch preparation is absent."));
        CaptureStages(); Current(); return 0;
    });

    public void Dispose()
    {
        Registered(); _disposed = true;
        if (!_busy) Release();
    }
    private void Release()
    {
        Registered();
        for (var i = _owners.Count - 1; i >= 0; --i)
        {
            var owner = _owners[i]; _owners.RemoveAt(i);
            try { owner.Dispose(); }
            catch (Exception error) { _cleanup.Add(error); _failedOwners.Add(owner); }
        }
        if (_cleanup.Count == 0) return;
        var failure = new AggregateException("Original game-state owners retain their reported native responsibilities.", _cleanup);
        failure.Data["SeekGameStateCleanupOwner"] = this; throw failure;
    }
}
