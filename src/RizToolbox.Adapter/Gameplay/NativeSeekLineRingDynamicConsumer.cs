using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekLineRingDynamicOccurrence(int Ordinal, long Line, long StartPoint,
    long EndPoint, int StartCanvasSlot, int EndCanvasSlot,
    int StartFloorIndex, int EndFloorIndex,
    SeekLineRingDynamicNumericResult Projection)
{
    // A culled producer occurrence leaves its existing renderer buffer alone.
    internal bool WouldSetPositions => Projection.Kind == SeekLineRingDynamicProjectionKind.Buffer;
}

/// <summary>
/// Projection of the actual dynamicLines occurrence array using two matching
/// historical observations of applied Canvas values, applied endpoint world
/// positions and current original LineControl/camera/UI inputs. It models only
/// the dynamic producer's chosen input buffer. No renderer setter is issued.
/// These sequential observations are not an atomic target-time engine state.
/// </summary>
internal sealed class SeekLineRingObservedDynamicPlan
{
    internal NativeSeekCanvasBasisCommands OriginalCanvasOwner { get; }
    internal NativeSeekLineEndpointCommands OriginalEndpointOwner { get; }
    internal NativeSeekLineRingSourceCapture OriginalSource { get; }
    internal SeekLineEndpointWorldSceneObservation ObservedInputs { get; }
    internal IReadOnlyList<SeekLineRingDynamicOccurrence> Occurrences { get; }
    internal int TotalBufferPoints { get; }
    internal bool AtomicSnapshot => false;
    internal bool DeterministicTarget => false;
    internal bool CompleteSeekPermitted => false;

    internal SeekLineRingObservedDynamicPlan(NativeSeekCanvasBasisCommands canvas,
        NativeSeekLineEndpointCommands endpoints, NativeSeekLineRingSourceCapture source,
        SeekLineEndpointWorldSceneObservation inputs, SeekLineRingDynamicOccurrence[] rows, int total)
    {
        OriginalCanvasOwner = canvas; OriginalEndpointOwner = endpoints;
        OriginalSource = source; ObservedInputs = inputs;
        Occurrences = Array.AsReadOnly(rows); TotalBufferPoints = total;
    }
}

internal static class NativeSeekLineRingDynamicConsumer
{
    [ThreadStatic] private static bool _projecting;

