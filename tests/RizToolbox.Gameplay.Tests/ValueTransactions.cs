using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal sealed record ValueBackendState(double GameRate, double AudioRate, double SchedulerRate,
    double GamePosition, double ChartPosition, double AudioPosition, double AudioOrigin,
    int NoteCursor, int JudgeCursor, bool Holding, double Offset)
{
    internal static ValueBackendState Initial => new(1, 1, 1, 4, 8, 4.2, 195.925, 4, 4, true, .2);
    internal static double ToChart(double seconds) => seconds < 10 ? seconds * 2 : 20 + (seconds - 10) * 3;
    internal static double ToSeconds(double chart) => chart < 20 ? chart / 2 : 10 + (chart - 20) / 3;
}

/// <summary>A controlled multi-participant backend, with real writes and baseline readback rather than dispatch counters.
/// The game-native backend does not claim this controlled restoration contract.</summary>
internal sealed class ValuePreparation : GameplayPreparedControl
{
    private readonly Backend _backend;
    private readonly GameplayControlRequest _request;
    private readonly ValueBackendState _baseline, _expected;
    private ValueBackendState _lastWritten;
    private readonly long _information, _controller;
    private bool _entered, _disposed;
    private ValuePreparation(Backend backend, GameplayControlRequest request, ValueBackendState expected)
    {
        _backend = backend; _request = request; _baseline = _lastWritten = backend.Values; _expected = expected;
        _information = backend.Identity; _controller = backend.ControllerIdentity;
    }
    internal static ValuePreparation Prepare(Backend backend, GameplayControlRequest request)
    {
        backend.Preparations++;
        if (!backend.AudioReady || !backend.GameReady || backend.Playing || !backend.Paused)
            throw new GameplayStateException(ErrorCode.NotReady, "All controlled audio/game participants must be ready and paused.");
        var baseline = backend.Values;
        ValueBackendState expected;
        if (request.Action == GameplayControlAction.SetSpeed)
        {
            if (request.Value is not double rate || rate < .25 || rate > 4)
                throw new GameplayStateException(ErrorCode.InvalidArgument, "The controlled rate range is [0.25, 4].");
            expected = baseline with { GameRate = rate, AudioRate = rate, SchedulerRate = rate };
        }
        else
        {
            var seconds = request.Unit == GameplayControlValueUnit.GameSeconds ? request.Value!.Value : ValueBackendState.ToSeconds(request.Value!.Value);
            if (!double.IsFinite(seconds) || seconds < 0 || seconds > 120)
                throw new GameplayStateException(ErrorCode.InvalidArgument, "The controlled position must map into [0, 120] game seconds.");
            var audio = seconds + baseline.Offset;
            expected = baseline with { GamePosition = seconds, ChartPosition = ValueBackendState.ToChart(seconds),
                AudioPosition = audio, AudioOrigin = 200.125 - audio, NoteCursor = (int)Math.Ceiling(seconds), JudgeCursor = (int)Math.Floor(seconds), Holding = false };
        }
        var prepared = new ValuePreparation(backend, request, expected);
        try { backend.OnPrepare?.Invoke(); return prepared; }
        catch { prepared.Dispose(); throw; }
    }
    internal override bool CanRestore => true;
    internal override void Commit()
    {
        if (_entered) throw new InvalidOperationException("A prepared command may commit only once.");
        _entered = true;
        if (_backend.Identity != _information || _backend.ControllerIdentity != _controller || _backend.Values != _baseline
            || !_backend.AudioReady || !_backend.GameReady || !_backend.Paused || _backend.Playing)
            throw new GameplayStateException(ErrorCode.Conflict, "A prepared participant changed before the first write.");
        _backend.ValueCommits++;
        if (_request.Action == GameplayControlAction.SetSpeed)
        {
            Write(_backend.Values with { AudioRate = _expected.AudioRate }, 1);
            Write(_backend.Values with { GameRate = _expected.GameRate }, 2);
            Write(_backend.Values with { SchedulerRate = _expected.SchedulerRate }, 3);
        }
        else
        {
            Write(_backend.Values with { AudioPosition = _expected.AudioPosition, AudioOrigin = _expected.AudioOrigin }, 1);
            Write(_backend.Values with { GamePosition = _expected.GamePosition, ChartPosition = _expected.ChartPosition }, 2);
            Write(_backend.Values with { NoteCursor = _expected.NoteCursor, JudgeCursor = _expected.JudgeCursor, Holding = _expected.Holding }, 3);
        }
        _backend.OnValueCommit?.Invoke();
    }
    private void Write(ValueBackendState value, int stage)
    {
        _backend.Values = _lastWritten = value;
        if (_backend.CommitFaultStage == stage) throw new IOException("controlled participant failure after stage " + stage);
    }
    internal override GameplayControlConfirmation Confirm()
    {
        _backend.ValueConfirmations++; _backend.OnValueConfirm?.Invoke();
        var observed = _backend.Values;
        var value = _request.Action == GameplayControlAction.SetSpeed ? observed.GameRate
            : _request.Unit == GameplayControlValueUnit.GameSeconds ? observed.GamePosition : observed.ChartPosition;
        return new(!_backend.ConfirmationFails && observed == _expected && _backend.Paused && !_backend.Playing,
            "Actual controlled game/audio/scheduler/cursor readback.", _backend.NonfiniteConfirmation ? double.NaN : value,
            _backend.WrongConfirmationUnit ? GameplayControlValueUnit.None : _request.Unit);
    }
    internal override void Restore()
    {
        _backend.Restorations++;
        if (_backend.RestoreThrows) throw new IOException("controlled restoration failure");
        if (_backend.RestoreFails || _backend.Identity != _information || _backend.ControllerIdentity != _controller || _backend.Values != _lastWritten)
            return;
        _backend.Values = _baseline;
        _backend.OnRestore?.Invoke();
    }
    internal override GameplayControlRestoration ConfirmRestored()
    {
        _backend.RestorationReadbacks++;
        return new(_backend.Identity == _information && _backend.ControllerIdentity == _controller && _backend.Values == _baseline
            && _backend.Paused && !_backend.Playing,
            "Separate actual readback of all original game/audio/rate/origin/cursor/hold participants and their captured identity.");
    }
    public override void Dispose() { if (_disposed) return; _disposed = true; _backend.PreparationDisposals++; }
}

