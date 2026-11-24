using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekCanvasPositionTarget(int Ordinal, long Transform, NativeVector3 LocalPosition);

/// <summary>The callback is the final admission immediately before entering the
/// exact Unity localPosition setter. RequestDispose revokes logical use only;
/// physical roots belong to Dispose and must survive an active callback.</summary>
internal interface INativeSeekCanvasPositionAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifySource();
    void BindLifetimeGuard(Action guard);
    NativeSeekCanvasTargetCapture SourceCapture { get; }
    IDisposable EnterOperation();
    IReadOnlyList<SeekCanvasPositionTarget> Targets { get; }
    IReadOnlyList<NativeVector3> ReadSnapshot();
    NativeVector3 ReadSlot(int ordinal);
    void WriteSlot(int ordinal, NativeVector3 value, NativeVector3 expected, Action beforeDispatch);
    void RequestDispose();
}

internal enum SeekCanvasPositionDirection { Apply, Restore }

/// <summary>One selected setter attempt. OriginalObserved compares the independent
/// baseline; Before is the exact conditional pre-state for this direction.</summary>
internal sealed record SeekCanvasPositionCommandEffects(int Sequence, SeekCanvasPositionDirection Direction,
    int Ordinal, long Transform, NativeVector3 Before, NativeVector3 Target,
    bool NativeEntered, bool NativeReturned, NativeVector3? Readback,
    bool TargetObserved, bool OriginalObserved, bool ExternalEffectsUnclosed,
    bool SideEffectsUnknown, string? Failure);

internal sealed record SeekCanvasPositionEffectsJournal(bool Prepared, bool ExecutionEntered,
    bool ExecutionFinished, bool AppliedValuesVerified, bool RestorationEntered,
    bool RestorationFinished, bool RestoredValuesVerified, bool Disposed,
    int AttemptedWrites, int AttemptedRestores, bool UnrecognizedState, bool SideEffectsUnknown,
    bool ExternalEffectsUnclosed, bool HasRetainedResponsibility, string? Failure, string? CleanupFailure,
    IReadOnlyList<SeekCanvasPositionTarget> Targets, IReadOnlyList<SeekCanvasPositionTarget> UniqueTargets,
    IReadOnlyList<NativeVector3> Baseline, IReadOnlyList<NativeVector3> Expected,
    IReadOnlyList<SeekCanvasPositionCommandEffects> Commands, object? EffectsOwner)
{
    internal bool CompleteSeekPermitted => false;
    internal NativeSeekCanvasTargetCapture? SourceCapture { get; init; }
    internal object? SourceCleanupOwner { get; init; }
}

/// <summary>Explicit actual Canvas Transform position commands. Selected raw-bit
/// value observations and conditional inverses do not close Unity callbacks or
/// provide complete Seek admission. This deliberately is not a value participant.</summary>
internal sealed partial class NativeSeekCanvasPositionCommands : IDisposable
{
    internal const int MaximumTargets = 65536;
    internal const int MaximumUnclosedOwners = 256;
    private const string CleanupOwnerKey = "SeekCanvasPositionCleanupOwner";
    private const string CleanupFailureKey = "SeekCanvasPositionCleanupFailure";
    private const string EffectsOwnerKey = "SeekCanvasPositionEffectsOwner";
    private const string SourceCleanupOwnerKey = "SeekCanvasPositionSourceCleanupOwner";
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekCanvasPositionCommands> Owners = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NativeSeekCanvasPositionCommands _registered;
    private INativeSeekCanvasPositionAccess _access = null!;
    private NativeSeekCanvasTargetCapture? _sourceCapture;
    private SeekCanvasPositionTarget[] _targets = Array.Empty<SeekCanvasPositionTarget>();
    private SeekCanvasPositionTarget[] _uniqueTargets = Array.Empty<SeekCanvasPositionTarget>();
    private NativeVector3[] _baseline = Array.Empty<NativeVector3>(), _expected = Array.Empty<NativeVector3>();
    private int[] _firstOrdinals = Array.Empty<int>();
    private readonly List<SeekCanvasPositionCommandEffects> _commands = new();
    private readonly List<int> _enteredOrdinals = new();
    private int _attemptedWrites, _attemptedRestores, _pendingCommand = -1;
    private bool _prepared, _executionEntered, _executionFinished, _appliedValuesVerified;
    private bool _restorationEntered, _restorationFinished, _restoredValuesVerified;
    private bool _busy, _disposed, _cleanupBusy, _cleanupAttempted, _released;
    private bool _notifyingDisposal, _disposalNotified, _unknown, _externalEffects, _unrecognized;
    private bool _retained, _ownerReserved;
    private bool _preparationEntered;
    private long _observationRevision;
    private Exception? _constructionFailure, _cleanupException;
    private object? _sourceCleanupOwner;
    private string? _failure, _cleanupFailure;

    private NativeSeekCanvasPositionCommands() { _registered = this; }

    internal bool CompleteSeekPermitted => false;
    internal bool SideEffectsUnknown { get { DiagnosticEntry(); return _unknown; } }
    internal bool HasRetainedResponsibility { get { DiagnosticEntry(); return _retained; } }
    internal long ObservationRevision { get { DiagnosticEntry(); return _observationRevision; } }
    internal NativeSeekCanvasTargetCapture SourceCapture
    {
        get
        {
            DiagnosticEntry();
            return _sourceCapture ?? throw new InvalidOperationException("The actual Canvas position owner has no attached source capture.");
        }
    }

    internal SeekCanvasPositionEffectsJournal GetJournal()
    {
        DiagnosticEntry();
        return new(_prepared, _executionEntered, _executionFinished, _appliedValuesVerified,
            _restorationEntered, _restorationFinished, _restoredValuesVerified, _disposed,
            _attemptedWrites, _attemptedRestores, _unrecognized, _unknown, _externalEffects, _retained,
            _failure, _cleanupFailure,
            Array.AsReadOnly(_targets.Select(row => row with { }).ToArray()),
            Array.AsReadOnly(_uniqueTargets.Select(row => row with { }).ToArray()),
            Array.AsReadOnly(_baseline.ToArray()), Array.AsReadOnly(_expected.ToArray()),
            Array.AsReadOnly(_commands.Select(row => row with { }).ToArray()), _retained ? this : null)
        { SourceCapture = _sourceCapture, SourceCleanupOwner = _sourceCleanupOwner };
    }
}
