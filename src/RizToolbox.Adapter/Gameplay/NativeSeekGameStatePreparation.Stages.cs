using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekGameStatePreparation
{
    private NativeSeekCanvasBasisCommands _canvas = null!;
    private NativeSeekLineEndpointCommands _lines = null!;
    private NativeSeekRendererCommands _renderer = null!;
    private NativeSeekNoteFlagsParticipant _flags = null!;
    private NativeSeekHoldStateParticipant _holdReset = null!;
    private NativeSeekHoldGeometryCommands? _holdGeometry;
    private NativeSeekHoldStateParticipant? _holdFinal;
    private NativeSeekNoteVisibilityCommands? _visibility;
    private SeekHoldGeometrySeed _holdSeed = null!;
    private bool _rendererEntered, _flagsEntered, _holdResetEntered, _canvasEntered, _linesEntered, _dynamicEntered;
    private bool _holdFinalEntered;
    private readonly List<Exception> _restoreErrors = new();
    private void CaptureStages()
    {
        var continuation = _holdEpoch.RequiresContinuation.ToHashSet();
        if (!continuation.SetEquals(_target.CrossingHolds
            .Where(row => row.Disposition == SeekCrossingHoldDisposition.ContinuationRequired).Select(row => row.Reference)))
            throw new InvalidOperationException("The actual Hold epoch does not cover every required crossing target.");
        Value(_capture.PrepareSeekNoteTime(_profiles.Clock, _target.RuntimeTimeTargets));
        if (_target.JudgeInitializationRequired)
            Value(_capture.PrepareSeekJudgeInitialization(_profiles.Target, _target.InitializationTargets));
        Value(_capture.PrepareJudgementCounterReset(_profiles.Clock));
        Value(_capture.PrepareSeekFingerIds(_profiles.Clock, _holdEpoch.FingerTargets));
        var queues = _target.QueueTargets;
        var queueTargets = new SeekJudgeQueueTargets(
            queues.Notes.Where(reference => !continuation.Contains(reference)).ToArray(),
            queues.TapWaiting.Where(reference => !continuation.Contains(reference)).ToArray(),
            queues.DragWaiting.Where(reference => !continuation.Contains(reference)).ToArray());
        var queue = Value(_capture.PrepareSeekJudgeQueues(_profiles.Clock, queueTargets));
        queue.BindHoldEpoch(_holdEpoch);
        _flags = Own(_capture.PrepareSeekNoteFlags(_profiles.Clock,
            _target.FlagTargets.Select(row => continuation.Contains(row.Reference) ? row with { Destroyed = false } : row).ToArray()));
        _renderer = Own(_capture.PrepareSeekRendererCommands(_profiles.Renderer, _target.RendererCommands));
        _renderer.RequireRestorable(); _renderer.RegisterNoteFlags(_flags);
        _canvas = Own(_capture.PrepareSeekCanvasBasis(_profiles.Canvas, _profiles.Position,
            Coordinates.ChartCoordinate, _speed));
        _lines = Own(NativeSeekLineEndpointCommands.Prepare(
            () => _canvas.CaptureLineRingSource(_profiles.Lines), _profiles.Position));
        CaptureHoldReset();
    }

    private void CaptureHoldReset()
    {
        var held = Own(_capture.PrepareSeekHoldGeometry(_profiles.Geometry, new(Coordinates.ChartCoordinate, _speed)));
        var raw = held.GetJournal(); var original = raw.Baseline!;
        if (raw.Plan!.RequiredWork.Any(row => row.Kind == SeekHoldGeometryRequiredWorkKind.EaseTableInitialization))
            throw new GameplayStateException(ErrorCode.NotReady, "The actual Hold geometry lacks its initialized native easing rows.");
        var remaining = original.Notes.Where(row => Coordinates.ChartCoordinate <= row.TailChartTime)
            .Select(row => row.Reference).ToHashSet();
        var pending = _target.NativeSorted.Where(remaining.Contains).ToArray();
        _holdSeed = new(Array.AsReadOnly(pending), Array.Empty<long>());
        var reset = new SeekHoldStateTargets(_holdSeed.HoldNotes, _holdSeed.RunningHoldNotes,
            original.Notes.Select(row => new SeekHoldNoteTarget(row.Reference, false, 0)).ToArray(),
            new(Coordinates.ChartCoordinate, _speed, 0));
        _holdReset = Own(_capture.PrepareSeekHoldState(_profiles.Hold, reset));
    }
    private static void Require(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }

    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("The game-state preparation was already entered.");
        VerifyPreparation(); return 0;
    });

    private void VerifyPreparation()
    {
        _target.VerifySource(); _chart.VerifySource();
        if (BitConverter.SingleToInt32Bits(_api.ReadExactField<float>(_information, _speedField)) != BitConverter.SingleToInt32Bits(_speed))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The chart movement speed changed after target preparation.");
        foreach (var value in _values) { value.VerifyPrepared(); Current(); }
        _flags.VerifyPrepared(); _holdReset.VerifyPrepared(); _renderer.VerifyExpectedValues();
        _canvas.VerifyPrepared(); _lines.VerifyPrepared();
    }

    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("A prepared game state commits once only.");
        (_epochParticipant ?? throw new InvalidOperationException("The original Hold epoch must be registered first in this transaction."))
            .VerifyStoppedForReconstruction();
        _entered = true;
        _rendererEntered = true; _renderer.Execute(); Current();
        foreach (var value in _values)
        {
            value.VerifyPrepared(); ++_attemptedValues; value.Commit();
            Require(value.Confirm().Observed == true, "A complete game-state value stage did not read back."); Current();
        }
        _flagsEntered = true; _flags.Commit(); Require(_flags.Confirm().Observed == true, "The target note flags did not read back.");
        _holdResetEntered = true; _holdReset.Commit(); Require(_holdReset.Confirm().Observed == true, "The original Hold lists and reset scalars did not read back.");
        _canvasEntered = true; _canvas.Execute(); Require(_canvas.VerifyAppliedValues(), "The actual target Canvas did not read back.");
        _linesEntered = true; _lines.Execute(); Require(_lines.VerifyAppliedValues(), "The actual target line endpoints did not read back.");
        _dynamicEntered = true; _lines.ApplyDynamicRenderer(_canvas, _profiles.LineRenderer); Current();
        _visibility = _renderer.PrepareNoteVisibility(_profiles.Visibility, Coordinates.ChartCoordinate, _speed, _flags);
        _visibility.Execute(); _visibility.VerifyAppliedValues(); Current();
        _holdGeometry = Own(_capture.PrepareSeekHoldGeometry(_profiles.Geometry,
            new(Coordinates.ChartCoordinate, _speed, _holdSeed)));
        _holdGeometry.Execute(); Require(_holdGeometry.VerifyAppliedValues(), "The actual target Hold geometry did not read back.");
        var holdTarget = _holdGeometry.GetJournal().HoldStateTargets
            ?? throw new InvalidOperationException("The actual Hold geometry did not produce its complete list/scalar targets.");
        _holdFinal = Own(_capture.PrepareSeekHoldState(_profiles.Hold, holdTarget));
        _holdFinalEntered = true; _holdFinal.Commit();
        Require(_holdFinal.Confirm().Observed == true, "The final actual Hold lists and scalars did not read back.");
        _holdEpoch.StartTargetHolds(); Require(_holdEpoch.VerifyTarget(), "The actual target Hold epoch did not read back.");
        _committed = true; return 0;
    });

    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_committed || _restoring) throw new InvalidOperationException("Complete game-state commit before confirmation.");
        foreach (var value in _values) { Require(value.Confirm().Observed == true, "A target game-state value changed."); Current(); }
        Require(_flags.Confirm().Observed == true, "The actual target note flags changed.");
        Require(_holdFinal!.Confirm().Observed == true && _holdGeometry!.VerifyAppliedTransforms(), "The complete target Hold state changed.");
        _visibility!.VerifyAppliedValuesAfterHold(_holdGeometry!);
        _renderer.VerifyExpectedPoolState();
        Require(_canvas.VerifyAppliedValues() && _lines.VerifyAppliedValues()
            && _lines.VerifyAppliedDynamicRendererValues(), "The target Canvas, endpoints or actual renderer values changed.");
        Require(_holdEpoch.VerifyTarget(), "The target Hold coroutines or actual input state changed.");
        _holdGeometry!.AcknowledgeVerifiedTransformOwnership();
        _canvas.AcknowledgeVerifiedTargetOwnership();
        _lines.AcknowledgeVerifiedTargetOwnership();
        _renderer.AcknowledgeVerifiedPoolOwnership();
        return new GameplayControlConfirmation(true,
            "Independent readback of actual note times, judgement queues/counters, input lists, native visibility, Canvas/line renderer and complete Hold geometry/list values. Engine callback and historical input equivalence are not implied.");
    });
}
