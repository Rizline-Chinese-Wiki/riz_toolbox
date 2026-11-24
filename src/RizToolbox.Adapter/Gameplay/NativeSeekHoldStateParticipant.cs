using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekHoldStateSourceIdentity(SeekRuntimeNoteSourceIdentity Notes, long HoldControl);
internal sealed record SeekHoldListStorage(long Reference, long Class, long Backing, long ArrayClass,
    int Size, int Version, IReadOnlyList<long> CapacitySlots);
internal sealed record SeekHoldArrayStorage(long Reference, long Class, IReadOnlyList<long> CapacitySlots);
internal sealed record SeekHoldListsState(SeekHoldListStorage HoldNotes, SeekHoldListStorage RunningHoldNotes);
internal sealed record SeekHoldControlValues(float NowTime, float Speed, float HoldLineLength);
internal sealed record SeekHoldNoteTarget(long Reference, bool IsStaticNote, float LastFrameHoldLineLength);
internal sealed record SeekHoldStateTargets(IReadOnlyList<long> HoldNotes, IReadOnlyList<long> RunningHoldNotes,
    IReadOnlyList<SeekHoldNoteTarget> Notes, SeekHoldControlValues Control);
internal sealed record SeekHoldRawState(SeekHoldStateSourceIdentity Identity, SeekHoldListsState Lists,
    SeekHoldControlValues Control, IReadOnlyList<SeekHoldNoteTarget> Notes);
internal enum SeekHoldListRestoreField { Size, Items, Version }
internal enum SeekHoldNoteField { IsStaticNote, LastFrameHoldLineLength }
internal enum SeekHoldControlField { NowTime, Speed, HoldLineLength }

/// <summary>Independently owns the complete actual NoteView, two Hold Lists and
/// original capacity references. Full graph checks occur at phase boundaries;
/// selected reads do not repeatedly scan the chart. Final dispatch callbacks run
/// after rooting/marshalling and use only managed lifecycle state.</summary>
internal interface INativeSeekHoldStateAccess : IDisposable
{
    void BindLifetimeGuard(Action guard);
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifySource();
    IReadOnlyList<SeekRuntimeNote> RuntimeHolds { get; }
    SeekHoldRawState ReadSnapshot();
    SeekHoldListsState ReadLists();
    SeekHoldNoteTarget ReadHold(int ordinal);
    SeekHoldControlValues ReadControl();
    SeekHoldArrayStorage ReadBacking(long reference);
    void Clear(int listSlot, SeekHoldListsState expectedCurrent, Action beforeDispatch);
    void Add(int listSlot, long reference, SeekHoldListsState expectedCurrent, Action beforeDispatch);
    void RestoreCapacitySlot(int listSlot, int capacitySlot, long value,
        SeekHoldListsState expectedCurrent, SeekHoldArrayStorage expectedBacking, Action beforeDispatch);
    void RestoreListField(int listSlot, SeekHoldListRestoreField field, long value,
        SeekHoldListsState expectedCurrent, Action beforeDispatch);
    void WriteHold(int ordinal, SeekHoldNoteField field, SeekHoldNoteTarget desired,
        SeekHoldNoteTarget expectedCurrent, Action beforeDispatch);
    void WriteControl(SeekHoldControlField field, SeekHoldControlValues desired,
        SeekHoldControlValues expectedCurrent, Action beforeDispatch);
    void RequestDispose();
}

/// <summary>Writes two explicit Hold occurrence sequences and explicit scalar
/// values. Restoration includes original List/backing identity, capacity tail,
/// size/version and bit-exact scalar baselines. This does not dispatch Hold.Update
/// or establish engine exit, Hold-idle, target geometry or complete Seek. Native
/// conditional samples cannot exclude external writers or unobserved ABA.</summary>
internal sealed partial class NativeSeekHoldStateParticipant : IGameplayValueParticipant
{
    internal const int MaximumCapacity = NativeSeekRuntimeNoteView.MaximumNotes;
    internal const int MaximumRetainedCapacity = 2 * MaximumCapacity;
    // Includes every detached growth backing, not just the two original arrays.
    internal const int MaximumTotalRetainedCapacity = 4 * MaximumCapacity;
    internal const int MaximumUnclosedOwners = 256;
    private INativeSeekHoldStateAccess _access;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private Exception? _constructionFailure;
    private SeekHoldRawState _baseline = null!;
    private SeekHoldListsState _expectedLists = null!;
    private SeekHoldControlValues _expectedControl = null!, _controlTarget = null!;
    private SeekHoldNoteTarget[] _expectedHolds = Array.Empty<SeekHoldNoteTarget>();
    private SeekHoldNoteTarget[] _holdTargets = Array.Empty<SeekHoldNoteTarget>();
    private long[] _references = Array.Empty<long>();
    private long[][] _listTargets = Array.Empty<long[]>();
    private int[] _representatives = Array.Empty<int>();
    private Dictionary<long, SeekHoldArrayStorage> _baselineArrays = new(), _expectedArrays = new();
    private readonly HashSet<long> _knownBackings = new();
    private readonly List<string> _operationErrors = new(), _restoreErrors = new();
    private PendingMutation? _pending;
    private int _attempted;
    private bool _busy, _entered, _committed, _confirmed, _restoring, _restored;
    private bool _disposeRequested, _notifyingDisposal, _disposalNotified, _cleanupBusy, _cleanupAttempted, _released;
    private bool _unrecognized, _retained, _ownerReserved, _hasDispatched;
    internal bool HasRetainedResponsibility => _retained;
    internal int AttemptedWrites => _attempted;
    internal bool SideEffectsUnknown { get; private set; }
    internal string? Failure { get; private set; }
    internal string? CleanupFailure { get; private set; }
    public string Id => "native.hold-control.explicit-two-lists-and-values";

    private NativeSeekHoldStateParticipant(INativeSeekHoldStateAccess? access) => _access = access!;
}
