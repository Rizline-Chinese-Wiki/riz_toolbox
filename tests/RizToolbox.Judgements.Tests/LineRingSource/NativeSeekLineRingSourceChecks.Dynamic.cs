using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    private static void ConfigureDynamicInputs(FakeNative n)
    {
        FiniteScene(n);
        // The producer's current speed intentionally differs from the Canvas
        // Plan request speed of 3. The floor window is broad enough to expose
        // the actual buffer, while the small UI Y scale keeps both world Y
        // endpoints inside the pinned [-5,+5] window.
        n.LineRingSet(n.LineRingControl, "_speed", .01f);
        n.LineRingSet(n.LineRingControl, "_nowTime", 5f);
        n.LineRingSet(n.LineRingControl, "_gameSceneScale", 4f);
        n.LineRingSet(n.GeometryCamera, "scale", 2f);
        n.GeometrySetTransform(n.GeometryUiRoot, n.GeometryTransforms[n.GeometryUiRoot] with
        { Position = new(0, 0, 0), Rotation = new(0, 0, 0, 1), Scale = new(2, .01f, 1) });
    }

    private static (WriterFixture Fixture, NativeSeekLineEndpointCommands Writer) StartDynamic(
        Action<FakeNative>? configure = null)
    {
        var f = new WriterFixture(parentEqual: false, configure: n =>
        {
            ConfigureDynamicInputs(n);
            configure?.Invoke(n);
        });
        try
        {
            f.Basis.Execute();
            var writer = f.Prepare();
            writer.Execute();
            return (f, writer);
        }
        catch { f.Dispose(); throw; }
    }

    private static void RetainDynamic(WriterFixture f, NativeSeekLineEndpointCommands writer)
    {
        Reject(writer.Dispose);
        f.Retain();
    }

    private static void DynamicAppliedNoNotes()
    {
        var (f, writer) = StartDynamic();
        using (f)
        {
            var n = f.Native;
            var beforeSetters = n.GeometryCalls.Count;
            var beforeRenderer = n.RendererCalls.Count;
            var beforeRanges = n.CanvasSourceRangeAttempts;
            var result = NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer);
            var source = f.Source!;
            Check(result.Occurrences.Count == 2 && source.Snapshot.NoteJoins.Count == 0
                && result.Occurrences[0].Ordinal == 0 && result.Occurrences[1].Ordinal == 1
                && result.Occurrences[0].Line == result.Occurrences[1].Line
                && result.Occurrences.All(row => row.WouldSetPositions
                    && row.Projection.Kind == SeekLineRingDynamicProjectionKind.Buffer
                    && row.Projection.Step == 1 && row.Projection.PositionCount == 101
                    && row.Projection.Points.Count == 101)
                && result.TotalBufferPoints == 202
                && ReferenceEquals(result.OriginalCanvasOwner, f.Basis)
                && ReferenceEquals(result.OriginalEndpointOwner, writer)
                && ReferenceEquals(result.OriginalSource, source)
                && !result.AtomicSnapshot && !result.DeterministicTarget && !result.CompleteSeekPermitted,
                "The actual no Note dynamic array was not projected as two ordered, nonempty alias occurrences.");

            var occurrence = result.Occurrences[0];
            var line = source.Snapshot.Lines.Single(row => row.Reference == occurrence.Line);
            var start = n.GeometryWorldPosition(new(line.Transform));
            var end = n.GeometryWorldPosition(new(line.EndTransform));
            var input = occurrence.Projection.Input;
            var points = occurrence.Projection.Points;
            var pointSource = source.Snapshot.Points.Single(row => row.Reference == occurrence.StartPoint);
            Check(Same(input.StartWorld, start) && Same(input.EndWorld, end)
                && Bits(input.Speed) == Bits(.01f)
                && Bits(input.CameraScale) == Bits(2f) && Bits(input.SceneScale) == Bits(4f)
                && Bits(result.ObservedInputs.Scene.Current.GameSceneScaleX) == Bits(2f)
                && pointSource.EaseType == 1
                && occurrence.StartCanvasSlot == 0 && occurrence.EndCanvasSlot == 1
                && Bits(input.StartCanvasFloor) == Bits(f.Basis.SourceCapture.Plan.Rows[0].FloorPosition)
                && Bits(input.EndCanvasFloor) == Bits(f.Basis.SourceCapture.Plan.Rows[1].FloorPosition)
                && Bits(points[0].Y) == Bits(0f) && Bits(points[0].Z) == Bits(0f)
                && Math.Abs(points[100].Y - ((end.Y - start.Y) / 2f) / 4f) < .00001f
                && Math.Abs(points[50].X - ((.375f * (end.X - start.X)) / 2f) / 4f) < .0001f
                && Math.Abs(points[51].X - ((.875f * (end.X - start.X)) / 2f) / 4f) < .0001f
                && Same(points[50], result.Occurrences[1].Projection.Points[50])
                && n.GeometryCalls.Count == beforeSetters && n.RendererCalls.Count == beforeRenderer
                && n.CanvasSourceRangeAttempts == beforeRanges,
                "The buffer did not use actual applied world positions, actual speed, original slots, or integer Ease samples.");
            RetainDynamic(f, writer);
        }
    }

    private static void DynamicOriginalOwnerGates()
    {
        using var f = new WriterFixture(parentEqual: false, configure: ConfigureDynamicInputs);
        var writer = f.Prepare(); writer.Execute();
        Reject(() => NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer));
        Check(!f.Basis.GetJournal().ExecutionFinished, "A pre-Canvas projection unexpectedly applied Canvas values.");
        f.Basis.Execute();
        using var other = new WriterFixture(parentEqual: false, configure: ConfigureDynamicInputs);
        NoNative(f.Native, () => Reject(() => NativeSeekLineRingDynamicConsumer.Project(other.Basis, writer)));
        NoNative(f.Native, () => Task.Run(() => Reject(() =>
            NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer))).GetAwaiter().GetResult());
        var result = NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer);
        Check(result.Occurrences.Any(row => row.WouldSetPositions),
            "The exact original Canvas owner did not admit its current applied values.");
        RetainDynamic(f, writer);
    }

    private static void DynamicCullAndEmpty()
    {
        var (future, writer) = StartDynamic(n =>
        {
            n.LineRingSet(n.NoteViewPoints[0][0], "floorPosition", 10000f);
            n.LineRingSet(n.NoteViewPoints[0][1], "floorPosition", 10000f);
        });
        using (future)
        {
            var result = NativeSeekLineRingDynamicConsumer.Project(future.Basis, writer);
            Check(result.Occurrences.Count == 2 && result.Occurrences.All(row =>
                !row.WouldSetPositions && row.Projection.Kind == SeekLineRingDynamicProjectionKind.OutsideFutureFloor
                && row.Projection.PositionCount is null && row.Projection.Points.Count == 0)
                && result.TotalBufferPoints == 0,
                "A floor cull was misreported as a zero length SetPositions buffer.");
            RetainDynamic(future, writer);
        }
        var (empty, emptyWriter) = StartDynamic(n => n.LineRingSetDynamic());
        using (empty)
        {
            var result = NativeSeekLineRingDynamicConsumer.Project(empty.Basis, emptyWriter);
            Check(result.Occurrences.Count == 0 && result.TotalBufferPoints == 0
                && empty.Source!.Snapshot.Groups.Count > 0 && !result.CompleteSeekPermitted,
                "A static or group only graph manufactured a dynamic buffer occurrence.");
            RetainDynamic(empty, emptyWriter);
        }
    }

    private static void DynamicChangingInputs()
    {
        var (f, writer) = StartDynamic();
        using (f)
        {
            var n = f.Native;
            var fired = false;
            n.AfterCanvasSourceRead = (name, receiver) =>
            {
                // Camera.scale is read only after the scene's first _speed.
                // Changing speed here makes the two complete scene samples
                // disagree, rather than only changing an earlier baseline.
                if (fired || name != "scale" || receiver != n.GeometryCamera) return;
                fired = true;
                n.LineRingSet(n.LineRingControl, "_speed", .02f);
            };
            Reject(() => NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer));
            n.AfterCanvasSourceRead = null;
            Check(fired && n.RendererCalls.Count == 0,
                "An unstable current LineControl speed produced a renderer input buffer.");
            var current = NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer);
            Check(current.Occurrences.Any(row => row.WouldSetPositions
                && Bits(row.Projection.Input.Speed) == Bits(.02f)),
                "A later stable current LineControl speed was replaced by the Canvas request speed.");
            RetainDynamic(f, writer);
        }

        var (drifting, driftWriter) = StartDynamic();
        using (drifting)
        {
            // Keep already-entered setter responsibilities with their original
            // owners even when an assertion fails; Dispose must not mask it.
            drifting.Retain();
            var n = drifting.Native;
            var fired = false;
            n.AfterCanvasSourceRead = (name, receiver) =>
            {
                // The first world receipt has already been issued before the
                // consumer requests this scene sample. A change before that
                // receipt would merely establish another stable history.
                if (fired || name != "scale" || receiver != n.GeometryCamera) return;
                fired = true;
                var old = n.GeometryTransforms[n.GeometryUiRoot];
                n.GeometrySetTransform(n.GeometryUiRoot, old with { Position = new(0, .5f, 0) });
            };
            Reject(() => NativeSeekLineRingDynamicConsumer.Project(drifting.Basis, driftWriter));
            n.AfterCanvasSourceRead = null;
            Check(fired && n.RendererCalls.Count == 0,
                "A changing applied endpoint world tuple produced a dynamic renderer buffer.");
            RetainDynamic(drifting, driftWriter);
        }
    }

    private static void DynamicRestoration()
    {
        var (f, writer) = StartDynamic();
        using (f)
        {
            var old = NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer);
            var first = old.Occurrences[0].Projection.Points[50];
            writer.RestoreSelectedValues();
            Check(writer.VerifyRestoredValues(), "The endpoint writer did not restore its selected values.");
            Reject(() => NativeSeekLineRingDynamicConsumer.Project(f.Basis, writer));
            Check(Same(first, old.Occurrences[0].Projection.Points[50])
                && old.Occurrences[0].Projection.PositionCount == 101,
                "A later restoration rewrote the owned historical buffer.");
            RetainDynamic(f, writer);
        }
    }

    private static void DynamicRawFloorVsPhysicalSlot()
    {
        var (swapped, writer) = StartDynamic(n =>
        {
            n.CanvasSourceSet(n.CanvasSourceMoves[0], "index", 1);
            n.CanvasSourceSet(n.CanvasSourceMoves[1], "index", 0);
            var dynamic = n.CanvasSourceSlots(n.LineRingDynamicArray)[0];
            var groupOnly = n.CanvasSourceSlots(n.LineRingGroupArray(n.LineRingGroups[0]))[1];
            n.LineRingSet(dynamic, "canvasIndex", 1);
            n.LineRingSet(groupOnly, "canvasIndex", 0);
        });
        using (swapped)
        {
            var n = swapped.Native;
            var result = NativeSeekLineRingDynamicConsumer.Project(swapped.Basis, writer);
            var row = result.Occurrences[0];
            var plan = swapped.Basis.SourceCapture.Plan;
            var physicalLine = swapped.Source!.Snapshot.Lines.Single(line => line.Reference == row.Line);
            var worldStart = result.ObservedInputs.AppliedWorld.Endpoints.Single(x => x.Transform == physicalLine.Transform);
            var worldEnd = result.ObservedInputs.AppliedWorld.Endpoints.Single(x => x.Transform == physicalLine.EndTransform);
            Check(result.Occurrences.Count == 2 && row.WouldSetPositions
                && row.StartCanvasSlot == 1 && row.EndCanvasSlot == 0
                && row.StartFloorIndex == 0 && row.EndFloorIndex == 1
                && Bits(plan.Rows[0].FloorPosition) != Bits(plan.Rows[1].FloorPosition)
                && Bits(row.Projection.Input.StartCanvasFloor) == Bits(plan.Rows[0].FloorPosition)
                && Bits(row.Projection.Input.EndCanvasFloor) == Bits(plan.Rows[1].FloorPosition)
                && worldStart.Parent == swapped.Source.ReadNumericBasis().TransformAt(1)
                && worldEnd.Parent == swapped.Source.ReadNumericBasis().TransformAt(0)
                && Same(row.Projection.Input.StartWorld, n.GeometryWorldPosition(new(physicalLine.Transform)))
                && Same(row.Projection.Input.EndWorld, n.GeometryWorldPosition(new(physicalLine.EndTransform))),
                "The producer floor array index was conflated with Start's physical CanvasMove match.");
            RetainDynamic(swapped, writer);
        }

        var (outside, outsideWriter) = StartDynamic(n =>
        {
            n.CanvasSourceSet(n.CanvasSourceMoves[0], "index", 999);
            n.LineRingSet(n.NoteViewPoints[0][0], "canvasIndex", 999);
        });
        using (outside)
        {
            Reject(() => NativeSeekLineRingDynamicConsumer.Project(outside.Basis, outsideWriter));
            Check(outside.Native.RendererCalls.Count == 0,
                "An out-of-bounds raw floor index reached a renderer setter.");
            RetainDynamic(outside, outsideWriter);
        }
    }
}
