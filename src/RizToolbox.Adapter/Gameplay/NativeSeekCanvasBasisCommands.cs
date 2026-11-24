namespace RizToolbox.Adapter.Gameplay;

/// <summary>One physical Canvas capture shared by the two selected-value owners.
/// Operation scopes are managed admission only; they never enter NoteView's
/// native in-flight gate or establish a complete Seek transaction.</summary>
internal interface INativeSeekCanvasBasisAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    NativeSeekCanvasTargetCapture SourceCapture { get; }
    INativeSeekCanvasCacheAccess CreateCacheAccess();
    INativeSeekCanvasPositionAccess CreatePositionAccess(ISeekCanvasPositionNativeProfile profile);
    IRetainedSeekLineRingReader CreateLineRingReader(ISeekLineRingSourceProfile profile, Action? constructionGuard = null);
    void VerifySource(Action verifyCallerLocal);
    bool IsOperationActive { get; }
    void BindTerminalObserver(Action observer);
    bool HasRetainedResponsibility { get; }
    object? SourceCleanupOwner { get; }
    Exception? CleanupFailure { get; }
}

/// <summary>Marks only a shared scope caller's managed withdrawal. An incomplete
/// observation still fails, but it does not falsely expire the sibling's source.
/// Actual source/ABI changes and unknown cleanup remain independently sticky.</summary>
internal static class SeekCanvasBasisCallerWithdrawal
{
    internal const string Key = "SeekCanvasBasisCallerWithdrawal";
    private static readonly object Token = new();
    internal static void Mark(Exception error) => error.Data[Key] = Token;
    internal static bool Is(Exception error) => ReferenceEquals(error.Data[Key], Token);
}

internal sealed record SeekCanvasBasisFailure(string Phase, string Message,
    Exception Failure, object? ChildOwner);

internal sealed record SeekCanvasBasisEffectsJournal(bool Prepared, bool ExecutionEntered,
    bool ExecutionFinished, bool AppliedValuesVerified, bool RestorationEntered,
    bool RestorationFinished, bool RestoredValuesVerified, bool Disposed,
    bool SideEffectsUnknown, bool ExternalEffectsUnclosed, bool HasRetainedResponsibility,
    string? Failure, string? CleanupFailure, NativeSeekCanvasTargetCapture? SourceCapture,
    NativeSeekCanvasTargetPlan? Plan, NativeSeekCanvasCacheParticipant? OriginalCacheOwner,
    NativeSeekCanvasPositionCommands? OriginalPositionOwner, SeekCanvasCacheEffectsJournal? CacheJournal,
    SeekCanvasPositionEffectsJournal? PositionJournal, IReadOnlyList<SeekCanvasBasisFailure> Failures,
    object? SourceCleanupOwner)
{
    internal bool CompleteSeekPermitted => false;
}

/// <summary>Coordinates one captured Plan's cache storage and Transform commands.
/// Cache values are observed before any position setter; selected-value recovery
/// runs in reverse order. The original position owner retains entered Unity
/// history; duplicate scene roots release only after explicit actual target or
/// restored-value ownership acknowledgement.</summary>
internal sealed partial class NativeSeekCanvasBasisCommands : IDisposable
{
    internal const int MaximumUnclosedOwners = 256;
    private const string OwnerKey = "SeekCanvasBasisOwner";
    private const string CleanupFailureKey = "SeekCanvasBasisCleanupFailure";
    private const string FailuresKey = "SeekCanvasBasisFailures";
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekCanvasBasisCommands> Owners = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NativeSeekCanvasBasisCommands _registered;
    private INativeSeekCanvasBasisAccess? _basis;
    private NativeSeekCanvasTargetCapture? _sourceCapture;
    private NativeSeekCanvasTargetPlan? _plan;
    private NativeSeekCanvasCacheParticipant? _cache;
    private NativeSeekCanvasPositionCommands? _positions;
    private readonly List<SeekCanvasBasisFailure> _failures = new();
    private bool _prepared, _executionEntered, _executionFinished, _appliedValuesVerified;
    private bool _restorationEntered, _restorationFinished, _restoredValuesVerified;
    private bool _busy, _disposed, _disposing, _bootstrapReleaseAttempted, _bootstrapReleased;
    private bool _ownerReserved, _retained;
    private long _appliedCacheRevision, _appliedPositionRevision, _restoredCacheRevision, _restoredPositionRevision;
    private Exception? _cleanupFailure;
    private object? _sourceCleanupOwner;
    private string _phase = "Reserve";
    private string? _failure, _restorationFailure;

    private NativeSeekCanvasBasisCommands() => _registered = this;

    internal bool CompleteSeekPermitted => false;
    internal NativeSeekCanvasCacheParticipant OriginalCacheOwner
    { get { DiagnosticEntry(); return _cache ?? throw new InvalidOperationException("The original cache owner has not been reserved."); } }
    internal NativeSeekCanvasPositionCommands OriginalPositionOwner
    { get { DiagnosticEntry(); return _positions ?? throw new InvalidOperationException("The original position owner has not been reserved."); } }
    internal NativeSeekCanvasTargetCapture SourceCapture
    { get { DiagnosticEntry(); return _sourceCapture ?? throw new InvalidOperationException("The shared source capture did not finish."); } }
    internal bool HasRetainedResponsibility
    { get { DiagnosticEntry(); RefreshResponsibility(); return _retained; } }

    internal SeekCanvasBasisEffectsJournal GetJournal()
    {
        DiagnosticEntry(); InvalidateStaleProofs();
        var cache = _cache?.GetJournal(); var positions = _positions?.GetJournal();
        RefreshResponsibility(cache, positions);
        return new(_prepared, _executionEntered, _executionFinished, _appliedValuesVerified,
            _restorationEntered, _restorationFinished, _restoredValuesVerified, _disposed,
            cache?.SideEffectsUnknown == true || positions?.SideEffectsUnknown == true || _cleanupFailure is not null,
            positions?.ExternalEffectsUnclosed == true, _retained, _failure, _cleanupFailure?.Message,
            _sourceCapture, _plan, _cache, _positions, cache, positions,
            Array.AsReadOnly(_failures.Select(item => item with { }).ToArray()), _sourceCleanupOwner);
    }
}
