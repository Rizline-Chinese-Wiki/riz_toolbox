using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Progress/game seconds are the ChartTimeToRealTime domain. They are
/// not raw media/GetTime seconds: native playback subtracts its applied offset
/// before progress time. No additional chart delay is applied by this plan.</summary>
internal sealed record SeekTargetCoordinates(double ProgressSeconds, float SyncedSeconds, float ChartCoordinate);
internal sealed record SeekTargetSourceIdentity(SeekRuntimeNoteSourceIdentity Notes, long NoteJudgeControl);
internal sealed record SeekTargetWindows(float JudgeTime, float MaxPerfectChallengeTime, float NormalPerfectChallengeTime, float BadTime);
internal sealed record SeekTargetChallengeInterval(long Reference, float Start, float End);
internal sealed record SeekChallengeFlagTarget(long Reference, bool InChallengeTime);
/// <summary>Official converted game-seconds cache targets. A null tail is
/// deliberately not a write request for an ordinary note unused Hold cache.</summary>
internal sealed record SeekRuntimeTimeTarget(long Reference, float RealTime, float? HoldEndRealTime);
/// <summary>Explicit immutable cache/count/flag targets only. RequiresInitialization
/// does not assert that a native ready flag or any other storage was written.</summary>
internal sealed record SeekJudgeInitializationTargets(bool RequiresInitialization, SeekTargetWindows Windows,
    int ChallengeComboCount, IReadOnlyList<SeekChallengeFlagTarget> Notes);
internal sealed record SeekTargetNoteState(long Reference, long NoteData, int RawType, float ChartTime,
    float RealTime, float HoldEndRealTime, bool InChallengeTime, bool IsActive, bool Destroyed, bool Missed, long FingerIds);
internal sealed record SeekTargetRawState(SeekTargetSourceIdentity Identity, bool InstantiateReady,
    SeekTargetWindows CachedWindows, SeekTargetWindows InformationWindows, bool ChallengeCompletely,
    bool AutoPlay, bool FreeHold, bool InstantiateBadHit, int ChallengeComboCount, long ChallengeTimes,
    IReadOnlyList<SeekTargetChallengeInterval> ChallengeIntervals, IReadOnlyList<SeekTargetNoteState> Notes,
    long InitializationNotesList, long InitializationNotesBacking, int InitializationNotesVersion,
    IReadOnlyList<long> InitializationNotes);

internal enum SeekCrossingHoldPolicy { ContinuationRequired, Skip }
/// <summary>Explicit new practice epoch: discard native-expired heads without
/// historical score/events; rearm other heads, reset three flags and clear
/// observed input members while preserving field nullness. Visibility is a
/// subsequent native geometry/activation task, not a time comparison.</summary>
internal sealed record SeekPracticePolicy(SeekCrossingHoldPolicy CrossingHolds = SeekCrossingHoldPolicy.ContinuationRequired);
internal enum SeekExpiredHeadQueue { Tap, Drag }
[Flags] internal enum SeekExpiredHeadReason { NormalChallengeWindow = 1, JudgeWindow = 2 }
internal sealed record SeekExpiredHeadOccurrence(long Reference, SeekExpiredHeadQueue Queue, int WaitingOrdinal, SeekExpiredHeadReason Reason);
internal enum SeekCrossingHoldDisposition { HeadWindowOpen, ContinuationRequired, SkippedByPolicy }
internal sealed record SeekCrossingHoldTarget(long Reference, ChartNoteLocation Location, float HeadChartTime,
    float EndChartTime, bool HeadExpired, SeekCrossingHoldDisposition Disposition);
internal enum SeekTargetRequiredWorkKind
{
    JudgeInitialization, ChallengeFlagsAndCount, InputEpochClosure, HoldEngineClosure,
    HoldContinuation, HoldGeometry, TargetVisibilityAndGeometry, MissingRuntimeInstance,
    RuntimeTimeMismatch, ExplicitlySkippedCrossingHold
}
internal sealed record SeekTargetRequiredWork(SeekTargetRequiredWorkKind Kind, long? Reference,
    ChartNoteLocation? Location, string Evidence);

