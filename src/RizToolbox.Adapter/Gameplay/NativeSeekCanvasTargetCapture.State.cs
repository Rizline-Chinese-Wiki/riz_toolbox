using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Bounded managed copies and complete bitwise source comparison. A
/// matching pair is sampled consistency, not atomicity or exclusion of ABA.</summary>
internal static class SeekCanvasSourceState
{
    internal static SeekCanvasSourceSnapshot Copy(SeekCanvasSourceSnapshot raw, Action guard)
    {
        ArgumentNullException.ThrowIfNull(raw); ArgumentNullException.ThrowIfNull(guard); guard();
        var identity = raw.Identity;
        if (identity is null || identity.Notes is null || identity.Notes.Chart is null || identity.Notes.Chart.Run is null
            || identity.Information == 0 || identity.Canvas == 0 || identity.Progress == 0
            || identity.Information != identity.Notes.Chart.Run.Information || identity.Progress != identity.Notes.Chart.Progress
            || identity.Notes.NoteControl == 0 || identity.Notes.RenderNotes == 0 || identity.Notes.AllNotes == 0
            || identity.Notes.Chart.Chart == 0 || identity.Notes.Chart.Lines == 0
            || raw.TransformsArray == 0 || raw.MovesArray == 0 || raw.EaseArray == 0)
            throw new InvalidDataException("The complete actual Canvas source identity/array association is absent or inconsistent.");
        var ownedIdentity = identity with { Notes = identity.Notes with
        { Chart = identity.Notes.Chart with { Run = identity.Notes.Chart.Run with { } } } };
        if (!float.IsFinite(raw.ScreenWidth) || !Finite(raw.Down) || !Finite(raw.Right))
            throw new InvalidDataException("The actual Canvas width and complete static Vector3 inputs must be finite.");
        var transforms = Rows(raw.Transforms, NativeSeekCanvasTargetPlan.MaxCanvases, false, value =>
        {
            if (value == 0) throw new InvalidDataException("A complete actual Canvas Transform slot is null.");
            return value;
        }, guard);
        var totalPoints = 0; var totalCapacity = 0;
        SeekCanvasSourceList List(SeekCanvasSourceList list)
        {
            if (list is null || list.Reference == 0 || list.Backing == 0 || list.Size <= 0
                || list.Size > NativeSeekCanvasTargetPlan.MaxKeypoints)
                throw new InvalidDataException("An actual Canvas KeyPoint List is absent, empty or outside its complete bound.");
            var backing = Rows(list.BackingReferences, NativeSeekCanvasTargetPlan.MaxKeypoints, true, value => value, guard);
            var points = Rows(list.Points, NativeSeekCanvasTargetPlan.MaxKeypoints, true, point =>
            {
                if (point is null || point.Reference == 0 || !float.IsFinite(point.Time)
                    || !float.IsFinite(point.Value) || !float.IsFinite(point.FloorPosition))
                    throw new InvalidDataException("A complete actual Canvas KeyPoint row is absent or nonfinite.");
                return point with { };
            }, guard);
            if (points.Length != list.Size || list.Size > backing.Length)
                throw new InvalidDataException("The actual Canvas List active prefix does not match its declared size/capacity.");
            for (var slot = 0; slot < points.Length; ++slot)
            { guard(); if (points[slot].Reference != backing[slot]) throw new InvalidDataException("An actual Canvas List getter differs from its exact active backing slot."); }
            totalPoints = checked(totalPoints + points.Length); totalCapacity = checked(totalCapacity + backing.Length);
            if (totalPoints > NativeSeekCanvasTargetPlan.MaxTotalKeypoints || totalCapacity > NativeSeekCanvasTargetPlan.MaxTotalKeypoints)
                throw new NotSupportedException("The complete actual Canvas KeyPoint/capacity aggregate budget is exceeded.");
            return list with { BackingReferences = Array.AsReadOnly(backing), Points = Array.AsReadOnly(points) };
        }
        var moves = Rows(raw.Moves, NativeSeekCanvasTargetPlan.MaxCanvases, false, move =>
        {
            if (move is null || move.Reference == 0) throw new InvalidDataException("A complete actual CanvasMove slot is absent.");
            return move with { Speed = List(move.Speed), X = List(move.X) };
        }, guard);
        if (moves.Length != transforms.Length)
            throw new InvalidDataException("The complete actual Transform and CanvasMove vectors have different lengths.");
        var eases = Rows(raw.Eases, NativeSeekCanvasTargetCapture.MaximumEaseRows, true, row =>
        {
            if (row is null || row.Reference == 0 || row.Progress == 0)
                throw new InvalidDataException("An actual initialized Ease row/sample array is absent.");
            var samples = Rows(row.Samples, 101, true, value =>
            {
                if (!float.IsFinite(value)) throw new InvalidDataException("A complete actual Ease table contains a nonfinite sample.");
                return value;
            }, guard);
            if (samples.Length != 101) throw new InvalidDataException("An actual initialized Ease row must contain exactly 101 Single samples.");
            return row with { Samples = Array.AsReadOnly(samples) };
        }, guard);
        for (var slot = 0; slot < eases.Length; ++slot)
        { guard(); if (eases[slot].Code != slot) throw new InvalidDataException("The actual initialized Ease code does not match its unique array slot."); }
        var pointsByReference = new Dictionary<long, SeekCanvasTargetKeyPoint>();
        var listsByReference = new Dictionary<long, SeekCanvasSourceList>();
        var movesByReference = new Dictionary<long, SeekCanvasSourceMove>();
        foreach (var move in moves)
        {
            guard();
            if (movesByReference.TryGetValue(move.Reference, out var priorMove) && !Same(move, priorMove))
                throw new InvalidOperationException("Repeated actual CanvasMove occurrences have inconsistent captured values.");
            movesByReference[move.Reference] = move;
            foreach (var list in new[] { move.Speed, move.X })
            {
                guard();
                if (listsByReference.TryGetValue(list.Reference, out var priorList) && !Same(list, priorList))
                    throw new InvalidOperationException("Aliased actual Canvas Lists have inconsistent captured storage.");
                listsByReference[list.Reference] = list;
                foreach (var point in list.Points)
                {
                    guard();
                    if (pointsByReference.TryGetValue(point.Reference, out var priorPoint) && !Same(point, priorPoint))
                        throw new InvalidOperationException("Repeated actual KeyPoint occurrences have inconsistent captured values.");
                    pointsByReference[point.Reference] = point;
                }
            }
            foreach (var point in move.X.Points)
            {
                guard();
                if ((uint)point.EaseType >= (uint)eases.Length)
                    throw new InvalidDataException("A complete actual X KeyPoint references an unknown or uninitialized Ease row.");
            }
        }
        guard();
        return raw with { Identity = ownedIdentity, Transforms = Array.AsReadOnly(transforms),
            Moves = Array.AsReadOnly(moves), Eases = Array.AsReadOnly(eases) };
    }
    private static T[] Rows<T>(IReadOnlyList<T> input, int maximum, bool nonempty, Func<T, T> clone, Action guard)
    {
        ArgumentNullException.ThrowIfNull(input); guard(); var count = input.Count; guard();
        if (count < 0 || count > maximum || nonempty && count == 0)
            throw new InvalidDataException("A complete actual Canvas source count is absent or exceeds its bound.");
        var rows = new List<T>(count);
        guard(); using var iterator = input.GetEnumerator(); guard();
        while (true)
        {
            guard(); var more = iterator.MoveNext(); guard(); if (!more) break;
            var row = iterator.Current; guard();
            if (rows.Count >= count) throw new InvalidDataException("Actual Canvas source enumeration exceeds its declared count.");
            rows.Add(clone(row)); guard();
        }
        guard(); var after = input.Count; guard();
        if (after != count || rows.Count != count)
            throw new InvalidOperationException("Complete actual Canvas source coverage changed while copying.");
        return rows.ToArray();
    }
    private static bool Finite(NativeVector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    private static bool Same(NativeVector3 a, NativeVector3 b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);
    private static bool Same(SeekCanvasTargetKeyPoint a, SeekCanvasTargetKeyPoint b)
        => a.Reference == b.Reference && Same(a.Time, b.Time) && Same(a.Value, b.Value)
            && a.EaseType == b.EaseType && Same(a.FloorPosition, b.FloorPosition);
    private static bool Same(SeekCanvasSourceList a, SeekCanvasSourceList b)
    {
        if (a.Reference != b.Reference || a.Backing != b.Backing || a.Size != b.Size || a.Version != b.Version
            || !a.BackingReferences.SequenceEqual(b.BackingReferences) || a.Points.Count != b.Points.Count) return false;
        for (var slot = 0; slot < a.Points.Count; ++slot) if (!Same(a.Points[slot], b.Points[slot])) return false;
        return true;
    }
    private static bool Same(SeekCanvasSourceMove a, SeekCanvasSourceMove b)
        => a.Reference == b.Reference && a.Index == b.Index && Same(a.Speed, b.Speed) && Same(a.X, b.X);
    internal static bool Same(SeekCanvasSourceSnapshot a, SeekCanvasSourceSnapshot b)
    {
        if (a.Identity != b.Identity || a.TransformsArray != b.TransformsArray || a.MovesArray != b.MovesArray
            || a.EaseArray != b.EaseArray || !Same(a.ScreenWidth, b.ScreenWidth) || !Same(a.Down, b.Down) || !Same(a.Right, b.Right)
            || !a.Transforms.SequenceEqual(b.Transforms) || a.Moves.Count != b.Moves.Count || a.Eases.Count != b.Eases.Count) return false;
        for (var slot = 0; slot < a.Moves.Count; ++slot) if (!Same(a.Moves[slot], b.Moves[slot])) return false;
        for (var slot = 0; slot < a.Eases.Count; ++slot)
        {
            var x = a.Eases[slot]; var y = b.Eases[slot];
            if (x.Reference != y.Reference || x.Code != y.Code || x.Progress != y.Progress || x.Samples.Count != y.Samples.Count) return false;
            for (var index = 0; index < x.Samples.Count; ++index) if (!Same(x.Samples[index], y.Samples[index])) return false;
        }
        return true;
    }
}
