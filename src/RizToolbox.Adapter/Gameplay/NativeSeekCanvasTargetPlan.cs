using System.Runtime.CompilerServices;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekCanvasTargetKeyPoint(long Reference, float Time, float Value, int EaseType, float FloorPosition);
internal sealed record SeekCanvasTargetMove(long Reference, int Index,
    IReadOnlyList<SeekCanvasTargetKeyPoint> SpeedKeyPoints, IReadOnlyList<SeekCanvasTargetKeyPoint> XPositionKeyPoints);
internal sealed record SeekCanvasTargetRequest(float ChartTime, float Speed, float ScreenWidth, NativeVector3 Down, NativeVector3 Right);
internal sealed record SeekCanvasTargetRow(int Ordinal, long MoveReference, int SpeedSlot, int XSlot,
    float FloorPosition, float FloorSpeed, float XPosition, NativeVector3 LocalPosition);

/// <summary>Pure numeric projection over explicitly copied source data and controlled
/// range/ease callbacks. It neither captures native roots nor writes the target basis.
/// Physical canvas/line/ring producers and complete Seek remain separate requirements.</summary>
internal sealed class NativeSeekCanvasTargetPlan
{
    internal const int MaxCanvases = 65536, MaxKeypoints = 65536, MaxTotalKeypoints = 1048576;
    [ThreadStatic] private static bool _capturing;
    internal SeekCanvasTargetRequest Request { get; }
    internal IReadOnlyList<SeekCanvasTargetMove> Moves { get; }
    internal IReadOnlyList<SeekCanvasTargetRow> Rows { get; }
    internal bool CompleteSeekPermitted => false;

