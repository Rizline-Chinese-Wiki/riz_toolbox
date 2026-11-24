using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekLineRingSourceIdentity(NativeSeekCanvasTargetCapture CanvasCapture,
    NativeSeekCanvasTargetPlan CanvasPlan, SeekRuntimeNoteSourceIdentity Notes,
    long Information, long Canvas, long LineControl);
internal sealed record SeekLineRingPoint(long Reference, float Time, float XPosition,
    int EaseType, int CanvasIndex, float FloorPosition);
internal sealed record SeekLineRingLine(long Reference, bool IsStatic, int CanvasIndex,
    long Transform, long StartPoint, long EndPoint, long EndTransform, long EndLine, long Group);
internal sealed record SeekLineRingGroup(long Reference, long RingTransform,
    long LinesArray, IReadOnlyList<long> Lines);
internal sealed record SeekLineRingControlBaseline(int NowTimeBits, int SceneWidthBits,
    int SpeedBits, int GameSceneScaleBits);
internal sealed record SeekLineRingGroupBaseline(long Reference, int NowLineIndex, bool RingActive);
internal sealed record SeekLineRingTransformBaseline(long Reference, long Parent,
    NativeVector3 Position, NativeVector3 LocalPosition, NativeRendererQuaternion LocalRotation, NativeVector3 LocalScale);
internal sealed record SeekLineRingChartPoint(int LineOrdinal, int PointOrdinal,
    long ChartLine, long Reference, float Time);
internal sealed record SeekLineRingNoteJoin(long Note, long NoteData, ChartNoteLocation Location,
    long ChartLine, int SegmentOrdinal, long RuntimeLine, long StartPoint, long EndPoint, long Group, bool IsStatic);

/// <summary>Complete actual occurrences plus independently observed baseline
/// values. Parent assignments and poses are baseline values; source identity is
/// the retained control/line/point/group graph and the original Canvas Plan.</summary>
internal sealed record SeekLineRingSourceSnapshot(SeekLineRingSourceIdentity Identity,
    long DynamicLinesArray, IReadOnlyList<long> DynamicLines,
    long LineGroupsArray, IReadOnlyList<long> LineGroups,
    IReadOnlyList<SeekLineRingLine> Lines, IReadOnlyList<SeekLineRingPoint> Points,
    IReadOnlyList<SeekLineRingGroup> Groups, SeekLineRingControlBaseline ControlBaseline,
    IReadOnlyList<SeekLineRingGroupBaseline> GroupBaselines,
    IReadOnlyList<SeekLineRingTransformBaseline> Transforms,
    IReadOnlyList<SeekLineRingChartPoint> ChartPoints, IReadOnlyList<SeekLineRingNoteJoin> NoteJoins);

internal sealed record SeekLineRingRequiredWork(string Kind, long Reference, string Reason);
internal sealed record SeekLineRingSourceCoverage(int DynamicOccurrences, int GroupOccurrences,
    int GroupSegmentOccurrences, int UniqueLines, int StaticLines,
    IReadOnlyList<long> LinesWithoutNotes, IReadOnlyList<SeekLineRingNoteJoin> ProvenJoins,
    IReadOnlyList<SeekLineRingRequiredWork> RequiredWork)
{
    internal int PhysicalGroupLineOccurrences { get; init; }
    internal bool CompleteSeekPermitted => false;
}

internal interface IRetainedSeekLineRingReader : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    NativeSeekCanvasTargetCapture SourceCapture { get; }
    IDisposable EnterOperation();
    void BindLifetimeGuard(Action guard);
    void VerifySource();
    SeekLineRingSourceSnapshot ReadSnapshot();
    void RequestDispose();
    void CompleteCapture();
}

internal sealed record SeekLineRingSourceJournal(bool Captured, bool SourceVerified,
    bool BaselineVerified, bool Disposed, bool SourceExpired, bool HasRetainedResponsibility,
    string? Failure, string? CleanupFailure, NativeSeekCanvasTargetCapture? CanvasCapture,
    object? CleanupOwner, object? SourceCleanupOwner)
{
    internal int AttemptedWrites => 0;
    internal bool CompleteSeekPermitted => false;
}

