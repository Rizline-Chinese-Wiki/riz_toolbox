using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal enum SeekRendererCommandKind { Preserve, Appear, Disappear }
internal sealed record SeekRendererCommand(long Reference, SeekRendererCommandKind Kind);
internal sealed record SeekRendererNoteState(long Reference, long Array, long ArrayClass, int Index,
    bool IsActive, bool Destroyed, bool Missed, IReadOnlyList<long> Plans);
internal sealed record SeekRendererPlanState(long Reference, int PoolIndex, long Pool, long Child, long TargetParent, bool Active);
internal sealed record SeekRendererPoolListState(long Reference, long Class, long Backing, long ArrayClass,
    int Size, int Version, IReadOnlyList<long> CapacitySlots);
internal sealed record SeekRendererPoolState(long Reference, long Prefab, int PoolSize, int ChildCount,
    long Transform, SeekRendererPoolListState Available, SeekRendererPoolListState Dirty);
internal sealed record SeekRendererTransformState(long Reference, long Parent, NativeVector3 LocalPosition,
    NativeRendererQuaternion LocalRotation, NativeVector3 LocalScale);
internal sealed record SeekRendererChildState(long Reference, long ParentPool, bool Dirty,
    long TargetComponent, long TargetComponentClass, long Transform);
internal sealed record SeekRendererGraphSnapshot(SeekRuntimeNoteSourceIdentity Identity,
    IReadOnlyList<SeekRendererNoteState> Notes, IReadOnlyList<SeekRendererPlanState> Plans,
    IReadOnlyList<SeekRendererPoolState> Pools, IReadOnlyList<SeekRendererChildState> Children,
    IReadOnlyList<SeekRendererTransformState> Transforms);
internal sealed record SeekRendererCommandEffects(int Ordinal, SeekRendererCommand Command,
    SeekRendererGraphSnapshot? Before, SeekRendererGraphSnapshot? After, bool NativeEntered,
    bool NativeReturned, bool ExpectedReachableStateObserved, bool GrowthObserved,
    bool DeferredEffectsPending, bool SideEffectsUnknown, string? Failure);
internal sealed record SeekRendererChildRetirement(long Child, long GameObject, long Transform, long TargetComponent,
    IReadOnlyList<long> SubtreeGameObjects, IReadOnlyList<long> SubtreeTransforms,
    bool Recognized, bool NativeEntered, bool NativeReturned, bool DeathObserved, string? Failure);
internal sealed record SeekRendererRetiredBackingState(long Pool, SeekRendererPoolListState Storage);
internal sealed record SeekRendererEffectsJournal(bool Prepared, bool ExecutionEntered, bool ExecutionFinished,
    bool Disposed, int AttemptedCommands, bool SideEffectsUnknown, bool DeferredEffectsPending,
    bool HasRetainedResponsibility, string? Failure, string? CleanupFailure,
    SeekRendererGraphSnapshot? Baseline, IReadOnlyList<SeekRendererCommandEffects> Commands, object? EffectsOwner)
{
    internal IReadOnlyList<SeekRendererAllocationOwnership> AllocationOwnership { get; init; } = Array.Empty<SeekRendererAllocationOwnership>();
    internal IReadOnlyList<SeekRendererChildRetirement> Retirements { get; init; } = Array.Empty<SeekRendererChildRetirement>();
}

/// <summary>Owns the independently rooted actual note/renderer/pool closure.
/// A selected read includes complete affected pool Lists and known children;
/// phase reads cover every actual note. Publications adopt roots before inspect.
/// RequestDispose closes logical use; physical roots are released by Dispose.</summary>
internal interface INativeSeekRendererAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifySource();
    void BindLifetimeGuard(Action guard);
    IReadOnlyList<SeekRuntimeNote> RuntimeNotes { get; }
    SeekRendererGraphSnapshot ReadSnapshot(int? selectedOrdinal = null);
    void ReserveEffects(int maximumNewChildren, int maximumNewBackings, int projectedCapacity);
    void Dispatch(int ordinal, SeekRendererCommandKind command, SeekRendererGraphSnapshot expected, Action beforeDispatch);
    void RequestDispose();
}

/// <summary>Exact inverse storage/Transform writes and independently observed
/// retirement of recognized clones. Unknown or deferred consumers stay retained.</summary>
internal interface INativeSeekRendererRestoreAccess
{
    void PrepareRestore(SeekRendererGraphSnapshot baseline);
    void AcceptRecognizedAllocations(SeekRendererGraphSnapshot observed, IReadOnlyList<SeekRendererRetiredBackingState> retiredBackings);
    IReadOnlyList<SeekRendererChildRetirement> Retirements { get; }
    void Restore(SeekRendererGraphSnapshot expected, SeekRendererGraphSnapshot baseline,
        IReadOnlyList<SeekRendererGraphSnapshot> recognized, Action beforeDispatch);
}

/// <summary>Actual explicit native commands and an immutable effects journal.
/// This is not a reversible value participant. Dispose never undoes a command,
/// and releasing this owner's GC roots never confirms Unity engine cleanup.</summary>
internal sealed partial class NativeSeekRendererCommands : IDisposable
{
    internal const int MaximumNotes = NativeSeekRuntimeNoteView.MaximumNotes;
    internal const int MaximumArrayCapacity = 65536;
    internal const int MaximumTotalCapacity = 1024 * 1024;
    private INativeSeekRendererAccess _access;
    private readonly NativeSeekRendererCommands _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly HashSet<long> _knownReferences = new();
    private Exception? _constructionFailure;
    private SeekRendererCommand[] _commands = Array.Empty<SeekRendererCommand>();
    private SeekRendererCommandEffects?[] _rows = Array.Empty<SeekRendererCommandEffects?>();
    private SeekRendererGraphSnapshot? _baseline, _current;
    private long[] _references = Array.Empty<long>();
    private int[] _ordinals = Array.Empty<int>();
    private bool _prepared, _busy, _executionEntered, _executionFinished, _disposed, _cleanupBusy, _released;
    private bool _unknown, _deferred, _retained, _notifyingDisposal;
    private int _attempted;
    private string? _failure, _cleanupFailure;
    private IReadOnlyList<SeekRendererChildRetirement> _retirements = Array.Empty<SeekRendererChildRetirement>();
    internal int AttemptedCommands => _attempted;
    internal bool SideEffectsUnknown => _unknown;
    internal bool HasRetainedResponsibility => _retained;
    internal string? CleanupFailure => _cleanupFailure;

    private NativeSeekRendererCommands(INativeSeekRendererAccess? access) { _access = access!; _registered = this; }
    internal SeekRendererEffectsJournal GetJournal()
    {
        // Journal reads are pure managed observations and may happen on a worker
        // thread. Only the owner thread may touch the live restore source; the
        // worker path returns the last managed retirement snapshot unchanged.
        if (Environment.CurrentManagedThreadId == _thread)
            _retirements = (_access as INativeSeekRendererRestoreAccess)?.Retirements ?? Array.Empty<SeekRendererChildRetirement>();
        return new(_prepared, _executionEntered, _executionFinished, _disposed, _attempted, _unknown, _deferred,
            _retained, _failure, _cleanupFailure, _baseline,
            Array.AsReadOnly(_rows.Where(row => row is not null).Select(row => row!).ToArray()), _retained ? this : null)
        { AllocationOwnership = _allocationOwnership, Retirements = _retirements };
    }
}
