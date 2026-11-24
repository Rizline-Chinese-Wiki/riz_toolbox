using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Chart-time evaluation over the independently captured geometry basis.
/// This does not assert that canvas/line/ring geometry has been rebuilt at ChartTime.</summary>
internal sealed record SeekHoldGeometrySeed(IReadOnlyList<long> HoldNotes, IReadOnlyList<long> RunningHoldNotes);
internal sealed record SeekHoldGeometryRequest(float ChartTime, float Speed, SeekHoldGeometrySeed? Seed = null);
internal sealed record SeekHoldGeometryNoteState(long Reference, float HeadChartTime, float TailChartTime,
    float NoteFloorPosition, float StartFloorPosition, int EaseType, float EndFloorPosition,
    bool IsActive, bool Missed, bool IsStaticNote, float LastFrameHoldLineLength,
    long NoteTransform, long EndFlagTransform, long HoldLineTransform,
    long CanvasParent, long EndFlagCanvasParent, long LineTransform, long EndLineTransform,
    long RingTransform, bool RingActive);
internal sealed record SeekHoldGeometryTransformState(long Reference, long Parent, NativeVector3 WorldPosition,
    NativeVector3 LocalPosition, NativeRendererQuaternion LocalRotation, NativeVector3 LocalScale);
internal sealed record SeekHoldGeometryEaseTable(int Code, IReadOnlyList<float> Progress);
internal sealed record SeekHoldGeometrySnapshot(SeekHoldStateSourceIdentity Identity, long UiRootTransform, long CameraTransform,
    long CanvasControl, IReadOnlyList<long> CanvasTransforms, IReadOnlyList<float> FloorPositions,
    long EaseTableReference, IReadOnlyList<SeekHoldGeometryEaseTable>? EaseTables,
    SeekHoldListsState Lists, SeekHoldControlValues Control, IReadOnlyList<SeekHoldGeometryNoteState> Notes,
    IReadOnlyList<SeekHoldGeometryTransformState> Transforms);
internal enum SeekHoldGeometryStepKind
{
    ParentEndFlagToCanvas, EndFlagLocalPosition, ParentHeadToLine, HeadLocalPosition,
    PendingLineScale, ParentToRing, ZeroLocalXY, RunningLineScale, AlignWorldXToLine,
    ParentToCanvas, CompletedLineScale
}
/// <summary>A dynamic geometry step. Length and preserved coordinates are computed
/// from its exact independently read pre-state, including previous native parent effects.</summary>
internal sealed record SeekHoldGeometryStep(int Ordinal, SeekHoldGeometryStepKind Kind);
internal enum SeekHoldGeometryOperationKind { SetParent, SetLocalPosition, SetWorldPosition, SetLocalScale }
internal enum SeekHoldGeometryRestoreField { Parent, LocalPosition, LocalRotation, LocalScale }
internal sealed record SeekHoldGeometryRestoreEffect(int Ordinal, long Transform,
    SeekHoldGeometryRestoreField Field, SeekHoldGeometrySnapshot Before,
    SeekHoldGeometrySnapshot? After, bool NativeEntered, bool NativeReturned,
    bool OriginalValueObserved, string? Failure);
internal sealed record SeekHoldGeometryOperation(int Ordinal, SeekHoldGeometryOperationKind Kind,
    long Transform, long Parent, bool WorldPositionStays, NativeVector3 Value);
internal enum SeekHoldGeometryRequiredWorkKind
{
    TargetCanvasLineRingBasis, HoldEngineClosure, InputEpochClosure, RendererEvent,
    MissingRuntimeInstances, TargetVisibility, TransformCallbackClosure, EaseTableInitialization
}
internal sealed record SeekHoldGeometryRequiredWork(SeekHoldGeometryRequiredWorkKind Kind, long? Reference,
    int? EventIndex, string Evidence);
internal sealed record SeekHoldGeometryPlan(SeekHoldGeometryRequest Request,
    IReadOnlyList<SeekHoldGeometryStep> Steps, IReadOnlyList<long> HoldNotes,
    IReadOnlyList<long> RunningHoldNotes, IReadOnlyList<SeekHoldGeometryRequiredWork> RequiredWork);
