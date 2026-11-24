using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    private static void NativeSwap()
    {
        using var f = new Fixture(configure: n =>
        {
            PlainFuture(n); n.SetGameplayField("seek.chart.bpm", 60f);
            n.TargetSetChart(0, 2, 0f, 2f); n.TargetSetTiming(0, 0f, 2f);
            n.TargetSetChart(1, 2, BitConverter.Int32BitsToSingle(0x3549539c), 3f); n.TargetSetTiming(1, BitConverter.Int32BitsToSingle(0x3549539c), 3f);
            n.TargetSetChart(2, 0, BitConverter.Int32BitsToSingle(0x35c9539c)); n.TargetSetTiming(2, BitConverter.Int32BitsToSingle(0x35c9539c));
        });
        var p = Capture(f, 0); var r = References(f);
        // Literal trace from 3EC820..3ECB63: H0 swaps H.75us, then
        // H.75us swaps Tap1.5us; the remaining Hold pair swaps again.
        Check(p.NativeSorted.SequenceEqual(new[] { r[2], r[1], r[0], r[3], r[4] }) && p.SortComparisons == 14,
            "Native non-transitive epsilon swaps were replaced by a comparator/stable sort or omitted self comparisons.");
        ReadOnly(f, p);
    }
    private static void EpsilonThreshold()
    {
        foreach (var bits in new[] { 0x358637bd, 0x358637be })
        {
            using var f = new Fixture(configure: n =>
            {
                PlainFuture(n); n.SetGameplayField("seek.chart.bpm", 60f);
                n.TargetSetChart(0, 2, 0f, 2f); n.TargetSetTiming(0, 0f, 2f);
                n.TargetSetChart(1, 0, BitConverter.Int32BitsToSingle(bits)); n.TargetSetTiming(1, BitConverter.Int32BitsToSingle(bits));
            });
            var p = Capture(f, 0); var r = References(f);
            Check(p.NativeSorted.Take(2).SequenceEqual(bits == 0x358637bd ? new[] { r[1], r[0] } : new[] { r[0], r[1] }),
                "The rounded difference was compared against float 1e-6 or a non-strict double threshold.");
        }
    }
    private static void PrefixBreak()
    {
        using var f = new Fixture(configure: n =>
        {
            PlainFuture(n); n.SetGameplayField("seek.chart.bpm", 60f); Window(n, .15f, .06f, BitConverter.Int32BitsToSingle(0x358637bd));
            n.TargetSetChart(0, 2, 0f, 2f); n.TargetSetTiming(0, 0f, 2f);
            n.TargetSetChart(1, 2, BitConverter.Int32BitsToSingle(0x3549539c), 3f); n.TargetSetTiming(1, BitConverter.Int32BitsToSingle(0x3549539c), 3f);
            n.TargetSetChart(2, 0, BitConverter.Int32BitsToSingle(0x35c9539c)); n.TargetSetTiming(2, BitConverter.Int32BitsToSingle(0x35c9539c));
        });
        var p = Capture(f, 0); var r = References(f); var order = new[] { r[2], r[1], r[0], r[3], r[4] };
        Queues(p.NativeQueueProjection, order, Array.Empty<long>(), Array.Empty<long>());
        Queues(p.QueueTargets, order, Array.Empty<long>(), Array.Empty<long>());
        Check(p.ExpiredHeads.Count == 0, "A prefix break was replaced by independent eligibility filtering or start<T expiry.");
    }
    private static void DragContinue()
    {
        using var f = new Fixture(configure: n =>
        {
            PlainFuture(n); Window(n, .15f, .0625f, .25f);
            n.TargetSetChart(0, 1, 2.25f); n.TargetSetTiming(0, 1.125f);
            Plain(n, 1, 1.1875f);
        });
        var p = Capture(f, 1); var r = References(f);
        Queues(p.QueueTargets, new[] { r[0], r[2], r[3], r[4] }, new[] { r[1] }, Array.Empty<long>());
        Check(p.ExpiredHeads.Count == 0, "A delayed Drag broke scanning or a future Tap was removed solely because it starts after target time.");
    }
    private static void FloatWindows()
    {
        using (var f = new Fixture(configure: n =>
        {
            PlainFuture(n); Window(n, .15f, .06f, BitConverter.Int32BitsToSingle(0x33c00000));
            var next = BitConverter.Int32BitsToSingle(0x3f800001); Plain(n, 0, next);
            Plain(n, 1, BitConverter.Int32BitsToSingle(0x3f800002));
        }))
        {
            var p = Capture(f, 1); var r = References(f);
            Queues(p.QueueTargets, r.Skip(1).ToArray(), new[] { r[0] }, Array.Empty<long>());
            Check(p.ExpiredHeads.Count == 0, "The native float32 addition was widened before comparing the exact inclusive head boundary.");
        }
        using (var f = new Fixture(configure: n =>
        {
            PlainFuture(n); Window(n, .125f, .0625f, .25f); Plain(n, 0, .875f); Plain(n, 1, .9375f);
            Plain(n, 2, MathF.BitDecrement(.9375f));
        }))
        {
            var p = Capture(f, 1); var r = References(f);
            Queues(p.QueueTargets, new[] { r[3], r[4] }, new[] { r[1] }, Array.Empty<long>());
            Check(p.ExpiredHeads.Select(row => row.Reference).OrderBy(value => value).SequenceEqual(new[] { r[0], r[2] }.OrderBy(value => value))
                && p.ExpiredHeads.All(row => row.Reason == SeekExpiredHeadReason.NormalChallengeWindow),
                "Native timeout equality used >=, lost one-float adjacency, or incorrectly added the judge-window reason at equality.");
        }
    }
    private static void ChallengeTimeout()
    {
        foreach (var complete in new[] { false, true })
        foreach (var challenge in new[] { false, true })
        {
            using var f = new Fixture(configure: n =>
            {
                PlainFuture(n); Window(n, .03125f, .125f, .25f, complete: complete); Plain(n, 0, .9375f);
                n.TargetSet(n.TargetNote(0), "inChallengeTime", challenge);
            });
            var p = Capture(f, 1); var r = References(f);
            var retained = complete && challenge;
            Check(p.ExpiredHeads.Count == (retained ? 0 : 1) && (retained || p.ExpiredHeads.Single().Reason == SeekExpiredHeadReason.JudgeWindow),
                "The per-note challenge flag and challengeCompletely conjunction was replaced by a global mode switch.");
            Queues(p.QueueTargets, r.Skip(1).ToArray(), retained ? new[] { r[0] } : Array.Empty<long>(), Array.Empty<long>());
        }
    }
}
