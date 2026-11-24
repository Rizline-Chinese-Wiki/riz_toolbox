using System.Runtime.CompilerServices;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal enum SeekLineEndpointLocalRole { Start, DedicatedCrossCanvasEnd }
internal enum SeekLineEndBinding { DedicatedCrossCanvasTransform, SameCanvasEndLineReference, SameCanvasNoEndLine }

internal sealed record SeekLineEndpointLocalWrite(long Line, SeekLineEndpointLocalRole Role,
    long Transform, int CanvasSlot, long ParentTransform, NativeVector3 LocalPosition);
internal sealed record SeekLineEndpointLocalRow(long Line, long StartPoint, long EndPoint,
    int StartCanvasSlot, SeekLineEndBinding EndBinding, long EndLine,
    long InitializedEndTransform, int? InitializedEndCanvasSlot);

/// <summary>
/// Pure, physical Transform-local initialization candidates for the exact
/// LevelControl.Start image. CanvasMove.index is matched by value, with repeated
/// values outside this support domain; the raw LinePoint.canvasIndex is never
/// substituted for the actual array slot. The Canvas request supplies speed.
/// This does not reconstruct world position, prefab rotation/scale, renderer,
/// ring seed, or admit complete Seek.
/// </summary>
internal sealed class NativeSeekLineEndpointLocalTarget
{
    [ThreadStatic] private static bool _projecting;
    // Frozen LevelControl.Start: 0x3eafe0, 0x3eafe9, 0x3eaff2, 0x3eb8be.
    // The literal X width is independent of CanvasPlan.Request.ScreenWidth.
    private static readonly float XWidth = BitConverter.Int32BitsToSingle(0x40b40000);
    private static readonly float ZOffset = BitConverter.Int32BitsToSingle(0x3f19999a);
    private static readonly float StartTimeScale = BitConverter.Int32BitsToSingle(unchecked((int)0xb727c5ac));
    private static readonly float CrossEndTimeScale = BitConverter.Int32BitsToSingle(unchecked((int)0xb8d1b717));

    internal SeekCanvasNumericBasis CanvasBasis { get; }
    internal NativeSeekCanvasTargetPlan CanvasPlan => CanvasBasis.CanvasPlan;
    internal IReadOnlyList<SeekLineEndpointLocalWrite> LocalTargets { get; }
    internal IReadOnlyList<SeekLineEndpointLocalRow> Rows { get; }
    internal bool CompleteSeekPermitted => false;

    private NativeSeekLineEndpointLocalTarget(SeekCanvasNumericBasis basis,
        SeekLineEndpointLocalWrite[] writes, SeekLineEndpointLocalRow[] rows)
    {
        CanvasBasis = basis;
        LocalTargets = Array.AsReadOnly(writes);
        Rows = Array.AsReadOnly(rows);
    }