internal sealed record SeekHoldGeometryCommandEffects(int StepIndex, SeekHoldGeometryStep Step,
    SeekHoldGeometryOperation? Operation, SeekHoldGeometrySnapshot? Before, SeekHoldGeometrySnapshot? After,
    bool NativeEntered, bool NativeReturned, bool CommandedPropertiesObserved, bool NoWriteRequired,
    bool ExternalEffectsUnclosed, bool SideEffectsUnknown, string? Failure);
internal sealed record SeekHoldGeometryEffectsJournal(bool Prepared, bool ExecutionEntered, bool ExecutionFinished,
    bool Disposed, int AttemptedCommands, bool SideEffectsUnknown, bool ExternalEffectsUnclosed,
    bool HasRetainedResponsibility, string? Failure, string? CleanupFailure,
    SeekHoldGeometrySnapshot? Baseline, SeekHoldGeometryPlan? Plan, SeekHoldStateTargets? HoldStateTargets,
    IReadOnlyList<SeekHoldGeometryCommandEffects> Commands, object? EffectsOwner)
{
    internal bool CompleteSeekPermitted => false;
}

/// <summary>Owns an independent full actual Hold/NoteView closure and all note,
/// end-flag, hold-line, canvas, line, ring and UI-root Transforms. Selected reads
/// include the selected note's full transform closure, phase reads the whole graph.
/// Native Transform callbacks remain external effects, even after value readback.</summary>
internal interface INativeSeekHoldGeometryAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifySource();
    void BindLifetimeGuard(Action guard);
    SeekHoldGeometrySnapshot ReadSnapshot(int? selectedOrdinal = null);
    void Dispatch(SeekHoldGeometryOperation operation, SeekHoldGeometrySnapshot expected, Action beforeDispatch);
    void RestoreTransform(int ordinal, SeekHoldGeometryTransformState original,
        SeekHoldGeometryRestoreField field, SeekHoldGeometrySnapshot expected, Action beforeDispatch)
        => throw new NotSupportedException("This Hold accessor has no independently captured transform restoration.");
    void RequestDispose();
}

/// <summary>Evaluates chart-time Hold transitions over an explicitly captured
/// geometry basis and executes actual official Transform commands. It is not an
/// inverse transaction; target-time basis, events and engine closure stay separate.</summary>
internal sealed partial class NativeSeekHoldGeometryCommands : IDisposable
{
    internal const int MaximumNotes = NativeSeekRuntimeNoteView.MaximumNotes;
    internal const int MaximumTransforms = 1024 * 1024;
    internal const int MaximumSteps = 16 * MaximumNotes;
    internal const int MaximumUnclosedOwners = 256;
    private INativeSeekHoldGeometryAccess _access;
    private readonly NativeSeekHoldGeometryCommands _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private Exception? _constructionFailure;
    private SeekHoldGeometrySnapshot? _baseline, _current;
    private SeekHoldGeometryPlan? _plan;
    private SeekHoldStateTargets? _stateTargets;
    private SeekHoldGeometryCommandEffects?[] _rows = Array.Empty<SeekHoldGeometryCommandEffects?>();
    private SeekHoldNoteTarget[] _noteTargets = Array.Empty<SeekHoldNoteTarget>();
    private readonly Dictionary<long, SeekHoldGeometryNoteState> _noteMap = new();
    private readonly Dictionary<long, SeekHoldGeometryTransformState> _transforms = new();
    private float _lastPendingLength;
    private bool _prepared, _busy, _executionEntered, _executionFinished, _disposed;
    private bool _cleanupBusy, _cleanupAttempted, _released, _notifyingDisposal, _disposalNotified;
    private bool _unknown, _externalEffects, _retained, _ownerReserved;
    private int _attempted;
    private string? _failure, _cleanupFailure;
    internal int AttemptedCommands => _attempted;
    internal bool SideEffectsUnknown => _unknown;
    internal bool HasRetainedResponsibility => _retained;
    private NativeSeekHoldGeometryCommands(INativeSeekHoldGeometryAccess? access)
        => (_access, _registered) = (access!, this);
    internal SeekHoldGeometryEffectsJournal GetJournal()
        => new(_prepared, _executionEntered, _executionFinished, _disposed, _attempted, _unknown, _externalEffects,
            _retained, _failure, _cleanupFailure, _baseline, _plan, _stateTargets,
            Array.AsReadOnly(_rows.Where(row => row is not null).Select(row => row!).ToArray()), _retained ? this : null);
}
