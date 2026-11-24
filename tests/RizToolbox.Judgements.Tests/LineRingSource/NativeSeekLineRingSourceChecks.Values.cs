using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    private static void SameProvenance()
    {
        using var f = new Fixture(); var n = f.Native; var start = Serial(f.Run); var basis = f.Prepare();
        var canvas = basis.SourceCapture; var plan = canvas.Plan; var serial = Serial(f.Run);
        var ranges = n.CanvasSourceRangeAttempts; var roots = n.CanvasBasisCurrentRoots(); var source = f.Capture(basis);
        var snapshot = source.Snapshot;
        source.VerifySource(); source.VerifyBaseline(); source.VerifySource(); source.VerifyBaseline();
        var journal = source.GetJournal();
        Check(start + 1 == serial && Serial(f.Run) == serial && ranges == 3 && n.CanvasSourceRangeAttempts == ranges
            && ReferenceEquals(source.CanvasCapture, canvas) && ReferenceEquals(snapshot.Identity.CanvasCapture, canvas)
            && ReferenceEquals(snapshot.Identity.CanvasPlan, plan) && ReferenceEquals(snapshot.Identity.Notes, OriginalView(basis).Identity)
            && snapshot.Identity.Information == n.GameplayInformation.ToInt64()
            && snapshot.Identity.Canvas == n.GeometryCanvasControl.ToInt64()
            && snapshot.Identity.LineControl == n.LineRingControl.ToInt64(),
            "Line/ring capture recaptured a NoteView/Canvas source, reevaluated Range, or replaced the original Plan.");
        Check(journal.Captured && journal.SourceVerified && journal.BaselineVerified && !journal.SourceExpired
            && ReferenceEquals(journal.CanvasCapture, canvas) && journal.AttemptedWrites == 0
            && !source.CompleteSeekPermitted && !journal.CompleteSeekPermitted && !source.Coverage.CompleteSeekPermitted
            && plan.Request.ChartTime == 5 && plan.Request.Speed == 3
            && snapshot.ControlBaseline.NowTimeBits == unchecked((int)0x80000000)
            && snapshot.ControlBaseline.SceneWidthBits == unchecked((int)0x7fc01234)
            && snapshot.ControlBaseline.SpeedBits == Bits(-7.25f),
            "Source baseline bits were replaced by target time/speed or read-only capture claimed complete Seek.");
        OriginalRootsRemain(n, roots, "A line/ring reader replaced or prematurely released the original physical source roots.");
        Check(!typeof(IGameplayValueParticipant).IsAssignableFrom(typeof(NativeSeekLineRingSourceCapture)),
            "A source-only capture was promoted into a value participant.");
        NoWrites(n, "Provenance collection dispatched a native writer.");
    }

    private static void FullGraphWithoutHold()
    {
        foreach (var noNotes in new[] { false, true })
        {
            using var f = new Fixture(n => { if (noNotes) n.LineRingNoNotes(); });
            var n = f.Native; var source = f.Capture(); var snapshot = source.Snapshot; var coverage = source.Coverage;
            source.VerifySource(); source.VerifyBaseline();
            Check(snapshot.DynamicLines.SequenceEqual(n.CanvasSourceSlots(n.LineRingDynamicArray).Select(value => value.ToInt64()))
                && snapshot.LineGroups.SequenceEqual(n.CanvasSourceSlots(n.LineRingGroupsArray).Select(value => value.ToInt64()))
                && snapshot.Lines.Count == 6
                && snapshot.Points.Select(point => point.Reference).ToHashSet().SetEquals(n.LineRingEndpointReferences())
                && snapshot.Groups.Count == 3
                && coverage.DynamicOccurrences == 3 && coverage.GroupOccurrences == 3 && coverage.GroupSegmentOccurrences == 6
                && coverage.UniqueLines == 6 && coverage.StaticLines == 3,
                $"Complete control graph mismatch: dynamic={snapshot.DynamicLines.Count}, groupSlots={snapshot.LineGroups.Count}, lines={snapshot.Lines.Count}, points={snapshot.Points.Count}, groups={snapshot.Groups.Count}, segments={coverage.GroupSegmentOccurrences}, static={coverage.StaticLines}, noNotes={noNotes}.");
            Check(snapshot.Lines.Any(line => line.Reference == n.LineRingOrphanStatic.ToInt64())
                && snapshot.Lines.Any(line => line.Reference == n.LineRingOrphanDynamic.ToInt64())
                && snapshot.Points.Any(point => point.Time == 30 && point.XPosition == 8 && point.EaseType == 0
                    && point.CanvasIndex == 1 && point.FloorPosition == 47)
                && snapshot.ChartPoints.Count == 6 && snapshot.NoteJoins.Count == (noNotes ? 0 : 5)
                && coverage.LinesWithoutNotes.Count == (noNotes ? 6 : 2)
                && n.CanvasSourceReads.All(row => row.Name is not ("holdControl" or "holdNotes" or "runningHoldNotes"))
                && n.CanvasSourceForbidden == 0,
                "No-Note input fabricated a join, dropped actual points, or depended on a Hold instance.");
            Check(coverage.RequiredWork.Count != 0 && !coverage.CompleteSeekPermitted,
                "Complete enumeration was mislabeled as target-time environment reconstruction.");
            NoWrites(n, "No-Hold complete capture invoked a producer or setter.");
        }
    }

    private static void EmptyGraphs()
    {
        using (var f = new Fixture(n => { n.LineRingNoNotes(emptyChart: true); n.LineRingSetDynamic(); n.LineRingSetGroups(); }))
        {
            var source = f.Capture(); source.VerifySource(); source.VerifyBaseline(); var s = source.Snapshot;
            Check(s.DynamicLinesArray == f.Native.LineRingDynamicArray.ToInt64() && s.LineGroupsArray == f.Native.LineRingGroupsArray.ToInt64()
                && s.DynamicLines.Count == 0 && s.LineGroups.Count == 0 && s.Lines.Count == 0 && s.Groups.Count == 0
                && s.NoteJoins.Count == 0 && source.Coverage.GroupSegmentOccurrences == 0
                && source.Coverage.UniqueLines == 0 && !source.Coverage.CompleteSeekPermitted,
                "Complete empty arrays lost their identities or invented a line/ring target.");
            Readonly(s.DynamicLines, "An empty dynamic array exposed writable storage.");
        }
        using (var f = new Fixture(n =>
        {
            n.LineRingNoNotes(); n.LineRingSetDynamic(); n.LineRingSetGroups(n.LineRingOrphanGroup);
            n.LineRingSetGroupLines(n.LineRingOrphanGroup);
        }))
        {
            var source = f.Capture(); var s = source.Snapshot; source.VerifySource(); source.VerifyBaseline();
            Check(s.LineGroups.Count == 1 && s.Groups.Count == 1 && s.Groups[0].Lines.Count == 0
                && s.Groups[0].LinesArray == f.Native.LineRingGroupArray(f.Native.LineRingOrphanGroup).ToInt64()
                && s.Lines.Count == 0 && source.Coverage.GroupSegmentOccurrences == 0,
                "A declared empty group.lines array was treated as an absent group or an invented segment.");
        }
        using (var f = new Fixture(n =>
        {
            n.LineRingNoNotes(); n.LineRingSetDynamic(IntPtr.Zero, n.LineRingOrphanDynamic, IntPtr.Zero);
            n.LineRingSetGroups(IntPtr.Zero, n.LineRingOrphanGroup, IntPtr.Zero);
            n.LineRingSetGroupLines(n.LineRingOrphanGroup, IntPtr.Zero, n.LineRingOrphanDynamic, IntPtr.Zero);
            n.LineRingSet(n.LineRingOrphanDynamic, "endLineInformation", IntPtr.Zero);
        }))
        {
            var source = f.Capture(); var s = source.Snapshot;
            Check(s.DynamicLines.SequenceEqual(new[] { 0L, f.Native.LineRingOrphanDynamic.ToInt64(), 0L })
                && s.LineGroups.SequenceEqual(new[] { 0L, f.Native.LineRingOrphanGroup.ToInt64(), 0L })
                && s.Groups.Single().Lines.SequenceEqual(new[] { 0L, f.Native.LineRingOrphanDynamic.ToInt64(), 0L })
                && source.Coverage.DynamicOccurrences == 3 && source.Coverage.GroupOccurrences == 3
                && source.Coverage.GroupSegmentOccurrences == 3 && source.Coverage.RequiredWork.Any(work => work.Reference == 0),
                "Null occurrence slots were dropped or treated as complete targets.");
        }
    }

    private static void OccurrencesAndAliases()
    {
        using (var f = new Fixture(n => n.LineRingAliasGraph()))
        {
            var n = f.Native; var basis = f.Prepare();
            var priorHandles = n.CanvasBasisCurrentRoots().Values.GroupBy(pointer => pointer.ToInt64()).ToDictionary(group => group.Key, group => group.Count());
            var core = Field(Field(Field(basis, "_basis"), "_source"), "_core");
            var priorRetained = ((System.Collections.IDictionary)Field(core, "_retained")).Keys.Cast<long>().ToHashSet();
            var source = f.Capture(basis); var s = source.Snapshot; var line = n.LineRingOrphanDynamic.ToInt64();
            Check(s.DynamicLines.Count == 4 && s.DynamicLines.Count(value => value == line) == 3
                && s.LineGroups.Count == 4 && s.LineGroups[0] == s.LineGroups[2]
                && s.Groups.Single(group => group.Reference == n.LineRingOrphanGroup.ToInt64()).Lines.Count(value => value == line) == 2
                && source.Coverage.GroupSegmentOccurrences == 10,
                "Logical group/line aliases were deduplicated or remapped by ordinal.");
            var physical = s.Lines.Single(value => value.Reference == line);
            Check(physical.StartPoint == physical.EndPoint && physical.Transform == physical.EndTransform
                && s.Groups.Single(group => group.Reference == physical.Group).RingTransform == physical.Transform
                && s.Lines.Select(value => value.Reference).Distinct().Count() == s.Lines.Count
                && s.Points.Select(value => value.Reference).Distinct().Count() == s.Points.Count
                && s.Transforms.Select(value => value.Reference).Distinct().Count() == s.Transforms.Count,
                "Alias capture duplicated physical line, endpoint, or Transform rows.");
            foreach (var reference in s.Lines.Select(value => value.Reference).Concat(s.Points.Select(value => value.Reference))
                .Concat(s.Groups.Select(value => value.Reference)).Concat(s.Transforms.Select(value => value.Reference)).Distinct())
                Check(n.NoteViewHandlesFor(new(reference)).Length == priorHandles.GetValueOrDefault(reference) + (priorRetained.Contains(reference) ? 0 : 1),
                    "Logical aliases acquired duplicate retained physical roots: " + reference);
            source.VerifySource(); source.VerifyBaseline();
        }
        using (var f = new Fixture(n => n.LineRingAliasGraph()))
        {
            var n = f.Native; var basis = f.Prepare(); var fired = false;
            n.AfterCanvasSourceRead = (name, owner) =>
            {
                if (fired || name != "xPosition" || owner != n.LineRingPoints[^1]) return;
                fired = true; n.LineRingSet(owner, name, 123.5f);
            };
            Reject(() => f.Capture(basis)); f.Clear();
            Check(fired, "Contradictory repeated-object sample never reached its official field boundary.");
        }
    }

    private static void ChartRuntimeJoins()
    {
        using var f = new Fixture(n => n.LineRingMultipleInstance()); var source = f.Capture(); var s = source.Snapshot;
        var first = f.Native.NoteViewRuntimeNotes[0].ToInt64(); var other = f.Native.NoteViewRuntimeNotes[^1].ToInt64();
        var a = s.NoteJoins.Single(join => join.Note == first); var b = s.NoteJoins.Single(join => join.Note == other);
        Check(s.NoteJoins.Count == 6 && source.Coverage.ProvenJoins.SequenceEqual(s.NoteJoins)
            && a.NoteData == b.NoteData && a.Location == b.Location && a.ChartLine == b.ChartLine
            && a.RuntimeLine != b.RuntimeLine && a.SegmentOrdinal == 0 && b.SegmentOrdinal == 1
            && a.StartPoint != b.StartPoint && a.EndPoint != b.EndPoint,
            "Multiple actual Notes at one chart location were collapsed or forced onto one interval.");
        foreach (var join in s.NoteJoins)
        {
            var line = s.Lines.Single(row => row.Reference == join.RuntimeLine);
            Check(line.StartPoint == join.StartPoint && line.EndPoint == join.EndPoint && line.Group == join.Group
                && line.IsStatic == join.IsStatic && s.ChartPoints.Any(point => point.ChartLine == join.ChartLine
                    && point.PointOrdinal == join.SegmentOrdinal && point.Reference == join.StartPoint),
                "A claimed chart/runtime join lacks its exact point-pair evidence.");
        }
        Check(source.Coverage.LinesWithoutNotes.Contains(f.Native.LineRingOrphanDynamic.ToInt64())
            && source.Coverage.LinesWithoutNotes.Contains(f.Native.LineRingOrphanStatic.ToInt64())
            && s.NoteJoins.All(join => join.RuntimeLine != f.Native.LineRingOrphanDynamic.ToInt64()
                && join.RuntimeLine != f.Native.LineRingOrphanStatic.ToInt64())
            && source.Coverage.RequiredWork.Any(work => work.Reference == f.Native.LineRingOrphanDynamic.ToInt64()),
            "A no-Note segment acquired an ordinal, nearest-time, or group-order chart association.");
        source.VerifySource(); source.VerifyBaseline();
    }

    private static void StaticAndGroupOnlySegments()
    {
        using var f = new Fixture(n =>
        {
            var closure = n.LineRingNewLine(n.LineRingOrphanGroup, n.LineRingPoints[^2], n.LineRingPoints[^1], true);
            n.LineRingSet(n.LineRingOrphanDynamic, "endLineInformation", closure);
        });
        var n = f.Native; var source = f.Capture(); var s = source.Snapshot;
        var extra = n.LineRingLines[^1];
        Check(s.Lines.Count == 7 && s.Lines.Single(line => line.Reference == n.LineRingOrphanStatic.ToInt64()).IsStatic
            && !s.DynamicLines.Contains(n.LineRingOrphanStatic.ToInt64())
            && s.Groups.Any(group => group.Lines.Contains(n.LineRingOrphanStatic.ToInt64()))
            && s.Lines.Single(line => line.Reference == n.LineRingOrphanDynamic.ToInt64()).EndLine == extra.ToInt64()
            && s.Lines.Any(line => line.Reference == extra.ToInt64()) && !s.DynamicLines.Contains(extra.ToInt64())
            && s.Groups.All(group => !group.Lines.Contains(extra.ToInt64()))
            && source.Coverage.StaticLines == 4 && source.Coverage.LinesWithoutNotes.Contains(extra.ToInt64()),
            "Static/group-only segments or an actual external endLine closure were omitted from the physical table.");
        foreach (var line in s.Lines)
            Check(s.Transforms.Any(row => row.Reference == line.Transform) && s.Transforms.Any(row => row.Reference == line.EndTransform),
                "An actual line/end Transform was omitted from the complete closure.");
        source.VerifySource(); source.VerifyBaseline(); NoWrites(n, "Static actual-source capture invoked initialization or renderer work.");
    }
}
