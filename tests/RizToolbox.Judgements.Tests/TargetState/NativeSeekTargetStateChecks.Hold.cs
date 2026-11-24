using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    private static void CrossingHold()
    {
        foreach (var target in new[] { 1.03125, 1.25 })
        {
            using var f = new Fixture(); var p = Capture(f, target); var hold = References(f)[2]; var crossing = p.CrossingHolds.Single();
            var expired = target == 1.25;
            Check(crossing.Reference == hold && crossing.Location == new ChartNoteLocation(0, 2)
                && crossing.HeadChartTime == 2f && crossing.EndChartTime == 6f && crossing.HeadExpired == expired
                && crossing.Disposition == (expired ? SeekCrossingHoldDisposition.ContinuationRequired : SeekCrossingHoldDisposition.HeadWindowOpen)
                && Requires(p, SeekTargetRequiredWorkKind.HoldGeometry) && Requires(p, SeekTargetRequiredWorkKind.HoldEngineClosure)
                && Requires(p, SeekTargetRequiredWorkKind.HoldContinuation, hold) == expired
                && p.QueueTargets.TapWaiting.Contains(hold) != expired,
                "A crossing Hold was discarded merely for starting before target or represented as resumed while its expired head needs continuation.");
            ReadOnly(f, p);
        }
    }
    private static void HoldBoundary()
    {
        using (var f = new Fixture())
        {
            using var chart = f.Run.CaptureSeekPlan(new(3), f.Native.TargetProfile); var p = Capture(f, 3); var hold = References(f)[2];
            Check(chart.Past.Any(row => row.Location == new ChartNoteLocation(0, 2)) && chart.CrossingHolds.Count == 0
                && p.Coordinates.ChartCoordinate == 6f && p.CrossingHolds.Single().Reference == hold
                && p.CrossingHolds.Single().Disposition == SeekCrossingHoldDisposition.ContinuationRequired
                && Requires(p, SeekTargetRequiredWorkKind.HoldContinuation, hold) && !p.CompleteSeekPermitted,
                "Chart-plan end equality was mistaken for HoldControl's strict now>tail consumption or engine completion.");
        }
        using (var f = new Fixture())
        {
            var p = Capture(f, 3.125); Check(p.CrossingHolds.Count == 0 && Requires(p, SeekTargetRequiredWorkKind.HoldEngineClosure),
                "A time beyond the Hold tail retained a crossing disposition or falsely certified engine closure.");
        }
    }
    private static void ExplicitSkip()
    {
        using var f = new Fixture(); var p = Capture(f, 1.03125, SeekCrossingHoldPolicy.Skip); var r = References(f); var crossing = p.CrossingHolds.Single();
        Check(crossing.Reference == r[2] && !crossing.HeadExpired && crossing.Disposition == SeekCrossingHoldDisposition.SkippedByPolicy
            && p.NativeQueueProjection.TapWaiting.Contains(r[2]) && !p.QueueTargets.TapWaiting.Contains(r[2])
            && Requires(p, SeekTargetRequiredWorkKind.ExplicitlySkippedCrossingHold, r[2])
            && Requires(p, SeekTargetRequiredWorkKind.HoldEngineClosure) && Requires(p, SeekTargetRequiredWorkKind.TargetVisibilityAndGeometry)
            && p.FlagTargets.Single(row => row.Reference == r[2]).Destroyed && p.FlagTargets.All(row => !row.IsActive && !row.Missed)
            && !p.CompleteSeekPermitted, "Explicit crossing skip silently became the default policy or claimed full Seek/engine closure.");
        Queues(p.QueueTargets, new[] { r[3], r[4] }, Array.Empty<long>(), new[] { r[1] }); Apply(f, p.QueueTargets);
    }
    private static void AutoQueues()
    {
        using var f = new Fixture(configure: n =>
        {
            PlainFuture(n); Plain(n, 0, .9375f); Plain(n, 1, 1f); n.TargetSetChart(2, 1, 2f); n.TargetSetTiming(2, 1f);
            Window(n, .125f, .0625f, .25f);
        });
        var p = Capture(f, 1); var r = References(f); var baseline = f.Native.JudgeQueueSnapshot();
        Queues(p.QueueTargets, new[] { r[3], r[4] }, new[] { r[0], r[1] }, new[] { r[2] }); ReadOnly(f, p);
        Apply(f, p.QueueTargets); p.VerifySource();
        var expectedForward = new[] { ("Clear", 0, 0L), ("Add", 0, r[3]), ("Add", 0, r[4]),
            ("Clear", 1, 0L), ("Add", 1, r[0]), ("Add", 1, r[1]), ("Clear", 2, 0L), ("Add", 2, r[2]) };
        Check(f.Native.JudgeQueueWrites.Take(expectedForward.Length).Select(row => (row.Operation, row.List, row.Requested)).SequenceEqual(expectedForward)
            && f.Native.JudgeQueueWrites.Skip(expectedForward.Length).All(row => row.Operation is "SetValue" or "_size" or "_items" or "_version")
            && f.Native.JudgeQueueStorageEquals(baseline), "Automatic queue projection was reordered, synthesized through fields or incompletely restored.");
    }
}