/// <summary>Owns one complete NoteView and an independent ChartPlan/BPM reader
/// captured for its Coordinates. VerifyAccess verifies both actual sources and
/// ready-paused identity; VerifyLocal is pure managed. Disposal retains unknown
/// native frees without retry. Neither owned object is borrowed from a caller.</summary>
internal interface IRetainedSeekTargetReader : IDisposable
{
    void VerifyOwnerThread();
    void VerifyAccess();
    void BindLifetimeGuard(Action guard);
    SeekTargetCoordinates Coordinates { get; }
    NativeSeekChartPlan ChartPlan { get; }
    NativeSeekRuntimeNoteView NoteView { get; }
    SeekTargetRawState ReadSnapshot();
    void VerifyLocal(SeekTargetSourceIdentity expected);
    void RequestDispose();
}

/// <summary>Executable managed target evaluation over an independently retained
/// actual source. Outputs feed the existing explicit writers; requirements and
/// reset commands never authorize full Seek, Hold continuation or visibility.</summary>
internal sealed partial class NativeSeekTargetStatePlan : IDisposable
{
    internal const int MaximumNotes = NativeSeekRuntimeNoteView.MaximumNotes;
    internal const int MaximumChallengeIntervals = 65536;
    private readonly IRetainedSeekTargetReader _source;
    private SeekTargetRawState _baseline = null!;
    private bool _busy, _disposed, _released, _releasing, _notifyingDisposal, _faulted;
    private readonly List<Exception> _cleanupFailures = new();
    internal SeekTargetCoordinates Coordinates { get; private set; } = null!;
    internal SeekTargetSourceIdentity Identity => _baseline.Identity;
    internal ObservedJudgementRun Run => Identity.Notes.Chart.Run;
    internal SeekPracticePolicy Policy { get; private set; } = null!;
    internal SeekTargetWindows EffectiveWindows { get; private set; } = null!;
    internal bool JudgeInitializationRequired => !_baseline.InstantiateReady;
    internal int ProjectedChallengeComboCount { get; private set; }
    internal SeekJudgeInitializationTargets InitializationTargets { get; private set; } = null!;
    internal IReadOnlyList<SeekRuntimeTimeTarget> RuntimeTimeTargets { get; private set; } = Array.Empty<SeekRuntimeTimeTarget>();
    internal IReadOnlyList<long> NativeSeed { get; private set; } = Array.Empty<long>();
    internal IReadOnlyList<long> NativeSorted { get; private set; } = Array.Empty<long>();
    internal SeekJudgeQueueTargets NativeQueueProjection { get; private set; } = null!;
    internal SeekJudgeQueueTargets QueueTargets { get; private set; } = null!;
    internal IReadOnlyList<SeekExpiredHeadOccurrence> ExpiredHeads { get; private set; } = Array.Empty<SeekExpiredHeadOccurrence>();
    internal IReadOnlyList<SeekCrossingHoldTarget> CrossingHolds { get; private set; } = Array.Empty<SeekCrossingHoldTarget>();
    internal IReadOnlyList<SeekNoteFlagTarget> FlagTargets { get; private set; } = Array.Empty<SeekNoteFlagTarget>();
    internal IReadOnlyList<SeekFingerIdsTarget> FingerTargets { get; private set; } = Array.Empty<SeekFingerIdsTarget>();
    internal IReadOnlyList<SeekRendererCommand> RendererCommands { get; private set; } = Array.Empty<SeekRendererCommand>();
    internal NativeNoteCursorState CursorTarget => NativeNoteCursorState.Rewound;
    internal IReadOnlyList<SeekTargetRequiredWork> RequiredWork { get; private set; } = Array.Empty<SeekTargetRequiredWork>();
    internal SeekRuntimeNoteCoverageReport Coverage { get; private set; } = null!;
    internal long SortComparisons { get; private set; }
    internal bool CompleteSeekPermitted => false;
    internal string? CleanupFailure { get; private set; }
    private NativeSeekTargetStatePlan(IRetainedSeekTargetReader source) => _source = source;
}
