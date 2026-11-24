using RizToolbox.Adapter.Gameplay;
using RizToolbox.Judgements.Tests.NoteFlags;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    internal static readonly (string Name, Action Run)[] TargetProviderCases =
    {
        ("Line ring source target provider resolves no Note dynamic and group only occurrences", TargetProviderNoNotes),
        ("Line ring source target provider separates baseline poses and retains immutable history", TargetProviderBaselines),
        ("Line ring source target provider first reads after aggregate and Run disposal", TargetProviderIndependentHold),
        ("Line ring source target provider rejects worker and callback reentry before native", TargetProviderThreadAndReentry),
        ("Line ring source target provider rejects source mutation in the post projection sample", TargetProviderPostSampleDrift),
        ("Line ring source target provider revokes proofs for incomplete mappings and duplicate Canvas indices", TargetProviderPureRefusals)
    };

    // Select a complete, actual no-Note graph with both cross-Canvas dynamic
    // and same-Canvas group-only lines. Every endpoint pair is an adjacent
    // pair in the retained full chart. The default fixture intentionally has
    // out-of-chart orphan lines and aliased Canvas moves, used below as REDs.
    private static void ConfigureTargetProvider(FakeNative n, bool uniqueCanvas = true)
    {
        n.LineRingNoNotes(emptyChart: true);
        if (uniqueCanvas)
        {
            var third = n.CanvasSourceCloneObject(n.CanvasSourceMoves[0]);
            n.CanvasSourceSlot(n.CanvasSourceMovesArray, 2, third);
            n.CanvasSourceSet(third, "index", 2);
            n.CanvasSourceSet(n.CanvasSourceMoves[0], "index", 0);
            n.CanvasSourceSet(n.CanvasSourceMoves[1], "index", 1);
        }
        var group = n.LineRingGroups[0];
        var first = n.NoteViewPoints[0][0]; var second = n.NoteViewPoints[0][1];
        n.LineRingSet(first, "xPosition", .25f); n.LineRingSet(first, "floorPosition", 4f);
        n.LineRingSet(first, "canvasIndex", uniqueCanvas ? 0 : 999);
        n.LineRingSet(second, "xPosition", -.5f); n.LineRingSet(second, "floorPosition", 10f);
        n.LineRingSet(second, "canvasIndex", uniqueCanvas ? 1 : -5);
        var dynamic = n.LineRingNewLine(group, first, second, isStatic: false);
        n.LineRingSet(dynamic, "endLineInformation", IntPtr.Zero);
        var staticStart = n.NoteViewPoints[0][1]; var staticEnd = n.NoteViewPoints[0][2];
        n.LineRingSet(staticStart, "canvasIndex", uniqueCanvas ? 1 : -5);
        n.LineRingSet(staticEnd, "canvasIndex", uniqueCanvas ? 1 : -5);
        var groupOnly = n.LineRingNewLine(group, staticStart, staticEnd, isStatic: true);
        n.LineRingSet(groupOnly, "canvasIndex", 1);
        n.LineRingSet(groupOnly, "endLineInformation", IntPtr.Zero);
        n.LineRingSetGroupLines(group, dynamic, groupOnly);
        n.LineRingSetGroups(group);
        n.LineRingSetDynamic(dynamic, dynamic); // Preserve physical aliases.
    }

    private static void TargetProviderNoNotes()
    {
        using var f = new Fixture(n => ConfigureTargetProvider(n));
        var n = f.Native; var aggregate = f.Prepare(); var source = f.Capture(aggregate);
        var serial = Serial(f.Run); var ranges = n.CanvasSourceRangeAttempts;
        var roots = n.CanvasBasisCurrentRoots();
        var result = source.ReadLocalTargetProjection();
        Check(result.Source.NoteJoins.Count == 0 && result.Source.Lines.Count == 2
            && result.ChartOccurrences.Lines.Count == 2 && result.ChartOccurrences.PhysicalOccurrences.Count == 4
            && result.ChartOccurrences.Lines.All(row => row.Resolution == SeekLineChartResolution.Exact)
            && result.ChartOccurrences.PhysicalOccurrences.Count(row => row.Kind == SeekLinePhysicalOccurrenceKind.DynamicSlot) == 2
            && result.ChartOccurrences.PhysicalOccurrences.Count(row => row.Kind == SeekLinePhysicalOccurrenceKind.GroupSegmentSlot) == 2,
            "The real retained reader lost a no-Note dynamic/group-only line or collapsed physical occurrences.");
        var start = result.Endpoints.LocalTargets.Single(row => row.Role == SeekLineEndpointLocalRole.Start
            && result.Source.DynamicLines.Contains(row.Line));
        var end = result.Endpoints.LocalTargets.Single(row => row.Role == SeekLineEndpointLocalRole.DedicatedCrossCanvasEnd);
        Check(start.CanvasSlot == 0 && end.CanvasSlot == 1
            && start.ParentTransform == result.Endpoints.CanvasBasis.TransformAt(0)
            && end.ParentTransform == result.Endpoints.CanvasBasis.TransformAt(1)
            && Bits(start.LocalPosition.X) == Bits(1.40625f) && Bits(start.LocalPosition.Y) == Bits(12f)
            && Bits(end.LocalPosition.X) == Bits(-2.8125f) && Bits(end.LocalPosition.Y) == Bits(30f)
            && ReferenceEquals(result.Source.Identity.CanvasCapture, aggregate.SourceCapture)
            && ReferenceEquals(result.Source.Identity.CanvasPlan, aggregate.SourceCapture.Plan)
            && ReferenceEquals(result.Endpoints.CanvasBasis, source.ReadNumericBasis())
            && Serial(f.Run) == serial && n.CanvasSourceRangeAttempts == ranges
            && source.GetJournal().SourceVerified && !source.GetJournal().BaselineVerified
            && !result.CompleteSeekPermitted && !result.Endpoints.CompleteSeekPermitted,
            "The formal provider replaced the original Plan/slots, used baseline speed, or claimed a complete Seek target.");
        OriginalRootsRemain(n, roots, "Projection consumed the reader's original retained roots.");
        NoWrites(n, "Formal local target provider reached a native setter, initializer or producer.");
    }

    private static void TargetProviderBaselines()
    {
        using var f = new Fixture(n => ConfigureTargetProvider(n)); var n = f.Native; var source = f.Capture();
        var before = source.ReadLocalTargetProjection(); var old = before.Endpoints.LocalTargets.ToArray();
        n.LineRingSet(n.LineRingControl, "_speed", 123f);
        n.LineRingSet(n.LineRingControl, "_nowTime", -77f);
        var transform = new IntPtr(before.Endpoints.LocalTargets[0].Transform);
        var pose = n.GeometryTransforms[transform];
        n.GeometrySetTransform(transform, pose with { Position = new(100, 200, 300) });
        var after = source.ReadLocalTargetProjection();
        Check(after.Endpoints.LocalTargets.SequenceEqual(old)
            && before.Endpoints.LocalTargets.SequenceEqual(old)
            && source.GetJournal().SourceVerified && !source.GetJournal().BaselineVerified,
            "Current baseline poses or control speed contaminated the original explicit Plan projection.");
        Reject(source.VerifyBaseline);
        Check(!source.GetJournal().SourceExpired, "A baseline-only change expired the original target source.");
        source.Dispose();
        NoNative(n, () =>
        {
            before.ChartOccurrences.StrictResolve();
            Check(before.Endpoints.LocalTargets.SequenceEqual(old), "Disposal altered an already owned target history.");
            Readonly(before.Endpoints.LocalTargets, "Provider local targets are mutable.");
            Readonly(before.Source.Lines, "Provider source history is mutable.");
            Reject(() => source.ReadLocalTargetProjection());
        });
        NoWrites(n, "Baseline-only provider projection dispatched a native writer.");
    }

    private static void TargetProviderIndependentHold()
    {
        using var f = new Fixture(n => ConfigureTargetProvider(n)); var n = f.Native;
        var aggregate = f.Prepare(); var source = f.Capture(aggregate);
        var capture = aggregate.SourceCapture; var plan = capture.Plan;
        f.Run.Dispose(); aggregate.Dispose(); var roots = n.CanvasBasisCurrentRoots();
        var result = source.ReadLocalTargetProjection();
        Check(ReferenceEquals(result.Source.Identity.CanvasCapture, capture)
            && ReferenceEquals(result.Source.Identity.CanvasPlan, plan)
            && result.Endpoints.LocalTargets.Count == 3,
            "The first target projection borrowed its disposed outer aggregate instead of the independent line hold.");
        OriginalRootsRemain(n, roots, "Independent target projection freed retained roots.");
        source.Dispose(); Check(n.ActiveRoots == 0, "Final target reader disposal did not release its original graph.");
        NoWrites(n, "Independent target reader executed a native writer.");
    }

    private static void TargetProviderThreadAndReentry()
    {
        using var f = new Fixture(n => ConfigureTargetProvider(n)); var n = f.Native; var source = f.Capture();
        source.ReadLocalTargetProjection();
        NoNative(n, () => Task.Run(() => Reject(() => source.ReadLocalTargetProjection())).GetAwaiter().GetResult());
        Check(source.GetJournal().SourceVerified, "A worker refusal revoked the owner thread's proof.");
        var fired = false;
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (fired || receiver != n.LineRingControl || name != "_nowTime") return;
            fired = true; NoNative(n, () => Reject(() => source.ReadLocalTargetProjection()));
        };
        source.ReadLocalTargetProjection(); f.Clear();
        Check(fired && source.GetJournal().SourceVerified && !source.GetJournal().BaselineVerified,
            "Nested projection entered native code or prevented fresh outer proof recovery.");
        NoWrites(n, "Provider reentry reached a native writer.");
    }

    private static void TargetProviderPostSampleDrift()
    {
        using var f = new Fixture(n => ConfigureTargetProvider(n)); var n = f.Native; var source = f.Capture();
        var history = source.ReadLocalTargetProjection();
        var first = n.NoteViewPoints[0][0]; var samples = 0;
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (receiver == n.LineRingControl && name == "_nowTime" && ++samples == 3)
                n.LineRingSet(first, "xPosition", .75f);
        };
        Reject(() => source.ReadLocalTargetProjection()); f.Clear();
        Check(samples >= 3 && source.GetJournal().SourceExpired && !source.GetJournal().SourceVerified
            && !source.GetJournal().BaselineVerified
            && history.Endpoints.LocalTargets.Any(row => Bits(row.LocalPosition.X) == Bits(1.40625f)),
            "A source mutation after projection was published or changed immutable prior targets.");
        NoNative(n, () => Reject(() => source.ReadLocalTargetProjection()));
        NoWrites(n, "A refused post-sample target reached a native writer.");
    }

    private static void TargetProviderPureRefusals()
    {
        foreach (var duplicateCanvas in new[] { false, true })
        {
            using var f = duplicateCanvas ? new Fixture(n => ConfigureTargetProvider(n, uniqueCanvas: false)) : new Fixture();
            var n = f.Native; var source = f.Capture(); source.VerifyBaseline();
            var error = Reject(() => source.ReadLocalTargetProjection());
            Check((duplicateCanvas ? error is NotSupportedException : error is InvalidOperationException)
                && !source.GetJournal().SourceVerified && !source.GetJournal().BaselineVerified
                && !source.GetJournal().SourceExpired,
                "Pure unresolved mapping/duplicate Canvas refusal preserved stale proof or poisoned the retained source.");
            source.VerifySource();
            Check(source.GetJournal().SourceVerified, "Pure target refusal incorrectly expired usable source history.");
            NoWrites(n, "A pure target refusal dispatched native initialization or writes.");
        }
    }
}
