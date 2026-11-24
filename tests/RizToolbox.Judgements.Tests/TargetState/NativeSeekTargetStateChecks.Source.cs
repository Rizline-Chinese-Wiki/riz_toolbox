using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    private static void CaptureSource()
    {
        IntPtr fingers = IntPtr.Zero; using var f = new Fixture(configure: n => fingers = n.TargetNonNullFingers(1));
        var p = Capture(f, .5); var references = References(f);
        Check(p.NativeSeed.SequenceEqual(references) && p.Identity.Notes.Chart.Run == f.Run.Run && p.Run == f.Run.Run
            && p.Policy.CrossingHolds == SeekCrossingHoldPolicy.ContinuationRequired && p.Coordinates.ProgressSeconds == .5
            && p.Coordinates.SyncedSeconds == .5f && p.Coordinates.ChartCoordinate == 1f
            && p.Coverage.RuntimeInstanceCount == 5 && p.Coverage.CoveredLocationCount == 5,
            "The target plan borrowed a registry/chart order, lost its actual source identity or used a different seconds domain.");
        Check(p.FlagTargets.Count == 5 && p.FlagTargets.All(row => !row.IsActive && !row.Missed)
            && p.FingerTargets[0].FingerIds is null && p.FingerTargets[1].FingerIds is { Count: 0 }
            && f.Native.TargetFloat(f.Native.TargetNote(1), "realTime") == 1f
            && p.RendererCommands.All(row => row.Kind == SeekRendererCommandKind.Disappear)
            && p.CursorTarget == NativeNoteCursorState.Rewound
            && Requires(p, SeekTargetRequiredWorkKind.InputEpochClosure) && Requires(p, SeekTargetRequiredWorkKind.HoldEngineClosure)
            && Requires(p, SeekTargetRequiredWorkKind.TargetVisibilityAndGeometry) && fingers != IntPtr.Zero,
            "Practice reset outputs guessed input nullness, final visibility or completed engine closure.");
        ReadOnly(f, p); p.VerifySource(); f.Run.Dispose(); p.VerifySource();
    }
    private static void CachedWindows()
    {
        using var f = new Fixture(configure: n =>
        {
            PlainFuture(n); Plain(n, 0, .9f); Window(n, .15f, .06f, .2f);
            n.TargetSet(n.GameplayInformation, "judgeTime", .01f); n.TargetSet(n.GameplayInformation, "normalPerfectJudgeTimeInChallengeTime", .01f);
            n.TargetSet(n.GameplayInformation, "badTime", .01f);
        });
        var p = Capture(f, 1); var r = References(f);
        Check(!p.JudgeInitializationRequired && p.EffectiveWindows.JudgeTime == .15f && p.EffectiveWindows.NormalPerfectChallengeTime == .06f
            && p.EffectiveWindows.BadTime == .2f && p.ProjectedChallengeComboCount == 271
            && p.ExpiredHeads.Single().Reference == r[0] && p.ExpiredHeads.Single().Reason == SeekExpiredHeadReason.NormalChallengeWindow,
            "An initialized Judge used mutable information windows or fabricated challenge initialization/counting.");
        Queues(p.QueueTargets, r.Skip(1).ToArray(), Array.Empty<long>(), Array.Empty<long>()); ReadOnly(f, p);
    }
    private static void Initialization()
    {
        using var f = new Fixture(configure: n =>
        {
            n.TargetSet(n.JudgeOwner, "_instantiateReady", false); n.TargetInitialJudgeNotes(0, 1, 2, 3, 4);
            n.TargetSet(n.JudgeOwner, "_judgeTime", 9f); n.TargetSet(n.JudgeOwner, "_normalPerfectJudgeTimeInChallengeTime", 8f);
            n.TargetSet(n.JudgeOwner, "badTime", 7f); n.TargetSetChallenges((0, 2)); n.TargetSet(n.TargetNote(4), "inChallengeTime", true);
        });
        var p = Capture(f, 0);
        Check(p.JudgeInitializationRequired && p.EffectiveWindows.JudgeTime == .15f && p.EffectiveWindows.BadTime == .2f
            && p.EffectiveWindows.NormalPerfectChallengeTime == .06f && p.ProjectedChallengeComboCount == 3
            && Requires(p, SeekTargetRequiredWorkKind.JudgeInitialization) && Requires(p, SeekTargetRequiredWorkKind.ChallengeFlagsAndCount),
            "Uninitialized Judge projection ignored (start,time] challenge bounds, Hold weight, existing true flags or selected information windows.");
        Check(f.Native.TargetInt(f.Native.GameplayInformation, "challengeTimeComboNum") == 271
            && !f.Native.TargetBoolean(f.Native.TargetNote(1), "inChallengeTime")
            && !f.Native.TargetBoolean(f.Native.TargetNote(2), "inChallengeTime") && f.Native.TargetBoolean(f.Native.TargetNote(4), "inChallengeTime"),
            "Read-only initialization projection mutated challenge flags/count in the native source."); ReadOnly(f, p);
        InitializationMembership();
    }
    private static void InitializationMembership()
    {
        using var f = new Fixture(configure: n =>
        {
            n.TargetSet(n.JudgeOwner, "_instantiateReady", false); n.TargetInitialJudgeNotes(0); n.TargetSetChallenges((0, 2));
            n.TargetSet(n.GameplayInformation, "judgeTime", .03125f);
            n.TargetSet(n.GameplayInformation, "normalPerfectJudgeTimeInChallengeTime", .125f);
            n.TargetSet(n.GameplayInformation, "challengeCompletely", true);
        });
        var p = Capture(f, 1.0625); var hold = References(f)[2];
        Check(p.ProjectedChallengeComboCount == 0 && p.ExpiredHeads.Single(row => row.Reference == hold).Reason == SeekExpiredHeadReason.JudgeWindow
            && p.CrossingHolds.Single().HeadExpired && !f.Native.TargetBoolean(f.Native.TargetNote(2), "inChallengeTime"),
            "First-Update challenge initialization was applied to all actual notes instead of the observed active Judge.notes membership.");
        ReadOnly(f, p);
        using var inverted = new Fixture(configure: n =>
        {
            n.TargetSet(n.JudgeOwner, "_instantiateReady", false); n.TargetInitialJudgeNotes(0, 1, 2, 3, 4); n.TargetSetChallenges((2, 1));
        });
        var noMatch = Capture(inverted, 0);
        Check(noMatch.ProjectedChallengeComboCount == 0, "A finite inverted challenge interval matched a native initialization note.");
        ReadOnly(inverted, noMatch);
    }
    private static void SyncedSeconds()
    {
        using var f = new Fixture(configure: n => { PlainFuture(n); n.SeekBpmSegments(); });
        const double target = 3.00000011d; var p = Capture(f, target);
        Check(p.Coordinates.ProgressSeconds == target && BitConverter.SingleToInt32Bits(p.Coordinates.SyncedSeconds) == 0x40400000
            && p.Coordinates.ChartCoordinate == 8f && f.Native.TargetConversions.Any(row => row.Method == "RealTimeToChartTime" && row.Input == target)
            && f.Native.TargetFloat(f.Native.GameplayInformation, "nowSyncedTime") == 4321f,
            "The target reapplied progress offset/chart delay, borrowed an old nowSyncedTime or skipped the actual inverse conversion bridge."); ReadOnly(f, p);
    }
    private static void DuplicateInstances()
    {
        using var f = new Fixture(FakeNative.NoteViewShape.MultipleSegments, n =>
        {
            for (var index = 0; index < n.JudgeRuntimeReferences.Length; ++index) n.TargetSetTiming(index, 20 + index);
        });
        var p = Capture(f, 0); var r = References(f);
        Check(r[0] != r[^1] && p.NativeSeed.SequenceEqual(r) && p.Coverage.RuntimeInstanceCount == 6
            && p.Coverage.CoveredLocationCount == 5 && p.Coverage.MultipleInstanceLocations.Count == 1
            && p.FlagTargets.Count == 6 && p.FingerTargets.Count == 6 && p.RendererCommands.Count == 6
            && p.NativeSorted.Contains(r[0]) && p.NativeSorted.Contains(r[^1])
            && Requires(p, SeekTargetRequiredWorkKind.RuntimeTimeMismatch, r[0]), "Two actual segment instances were collapsed into one chart-location target or actual times were replaced by chart conversion.");
        Apply(f, p.QueueTargets);
    }
    private static void MissingInstances()
    {
        using var f = new Fixture(FakeNative.NoteViewShape.Partial); var p = Capture(f, 0); var r = References(f);
        var missing = p.RequiredWork.Where(row => row.Kind == SeekTargetRequiredWorkKind.MissingRuntimeInstance).ToArray();
        Check(p.Coverage.RuntimeInstanceCount == 4 && p.Coverage.MissingLocations.Single() == new ChartNoteLocation(1, 1)
            && missing.Length == 1 && missing[0].Location == new ChartNoteLocation(1, 1) && missing[0].Reference is null
            && p.FlagTargets.Count == 4 && p.NativeSeed.SequenceEqual(r) && p.NativeSorted.All(r.Contains),
            "A chart-only missing location was silently completed or given a fabricated runtime reference."); ReadOnly(f, p);
    }
}
