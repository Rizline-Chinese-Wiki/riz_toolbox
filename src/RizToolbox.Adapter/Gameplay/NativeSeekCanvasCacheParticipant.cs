namespace RizToolbox.Adapter.Gameplay;

internal enum SeekCanvasCacheKind { FloorPositions, FloorSpeeds, XPositions, NowTime, Speed }

/// <summary>An occurrence in the complete three-array, time and speed cache view.
/// Array owners are actual Single[] references; scalar owners are the same actual
/// CanvasControl reference. TargetBits is a finite Single represented exactly.</summary>
internal sealed record SeekCanvasCacheSlot(int Ordinal, SeekCanvasCacheKind Kind,
    long Owner, int Index, int TargetBits);

/// <summary>Retains independent actual source and cache roots. The final callback
/// immediately precedes the exact conditional native store. Logical disposal must
/// revoke callbacks without releasing roots while a participant operation is active.</summary>
internal interface INativeSeekCanvasCacheAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifySource();
    void BindLifetimeGuard(Action guard);
    NativeSeekCanvasTargetCapture SourceCapture { get; }
    IDisposable EnterOperation();
    IReadOnlyList<SeekCanvasCacheSlot> Slots { get; }
    IReadOnlyList<int> ReadSnapshot();
    int ReadSlot(int ordinal);
    void WriteSlot(int ordinal, int bits, int expectedBits, Action beforeDispatch);
    void RequestDispose();
}

internal enum SeekCanvasCacheDirection { Apply, Restore }

/// <summary>One conditional storage attempt. Readback is an actual raw-bit
/// observation, including an admitted store which subsequently threw.</summary>
internal sealed record SeekCanvasCacheStoreEffects(int Sequence, SeekCanvasCacheDirection Direction,
    int Ordinal, SeekCanvasCacheKind Kind, long Owner, int Index, int BeforeBits, int TargetBits,
    bool Attempted, bool NativeEntered, bool NativeReturned, int? Readback,
    bool TargetObserved, bool OriginalObserved, string? Failure);

internal sealed record SeekCanvasCacheEffectsJournal(bool Prepared, bool CommitEntered,
    bool CommitFinished, bool AppliedValuesVerified, bool RestorationEntered,
    bool RestorationFinished, bool RestoredValuesVerified, bool Disposed,
    int AttemptedWrites, int AttemptedRestores, bool UnrecognizedState, bool SideEffectsUnknown,
    bool HasRetainedResponsibility, string? Failure, string? CleanupFailure,
    IReadOnlyList<SeekCanvasCacheSlot> Slots, IReadOnlyList<SeekCanvasCacheSlot> UniqueSlots,
    IReadOnlyList<int> Baseline, IReadOnlyList<int> Expected,
    IReadOnlyList<SeekCanvasCacheStoreEffects> Stores, object? CleanupOwner)
{
    internal bool CompleteSeekPermitted => false;
    internal object? SourceCleanupOwner { get; init; }
    internal NativeSeekCanvasTargetCapture? SourceCapture { get; init; }
}

/// <summary>Explicit selected Canvas computation-cache storage only. It does not
/// invoke producers, consumers or Unity position setters, and cannot admit a full
/// Seek. Exact complete value confirmation can close only this storage's own roots.</summary>
internal sealed partial class NativeSeekCanvasCacheParticipant : IGameplayValueParticipant
{
    internal const int MaximumArrayLength = 65536;
    internal const int MaximumSlots = 3 * MaximumArrayLength + 2;
    internal const int MaximumUnclosedOwners = 256;
    private const string CleanupOwnerKey = "SeekCanvasCacheCleanupOwner";
    private const string CleanupFailureKey = "SeekCanvasCacheCleanupFailure";
    private const string SourceCleanupOwnerKey = "SeekCanvasCacheSourceCleanupOwner";
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekCanvasCacheParticipant> Owners = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private INativeSeekCanvasCacheAccess _access = null!;
    private NativeSeekCanvasTargetCapture? _sourceCapture;
    private SeekCanvasCacheSlot[] _slots = Array.Empty<SeekCanvasCacheSlot>();
    private SeekCanvasCacheSlot[] _uniqueSlots = Array.Empty<SeekCanvasCacheSlot>();
    private int[] _baseline = Array.Empty<int>(), _expected = Array.Empty<int>();
    private int[] _firstOrdinals = Array.Empty<int>(), _nextAliases = Array.Empty<int>();
    private readonly List<SeekCanvasCacheStoreEffects> _stores = new();
    private readonly List<int> _enteredOrdinals = new();
    private int _attemptedWrites, _attemptedRestores, _pendingStore = -1;
    private bool _prepared, _commitEntered, _commitFinished, _appliedValuesVerified;
    private bool _restorationEntered, _restorationFinished, _restoredValuesVerified;
    private bool _busy, _disposed, _cleanupBusy, _cleanupAttempted, _released;
    private bool _notifyingDisposal, _disposalNotified, _unknown, _unrecognized;
    private bool _retained, _ownerReserved, _hasDispatched, _dispatchContractUnknown;
    private bool _preparationEntered;
    private long _observationRevision;
    private Exception? _constructionFailure, _cleanupException;
    private object? _sourceCleanupOwner;
    private string? _failure, _cleanupFailure;

    private NativeSeekCanvasCacheParticipant() { }

    public string Id => "native.canvas.explicit-cache-storage";
    internal bool CompleteSeekPermitted => false;
    internal int AttemptedWrites { get { DiagnosticEntry(); return _attemptedWrites; } }
    internal bool SideEffectsUnknown { get { DiagnosticEntry(); return _unknown; } }
    internal bool HasRetainedResponsibility { get { DiagnosticEntry(); return _retained; } }
    internal long ObservationRevision { get { DiagnosticEntry(); return _observationRevision; } }
    internal NativeSeekCanvasTargetCapture SourceCapture
    {
        get
        {
            DiagnosticEntry();
            return _sourceCapture ?? throw new InvalidOperationException("The actual Canvas cache owner has no attached source capture.");
        }
    }

    internal SeekCanvasCacheEffectsJournal GetJournal()
    {
        DiagnosticEntry();
        return new(_prepared, _commitEntered, _commitFinished, _appliedValuesVerified,
            _restorationEntered, _restorationFinished, _restoredValuesVerified, _disposed,
            _attemptedWrites, _attemptedRestores, _unrecognized, _unknown, _retained,
            _failure, _cleanupFailure,
            Array.AsReadOnly(_slots.Select(row => row with { }).ToArray()),
            Array.AsReadOnly(_uniqueSlots.Select(row => row with { }).ToArray()),
            Array.AsReadOnly(_baseline.ToArray()), Array.AsReadOnly(_expected.ToArray()),
            Array.AsReadOnly(_stores.Select(row => row with { }).ToArray()), _retained ? this : null)
        { SourceCleanupOwner = _sourceCleanupOwner, SourceCapture = _sourceCapture };
    }
}
