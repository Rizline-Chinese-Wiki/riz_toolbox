using System.Collections;
using System.Reflection;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static class NativeSeekLineChartOccurrenceChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Line chart occurrence maps no Note dynamic and group segments by endpoint references", () => RunGroup(7, NoNoteAndReferences)),
        ("Line chart occurrence rejects reversed and ambiguous adjacent point pairs", () => RunGroup(7, AdjacencyAndAmbiguity)),
        ("Line chart occurrence checks time bits and Note join consistency", () => RunGroup(7, TimesAndNotes)),
        ("Line chart occurrence preserves physical slots and complete bounded expansion", () => RunGroup(6, SlotsAndBounds)),
        ("Line chart occurrence owns immutable copies and rejects guard reentry", () => RunGroup(7, CopyAndGuards))
    };

    private static int _checks;
    private static void RunGroup(int expected, Action run)
    {
        _checks = 0;
        run();
        if (_checks != expected) throw new InvalidOperationException($"Expected {expected} occurrence assertions, ran {_checks}.");
    }
    private static void Check(bool condition, string message)
    {
        ++_checks;
        if (!condition) throw new Exception(message);
    }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        ++_checks;
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static SeekLineRingChartPoint C(int line, int point, long chartLine, long reference, float time)
        => new(line, point, chartLine, reference, time);
    private static SeekLineRingNoteJoin Join(long runtimeLine = 10001, long chartLine = 2001,
        int segment = 0, int lineOrdinal = 0, long start = 701, long end = 702)
        => new(9001, 9002, new(lineOrdinal, 0), chartLine, segment, runtimeLine,
            start, end, 801, false);

    // Reflection makes the already frozen Adapter's pure identity constructor
    // usable here. This is a model fixture, not a live game observation.
    private static SeekLineRingSourceSnapshot Fixture()
    {
        var key = new SeekCanvasTargetKeyPoint(91, 0f, 0f, 0, 0f);
        var move = new SeekCanvasTargetMove(90, 0, new[] { key }, new[] { key });
        var plan = NativeSeekCanvasTargetPlan.Capture(new[] { move },
            new(0f, 1f, 1f, new(0f, 0f, 0f), new(0f, 0f, 0f)),
            (_, _) => 0f, (_, _) => 0f, () => { });
        var capture = (NativeSeekCanvasTargetCapture)Activator.CreateInstance(
            typeof(NativeSeekCanvasTargetCapture), nonPublic: true)!;
        typeof(NativeSeekCanvasTargetCapture).GetField("_plan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(capture, plan);
        var run = new ObservedJudgementRun("fixture", "fixture", 1, 1, 100, 200);
        var noteIdentity = new SeekRuntimeNoteSourceIdentity(new SeekChartIdentity(run, 300, 400, 500),
            600, 700, 800);
        SeekLineRingTransformBaseline T(long reference)
            => new(reference, 0, new(0f, 0f, 0f), new(0f, 0f, 0f),
                new NativeRendererQuaternion(0f, 0f, 0f, 1f), new(1f, 1f, 1f));
        return new(new(capture, plan, noteIdentity, 100, 900, 901),
            1100, new long[] { 10001 }, 1101, new long[] { 801 },
            new[]
            {
                new SeekLineRingLine(10001, false, 0, 501, 701, 702, 502, 0, 801),
                new SeekLineRingLine(10002, true, 0, 503, 702, 703, 0, 0, 801)
            },
            new[]
            {
                new SeekLineRingPoint(701, 0f, 0f, 0, 0, 0f),
                new SeekLineRingPoint(702, 1f, 0f, 0, 0, 0f),
                new SeekLineRingPoint(703, 2f, 0f, 0, 0, 0f)
            },
            new[] { new SeekLineRingGroup(801, 504, 1102, new long[] { 10002 }) },
            new(Bits(0f), Bits(0f), Bits(0f), Bits(0f)),
            new[] { new SeekLineRingGroupBaseline(801, 0, false) },
            new[] { T(501), T(502), T(503), T(504) },
            new[] { C(0, 0, 2001, 701, 0f), C(0, 1, 2001, 702, 1f), C(0, 2, 2001, 703, 2f) },
            Array.Empty<SeekLineRingNoteJoin>());
    }

    private static void NoNoteAndReferences()
    {
        var source = Fixture();
        var guardCount = 0;
        var map = NativeSeekLineChartOccurrenceMap.Project(source, () => ++guardCount);
        Check(guardCount > 30 && !map.CompleteSeekPermitted, "The complete copy/guard or admission boundary was lost.");
        Check(map.Lines.Count == 2 && map.PhysicalOccurrences.Count == 2 && map.GroupOccurrences.Count == 1,
            "Unique lines and physical source occurrences were conflated.");
        var dynamic = map.PhysicalOccurrences[0];
        var grouped = map.PhysicalOccurrences[1];
        Check(dynamic.Kind == SeekLinePhysicalOccurrenceKind.DynamicSlot && dynamic.ControlSlot == 0
            && dynamic.PhysicalLine == 10001 && grouped.Kind == SeekLinePhysicalOccurrenceKind.GroupSegmentSlot
            && grouped.ControlSlot == 0 && grouped.SegmentSlot == 0 && grouped.GroupReference == 801
            && grouped.PhysicalLine == 10002, "Dynamic and group-only occurrence slots were not retained.");
        Check(dynamic.Mapping!.Resolution == SeekLineChartResolution.Exact
            && grouped.Mapping!.Resolution == SeekLineChartResolution.Exact,
            "No-Note dynamic or group-only/static Line was not exactly mapped.");
        var first = dynamic.Mapping!.Exact!;
        var second = grouped.Mapping!.Exact!;
        Check(first.LineOrdinal == 0 && first.ChartLine == 2001 && first.SegmentOrdinal == 0
            && first.StartReference == 701 && first.EndReference == 702
            && second.SegmentOrdinal == 1 && second.StartReference == 702 && second.EndReference == 703,
            "Exact raw reference pair or adjacent chart ordinal was lost.");
        map.StrictResolve(); ++_checks;

        var otherOrdinal = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            ChartPoints = new[] { C(7, 0, 98765, 701, 0f), C(7, 1, 98765, 702, 1f),
                C(7, 2, 98765, 703, 2f) }
        }, () => { });
        Check(otherOrdinal.Lines[0].Exact is { LineOrdinal: 7, ChartLine: 98765, SegmentOrdinal: 0 },
            "Raw chart Line reference was confused with its ordinal or the physical Line reference.");

    }

    private static void AdjacencyAndAmbiguity()
    {
        var source = Fixture();
        var reversed = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            ChartPoints = new[] { C(0, 0, 2001, 702, 1f), C(0, 1, 2001, 701, 0f),
                C(0, 2, 2001, 703, 2f) }
        }, () => { });
        Check(reversed.Lines[0].Resolution == SeekLineChartResolution.NoAdjacentChartPair,
            "Reversed or nonadjacent point references were treated as a segment.");
        Reject<InvalidOperationException>(() => reversed.StrictResolve(), "Strict resolution admitted an unmatched line.");

        var samePairTwice = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            ChartPoints = new[] { C(0, 0, 2001, 701, 0f), C(0, 1, 2001, 702, 1f),
                C(0, 2, 2001, 701, 0f), C(0, 3, 2001, 702, 1f), C(0, 4, 2001, 703, 2f) }
        }, () => { });
        Check(samePairTwice.Lines[0].Resolution == SeekLineChartResolution.AmbiguousChartOccurrence
            && samePairTwice.Lines[0].Candidates.Select(c => c.SegmentOrdinal).SequenceEqual(new[] { 0, 2 }),
            "Repeated pair candidates were collapsed to the first occurrence.");
        Reject<InvalidOperationException>(() => samePairTwice.StrictResolve(), "Strict resolution admitted a repeated pair.");
        var notedDuplicate = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            ChartPoints = new[] { C(0, 0, 2001, 701, 0f), C(0, 1, 2001, 702, 1f),
                C(0, 2, 2001, 701, 0f), C(0, 3, 2001, 702, 1f), C(0, 4, 2001, 703, 2f) },
            NoteJoins = new[] { Join() }
        }, () => { });
        Check(notedDuplicate.Lines[0].Resolution == SeekLineChartResolution.AmbiguousChartOccurrence
            && notedDuplicate.Lines[0].Candidates.Count == 2,
            "A Note join was used to choose one of two physical chart occurrences.");

        var crossLinePair = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            ChartPoints = new[] { C(0, 0, 2001, 701, 0f), C(0, 1, 2001, 702, 1f),
                C(1, 0, 2002, 701, 0f), C(1, 1, 2002, 702, 1f), C(1, 2, 2002, 703, 2f) }
        }, () => { });
        Check(crossLinePair.Lines[0].Resolution == SeekLineChartResolution.AmbiguousChartOccurrence
            && crossLinePair.Lines[0].Candidates.Select(c => c.ChartLine).SequenceEqual(new long[] { 2001, 2002 }),
            "The same pair reused across chart Lines was silently chosen.");

        var reusedLineIdentity = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            ChartPoints = new[] { C(0, 0, 2001, 701, 0f), C(0, 1, 2001, 702, 1f),
                C(1, 0, 2001, 702, 1f), C(1, 1, 2001, 703, 2f) }
        }, () => { });
        Check(reusedLineIdentity.Lines.All(line => line.Resolution == SeekLineChartResolution.AmbiguousChartLineIdentity),
            "Reused chart Line identity under different ordinals was admitted.");

    }

    private static void TimesAndNotes()
    {
        var source = Fixture();
        var timeMismatch = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            ChartPoints = new[] { C(0, 0, 2001, 701, -0f), C(0, 1, 2001, 702, 1f),
                C(0, 2, 2001, 703, 2f) }
        }, () => { });
        Check(timeMismatch.Lines[0].Resolution == SeekLineChartResolution.EndpointTimeBitsMismatch
            && timeMismatch.Lines[0].Candidates.Count == 1,
            "Equal numeric time with different Single bits was accepted or lost its candidate.");
        var missingEndpoint = NativeSeekLineChartOccurrenceMap.Project(source with
        { Lines = new[] { source.Lines[0] with { StartPoint = 0 }, source.Lines[1] } }, () => { });
        Check(missingEndpoint.Lines[0].Resolution == SeekLineChartResolution.MissingEndpoint
            && missingEndpoint.Lines[0].Reason.Contains("absent"),
            "A missing physical endpoint was treated as an inferred chart pair.");

        var consistentJoin = NativeSeekLineChartOccurrenceMap.Project(source with
        { NoteJoins = new[] { Join() } }, () => { });
        Check(consistentJoin.Lines[0].Exact is not null && consistentJoin.NoteJoinsOutsideControlGraph.Count == 0,
            "A consistent raw Note join displaced the exact reference mapping.");
        Reject<InvalidDataException>(() => NativeSeekLineChartOccurrenceMap.Project(source with
        { NoteJoins = new[] { Join(start: 702) } }, () => { }),
            "A Note join contradicting the physical endpoint was accepted.");
        Reject<InvalidDataException>(() => NativeSeekLineChartOccurrenceMap.Project(source with
        { NoteJoins = new[] { Join(segment: 1) } }, () => { }),
            "A Note join contradicting the chart segment ordinal was accepted.");

        var externalJoin = NativeSeekLineChartOccurrenceMap.Project(source with
        { NoteJoins = new[] { Join(runtimeLine: 99999) } }, () => { });
        Check(externalJoin.NoteJoinsOutsideControlGraph.Count == 1
            && externalJoin.NoteJoinsOutsideControlGraph[0].Join.RuntimeLine == 99999
            && externalJoin.NoteJoinsOutsideControlGraph[0].Reason.Contains("outside"),
            "A Note join outside the control graph disappeared.");
        Reject<InvalidOperationException>(() => externalJoin.StrictResolve(),
            "Strict resolution admitted a Note join outside the control graph.");

    }

    private static void SlotsAndBounds()
    {
        var source = Fixture();
        var repeatSlots = NativeSeekLineChartOccurrenceMap.Project(source with
        {
            DynamicLines = new long[] { 10001, 10001 },
            LineGroups = new long[] { 801, 801 },
            Groups = new[] { source.Groups[0] with { Lines = new long[] { 10002, 10002 } } }
        }, () => { });
        Check(repeatSlots.Lines.Count == 2 && repeatSlots.GroupOccurrences.Count == 2
            && repeatSlots.PhysicalOccurrences.Count == 6
            && repeatSlots.PhysicalOccurrences.Count(o => o.PhysicalLine == 10002) == 4,
            "Repeated physical slots were deduplicated by object identity.");
        repeatSlots.StrictResolve(); ++_checks;

        var nullSlots = NativeSeekLineChartOccurrenceMap.Project(source with
        { DynamicLines = new long[] { 0 }, LineGroups = new long[] { 0 } }, () => { });
        Check(nullSlots.PhysicalOccurrences.Count == 1 && nullSlots.PhysicalOccurrences[0].Reason is not null
            && nullSlots.GroupOccurrences[0].GroupReference == 0 && nullSlots.GroupOccurrences[0].Reason is not null,
            "A null physical occurrence was hidden.");
        Reject<InvalidOperationException>(() => nullSlots.StrictResolve(), "Strict resolution admitted null slots.");
        var tooManyExpanded = source with
        {
            LineGroups = new long[] { 801, 801 },
            Groups = new[] { source.Groups[0] with { Lines = Enumerable.Repeat(10002L, 40000).ToArray() } }
        };
        Reject<NotSupportedException>(() => NativeSeekLineChartOccurrenceMap.Project(tooManyExpanded, () => { }),
            "An expanded group occurrence suffix exceeded its bound without refusing the whole map.");

        var trailing = new List<SeekLineRingChartPoint>
        { C(0, 0, 2001, 701, 0f), C(0, 1, 2001, 702, 1f), C(0, 2, 2001, 703, 2f) };
        for (var i = 0; i < 1000; ++i) trailing.Add(C(1, i, 2002, 100000 + i, 3f));
        trailing.Add(C(1, 1000, 2002, 701, 0f));
        trailing.Add(C(1, 1001, 2002, 702, 1f));
        var noPrefix = NativeSeekLineChartOccurrenceMap.Project(source with { ChartPoints = trailing }, () => { });
        Check(noPrefix.Lines[0].Resolution == SeekLineChartResolution.AmbiguousChartOccurrence
            && noPrefix.Lines[0].Candidates.Count == 2
            && noPrefix.Lines[0].Candidates[1].SegmentOrdinal == 1000,
            "A late chart occurrence was truncated or ignored.");

    }

    private static void CopyAndGuards()
    {
        var source = Fixture();
        var mutableDynamic = new long[] { 10001 };
        var mutableChart = new[] { C(0, 0, 2001, 701, 0f), C(0, 1, 2001, 702, 1f),
            C(0, 2, 2001, 703, 2f) };
        var copied = NativeSeekLineChartOccurrenceMap.Project(source with
        { DynamicLines = mutableDynamic, ChartPoints = mutableChart }, () => { });
        mutableDynamic[0] = 0;
        mutableChart[0] = C(0, 0, 9999, 123, 9f);
        Check(copied.PhysicalOccurrences[0].PhysicalLine == 10001
            && copied.Lines[0].Exact is { ChartLine: 2001, StartReference: 701 },
            "The result retained caller-owned source arrays.");
        Reject<NotSupportedException>(() => ((IList)copied.Lines)[0] = copied.Lines[1],
            "A published line map was mutable.");
        Reject<NotSupportedException>(() => ((IList)copied.Lines[0].Candidates)[0] = copied.Lines[1].Candidates[0],
            "A published candidate vector was mutable.");

        var reentered = false;
        Action? reentryGuard = null;
        reentryGuard = () =>
        {
            if (reentered) return;
            reentered = true;
            Reject<InvalidOperationException>(() => NativeSeekLineChartOccurrenceMap.Project(source, reentryGuard!),
                "Synchronous guard reentry was admitted.");
        };
        var afterReentry = NativeSeekLineChartOccurrenceMap.Project(source, reentryGuard);
        Check(reentered && afterReentry.Lines[0].Exact is not null, "Guard reentry prevented outer recovery.");
        Reject<InvalidOperationException>(() => NativeSeekLineChartOccurrenceMap.Project(source,
            () => throw new InvalidOperationException("guard revoked")), "A failing guard was ignored.");
        Check(NativeSeekLineChartOccurrenceMap.Project(source, () => { }).Lines[0].Exact is not null,
            "A failed guard left the thread-local reentry latch set.");

    }
}
