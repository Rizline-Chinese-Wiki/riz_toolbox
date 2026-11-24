namespace RizToolbox.Sdk;

public static class GameplayCapabilityIds
{
    public const string Clock = "riz.gameplay.clock";
    public const string Controls = "riz.gameplay.controls";
}
public enum GameplayClockUnit { Unknown, RawGameUnits, Seconds, ChartCoordinates }
public sealed record GameplayClockReading(bool Available, double? Value, GameplayClockUnit Unit, string Source, OperationError? Error = null)
{
    /// <summary>Unit provenance is separate from runtime calibration or synchronization.</summary>
    public string? UnitEvidence { get; init; }
}
public sealed partial record GameplayClockSnapshot(long Revision, long ControllerGeneration, string? ObservedRunId,
    double CaptureStartedMonotonicSeconds, double CaptureFinishedMonotonicSeconds,
    GameplayClockReading GameTime, GameplayClockReading GameRealTime, GameplayClockReading GameSyncedTime,
    GameplayClockReading AudioDspTime, GameplayClockReading HostMonotonicTime, bool SecondsMappingVerified = false)
{
    public GameplayClockMappingSnapshot? Mapping { get; init; }
}

/// <summary>Current chart conversion and progress state. The capture interval bounds a sample correlation only;
/// any observed rates apply only to ObservedInterval; they never authorize clock extrapolation.</summary>
public sealed partial record GameplayClockMappingSnapshot(GameplayClockReading GameTimeInSeconds,
    GameplayClockReading ProgressTimeInSeconds, GameplayClockReading AppliedOffsetSeconds,
    GameplayClockReading ChartDelaySeconds, bool? Playing, bool? Paused, string Evidence,
    bool RuntimeVerified = false)
{
    public string? BuildFingerprint { get; init; }
    public double CaptureStartedMonotonicSeconds { get; init; }
    public double CaptureFinishedMonotonicSeconds { get; init; }
    public double? AudioDspSampleSeconds { get; init; }
    public double? HostMonotonicSampleSeconds { get; init; }
    public double? GameSecondsPerDspSecond { get; init; }
    public double? GameSecondsPerHostSecond { get; init; }
    public double? LatencySeconds { get; init; }
}

/// <summary>Seconds denotes the game's progress-time domain, never host or DSP absolute time.</summary>
public sealed record GameplayTimeConversionRequest(long ControllerGeneration, string ObservedRunId,
    double Value, GameplayClockUnit SourceUnit, GameplayClockUnit TargetUnit);
public sealed record GameplayTimeConversion(GameplayTimeConversionRequest Request, GameplayClockReading Converted,
    string Evidence, bool RuntimeVerified = false);

public interface IGameplayClockService
{
    Result<GameplayClockSnapshot> Read(CancellationToken cancellationToken = default,
        GameplayClockObservationPolicy? observationPolicy = null);
    Result<GameplayTimeConversion> Convert(GameplayTimeConversionRequest request, CancellationToken cancellationToken = default);
    Result<GameplayObservedClockConversion> ConvertObserved(GameplayObservedClockConversionRequest request,
        CancellationToken cancellationToken = default);
}

public enum GameplayControlAction { Pause, Resume, Retry, Skip, SetSpeed, Seek }
public enum GameplayControlValueUnit { None, PlaybackRateMultiplier, GameSeconds, ChartCoordinates }
/// <summary>Every explicit request targets one observed run and controller generation. SetSpeed requires a
/// positive finite playback-rate multiplier; Seek requires a finite position with an explicit game-time unit.
/// The backend validates its supported ranges and prepares all affected audio/game state before committing.</summary>
public sealed record GameplayControlRequest(GameplayControlAction Action, long ControllerGeneration,
    string? ObservedRunId, double? Value = null, GameplayControlValueUnit Unit = GameplayControlValueUnit.None);
public enum GameplayControlTransactionState { Dispatched, Confirmed, Restored, SideEffectsUnknown }
public sealed record GameplayControlAvailability(GameplayControlAction Action, bool Supported, string Evidence, OperationError? Error = null)
{
    public IReadOnlyList<GameplayControlValueUnit> ValueUnits { get; init; } = Array.Empty<GameplayControlValueUnit>();
    /// <summary>Game-state preconditions sampled for this action. Null when the operation is
    /// unsupported or no controller state was published. Execution checks the live state again.</summary>
    public Result? StateReadiness { get; init; }
}
public sealed record GameplayControlSnapshot(long ControllerGeneration, string? ObservedRunId, bool ControllerAvailable,
    bool LoadOver, bool TimeReady, bool Playing, bool Paused, bool LevelOver, bool Crashed, bool CanDoOperation,
    IReadOnlyList<GameplayControlAvailability> Operations, IReadOnlyList<GameplayModification> Modifications,
    string? CurrentOwner = null)
{
    public long ObservationRevision { get; init; }
    public long HostTickSequence { get; init; }
    public double ObservedAtMonotonicSeconds { get; init; }
}
public sealed record GameplayModification(string Owner, GameplayControlAction Action, long ControllerGeneration,
    string? ObservedRunId, double MonotonicSeconds, bool Dispatched, string Outcome,
    bool LocalResultBehaviorVerified = false, bool SubmissionBehaviorVerified = false)
{
    public GameplayControlRequest? Request { get; init; }
    public GameplayControlTransactionState TransactionState { get; init; }
    public double? ConfirmedValue { get; init; }
    public GameplayControlValueUnit ConfirmedUnit { get; init; }
}
public sealed record GameplayControlReceipt(GameplayModification Modification, bool? ImmediatePostconditionObserved,
    string Evidence, bool Experimental = true);