    private NativeSeekCanvasTargetPlan(SeekCanvasTargetRequest request, SeekCanvasTargetMove[] moves, SeekCanvasTargetRow[] rows)
    { Request = request; Moves = Array.AsReadOnly(moves); Rows = Array.AsReadOnly(rows); }
    internal static NativeSeekCanvasTargetPlan Capture(IReadOnlyList<SeekCanvasTargetMove> moves,
        SeekCanvasTargetRequest request, Func<double, double, float> interval, Func<float, int, float> ease, Action guard)
    {
        // Deny synchronous callback reentry before invoking even its nested guard.
        if (_capturing) throw new InvalidOperationException("Canvas projection cannot synchronously reenter its capture thread.");
        _capturing = true;
        try
        {
            ArgumentNullException.ThrowIfNull(moves); ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(interval); ArgumentNullException.ThrowIfNull(ease); ArgumentNullException.ThrowIfNull(guard);
            guard();
            if (!float.IsFinite(request.ChartTime) || !float.IsFinite(request.Speed) || !float.IsFinite(request.ScreenWidth)
                || !VectorFinite(request.Down) || !VectorFinite(request.Right))
                throw new InvalidDataException("Canvas projection requires finite request scalars and captured vector components.");
            var ownedRequest = request with { }; var total = 0;
            var ownedMoves = Copy(moves, MaxCanvases, false, move =>
            {
                if (move is null) throw new InvalidDataException("The complete Canvas source has an absent move.");
                var speed = Points(move.SpeedKeyPoints, guard, ref total);
                var x = Points(move.XPositionKeyPoints, guard, ref total);
                return move with { SpeedKeyPoints = Array.AsReadOnly(speed), XPositionKeyPoints = Array.AsReadOnly(x) };
            }, guard);
            // Validate and copy the complete source before invoking either producer.
            var rows = new SeekCanvasTargetRow[ownedMoves.Length];
            for (var ordinal = 0; ordinal < ownedMoves.Length; ++ordinal)
            {
                guard(); var move = ownedMoves[ordinal];
                var speedSlot = Select(move.SpeedKeyPoints, ownedRequest.ChartTime);
                var speedPoint = move.SpeedKeyPoints[speedSlot];
                guard(); var duration = interval((double)speedPoint.Time, (double)ownedRequest.ChartTime); guard();
                duration = Finite(duration);
                var distance = Finite(duration * speedPoint.Value);
                var floor = Finite(distance + speedPoint.FloorPosition);
                var xSlot = Select(move.XPositionKeyPoints, ownedRequest.ChartTime);
                var lower = move.XPositionKeyPoints[xSlot];
                var upper = move.XPositionKeyPoints[Math.Min(xSlot + 1, move.XPositionKeyPoints.Count - 1)];
                var span = Finite(upper.Time - lower.Time);
                float x;
                if (Math.Abs((double)span) <= 0.000001d) x = lower.Value;
                else
                {
                    var offset = Finite(ownedRequest.ChartTime - lower.Time);
                    var progress = Finite(offset / span);
                    guard(); var sample = ease(progress, lower.EaseType); guard();
                    sample = Math.Clamp(Finite(sample), 0f, 1f);
                    var difference = Finite(upper.Value - lower.Value);
                    var product = Finite(difference * sample);
                    x = Finite(product + lower.Value);
                }
                x = Finite(x * ownedRequest.ScreenWidth);
                var vertical = Finite(floor * ownedRequest.Speed);
                var dx = Finite(vertical * ownedRequest.Down.X);
                var dy = Finite(vertical * ownedRequest.Down.Y);
                var dz = Finite(vertical * ownedRequest.Down.Z);
                var rx = Finite(x * ownedRequest.Right.X);
                var ry = Finite(x * ownedRequest.Right.Y);
                var rz = Finite(x * ownedRequest.Right.Z);
                var local = new NativeVector3(Finite(dx + rx), Finite(dy + ry), Finite(dz + rz));
                rows[ordinal] = new(ordinal, move.Reference, speedSlot, xSlot, floor, speedPoint.Value, x, local);
                guard();
            }
            guard(); return new(ownedRequest, ownedMoves, rows);
        }
        finally { _capturing = false; }
    }
    private static bool VectorFinite(NativeVector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    // Preserve a Single operation boundary before the next multiply/add; do not
    // fold the required rounded native sequence into a fused operation.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Finite(float value)
    { if (!float.IsFinite(value)) throw new InvalidDataException("A Canvas Single intermediate is nonfinite or overflowed."); return value; }
    private static int Select(IReadOnlyList<SeekCanvasTargetKeyPoint> points, float time)
    {
        for (var slot = points.Count - 1; slot >= 0; --slot) if (time > points[slot].Time) return slot;
        return 0;
    }
    private static SeekCanvasTargetKeyPoint[] Points(IReadOnlyList<SeekCanvasTargetKeyPoint> points, Action guard, ref int total)
    {
        var rows = Copy(points, MaxKeypoints, true, point =>
        {
            if (point is null || !float.IsFinite(point.Time) || !float.IsFinite(point.Value) || !float.IsFinite(point.FloorPosition))
                throw new InvalidDataException("The complete keypoint source has an absent or nonfinite row.");
            return point with { };
        }, guard);
        total = checked(total + rows.Length);
        if (total > MaxTotalKeypoints) throw new NotSupportedException("The complete Canvas keypoint budget is exceeded.");
        return rows;
    }
    private static T[] Copy<T>(IReadOnlyList<T> input, int maximum, bool nonempty, Func<T, T> clone, Action guard)
    {
        ArgumentNullException.ThrowIfNull(input); guard(); var count = input.Count; guard();
        if (count < 0 || count > maximum || nonempty && count == 0) throw new InvalidDataException("The complete Canvas source count is invalid or exceeds its bound.");
        var rows = new List<T>(count);
        guard(); using var iterator = input.GetEnumerator(); guard();
        while (true)
        {
            guard(); var more = iterator.MoveNext(); guard();
            if (!more) break;
            var row = iterator.Current; guard();
            if (rows.Count >= count) throw new InvalidDataException("Source enumeration has an extra row beyond its declared count.");
            rows.Add(clone(row)); guard();
        }
        guard(); var after = input.Count; guard();
        if (after != count || rows.Count != count) throw new InvalidOperationException("Complete Canvas source coverage changed during copying.");
        return rows.ToArray();
    }
}