    internal static SeekLineRingObservedDynamicPlan Project(NativeSeekCanvasBasisCommands canvas,
        NativeSeekLineEndpointCommands endpoints)
    {
        if (_projecting) throw new InvalidOperationException("Dynamic producer input projection cannot synchronously reenter.");
        _projecting = true;
        try
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(endpoints);
            var source = endpoints.OriginalSource;
            if (!ReferenceEquals(canvas.SourceCapture, source.CanvasCapture))
                throw new InvalidOperationException("The endpoint writer belongs to another original Canvas capture.");

            // The first complete native observation brackets the pure arithmetic.
            // A prior journal or one child's separate confirmation is not proof.
            if (!canvas.VerifyAppliedValues()) throw new InvalidOperationException("Canvas applied values were not verified.");
            canvas.RequireAppliedLineRingSource(source);
            var world = endpoints.ReadAppliedWorldObservation();
            var paired = source.ReadSceneAfterAppliedWorld(world);
            void Guard()
            {
                canvas.RequireAppliedLineRingSource(source);
                endpoints.RequireIssuedWorldObservation(world);
            }
            Guard();
            var rows = ProjectOwned(paired, Guard);
            Guard();

            // Recheck both original owners and all current scalar inputs. The
            // result remains historical even if the ordered readings agree.
            if (!canvas.VerifyAppliedValues()) throw new InvalidOperationException("Canvas applied values changed after projection.");
            canvas.RequireAppliedLineRingSource(source);
            var laterWorld = endpoints.ReadAppliedWorldObservation();
            var later = source.ReadSceneAfterAppliedWorld(laterWorld);
            if (!Same(paired, later))
                throw new InvalidOperationException("Dynamic producer inputs changed between complete observations.");
            canvas.RequireAppliedLineRingSource(source);
            endpoints.RequireIssuedWorldObservation(laterWorld);

            var total = 0;
            foreach (var row in rows)
                total = checked(total + (row.Projection.PositionCount ?? 0));
            if (total > checked(NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences *
                    NativeSeekLineRingNumericMath.EaseSamples))
                throw new NotSupportedException("The complete dynamic output point budget was exceeded.");
            return new(canvas, endpoints, source, later, rows, total);
        }
        finally { _projecting = false; }
    }

    private static SeekLineRingDynamicOccurrence[] ProjectOwned(
        SeekLineEndpointWorldSceneObservation paired, Action guard)
    {
        guard();
        var world = paired.AppliedWorld;
        var projection = world.LocalProjection ?? throw new InvalidDataException("The writer omitted its original endpoint projection.");
        var source = SeekLineRingSourceState.Copy(projection.Source, guard);
        var basis = projection.Endpoints.CanvasBasis;
        basis.VerifyAssociation(source.Identity);
        if (!ReferenceEquals(world.OriginalSource, paired.Scene.OriginalSource)
            || !ReferenceEquals(projection.Source.Identity.CanvasCapture, basis.CanvasCapture)
            || !ReferenceEquals(projection.Endpoints.CanvasPlan, source.Identity.CanvasPlan)
            || world.SourceIdentity != source.Identity || paired.Scene.SourceIdentity != source.Identity)
            throw new InvalidOperationException("Observed world, source and Canvas Plan have different original provenance.");
        var plan = basis.CanvasPlan;
        if (!ReferenceEquals(plan, source.Identity.CanvasPlan)
            || plan.Rows.Count != basis.TransformReferences.Count)
            throw new InvalidDataException("The original Canvas slot table is incomplete.");
        var lineByReference = source.Lines.ToDictionary(line => line.Reference);
        var pointByReference = source.Points.ToDictionary(point => point.Reference);
        var bindingByLine = projection.Endpoints.Rows.ToDictionary(row => row.Line);
        var worldByTransform = world.Endpoints.ToDictionary(row => row.Transform);
        if (bindingByLine.Count != source.Lines.Count
            || worldByTransform.Count != world.Endpoints.Count)
            throw new InvalidDataException("The applied endpoint receipt omits a physical line binding or aliases a Transform inconsistently.");
        var scene = paired.Scene.Current;
        if (!float.IsFinite(scene.LineSpeed) || scene.LineSpeed <= 0f)
            throw new NotSupportedException("The observed LineControl speed must be finite and positive.");
        var results = new SeekLineRingDynamicOccurrence[source.DynamicLines.Count];
        var byLine = new Dictionary<long, SeekLineRingDynamicNumericResult>();
        var total = 0;
        for (var ordinal = 0; ordinal < results.Length; ++ordinal)
        {
            guard();
            var lineReference = source.DynamicLines[ordinal];
            if (lineReference == 0 || !lineByReference.TryGetValue(lineReference, out var line)
                || line.IsStatic || !bindingByLine.TryGetValue(lineReference, out var binding)
                || binding.EndBinding != SeekLineEndBinding.DedicatedCrossCanvasTransform
                || binding.StartPoint != line.StartPoint || binding.EndPoint != line.EndPoint
                || binding.StartCanvasSlot != line.CanvasIndex
                || binding.InitializedEndTransform != line.EndTransform)
                throw new InvalidDataException("An actual dynamic occurrence has no proved physical endpoint, Canvas slot or applied world tuple.");
            if (binding.InitializedEndCanvasSlot is not int endSlot
                || (uint)binding.StartCanvasSlot >= (uint)plan.Rows.Count
                || (uint)endSlot >= (uint)plan.Rows.Count
                || !pointByReference.TryGetValue(line.StartPoint, out var start)
                || !pointByReference.TryGetValue(line.EndPoint, out var end)
                || !worldByTransform.TryGetValue(line.Transform, out var startWorld)
                || !worldByTransform.TryGetValue(line.EndTransform, out var endWorld))
                throw new InvalidDataException("An actual dynamic endpoint has no complete start/end point, Canvas row or world sample.");
            if (startWorld.Parent != basis.TransformAt(binding.StartCanvasSlot)
                || endWorld.Parent != basis.TransformAt(endSlot))
                throw new InvalidDataException("An applied endpoint no longer has its proved original Canvas parent.");
            // SetDynamicLines indexes CanvasControl.floorPositions by the raw
            // LinePoint.canvasIndex. Start's CanvasMove.index lookup is a
            // separate association used only for physical endpoint parents.
            if ((uint)start.CanvasIndex >= (uint)plan.Rows.Count
                || (uint)end.CanvasIndex >= (uint)plan.Rows.Count)
                throw new InvalidDataException("A dynamic LinePoint raw floor array index is outside the complete Canvas Plan.");
            if ((uint)start.EaseType >= (uint)basis.Eases.Count)
                throw new InvalidDataException("The dynamic start point selects an uninitialized Ease type.");
            var input = new SeekLineRingDynamicNumericInput(start.FloorPosition, end.FloorPosition,
                plan.Rows[start.CanvasIndex].FloorPosition, plan.Rows[end.CanvasIndex].FloorPosition,
                scene.LineSpeed, startWorld.World, endWorld.World,
                scene.CameraScale, scene.LineGameSceneScale);
            var numeric = NativeSeekLineRingNumericMath.ProjectDynamic(input,
                basis.Eases[start.EaseType].Samples, guard);
            if (byLine.TryGetValue(lineReference, out var earlier))
            {
                if (!Same(earlier, numeric))
                    throw new InvalidDataException("Aliased dynamic occurrences demand conflicting buffers for one physical Line.");
            }
            else byLine.Add(lineReference, numeric);
            total = checked(total + (numeric.PositionCount ?? 0));
            if (total > checked(NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences *
                    NativeSeekLineRingNumericMath.EaseSamples))
                throw new NotSupportedException("The complete dynamic output point budget was exceeded.");
            results[ordinal] = new(ordinal, lineReference, line.StartPoint, line.EndPoint,
                binding.StartCanvasSlot, endSlot, start.CanvasIndex, end.CanvasIndex, numeric);
            guard();
        }
        return results;
    }

    private static bool Same(SeekLineRingDynamicNumericResult a, SeekLineRingDynamicNumericResult b)
    {
        if (a.Kind != b.Kind || a.Step != b.Step || a.PositionCount != b.PositionCount
            || a.Points.Count != b.Points.Count) return false;
        for (var i = 0; i < a.Points.Count; ++i)
            if (!Same(a.Points[i], b.Points[i])) return false;
        return true;
    }
    private static bool Same(SeekLineEndpointWorldSceneObservation a, SeekLineEndpointWorldSceneObservation b)
    {
        if (!ReferenceEquals(a.AppliedWorld.OriginalSource, b.AppliedWorld.OriginalSource)
            || !ReferenceEquals(a.AppliedWorld.CommandOwner, b.AppliedWorld.CommandOwner)
            || !ReferenceEquals(a.AppliedWorld.LocalProjection, b.AppliedWorld.LocalProjection)
            || a.AppliedWorld.SourceIdentity != b.AppliedWorld.SourceIdentity
            || a.AppliedWorld.Endpoints.Count != b.AppliedWorld.Endpoints.Count
            || a.Scene.SourceIdentity != b.Scene.SourceIdentity
            || !ReferenceEquals(a.Scene.OriginalSource, b.Scene.OriginalSource)) return false;
        var x = a.Scene.Current; var y = b.Scene.Current;
        if (x.Information != y.Information || x.UIControl != y.UIControl
            || x.CameraControl != y.CameraControl || x.GameSceneCanvas != y.GameSceneCanvas
            || x.LevelSceneWidthBits != y.LevelSceneWidthBits || x.LineSceneWidthBits != y.LineSceneWidthBits
            || x.LineGameSceneScaleBits != y.LineGameSceneScaleBits || x.LineNowTimeBits != y.LineNowTimeBits
            || x.LineSpeedBits != y.LineSpeedBits || x.CameraXBits != y.CameraXBits
            || x.CameraScaleBits != y.CameraScaleBits || !Same(x.GameSceneLocalScale, y.GameSceneLocalScale)) return false;
        for (var i = 0; i < a.AppliedWorld.Endpoints.Count; ++i)
        {
            var left = a.AppliedWorld.Endpoints[i]; var right = b.AppliedWorld.Endpoints[i];
            if (left.Transform != right.Transform || left.Parent != right.Parent
                || !Same(left.Local, right.Local) || !Same(left.World, right.World)) return false;
        }
        return true;
    }
    private static bool Same(NativeVector3 a, NativeVector3 b)
        => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
        && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);
}