internal static class ValueControlChecks
{
    internal static readonly (string Name, Action Run)[] All =
    {
        ("valued speed commits actual audio game and scheduler rates with request and shared run audit", Speed),
        ("seek in game seconds updates actual audio origin chart and judge state before confirmation", SeekSeconds),
        ("seek in chart coordinates uses the actual piecewise conversion without reapplying chart delay", SeekChart),
        ("pause value changes and resume preserve the requested values and Dispose releases only access", PauseValueResume),
        ("malformed nonfinite unitless and valueless value controls never reach preparation", InvalidRequests),
        ("explicit request target mismatches do not prepare or mutate the current run", TargetRequests),
        ("value control without an observed run is refused without fabricating an identity", UnknownRun),
        ("same object with a different observed run invalidates the old lease and request", ReplacedRun),
        ("exclusive ownership covers value preparation and a second plugin cannot commit", ValueExclusive),
        ("all audio game and range preparation failures preserve the original run without a modification", PreparationFailures),
        ("every partial speed and seek participant failure restores the full captured baseline", PartialFailures),
        ("false wrong-unit and nonfinite confirmations fail and restore instead of returning success", ConfirmationFailures),
        ("failed restoration withdraws just that action until a new controller generation", UnknownEffects),
        ("restoration exceptions preserve the original commit failure and unknown-effect audit", RestorationException),
        ("restoration setters cannot sign Restored without separate unchanged baseline readback", RestorationReadback),
        ("cancellation or scope stop during preparation has no commit or audit and releases preparation", PreparationCancellation),
        ("value preparation blocks command clock conversion and state reentry without replacing roots", PreparationReentry),
        ("run controller and state changes during preparation cannot commit", ChangedPreparation),
        ("stop during value commit restores the baseline before deferred backend cleanup", StopValueCommit),
        ("identity replacement during commit or confirmation cannot confirm or overwrite the replacement", ChangedCommit),
        ("external participant writes during confirmation are preserved when restoration is unknown", ExternalWrite),
        ("formal PluginHost current SDK value requests preserve owned leases and committed state", HostValues),
        ("clock snapshots distinguish chart coordinates progress seconds offset delay and sample interval", MappedClocks),
        ("run-bound clock conversion follows both BPM segments and keeps host DSP calibration unknown", Conversions),
        ("conversion validates finite units target identity and backend result without controller mutation", ConversionGuards),
        ("mapping failure preserves independent game DSP and monotonic readings", MappingFailure),
        ("clock capture intervals include conversion time without claiming calibrated rates", CaptureInterval),
        ("run changes during clock sampling and conversion never publish a mixed old-run mapping", ClockRunChanges),
        ("nonfinite game DSP and converted clock readings become independently unavailable", NonfiniteReadings),
        ("clock mapping and conversion block reentry and defer module cleanup until capture returns", ClockCaptureOwnership)
    };
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static T Need<T>(Result<T> result) { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing value."); return result.Value!; }
    private static void Error<T>(Result<T> result, ErrorCode code) => Check(!result.Success && result.Error?.KnownCode == code, "Expected " + code + "; got " + result.Error);
    private static IGameplayControlLease Paused(Fixture f)
    {
        f.Backend.Playing = false; f.Backend.Paused = true;
        if (f.Lifecycle is { } lifecycle) f.Lifecycle = lifecycle with { Phase = RunLifecyclePhase.Paused, Paused = true };
        return Need(f.Controls.Acquire(true));
    }
    private static GameplayControlRequest SpeedRequest(IGameplayControlLease lease, double value = 1.5)
        => new(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId, value, GameplayControlValueUnit.PlaybackRateMultiplier);
    private static GameplayControlRequest SeekRequest(IGameplayControlLease lease, double value = 15, GameplayControlValueUnit unit = GameplayControlValueUnit.GameSeconds)
        => new(GameplayControlAction.Seek, lease.ControllerGeneration, lease.ObservedRunId, value, unit);
    private static GameplayModification Last(Fixture f) => Need(f.SampledControlState()).Modifications.Last();

    private static void Speed()
    {
        var journal = new List<RunModificationInput>(); using var f = new Fixture(record: journal.Add, values: true); var lease = Paused(f);
        var request = SpeedRequest(lease, 1.75); var receipt = Need(lease.Execute(request));
        Check(f.Backend.Values.GameRate == 1.75 && f.Backend.Values.AudioRate == 1.75 && f.Backend.Values.SchedulerRate == 1.75
            && f.Backend.ValueCommits == 1 && f.Backend.ValueConfirmations == 1 && f.Backend.PreparationDisposals == 1, "The actual participants did not take the requested speed.");
        Check(receipt.Modification.Request == request && receipt.Modification.ConfirmedValue == 1.75
            && receipt.Modification.ConfirmedUnit == GameplayControlValueUnit.PlaybackRateMultiplier
            && receipt.Modification.TransactionState == GameplayControlTransactionState.Confirmed && receipt.ImmediatePostconditionObserved == true,
            "A dispatched enum was substituted for confirmed value evidence.");
        Check(journal.Single().ObservedRunId == "controlled-run" && journal[0].Outcome == RunModificationOutcome.Applied
            && journal[0].Detail.Contains("1.75") && journal[0].Detail.Contains("PlaybackRateMultiplier")
            && !journal[0].LocalResultBehaviorVerified && !journal[0].SubmissionBehaviorVerified, "The shared audit dropped value/unit/run or fabricated results verification.");
    }
    private static void SeekSeconds()
    {
        using var f = new Fixture(values: true); var lease = Paused(f); var receipt = Need(lease.Execute(SeekRequest(lease)));
        var actual = f.Backend.Values;
        Check(actual.GamePosition == 15 && actual.ChartPosition == 35 && actual.AudioPosition == 15.2
            && Math.Abs(actual.AudioOrigin - 184.925) < 1e-10 && actual.NoteCursor == 15 && actual.JudgeCursor == 15 && !actual.Holding,
            "Seek left audio, chart, scheduling or consumed-note state at the old location.");
        Check(receipt.Modification.ConfirmedValue == 15 && receipt.Modification.ConfirmedUnit == GameplayControlValueUnit.GameSeconds, "Seek receipt lost its actual seconds unit.");
    }
    private static void SeekChart()
    {
        using var f = new Fixture(values: true); var lease = Paused(f); var request = SeekRequest(lease, 35, GameplayControlValueUnit.ChartCoordinates);
        var result = Need(lease.Execute(request));
        Check(f.Backend.Values.GamePosition == 15 && f.Backend.Values.AudioPosition == 15.2
            && result.Modification.Request == request && result.Modification.ConfirmedValue == 35
            && result.Modification.ConfirmedUnit == GameplayControlValueUnit.ChartCoordinates, "Chart seek used a constant BPM, wrong unit or duplicate chart delay.");
    }
    private static void PauseValueResume()
    {
        using var f = new Fixture(values: true); var lease = Need(f.Controls.Acquire(true));
        Error(lease.Execute(SpeedRequest(lease)), ErrorCode.InvalidGameState);
        Need(lease.Execute(GameplayControlAction.Pause)); Need(lease.Execute(SpeedRequest(lease))); Need(lease.Execute(SeekRequest(lease)));
        Need(lease.Execute(GameplayControlAction.Resume)); var committed = f.Backend.Values; lease.Dispose();
        Check(f.Backend.Playing && !f.Backend.Paused && committed.GameRate == 1.5 && committed.GamePosition == 15
            && f.Backend.Values == committed && f.Backend.Restorations == 0, "Resume or lease Dispose silently undid committed value commands.");
    }
    private static void InvalidRequests()
    {
        using var f = new Fixture(values: true); var lease = Paused(f); var captures = f.Backend.Captures;
        var requests = new[] { SpeedRequest(lease, double.NaN), SpeedRequest(lease, double.PositiveInfinity), SpeedRequest(lease, 0), SpeedRequest(lease, -1),
            SpeedRequest(lease) with { Unit = GameplayControlValueUnit.GameSeconds }, SpeedRequest(lease) with { Value = null },
            SeekRequest(lease, double.NegativeInfinity), SeekRequest(lease) with { Unit = GameplayControlValueUnit.None },
            SeekRequest(lease) with { Unit = (GameplayControlValueUnit)999 }, SpeedRequest(lease) with { Action = (GameplayControlAction)999 },
            SpeedRequest(lease) with { ControllerGeneration = 0 }, new(GameplayControlAction.Pause, lease.ControllerGeneration, lease.ObservedRunId, 1, GameplayControlValueUnit.GameSeconds) };
        foreach (var request in requests) Error(lease.Execute(request), ErrorCode.InvalidArgument);
        Error(lease.Execute(GameplayControlAction.SetSpeed), ErrorCode.InvalidArgument); Error(lease.Execute(GameplayControlAction.Seek), ErrorCode.InvalidArgument);
        Error(lease.Execute((GameplayControlRequest)null!), ErrorCode.InvalidArgument);
        Check(f.Backend.Captures == captures && f.Backend.Preparations == 0 && f.Backend.Values == ValueBackendState.Initial, "Malformed values touched native preparation or mutated the baseline.");
    }
    private static void TargetRequests()
    {
        using var f = new Fixture(values: true); var lease = Paused(f);
        Error(lease.Execute(SpeedRequest(lease) with { ControllerGeneration = lease.ControllerGeneration + 1 }), ErrorCode.TargetExpired);
        Error(lease.Execute(SeekRequest(lease) with { ObservedRunId = "another-run" }), ErrorCode.TargetExpired);
        Error(lease.Execute(SpeedRequest(lease) with { ObservedRunId = null }), ErrorCode.TargetExpired);
        Check(f.Backend.Preparations == 0 && Need(f.SampledControlState()).Modifications.Count == 0, "A mismatched request prepared the current run.");
        Need(lease.Execute(SpeedRequest(lease)));
    }
    private static void UnknownRun()
    {
        using var f = new Fixture(values: true); f.Lifecycle = null; var lease = Paused(f);
        Error(lease.Execute(SeekRequest(lease)), ErrorCode.InvalidGameState);
        Check(lease.ObservedRunId is null && f.Backend.Preparations == 0, "Value dispatch invented a run identity.");
    }
    private static void ReplacedRun()
    {
        using var f = new Fixture(values: true); var old = Paused(f); f.Lifecycle = f.Lifecycle! with { RunId = "next-run", Revision = 2 };
        Error(old.Execute(SpeedRequest(old)), ErrorCode.TargetExpired); var next = Need(f.Controls.Acquire(true));
        Check(next.ControllerGeneration > old.ControllerGeneration && next.ObservedRunId == "next-run", "A reused controller bridged two observed runs.");
        Need(next.Execute(SpeedRequest(next))); Check(f.Backend.ValueCommits == 1, "An expired run request committed.");
    }
    private static void ValueExclusive()
    {
        using var f = new Fixture(values: true); var first = Paused(f); using var other = new Context("second", f.Main);
        var controls = f.Service.ControlsForPlugin(other); Error(controls.Acquire(true), ErrorCode.Conflict);
        Need(first.Execute(SpeedRequest(first))); first.Dispose(); var second = Need(controls.Acquire(true)); Need(second.Execute(SeekRequest(second)));
        Check(f.Backend.ValueCommits == 2 && Last(f).Owner == "second", "Value commands bypassed exclusive control ownership.");
    }
    private static void PreparationFailures()
    {
        for (var mode = 0; mode < 3; mode++)
        {
            using var f = new Fixture(values: true); var lease = Paused(f);
            if (mode == 0) f.Backend.AudioReady = false; if (mode == 1) f.Backend.GameReady = false;
            Error(lease.Execute(mode == 2 ? SpeedRequest(lease, 5) : SeekRequest(lease)), mode == 2 ? ErrorCode.InvalidArgument : ErrorCode.NotReady);
            Check(f.Backend.Values == ValueBackendState.Initial && f.Backend.ValueCommits == 0 && Need(f.SampledControlState()).Modifications.Count == 0,
                "Preparation changed the game or recorded a mutation before all participants were ready.");
        }
    }
    private static void PartialFailures()
    {
        foreach (var action in new[] { GameplayControlAction.SetSpeed, GameplayControlAction.Seek })
        for (var stage = 1; stage <= 3; stage++)
        {
            var journal = new List<RunModificationInput>(); using var f = new Fixture(record: journal.Add, values: true); var lease = Paused(f); f.Backend.CommitFaultStage = stage;
            var request = action == GameplayControlAction.SetSpeed ? SpeedRequest(lease) : SeekRequest(lease);
            Error(lease.Execute(request), ErrorCode.InternalError);
            Check(f.Backend.Values == ValueBackendState.Initial && f.Backend.Restorations == 1 && f.Backend.RestorationReadbacks == 1 && f.Backend.ValueConfirmations == 0
                && f.Backend.PreparationDisposals == 1 && Last(f).TransactionState == GameplayControlTransactionState.Restored
                && Last(f).Request == request && journal.Single().Outcome == RunModificationOutcome.Restored,
                "Partial participant failure left a half-reset run or lost restored audit.");
            Check(Need(f.SampledControlState()).Operations.Single(x => x.Action == action).Supported, "A fully restored transaction unnecessarily withdrew the operation.");
            Error(lease.Execute(request), ErrorCode.TargetExpired);
        }
    }
    private static void ConfirmationFailures()
    {
        for (var mode = 0; mode < 3; mode++)
        {
            using var f = new Fixture(values: true); var lease = Paused(f);
            f.Backend.ConfirmationFails = mode == 0; f.Backend.WrongConfirmationUnit = mode == 1; f.Backend.NonfiniteConfirmation = mode == 2;
            Error(lease.Execute(SeekRequest(lease)), ErrorCode.InvalidGameState);
            Check(f.Backend.Values == ValueBackendState.Initial && f.Backend.ValueConfirmations == 1 && f.Backend.Restorations == 1
                && Last(f).Dispatched && Last(f).TransactionState == GameplayControlTransactionState.Restored, "Unconfirmed value was declared success or not restored.");
        }
    }
    private static void UnknownEffects()
    {
        var journal = new List<RunModificationInput>(); using var f = new Fixture(record: journal.Add, values: true); var lease = Paused(f);
        f.Backend.CommitFaultStage = 1; f.Backend.RestoreFails = true;
        Error(lease.Execute(SeekRequest(lease)), ErrorCode.InternalError); var state = Need(f.SampledControlState());
        Check(f.Backend.Values != ValueBackendState.Initial && Last(f).TransactionState == GameplayControlTransactionState.SideEffectsUnknown
            && journal.Single().Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown
            && !state.Operations.Single(x => x.Action == GameplayControlAction.Seek).Supported
            && state.Operations.Single(x => x.Action == GameplayControlAction.SetSpeed).Supported, "Unknown effects were erased or unrelated capabilities were withdrawn.");
        var next = Need(f.Controls.Acquire(true)); Error(next.Execute(SeekRequest(next)), ErrorCode.OperationUnsupported); next.Dispose();
        f.Backend.Identity++; f.Backend.CommitFaultStage = 0; f.Backend.RestoreFails = false; f.Backend.Values = ValueBackendState.Initial;
        var rebuilt = Need(f.Controls.Acquire(true)); Need(rebuilt.Execute(SeekRequest(rebuilt)));
    }
    private static void RestorationException()
    {
        var records = new List<RunModificationInput>(); using var f = new Fixture(values: true, record: x => { records.Add(x); throw new IOException("audit fault"); });
        var lease = Paused(f); f.Backend.CommitFaultStage = 2; f.Backend.RestoreThrows = true;
        var result = lease.Execute(SpeedRequest(lease)); Error(result, ErrorCode.InternalError);
        Check(result.Error!.Message.Contains("stage 2") && result.Error.Message.Contains("Restoration threw") && records.Single().Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown
            && result.Error.CapabilityId == GameplayCapabilityIds.Controls && !result.Error.Retryable, "A recovery/audit exception replaced the actual failure or its structured context.");
    }
    private static void RestorationReadback()
    {
        using var f = new Fixture(values: true); var lease = Paused(f); f.Backend.CommitFaultStage = 2;
        f.Backend.OnRestore = () => f.Backend.Values = f.Backend.Values with { GameRate = 3.5 };
        Error(lease.Execute(SpeedRequest(lease)), ErrorCode.InternalError);
        Check(f.Backend.RestorationReadbacks == 1 && f.Backend.Values.GameRate == 3.5
            && Last(f).TransactionState == GameplayControlTransactionState.SideEffectsUnknown
            && !Need(f.SampledControlState()).Operations.Single(x => x.Action == GameplayControlAction.SetSpeed).Supported,
            "A completed restoration setter was accepted without reading its actual baseline back.");
    }
    private static void PreparationCancellation()
    {
        for (var mode = 0; mode < 2; mode++)
        {
            using var f = new Fixture(values: true); using var cancel = new CancellationTokenSource(); var lease = Paused(f);
            f.Backend.OnPrepare = mode == 0 ? cancel.Cancel : f.Context.Dispose;
            Error(lease.Execute(SpeedRequest(lease), cancel.Token), mode == 0 ? ErrorCode.Cancelled : ErrorCode.ScopeExpired);
            Check(f.Backend.ValueCommits == 0 && f.Backend.Restorations == 0 && f.Backend.Values == ValueBackendState.Initial
                && f.Backend.PreparationDisposals == 1, "Cancellation after preparation entered commit or restored an unchanged run.");
        }
    }
    private static void PreparationReentry()
    {
        using var f = new Fixture(values: true); var lease = Paused(f);
        f.Backend.OnPrepare = () =>
        {
            var captures = f.Backend.Captures; var releases = f.Backend.CaptureDisposals;
            Error(lease.Execute(GameplayControlAction.Resume), ErrorCode.Conflict); Error(f.Controls.GetState(), ErrorCode.Conflict);
            Error(f.Clocks.Convert(new(lease.ControllerGeneration, lease.ObservedRunId!, 4, GameplayClockUnit.Seconds, GameplayClockUnit.ChartCoordinates)), ErrorCode.Conflict);
            Check(captures == f.Backend.Captures && releases == f.Backend.CaptureDisposals, "Preparation reentry replaced captured roots.");
        };
        Need(lease.Execute(SpeedRequest(lease))); Check(f.Backend.ValueCommits == 1, "Reentry added a second commit.");
    }
    private static void ChangedPreparation()
    {
        for (var mode = 0; mode < 3; mode++)
        {
            using var f = new Fixture(values: true); var lease = Paused(f);
            f.Backend.OnPrepare = () => { if (mode == 0) f.Backend.ControllerIdentity++; if (mode == 1) f.Lifecycle = f.Lifecycle! with { RunId = "new-run" }; if (mode == 2) f.Backend.Over = true; };
            Error(lease.Execute(SeekRequest(lease)), mode == 2 ? ErrorCode.InvalidGameState : ErrorCode.TargetExpired);
            Check(f.Backend.ValueCommits == 0 && f.Backend.Restorations == 0 && f.Backend.Values == ValueBackendState.Initial && f.Backend.PreparationDisposals == 1,
                "Preparation crossed a target or readiness change and committed.");
        }
    }
    private static void StopValueCommit()
    {
        for (var mode = 0; mode < 2; mode++)
        {
            using var f = new Fixture(values: true); var lease = Paused(f);
            f.Backend.OnValueCommit = () =>
            {
                var released = f.Backend.CaptureDisposals;
                if (mode == 0) f.Service.Dispose(); else f.Context.Dispose();
                Check(f.Backend.CaptureDisposals == released && f.Backend.Disposals == 0, "Value stop released preparation roots before recovery.");
            };
            Error(lease.Execute(SeekRequest(lease)), ErrorCode.TargetExpired);
            Check(f.Backend.Values == ValueBackendState.Initial && f.Backend.Restorations == 1 && f.Backend.ValueConfirmations == 0
                && f.Backend.PreparationDisposals == 1, "Stop during commit orphaned the changed game/audio state.");
            if (mode == 0) Check(f.Backend.Disposals == 1 && f.Backend.CaptureDisposals == f.Backend.Captures, "Deferred module cleanup was incomplete.");
        }
    }
    private static void ChangedCommit()
    {
        for (var mode = 0; mode < 2; mode++)
        {
            using var f = new Fixture(values: true); var lease = Paused(f);
            if (mode == 0) f.Backend.OnValueCommit = () => f.Backend.ControllerIdentity++;
            else f.Backend.OnValueConfirm = () => f.Backend.Identity++;
            Error(lease.Execute(SpeedRequest(lease)), ErrorCode.TargetExpired);
            Check(f.Backend.Restorations == 1 && f.Backend.Values.GameRate == 1.5 && f.Backend.Values.AudioRate == 1.5,
                "A replacement identity was confirmed or overwritten with the previous object's baseline.");
        }
    }
    private static void ExternalWrite()
    {
        using var f = new Fixture(values: true); var lease = Paused(f);
        f.Backend.OnValueConfirm = () => f.Backend.Values = f.Backend.Values with { AudioRate = 2.75 };
        Error(lease.Execute(SpeedRequest(lease)), ErrorCode.InvalidGameState);
        Check(f.Backend.Values.AudioRate == 2.75 && Last(f).TransactionState == GameplayControlTransactionState.SideEffectsUnknown,
            "Recovery overwrote a foreign participant update or claimed restoration.");
    }
    private static void HostValues()
    {
        using var f = new Fixture(values: true); f.Backend.Playing = false; f.Backend.Paused = true;
        var capabilities = new CapabilityRegistry(new("controlled", "controlled", "controlled", "controlled")); var events = new EventHub(new Logger());
        using var host = new PluginHost(capabilities, f.Main, events, new Logger());
        capabilities.RegisterScoped<IGameplayControlService>(GameplayCapabilityIds.Controls, c => f.Service.ControlsForPlugin(c));
        IGameplayControlLease? lease = null;
        host.Register(new Plugin(c => { lease = Need(c.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!.Acquire(true)); Need(lease.Execute(SpeedRequest(lease))); }));
        Check(host.Enable("probe").Success, "Formal host could not invoke the current value contract."); host.Disable("probe");
        Error(lease!.Execute(SeekRequest(lease)), ErrorCode.ScopeExpired);
        Check(f.Backend.Values.GameRate == 1.5 && f.Backend.Restorations == 0, "Host scope cleanup pretended to reverse a confirmed command.");
    }
    private static void MappedClocks()
    {
        using var f = new Fixture(values: true); var snapshot = Need(f.Clocks.Read()); var mapping = snapshot.Mapping!;
        Check(snapshot.GameTime.Unit == GameplayClockUnit.ChartCoordinates && snapshot.GameRealTime.Unit == GameplayClockUnit.Seconds
            && snapshot.GameSyncedTime.Unit == GameplayClockUnit.Seconds && mapping.GameTimeInSeconds.Value == 4
            && mapping.ProgressTimeInSeconds.Value == 4 && mapping.AppliedOffsetSeconds.Value == .2 && mapping.ChartDelaySeconds.Value == .35,
            "Chart coordinates seconds effective offset and chart delay were conflated.");
        Check(mapping.CaptureStartedMonotonicSeconds == snapshot.CaptureStartedMonotonicSeconds && mapping.CaptureFinishedMonotonicSeconds == snapshot.CaptureFinishedMonotonicSeconds
            && mapping.AudioDspSampleSeconds == snapshot.AudioDspTime.Value && mapping.HostMonotonicSampleSeconds == snapshot.HostMonotonicTime.Value
            && mapping.GameSecondsPerDspSecond is null && mapping.GameSecondsPerHostSecond is null && mapping.LatencySeconds is null
            && !mapping.RuntimeVerified && !snapshot.SecondsMappingVerified, "Sample anchors were promoted to a verified/extrapolatable clock mapping.");
    }
    private static void Conversions()
    {
        using var f = new Fixture(values: true); var snapshot = Need(f.Clocks.Read());
        GameplayTimeConversion Convert(double value, GameplayClockUnit from, GameplayClockUnit to)
            => Need(f.Clocks.Convert(new(snapshot.ControllerGeneration, snapshot.ObservedRunId!, value, from, to)));
        Check(Convert(4, GameplayClockUnit.Seconds, GameplayClockUnit.ChartCoordinates).Converted.Value == 8
            && Convert(15, GameplayClockUnit.Seconds, GameplayClockUnit.ChartCoordinates).Converted.Value == 35
            && Convert(35, GameplayClockUnit.ChartCoordinates, GameplayClockUnit.Seconds).Converted.Value == 15
            && Convert(4, GameplayClockUnit.Seconds, GameplayClockUnit.Seconds).Converted.Value == 4, "Both actual BPM segments or inverse conversion failed.");
        Check(f.Backend.Values == ValueBackendState.Initial && f.Backend.ValueCommits == 0, "A time conversion changed gameplay state.");
    }
    private static void ConversionGuards()
    {
        using var f = new Fixture(values: true); var snapshot = Need(f.Clocks.Read());
        var request = new GameplayTimeConversionRequest(snapshot.ControllerGeneration, snapshot.ObservedRunId!, 10, GameplayClockUnit.Seconds, GameplayClockUnit.ChartCoordinates);
        var captures = f.Backend.Captures;
        foreach (var malformed in new[] { request with { Value = double.NaN }, request with { SourceUnit = GameplayClockUnit.RawGameUnits }, request with { TargetUnit = GameplayClockUnit.Unknown }, request with { ObservedRunId = "" } })
            Error(f.Clocks.Convert(malformed), ErrorCode.InvalidArgument);
        Check(f.Backend.Captures == captures && f.Backend.Conversions == 0, "Invalid conversion touched the backend.");
        Error(f.Clocks.Convert(request with { ControllerGeneration = request.ControllerGeneration + 1 }), ErrorCode.TargetExpired);
        Error(f.Clocks.Convert(request with { ObservedRunId = "stale-run" }), ErrorCode.TargetExpired);
        f.Backend.WrongConversionUnit = true; Error(f.Clocks.Convert(request), ErrorCode.ObservationUnavailable);
        f.Backend.WrongConversionUnit = false; Error(f.Clocks.Convert(request with { Value = double.MaxValue }), ErrorCode.ObservationUnavailable);
        Check(f.Backend.Values == ValueBackendState.Initial && f.Backend.ValueCommits == 0, "A refused conversion mutated the controller.");
    }
    private static void MappingFailure()
    {
        using var f = new Fixture(values: true); f.Backend.MappingFails = true; var snapshot = Need(f.Clocks.Read());
        Check(snapshot.GameTime.Available && snapshot.GameRealTime.Available && snapshot.AudioDspTime.Available && snapshot.HostMonotonicTime.Available
            && snapshot.Mapping?.GameTimeInSeconds.Available == false && !snapshot.SecondsMappingVerified, "A failed mapping disabled independent clocks or claimed calibration.");
    }
    private static void CaptureInterval()
    {
        double host = 10;
        using var f = new Fixture(values: true, monotonicSeconds: () => host); f.Backend.OnReadMapping = () => host += .025;
        var snapshot = Need(f.Clocks.Read());
        Check(snapshot.CaptureFinishedMonotonicSeconds - snapshot.CaptureStartedMonotonicSeconds >= .02
            && snapshot.Mapping!.CaptureStartedMonotonicSeconds == snapshot.CaptureStartedMonotonicSeconds
            && snapshot.Mapping.CaptureFinishedMonotonicSeconds == snapshot.CaptureFinishedMonotonicSeconds
            && snapshot.Mapping.GameSecondsPerHostSecond is null, "A conversion delay escaped the capture envelope or invented a clock rate.");
    }
    private static void ClockRunChanges()
    {
        using var f = new Fixture(values: true); var lease = Paused(f);
        f.Backend.OnReadMapping = () => f.Lifecycle = f.Lifecycle! with { RunId = "second-run", Revision = 2 };
        var snapshot = Need(f.Clocks.Read());
        Check(snapshot.ObservedRunId is null && !snapshot.GameTime.Available && snapshot.Mapping is null
            && snapshot.AudioDspTime.Available && snapshot.HostMonotonicTime.Available, "A mixed clock sample retained old run identity or lost independent clocks.");
        Error(lease.Execute(SpeedRequest(lease)), ErrorCode.TargetExpired); f.Backend.OnReadMapping = null;
        var current = Need(f.Clocks.Read());
        f.Backend.OnConvert = () => f.Lifecycle = f.Lifecycle! with { RunId = "third-run", Revision = 3 };
        Error(f.Clocks.Convert(new(current.ControllerGeneration, current.ObservedRunId!, 35, GameplayClockUnit.ChartCoordinates, GameplayClockUnit.Seconds)), ErrorCode.TargetExpired);
        Check(f.Backend.Values == ValueBackendState.Initial, "A run transition during conversion mutated gameplay.");
    }
    private static void NonfiniteReadings()
    {
        using var f = new Fixture(values: true); f.Backend.NonfiniteClock = "nowRealTime"; f.Backend.NonfiniteDsp = true; f.Backend.NonfiniteMapping = true;
        var sample = Need(f.Clocks.Read());
        Check(!sample.GameRealTime.Available && sample.GameRealTime.Value is null && sample.GameRealTime.Error?.KnownCode == ErrorCode.ObservationUnavailable
            && !sample.AudioDspTime.Available && sample.AudioDspTime.Value is null && sample.GameSyncedTime.Available && sample.GameTime.Available
            && sample.Mapping?.GameTimeInSeconds.Available == false && sample.Mapping.GameTimeInSeconds.Value is null
            && sample.Mapping.ProgressTimeInSeconds.Available && sample.Mapping.AudioDspSampleSeconds is null && sample.HostMonotonicTime.Available,
            "A nonfinite sample was exposed as a clock value or disabled independent sources.");
    }
    private static void ClockCaptureOwnership()
    {
        for (var mode = 0; mode < 2; mode++)
        {
            using var f = new Fixture(values: true); var current = Need(f.Clocks.Read());
            Action duringCapture = () =>
            {
                var captures = f.Backend.Captures; var released = f.Backend.CaptureDisposals;
                Error(f.Clocks.Read(), ErrorCode.Conflict); Error(f.Controls.Acquire(true), ErrorCode.Conflict);
                Check(f.Backend.Captures == captures && f.Backend.CaptureDisposals == released, "A clock callback replaced the active capture roots.");
                f.Service.Dispose();
                Check(f.Backend.Disposals == 0 && f.Backend.CaptureDisposals == released, "Module stop freed native capture roots during clock conversion.");
            };
            if (mode == 0)
            {
                f.Backend.OnReadMapping = duringCapture; Error(f.Clocks.Read(), ErrorCode.ScopeExpired);
            }
            else
            {
                f.Backend.OnConvert = duringCapture;
                Error(f.Clocks.Convert(new(current.ControllerGeneration, current.ObservedRunId!, 35, GameplayClockUnit.ChartCoordinates, GameplayClockUnit.Seconds)), ErrorCode.ScopeExpired);
            }
            Check(f.Backend.Disposals == 1 && f.Backend.CaptureDisposals == f.Backend.Captures, "Clock capture finally did not release deferred roots/backend once.");
        }
    }
}