    internal static NativeSeekLineEndpointLocalTarget Project(SeekCanvasNumericBasis basis,
        SeekLineRingSourceSnapshot source, Action guard)
    {
        if (_projecting) throw new InvalidOperationException("Endpoint local projection cannot synchronously reenter.");
        _projecting = true;
        try
        {
            ArgumentNullException.ThrowIfNull(basis);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(guard);
            guard();
            // SourceState's bounded copy checks the entire physical graph. No
            // Note join, baseline pose, getter, or producer participates.
            var owned = SeekLineRingSourceState.Copy(source, guard);
            guard(); basis.VerifyAssociation(owned.Identity); guard();
            var speed = basis.CanvasPlan.Request.Speed;
            if (!float.IsFinite(speed) || speed <= 0f)
                throw new InvalidDataException("The explicit Canvas target speed must be finite and positive.");
            if (basis.CanvasPlan.Moves.Count != basis.TransformReferences.Count
                || basis.CanvasPlan.Rows.Count != basis.TransformReferences.Count)
                throw new InvalidDataException("The original Canvas move/Transform slot association is incomplete.");
            // Start continues its outer slot loop after a match. In particular,
            // cross-canvas endTransform may be overwritten by a later match.
            // Do not collapse this into a first/last match shortcut.
            var slotByValue = new Dictionary<int, int>();
            for (var slot = 0; slot < basis.CanvasPlan.Moves.Count; ++slot)
            {
                guard();
                if (!slotByValue.TryAdd(basis.CanvasPlan.Moves[slot].Index, slot))
                    throw new NotSupportedException("Repeated CanvasMove.index values have ambiguous Start slot iteration.");
            }

            var points = owned.Points.ToDictionary(p => p.Reference);
            var lines = owned.Lines.ToDictionary(line => line.Reference);
            var groups = owned.Groups.ToDictionary(group => group.Reference);
            foreach (var reference in owned.DynamicLines)
            {
                guard();
                if (reference == 0 || !lines.ContainsKey(reference))
                    throw new InvalidDataException("A complete dynamic occurrence has no actual physical Line.");
            }
            foreach (var groupReference in owned.LineGroups)
            {
                guard();
                if (groupReference == 0 || !groups.TryGetValue(groupReference, out var group))
                    throw new InvalidDataException("A complete group occurrence has no actual physical group.");
                foreach (var reference in group.Lines)
                {
                    guard();
                    if (reference == 0 || !lines.ContainsKey(reference))
                        throw new InvalidDataException("A complete segment occurrence has no actual physical Line.");
                }
            }

            var writes = new List<SeekLineEndpointLocalWrite>(checked(owned.Lines.Count * 2));
            var rows = new List<SeekLineEndpointLocalRow>(owned.Lines.Count);
            var byTransform = new Dictionary<long, SeekLineEndpointLocalWrite>();
            foreach (var line in owned.Lines)
            {
                guard();
                if (line.StartPoint == 0 || line.EndPoint == 0 || line.Transform == 0
                    || !points.TryGetValue(line.StartPoint, out var start)
                    || !points.TryGetValue(line.EndPoint, out var end))
                    throw new InvalidDataException("A physical Line lacks its complete actual endpoints or start Transform.");
                if (!Finite(start) || !Finite(end))
                    throw new InvalidDataException("An actual endpoint source scalar is nonfinite.");
                if (!slotByValue.TryGetValue(start.CanvasIndex, out var startSlot)
                    || !slotByValue.TryGetValue(end.CanvasIndex, out var endSlot))
                    throw new InvalidDataException("A LinePoint canvas value has no actual CanvasMove.index match.");
                if (line.CanvasIndex != startSlot || line.IsStatic != (start.CanvasIndex == end.CanvasIndex))
                    throw new InvalidDataException("A physical Line contradicts Start's canvas match or static branch.");
                Add(new(line.Reference, SeekLineEndpointLocalRole.Start, line.Transform,
                    startSlot, basis.TransformAt(startSlot), Local(start, start.Time, speed, StartTimeScale)));

                if (!line.IsStatic)
                {
                    if (line.EndTransform == 0)
                        throw new InvalidDataException("A cross-canvas Line lacks its dedicated end Transform.");
                    Add(new(line.Reference, SeekLineEndpointLocalRole.DedicatedCrossCanvasEnd,
                        line.EndTransform, endSlot, basis.TransformAt(endSlot),
                        Local(end, start.Time, speed, CrossEndTimeScale)));
                    rows.Add(new(line.Reference, line.StartPoint, line.EndPoint, startSlot,
                        SeekLineEndBinding.DedicatedCrossCanvasTransform, line.EndLine,
                        line.EndTransform, endSlot));
                }
                else if (line.EndLine != 0)
                {
                    // Start 0x3ebf51..60 publishes this actual endLine reference,
                    // but its saved previous pointer is not updated on every
                    // static segment. It does not prove a shared endpoint
                    // Transform, even when a particular chart has matching points.
                    rows.Add(new(line.Reference, line.StartPoint, line.EndPoint, startSlot,
                        SeekLineEndBinding.SameCanvasEndLineReference, line.EndLine,
                        0, null));
                }
                else
                {
                    // The same-canvas branch writes a static buffer, not
                    // LineInformation.endTransform. A last segment may be here,
                    // but no physical end Transform is bound by this Start path.
                    rows.Add(new(line.Reference, line.StartPoint, line.EndPoint, startSlot,
                        SeekLineEndBinding.SameCanvasNoEndLine, 0, 0, null));
                }
                guard();
            }
            guard();
            return new(basis, writes.ToArray(), rows.ToArray());

            void Add(SeekLineEndpointLocalWrite next)
            {
                guard();
                if (byTransform.TryGetValue(next.Transform, out var prior))
                {
                    if (prior.CanvasSlot != next.CanvasSlot || prior.ParentTransform != next.ParentTransform
                        || !Same(prior.LocalPosition, next.LocalPosition))
                        throw new InvalidDataException("Aliased physical endpoint Transforms demand contradictory target locals.");
                }
                else byTransform.Add(next.Transform, next);
                writes.Add(next);
                guard();
            }
        }
        finally { _projecting = false; }
    }
    private static bool Finite(SeekLineRingPoint point)
        => float.IsFinite(point.Time) && float.IsFinite(point.XPosition) && float.IsFinite(point.FloorPosition);
    private static bool Same(NativeVector3 a, NativeVector3 b)
        => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
        && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);
    private static NativeVector3 Local(SeekLineRingPoint point, float startTime, float speed, float timeScale)
        => new(FiniteStep(point.XPosition * XWidth), FiniteStep(point.FloorPosition * speed),
            FiniteStep(FiniteStep(startTime * timeScale) + ZOffset));
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float FiniteStep(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("An endpoint Single intermediate is nonfinite.");
        return value;
    }
}
