using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Fixed metadata/code evidence for read-only actual Canvas sources.
/// This profile has no Hold dependency and grants no control/Seek admission.</summary>
internal interface ISeekCanvasNativeProfile : ISeekChartNativeProfile
{
    IntPtr InformationClass { get; }
    IntPtr CanvasClass { get; }
    IntPtr CanvasMoveClass { get; }
    IntPtr KeyPointClass { get; }
    IntPtr TransformClass { get; }
    IntPtr VectorClass { get; }
    IntPtr EaseClass { get; }
    IntPtr EaseInfoClass { get; }
    IntPtr ChartTimeRangeMethod { get; }
}

internal sealed record SeekCanvasSourceIdentity(SeekRuntimeNoteSourceIdentity Notes, long Information, long Progress, long Canvas);
internal sealed record SeekCanvasSourceList(long Reference, long Backing, int Size, int Version,
    IReadOnlyList<long> BackingReferences, IReadOnlyList<SeekCanvasTargetKeyPoint> Points);
internal sealed record SeekCanvasSourceMove(long Reference, int Index, SeekCanvasSourceList Speed, SeekCanvasSourceList X);
internal sealed record SeekCanvasSourceEase(long Reference, int Code, long Progress, IReadOnlyList<float> Samples);
internal sealed record SeekCanvasSourceSnapshot(SeekCanvasSourceIdentity Identity, long TransformsArray,
    IReadOnlyList<long> Transforms, long MovesArray, IReadOnlyList<SeekCanvasSourceMove> Moves,
    float ScreenWidth, NativeVector3 Down, NativeVector3 Right, long EaseArray, IReadOnlyList<SeekCanvasSourceEase> Eases);

/// <summary>Independently retains the complete NoteView and actual Canvas graph,
/// including every array slot, List capacity/tail and initialized Ease sample.
/// Each read verifies saved/current exact field, class, array and range metadata.
/// Local checks and disposal admission must not dispatch native exports.</summary>
internal interface IRetainedSeekCanvasTargetReader : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifyAccess();
    void BindLifetimeGuard(Action guard);
    SeekCanvasSourceSnapshot ReadSnapshot();
    float ChartTimeRange(double from, double to);
    void RequestDispose();
}

/// <summary>Owns actual Canvas sources and an immutable numeric target plan.
/// Verification compares the retained source without evaluating producers again.
/// It neither writes Transform state nor establishes a complete target environment.</summary>
internal sealed partial class NativeSeekCanvasTargetCapture : IDisposable
{
    internal const int MaximumUnclosedOwners = 256;
    internal const int MaximumEaseRows = 19;
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekCanvasTargetCapture> Owners = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private IRetainedSeekCanvasTargetReader? _source;
    private SeekCanvasSourceSnapshot _baseline = null!;
    private NativeSeekCanvasTargetPlan _plan = null!;
    private bool _busy, _disposed, _faulted, _releaseAttempted, _releasing, _released, _ownerReserved;
    private Exception? _constructionFailure;
    private object? _constructionOwner;
    private readonly List<Exception> _cleanupFailures = new();
    internal NativeSeekCanvasTargetPlan Plan => _plan ?? throw new InvalidOperationException("The Canvas source capture did not finish producing its immutable plan.");
    // Immutable occurrence mapping used by explicit target writers. This is
    // source data; reading it does not grant native access or Seek admission.
    internal IReadOnlyList<long> TransformReferences => _baseline?.Transforms
        ?? throw new InvalidOperationException("The Canvas source capture has no complete Transform mapping.");
    internal bool CompleteSeekPermitted => false;
    internal bool HasRetainedResponsibility => _cleanupFailures.Count != 0;

    private NativeSeekCanvasTargetCapture() { }
}
