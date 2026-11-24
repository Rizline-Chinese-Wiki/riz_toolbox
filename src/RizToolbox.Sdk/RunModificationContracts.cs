namespace RizToolbox.Sdk;

public static class RunModificationCapabilityIds { public const string Read = "riz.gameplay.modifications"; }
public enum RunModificationCategory { Unknown, GameplayControl, Audio, LocalContent }
public enum RunModificationOutcome
{
    Unknown, Applied, Restored, InvocationFailedSideEffectsUnknown, EffectActivated, EffectCarriedIntoRun,
    /// <summary>The tracking lease ended. This does not assert baseline restoration or removal from a past result.</summary>
    EffectReleased
}
public enum RunModificationAssociation { Unassociated, CurrentObservedRun, ExplicitObservedRun, ActiveEffectAtRunStart }
/// <summary>Immutable observation. RunId is null unless a real lifecycle identity can be associated safely.</summary>
public sealed record RunModificationRecord(long Sequence, string SessionId, string? RunId, long LifecycleRevision,
    long SceneGeneration, string Owner, string Source, string Operation, string TargetId,
    RunModificationCategory Category, RunModificationOutcome Outcome, RunModificationAssociation Association,
    string Detail, double MonotonicSeconds, string? EffectId = null, string? ReportedRunId = null,
    string? ReportedSessionId = null, long? ReportedLifecycleRevision = null,
    bool LocalResultBehaviorVerified = false, bool SubmissionBehaviorVerified = false);
/// <summary>A closed run can still record the release of an effect already associated with that run.</summary>
public sealed record RunModificationRunInfo(string SessionId, string RunId, string? ChartId,
    long? StartRevision, long LastObservedRevision, long? EndRevision, RunEndReason? EndReason,
    bool ClosedToNewEntries, int RetainedEntries, long EvictedEntries);
public sealed record RunModificationActiveEffect(string EffectId, string Owner, string Source, string Operation,
    string TargetId, RunModificationCategory Category, string Detail, long ActivatedLifecycleRevision,
    bool LocalResultBehaviorVerified, bool SubmissionBehaviorVerified);
public sealed record RunModificationCounters(long Recorded, long Unassociated, long DroppedInvalid,
    long DroppedReentrant, long DroppedExpired, long DroppedCapacity, long DroppedStaleLifecycle,
    long RejectedWrongThread, long EvictedRecords, long EvictedRuns, long InternalFailures);
public sealed record RunModificationQuery(string? RunId = null, string? Owner = null, string? Source = null,
    int MaximumEntries = 256);
public sealed record RunModificationJournalSnapshot(long Revision, GameLifecycleSnapshot Lifecycle,
    IReadOnlyList<RunModificationRecord> Records, IReadOnlyList<RunModificationRunInfo> Runs,
    IReadOnlyList<RunModificationActiveEffect> ActiveEffects, RunModificationCounters Counters, bool Truncated);
/// <summary>Main-thread, scope-bound read-only audit. No SDK write-record endpoint or pointer is exposed.</summary>
public interface IRunModificationService
{
    Result<RunModificationJournalSnapshot> Read(RunModificationQuery? query = null);
}