/// <summary>Owns one reader hold on the original Canvas source. Captures actual
/// lines/groups without Hold or Note coverage assumptions; no producer, setter,
/// easing initializer, target projection or complete Seek admission is invoked.</summary>
internal sealed partial class NativeSeekLineRingSourceCapture : IDisposable
{
    internal const int MaximumUnclosedOwners = 256;
    internal const int MaximumDynamicOccurrences = NativeSeekRuntimeNoteView.MaximumLines;
    internal const int MaximumGroupOccurrences = NativeSeekRuntimeNoteView.MaximumLines;
    internal const int MaximumGroups = NativeSeekRuntimeNoteView.MaximumLines;
    internal const int MaximumGroupLineOccurrences = NativeSeekRuntimeNoteView.MaximumLinePoints;
    internal const int MaximumLines = NativeSeekRuntimeNoteView.MaximumLinePoints;
    internal const int MaximumPoints = NativeSeekRuntimeNoteView.MaximumLinePoints;
    internal const int MaximumTransforms = NativeSeekRuntimeNoteView.MaximumLinePoints * 4;
    internal const int MaximumParentDepth = 256;
    internal const int MaximumChartPoints = 1048576;
    internal const int MaximumNoteJoins = NativeSeekRuntimeNoteView.MaximumNotes;
    private const string OwnerKey = "SeekLineRingSourceCleanupOwner";
    private const string FailureKey = "SeekLineRingSourceCleanupFailure";
    private const string SourceOwnerKey = "SeekLineRingSourceNativeCleanupOwner";
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekLineRingSourceCapture> Owners = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private IRetainedSeekLineRingReader? _reader;
    private NativeSeekCanvasTargetCapture? _canvasCapture;
    private SeekLineRingSourceSnapshot? _snapshot;
    private SeekCanvasNumericBasis? _numericBasis;
    private SeekLineRingSourceCoverage? _coverage;
    private bool _busy, _disposed, _releasing, _released, _releaseAttempted, _ownerReserved;
    private bool _sourceVerified, _baselineVerified, _sourceExpired, _retained, _notifyingDisposal;
    private Exception? _cleanupFailure;
    private object? _sourceCleanupOwner;
    private string? _failure;

    private NativeSeekLineRingSourceCapture() { }
    internal bool CompleteSeekPermitted => false;
    internal bool HasRetainedResponsibility { get { DiagnosticEntry(); return _retained; } }
    internal NativeSeekCanvasTargetCapture CanvasCapture
    { get { DiagnosticEntry(); return _canvasCapture ?? throw new InvalidOperationException("The original Canvas capture is not yet available."); } }
    internal SeekLineRingSourceSnapshot Snapshot
    { get { DiagnosticEntry(); return _snapshot ?? throw new InvalidOperationException("The complete line/ring source did not finish capture."); } }
    internal SeekLineRingSourceCoverage Coverage
    { get { DiagnosticEntry(); return _coverage ?? throw new InvalidOperationException("The complete line/ring coverage is unavailable."); } }
    /// <summary>Fresh source admission before returning the already owned
    /// numeric copy. This does not verify mutable line/ring baselines.</summary>
    internal SeekCanvasNumericBasis ReadNumericBasis()
    {
        VerifySource(); DiagnosticEntry(); Lifetime();
        return _numericBasis ?? throw new InvalidOperationException("The original Canvas numeric basis was not captured.");
    }
    internal SeekLineRingSourceJournal GetJournal()
    {
        DiagnosticEntry(); return new(_snapshot is not null, _sourceVerified, _baselineVerified,
            _disposed, _sourceExpired, _retained, _failure, _cleanupFailure?.Message,
            _canvasCapture, _retained ? this : null, _sourceCleanupOwner);
    }
}
