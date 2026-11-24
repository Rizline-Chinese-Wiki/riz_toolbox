using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldGeometryChecks
{
    private static SeekHoldGeometryRequest Empty(float time = 3, float speed = 2)
        => new(time, speed, new(Array.Empty<long>(), Array.Empty<long>()));
    private static SeekHoldGeometryEffectsJournal Execute(Fixture f, NativeSeekHoldGeometryCommands p)
    {
        try { p.Execute(); return p.GetJournal(); }
        finally { if (p.AttemptedCommands != 0) f.Retain(p); }
    }
    private static void ReadOnly()
    {
        using var f = new Fixture(); var p = f.Prepare(); var j = p.GetJournal();
        Check(j.Prepared && !j.ExecutionEntered && !j.ExecutionFinished && !j.CompleteSeekPermitted
            && j.Baseline!.Notes.Count == f.Native.HoldStateNotes.Count && f.Native.GeometryCalls.Count == 0,
            "Preparation did not independently capture the actual Hold subset without writes.");
        Check(j.Plan!.RequiredWork.Any(x => x.Kind == SeekHoldGeometryRequiredWorkKind.TargetCanvasLineRingBasis)
            && j.Plan.RequiredWork.Any(x => x.Kind == SeekHoldGeometryRequiredWorkKind.HoldEngineClosure),
            "Captured basis was incorrectly advertised as target-time or engine closure.");
        using var absent = new Fixture(configure: n => n.GeometryRemoveEase());
        var missing = absent.Prepare(Empty()).GetJournal();
        Check(missing.Plan!.RequiredWork.Count(x => x.Kind == SeekHoldGeometryRequiredWorkKind.EaseTableInitialization)
            == missing.Baseline!.Notes.Count && absent.Native.GeometryCalls.Count == 0,
            "Absent ease initialization was guessed or dispatched during preparation.");
    }
    private static void ParentOracle()
    {
        using var f = new Fixture(configure: n =>
        {
            foreach (var hold in n.HoldStateNotes) n.TargetSet(n.HoldStateData(hold), "floorPosition", 4f);
        });
        var p = f.Prepare(Empty()); var baseline = p.GetJournal().Baseline!;
        var j = Execute(f, p);
        Check(j.ExecutionFinished && !j.CompleteSeekPermitted && j.ExternalEffectsUnclosed,
            "Official Transform commands were not completed with their external effects retained.");
        foreach (var row in j.Commands.Where(x => x.Step.Kind == SeekHoldGeometryStepKind.HeadLocalPosition))
        {
            var note = baseline.Notes[row.Step.Ordinal];
            var ui = baseline.Transforms.Single(x => x.Reference == baseline.UiRootTransform);
            var camera = baseline.Transforms.Single(x => x.Reference == baseline.CameraTransform);
            var line = baseline.Transforms.Single(x => x.Reference == note.LineTransform);
            var end = baseline.Transforms.Single(x => x.Reference == note.EndLineTransform);
            var scale = ui.LocalScale.X * camera.LocalScale.X;
            var y = (note.NoteFloorPosition - note.StartFloorPosition) * 2;
            var q = y * scale / (end.WorldPosition.Y - line.WorldPosition.Y);
            Check(q < 0, "This independent parent oracle must exercise the clamped first ease sample.");
            var actual = f.Native.GeometryTransforms[new(note.NoteTransform)];
            Check(actual.Parent.ToInt64() == note.LineTransform && Same(actual.Position, new(0, y, -.4f / scale)),
                "Head placement substituted chart floor position for the actual nonidentity parent basis.");
            Check(Math.Abs(f.Native.GeometryWorldPosition(new(note.NoteTransform)).Y - y) > 1,
                "The parent oracle accidentally became an identity world transform.");
        }
        Check(f.Native.GeometryCalls.Where(x => x.Operation == "SetParent").All(x => x.WorldPositionStays == true),
            "Geometry changed the official world-preserving reparent contract.");
    }
    private static void Boundaries()
    {
        foreach (var time in new[] { -.01f, 0f, 8f, MathF.BitIncrement(8f) })
        {
            using var f = new Fixture(); var reference = f.Native.HoldStateNotes[0].ToInt64();
            var p = f.Prepare(new(time, 2, new(new[] { reference }, Array.Empty<long>()))); var j = p.GetJournal();
            Check(j.Baseline!.Notes[0].HeadChartTime == 0 && j.Baseline.Notes[0].TailChartTime == 8,
                "The boundary fixture no longer uses the native raw chart head/tail coordinates.");
            Check(j.Plan!.HoldNotes.Contains(reference) == (time < 0)
                && j.Plan.RunningHoldNotes.Contains(reference) == (time >= 0 && time <= 8),
                "Native head equality or strict tail completion changed.");
            Check(j.Plan.RequiredWork.Any(x => x.Kind == SeekHoldGeometryRequiredWorkKind.RendererEvent && x.EventIndex == 1)
                == (time > 8) && f.Native.GeometryCalls.Count == 0, "Boundary planning replayed renderer events.");
        }
        foreach (var chartTime in new[] { 1.5f, 2f, 4f, 8f, MathF.BitIncrement(8f) })
        {
            using var f = new Fixture(configure: n =>
            { n.TargetSetChart(0, 2, 2, 8); n.TargetSetTiming(0, 1, 4); });
            var reference = f.Native.HoldStateNotes[0].ToInt64();
            var journal = f.Prepare(new(chartTime, 2, new(new[] { reference }, Array.Empty<long>()))).GetJournal();
            Check(journal.Baseline!.Notes[0].HeadChartTime == 2 && journal.Baseline.Notes[0].TailChartTime == 8,
                "Hold geometry substituted cached game seconds for raw chart coordinates.");
            Check(journal.Plan!.HoldNotes.Contains(reference) == (chartTime < 2)
                && journal.Plan.RunningHoldNotes.Contains(reference) == (chartTime >= 2 && chartTime <= 8),
                "A nonidentity time conversion changed native Hold head/tail transition boundaries.");
            Check(f.Native.TargetFloat(f.Native.HoldStateNotes[0], "realTime") == 1
                && f.Native.TargetFloat(f.Native.HoldStateNotes[0], "holdEndRealTime") == 4 && f.Native.GeometryCalls.Count == 0,
                "Read-only chart-domain planning wrote the game-seconds timing caches.");
        }
    }
    private static void Epsilon()
    {
        float length;
        using (var first = new Fixture())
        {
            var a = first.Native.HoldStateNotes[0].ToInt64();
            var p = first.Prepare(new(-1, 2, new(new[] { a }, Array.Empty<long>())));
            Execute(first, p);
            length = (first.Native.GeometryWorldPosition(first.Native.GeometryEndTransforms[0]).Y
                - first.Native.GeometryWorldPosition(first.Native.GeometryNoteTransforms[0]).Y)
                / first.Native.GeometryTransforms[first.Native.GeometryUiRoot].Scale.X;
        }
        foreach (var delta in new[] { 0f, .0000005f, .000002f })
        {
            var previous = length + delta;
            using var f = new Fixture(configure: n => n.TargetSet(n.HoldStateNotes[0], "lastFrameHoldLineLength", previous));
            var a = f.Native.HoldStateNotes[0].ToInt64();
            var j = Execute(f, f.Prepare(new(-1, 2, new(new[] { a }, Array.Empty<long>()))));
            var row = j.Commands.Single(x => x.Step.Kind == SeekHoldGeometryStepKind.PendingLineScale);
            var noWrite = Math.Abs((double)(previous - length)) <= 1e-6;
            Check(row.NoWriteRequired == noWrite && (row.Operation is null) == noWrite,
                "Pending epsilon did not compare the rounded Single difference as a Double.");
            Check(Bits(j.HoldStateTargets!.Notes[0].LastFrameHoldLineLength) == Bits(noWrite ? previous : length),
                "A no-op epsilon branch changed the explicit cached target bits.");
        }
    }
    private static void ArithmeticRejections()
    {
        foreach (var request in new[] { Empty(float.NaN), Empty(float.PositiveInfinity), Empty(0, 0), Empty(0, -1), Empty(0, float.NaN) })
        {
            using var f = new Fixture(); Expect<ArgumentException>(() => f.Prepare(request));
            Check(f.Native.GeometryCalls.Count == 0, "Invalid request dispatched native geometry.");
        }
        using (var zero = new Fixture(configure: n => n.GeometrySetTransform(n.GeometryUiRoot,
            n.GeometryTransforms[n.GeometryUiRoot] with { Scale = new(0, 1, 1) })))
        { Expect<NotSupportedException>(() => zero.Prepare()); Check(zero.Native.GeometryCalls.Count == 0, "Zero scale wrote a Transform."); }
        using var overflow = new Fixture(configure: n => n.GeometrySetEndFloor(0, float.MaxValue));
        var p = overflow.Prepare(Empty()); Expect<NotSupportedException>(() => Execute(overflow, p));
        Check(!p.GetJournal().ExecutionFinished && p.GetJournal().HoldStateTargets is null,
            "An overflowing chosen operation was published as completed geometry.");
    }
    private static void ActualInstances()
    {
        foreach (var tailCanvas in new[] { -.4f, 0f, .4f })
        {
            using var rounded = new Fixture(configure: n => n.GeometrySetTailCanvas(0, tailCanvas));
            var note = rounded.Prepare(Empty()).GetJournal().Baseline!.Notes[0];
            Check(note.EndFlagCanvasParent == rounded.Native.GeometryCanvasParents[0].ToInt64(),
                "Tail canvas selection did not use Single-plus-half followed by native truncation.");
        }
        using (var rejected = new Fixture(configure: n => n.GeometrySetTailCanvas(0, .5f)))
            Expect<InvalidDataException>(() => rejected.Prepare(Empty()));
        using var f = new Fixture(FakeNative.NoteViewShape.MultipleSegments);
        var holds = f.Native.HoldStateNotes.Select(x => x.ToInt64()).ToArray();
        Check(holds.Length == 3 && holds[0] != holds[2]
            && f.Native.HoldStateData(new(holds[0])) == f.Native.HoldStateData(new(holds[2])), "The actual multi-instance fixture collapsed chart aliases.");
        var seed = new[] { holds[2], holds[0], holds[2] }; var p = f.Prepare(new(-1, 2, new(seed, Array.Empty<long>())));
        Check(p.GetJournal().Baseline!.Notes.Select(x => x.Reference).SequenceEqual(holds)
            && p.GetJournal().Plan!.HoldNotes.SequenceEqual(seed), "Actual instances or duplicate occurrences were reordered or deduplicated.");
    }
    private static void Aliases()
    {
        using (var f = new Fixture(configure: n => n.GeometryComponentTransforms[n.HoldStateNotes[1]] = n.GeometryNoteTransforms[0]))
        {
            var p = f.Prepare(Empty()); var b = p.GetJournal().Baseline!;
            Check(b.Notes[0].NoteTransform == b.Notes[1].NoteTransform
                && b.Transforms.Select(x => x.Reference).Distinct().Count() == b.Transforms.Count,
                "Shared Transform identity was duplicated in the full closure.");
            var j = Execute(f, p); Check(j.ExecutionFinished, "Shared Transform effects were not propagated into the current graph.");
        }
        using var alias = new Fixture(configure: n =>
        { n.TargetHoldLists[1] = n.TargetHoldLists[0]; n.TargetSet(n.TargetHold, "runningHoldNotes", n.TargetHoldLists[0]); });
        var a = alias.Native.HoldStateNotes[0].ToInt64();
        Expect<InvalidDataException>(() => alias.Prepare(new(-1, 2, new(new[] { a }, Array.Empty<long>()))));
        Check(alias.Native.GeometryCalls.Count == 0, "Contradictory aliased List targets reached a geometry setter.");
    }
    private static void ReparentReadback()
    {
        using var f = new Fixture(); var reference = f.Native.HoldStateNotes[0].ToInt64();
        var p = f.Prepare(new(3, 2, new(Array.Empty<long>(), new[] { reference })));
        float? nativeZ = null; NativeRendererQuaternion? rotation = null; NativeVector3? scale = null;
        f.Native.AfterGeometryStore = call =>
        {
            if (call.Operation == "SetParent" && call.Receiver == f.Native.GeometryNoteTransforms[0] && call.Parent == f.Native.GeometryRings[0])
            { var actual = f.Native.GeometryTransforms[call.Receiver]; nativeZ = actual.Position.Z; rotation = actual.Rotation; scale = actual.Scale; }
        };
        var j = Execute(f, p); var row = j.Commands.Single(x => x.Step.Kind == SeekHoldGeometryStepKind.ZeroLocalXY);
        var before = row.Before!.Transforms.Single(x => x.Reference == row.Operation!.Transform);
        Check(nativeZ is not null && Bits(nativeZ.Value) == Bits(row.Operation!.Value.Z)
            && Bits(nativeZ.Value) == Bits(before.LocalPosition.Z) && before.LocalRotation == rotation && before.LocalScale == scale,
            "The next step used pre-reparent local coordinates/rotation/scale instead of actual native readback.");
        Check(Bits(nativeZ.GetValueOrDefault()) != Bits(-.2f), "The reparent test did not actually change local Z.");
    }
}
