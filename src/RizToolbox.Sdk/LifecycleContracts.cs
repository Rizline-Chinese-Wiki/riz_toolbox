namespace RizToolbox.Sdk;

public static class GameLifecycleCapabilityIds
{
    public const string Lifecycle = "riz.lifecycle.observe";
    /// <summary>Experimental direct observation of accepted Retry cancellation on a pinned native build.</summary>
    public const string RetryCancellation = "riz.lifecycle.retry-cancellation.observe";
    /// <summary>Experimental direct observation of accepted Back followed by its terminal native Update edge.</summary>
    public const string BackCancellation = "riz.lifecycle.back-cancellation.observe";
    /// <summary>Experimental direct observation of accepted Skip and its verified native termination side effect.</summary>
    public const string SkipTermination = "riz.lifecycle.skip-termination.observe";
    /// <summary>Experimental provenance of actual original ControllerOver results on a pinned native build.</summary>
    public const string ResultOrigin = "riz.lifecycle.result-origin.observe";
}

public enum RunLifecyclePhase { Unknown, Loading, Playing, Paused, AwaitingResult, Finished, ObservationLost }
public enum GameLifecycleChangeKind { SnapshotChanged, SceneChanged, RunStarted, Paused, Resumed, RunEnded, ObservationChanged, ResultObserved }
public enum RunEndReason { Unknown, Completed, Failed, Skipped, Aborted }
public enum LifecycleObservationSource { Unknown, SampledGameState, VerifiedCallback }

/// <summary>ObservedAt is the framework observation time, not an exact original game callback timestamp.
/// A 250 ms sampler can miss states that begin and end between samples.</summary>
public sealed record LifecycleObservation(DateTimeOffset ObservedAtUtc, LifecycleObservationSource Source,
    int SamplingIntervalMilliseconds, string Evidence);

public sealed record GameLifecycleSnapshot(string SessionId, long Revision, long SceneGeneration, string Scene,
    string? RunId, string? ChartId, RunLifecyclePhase Phase, bool? Paused,
    bool ScenesAvailable, bool RunsAvailable, bool PauseAvailable, IReadOnlyList<OperationError> Limitations);

/// <summary>Result.RunId is null when no observed start can be reliably associated with that result.</summary>
public sealed record GameLifecycleChange(GameLifecycleChangeKind Kind, RunLifecyclePhase PreviousPhase,
    GameLifecycleSnapshot Snapshot, LifecycleObservation Observation, RunEndReason? EndReason = null,
    RunFinished? Result = null)
{
    /// <summary>A current live run in Snapshot is not automatically the owner of an unassociated ResultObserved.</summary>
    public string? RelatedRunId => Result is null ? Snapshot.RunId : Result.RunId;
}

/// <summary>InitialSnapshot and registration are atomic on the verified main thread. Callbacks contain strictly later revisions.
/// Dispose is idempotent; plugin views automatically own subscriptions for the current activation.</summary>
public interface IGameLifecycleSubscription : IDisposable
{
    GameLifecycleSnapshot InitialSnapshot { get; }
}

/// <summary>Read-only lifecycle observation on the verified main thread. Does not synthesize cancellation from scene changes,
/// create historical starts from results, or claim sampling is an exact note/input callback stream.</summary>
public interface IGameLifecycleService
{
    Result<GameLifecycleSnapshot> GetSnapshot();
    Result<IGameLifecycleSubscription> Subscribe(Action<GameLifecycleChange> handler);
}