public enum GameplayControlCommandState { Queued, Executing, Succeeded, Rejected, Cancelled, Unknown }

/// <summary>Immutable progress of one request. The receipt keeps the action's exact dispatch or confirmation evidence.</summary>
public sealed record GameplayControlCommandSnapshot(Guid Id, GameplayControlRequest Request, GameplayControlCommandState State,
    GameplayControlReceipt? Receipt = null, OperationError? Error = null);

public interface IGameplayControlCommand
{
    /// <summary>Reads the published record without accessing the game. Terminal records remain readable after scope expiry.</summary>
    GameplayControlCommandSnapshot Snapshot { get; }

    /// <summary>Cancel on the host thread before execution begins. An executing command reports Conflict.</summary>
    Result Cancel();
}

/// <summary>A sampled, read-only control observation. GetState reads the host's latest publication.
/// Operations contains the sampled per-action availability even when the controller/run state is unavailable;
/// a failed probe is reported separately and leaves Operations empty. Support never grants a control lease.</summary>
public sealed record GameplayControlObservationSnapshot(long Revision, GameplayControlSnapshot? State,
    IReadOnlyList<GameplayControlAvailability> Operations, OperationError? Error = null)
{
    public OperationError? ProbeError { get; init; }
    public long HostTickSequence { get; init; }
    public double ObservedAtMonotonicSeconds { get; init; }
}

/// <summary>InitialSnapshot and registration are atomic on the verified main thread. Before the first host
/// publication InitialSnapshot reports NotReady. Callbacks contain only
/// later semantic revisions sampled after playback maintenance; intermediate states can be missed.
/// The current plugin scope owns this subscription. Cancellation stops later delivery; removal occurs on
/// the main thread. Dispose is idempotent and never acquires control.</summary>
public interface IGameplayControlSubscription : IDisposable
{
    GameplayControlObservationSnapshot InitialSnapshot { get; }
}

/// <summary>Explicit experimental, exclusive operation ownership. Dispose releases access; it does not undo Retry/Skip or auto-resume.</summary>
public interface IGameplayControlLease : IDisposable
{
    long ControllerGeneration { get; }
    string? ObservedRunId { get; }
    /// <summary>Queues one command for the host command phase. Scope expiry or token cancellation before dispatch prevents execution.</summary>
    Result<IGameplayControlCommand> Submit(GameplayControlRequest request, CancellationToken cancellationToken = default);
    Result<GameplayControlReceipt> Execute(GameplayControlRequest request, CancellationToken cancellationToken = default);
    /// <summary>Convenience for the valueless Pause/Resume/Retry/Skip commands. Speed/Seek require an explicit request.</summary>
    Result<GameplayControlReceipt> Execute(GameplayControlAction action, CancellationToken cancellationToken = default);
}
public interface IGameplayControlService
{
    /// <summary>Read the latest host sample or its availability error. This does not acquire control.</summary>
    Result<GameplayControlSnapshot> GetState(CancellationToken cancellationToken = default);
    Result<IGameplayControlSubscription> Subscribe(Action<GameplayControlObservationSnapshot> handler,
        CancellationToken cancellationToken = default);
    Result<IGameplayControlLease> Acquire(bool allowExperimental = false, CancellationToken cancellationToken = default);
}

/// <summary>Observed request with an explicit runtime note identity. Raw arguments
/// are preserved; sentinel values are not time offsets or score acceptance.</summary>
public sealed record GameplayJudgementObservation(long Invocation, int RawJudgeType, float RawDelta,
    string? NoteId, string IdentitySource, GameplayClockUnit DeltaUnit, string Evidence)
{
    public GameplayRuntimeNoteId? RuntimeNote { get; init; }
    public GameplayChartNoteLocation? ChartNote { get; init; }
    public JudgementNoteAssociation ChartAssociation { get; init; }
    public JudgementDeltaKind DeltaKind { get; init; }
    public string SourceId { get; init; } = "unknown";
    public long SceneGeneration { get; init; }
    public long LifecycleRevisionAtEntry { get; init; }
    public bool OriginalReturned { get; init; }
    public GameplayJudgementCounters? CountersBefore { get; init; }
    public GameplayJudgementCounters? CountersAfter { get; init; }
}
