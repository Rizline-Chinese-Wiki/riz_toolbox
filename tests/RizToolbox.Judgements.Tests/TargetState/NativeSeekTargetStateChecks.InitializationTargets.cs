using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    internal static readonly (string Name, Action Run)[] InitializationTargetCases =
    {
        ("Judge initialization targets retain all actual flags and native OR occurrence counting", ExplicitInitializationTargets),
        ("Judge initialization targets preserve initialized cached windows and signed combo", InitializedTargets),
        ("Judge initialization targets use actual initialization membership rather than all notes", InitializationTargetMembership),
        ("Judge initialization targets remain immutable without native reads or implied write admission", ImmutableInitializationTargets)
    };
    private static void ExplicitInitializationTargets()
    {
        using var f = new Fixture(configure: n =>
        {
            n.TargetSet(n.JudgeOwner, "_instantiateReady", false); n.TargetInitialJudgeNotes(0, 1, 2, 3, 4);
            n.TargetSet(n.JudgeOwner, "_judgeTime", 9f); n.TargetSetChallenges((0, 2));
            n.TargetSet(n.TargetNote(4), "inChallengeTime", true);
        });
        var plan = Capture(f, 0); var target = plan.InitializationTargets; var r = References(f);
        Check(target.RequiresInitialization && target.Windows.JudgeTime == .15f && target.ChallengeComboCount == 3,
            "Explicit initialization targets lost information windows or native Hold weighted OR counting.");
        Check(target.Notes.Select(x => x.Reference).SequenceEqual(r)
            && target.Notes.Select(x => x.InChallengeTime).SequenceEqual(new[] { false, true, true, false, true }),
            "Initialization targets omitted an actual note or cleared an existing true challenge flag.");
        Check(f.Native.TargetInt(f.Native.GameplayInformation, "challengeTimeComboNum") == 271
            && !f.Native.TargetBoolean(f.Native.TargetNote(2), "inChallengeTime") && !plan.CompleteSeekPermitted,
            "Target generation wrote native fields or granted complete Seek.");
        ReadOnly(f, plan);
    }
    private static void InitializedTargets()
    {
        using var f = new Fixture(configure: n =>
        {
            n.TargetSet(n.JudgeOwner, "_judgeTime", .03125f);
            n.TargetSet(n.GameplayInformation, "judgeTime", 8f);
            n.TargetSet(n.GameplayInformation, "challengeTimeComboNum", int.MinValue);
            n.TargetSet(n.TargetNote(0), "inChallengeTime", true);
        });
        var plan = Capture(f, 0); var target = plan.InitializationTargets;
        Check(!target.RequiresInitialization && target.Windows.JudgeTime == .03125f && target.ChallengeComboCount == int.MinValue
            && target.Notes[0].InChallengeTime && target.Notes.Skip(1).All(x => !x.InChallengeTime),
            "Initialized targets recalculated flags/count or replaced cached windows with mutable information defaults.");
        ReadOnly(f, plan);
    }
    private static void InitializationTargetMembership()
    {
        using var f = new Fixture(configure: n =>
        {
            n.TargetSet(n.JudgeOwner, "_instantiateReady", false); n.TargetInitialJudgeNotes(1, 1, 2);
            n.TargetSetChallenges((0, 2), (0, 2));
        });
        var plan = Capture(f, 0); var target = plan.InitializationTargets;
        Check(target.ChallengeComboCount == 3
            && target.Notes.Select(x => x.InChallengeTime).SequenceEqual(new[] { false, true, true, false, false }),
            "Duplicate initialization occurrences or overlapping intervals counted an already true flag twice.");
        using var outside = new Fixture(configure: n =>
        { n.TargetSet(n.JudgeOwner, "_instantiateReady", false); n.TargetInitialJudgeNotes(0); n.TargetSetChallenges((0, 2)); });
        var other = Capture(outside, 0).InitializationTargets;
        Check(other.ChallengeComboCount == 0 && other.Notes.All(x => !x.InChallengeTime),
            "Challenge initialization substituted all actual notes for the observed initialization List.");
        ReadOnly(f, plan);
    }
    private static void ImmutableInitializationTargets()
    {
        using var f = new Fixture(); var plan = Capture(f, 0); var target = plan.InitializationTargets;
        var rows = target.Notes.ToArray(); var reads = f.Native.FieldReads; var invokes = f.Native.Invocations;
        var list = (IList<SeekChallengeFlagTarget>)target.Notes;
        Expect<NotSupportedException>(() => list[0] = list[0] with { InChallengeTime = !list[0].InChallengeTime });
        plan.Dispose();
        Check(ReferenceEquals(plan.InitializationTargets, target) && target.Notes.SequenceEqual(rows)
            && f.Native.FieldReads == reads && f.Native.Invocations == invokes,
            "An immutable target read after disposal touched native state or changed the copied snapshot.");
    }
}
