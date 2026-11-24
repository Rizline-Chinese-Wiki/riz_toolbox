using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    internal static readonly (string Name, Action Run)[] TimeTargetCases =
    {
        ("Note time targets use official converted game seconds instead of raw chart coordinates or stale caches", ConvertedTimeTargets),
        ("Note time targets leave unused ordinary-note tail caches unspecified", OrdinaryTailTargets),
        ("Note time targets retain distinct actual instances sharing a chart location", MultipleTimeTargets),
        ("Note time targets are immutable snapshots and do not dispatch native writes", ImmutableTimeTargets)
    };
    private static void ConvertedTimeTargets()
    {
        using var f = new Fixture(configure: n => { n.TargetSetChart(0, 2, 2, 8); n.TargetSetTiming(0, 55, 66); });
        var p = Capture(f, 0); var r = References(f); var target = p.RuntimeTimeTargets.Single(x => x.Reference == r[0]);
        Check(target.RealTime == 1 && target.HoldEndRealTime == 4 && target.RealTime != 2 && target.HoldEndRealTime != 8,
            "Timing targets mixed cached game seconds with the raw chart domain.");
        Check(p.RequiredWork.Any(x => x.Kind == SeekTargetRequiredWorkKind.RuntimeTimeMismatch && x.Reference == r[0])
            && f.Native.TargetFloat(f.Native.TargetNote(0), "realTime") == 55 && f.Native.TargetFloat(f.Native.TargetNote(0), "holdEndRealTime") == 66,
            "Target generation overwrote stale native timing caches or silently removed unfinished work.");
        ReadOnly(f, p);
    }
    private static void OrdinaryTailTargets()
    {
        using var f = new Fixture(configure: n => n.TargetSetTiming(0, 55, float.NaN));
        var p = Capture(f, 0); var target = p.RuntimeTimeTargets.Single(x => x.Reference == References(f)[0]);
        Check(target.RealTime == 0 && target.HoldEndRealTime is null
            && float.IsNaN(f.Native.TargetFloat(f.Native.TargetNote(0), "holdEndRealTime")),
            "Ordinary-note target generation guessed or changed an unused Hold tail cache.");
        ReadOnly(f, p);
    }
    private static void MultipleTimeTargets()
    {
        using var f = new Fixture(FakeNative.NoteViewShape.MultipleSegments,
            n => { n.TargetSetChart(0, 2, 2, 8); n.TargetSetTiming(0, 55, 66); });
        var p = Capture(f, 0); var r = References(f);
        Check(p.RuntimeTimeTargets.Count == r.Length && p.RuntimeTimeTargets.Select(x => x.Reference).SequenceEqual(r)
            && r.Length == 6 && r[0] != r[5] && f.Native.TargetData(0) == f.Native.TargetData(5),
            "Timing targets lost the original actual-instance order or merged a shared chart location.");
        var first = p.RuntimeTimeTargets[0]; var second = p.RuntimeTimeTargets[5];
        Check(first.RealTime == 1 && first.HoldEndRealTime == 4 && second.RealTime == first.RealTime
            && second.HoldEndRealTime == first.HoldEndRealTime, "Shared chart conversion was not mapped onto both actual instances.");
        ReadOnly(f, p);
    }
    private static void ImmutableTimeTargets()
    {
        using var f = new Fixture(); var p = Capture(f, 0); var targets = p.RuntimeTimeTargets; var original = targets.ToArray();
        var reads = f.Native.FieldReads; var invokes = f.Native.Invocations;
        Expect<NotSupportedException>(() => ((IList<SeekRuntimeTimeTarget>)targets)[0] = targets[0] with { RealTime = 99 });
        p.Dispose();
        Check(ReferenceEquals(p.RuntimeTimeTargets, targets) && targets.SequenceEqual(original)
            && f.Native.FieldReads == reads && f.Native.Invocations == invokes && f.Native.ValueFieldWrites == 0
            && !p.CompleteSeekPermitted, "A copied timing target read used native state, changed storage or granted Seek.");
    }
}
