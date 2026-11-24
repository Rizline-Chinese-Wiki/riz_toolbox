namespace RizToolbox.Sdk;

public static class JudgementCapabilityIds { public const string Observe = "riz.gameplay.judgements"; }
public enum JudgementDeltaKind { Unknown, MeasuredTimeDifference, Sentinel }
public enum JudgementNoteAssociation { Missing, Unique, Ambiguous }
public sealed record GameplayRuntimeNoteId(string SessionId, string ObservedRunId, long Sequence);
public sealed record GameplayChartNoteLocation(int LineOrdinal, int NoteOrdinal);
public sealed record GameplayJudgementCounters(int Bad, int Miss, int Perfect, int Early, int Late,
    int MaxPerfect, int ChallengeTimeMiss);
public sealed record JudgementSourceStatus(string Id, JudgementDeltaKind DeltaKind, long CapturedCalls);
public sealed record JudgementObserverStatus(bool Installed, bool RunIdentityAvailable, string? ObservedRunId,
    long Observations, long UnassociatedCalls, IReadOnlyList<JudgementSourceStatus> Sources, string? UnavailableReason);

/// <summary>Read-only, synchronous requests observed at the real NormalMode method
/// within a verified retained note scope. Original return and counter snapshots
/// are separate facts; a request is not a promise of score acceptance.</summary>
public interface IGameplayJudgementService
{
    Result<JudgementObserverStatus> GetStatus();
    Result<IDisposable> Subscribe(Action<GameplayJudgementObservation> observer, int priority = 0);
}
