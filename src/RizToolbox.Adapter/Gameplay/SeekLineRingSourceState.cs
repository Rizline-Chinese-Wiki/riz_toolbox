using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal static class SeekLineRingSourceState
{
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static bool Vector(NativeVector3 a, NativeVector3 b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z);
    private static bool Rotation(NativeRendererQuaternion a, NativeRendererQuaternion b)
        => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z) && Bits(a.W) == Bits(b.W);
    private static bool Point(SeekLineRingPoint a, SeekLineRingPoint b)
        => a.Reference == b.Reference && Bits(a.Time) == Bits(b.Time) && Bits(a.XPosition) == Bits(b.XPosition)
            && a.EaseType == b.EaseType && a.CanvasIndex == b.CanvasIndex && Bits(a.FloorPosition) == Bits(b.FloorPosition);
    private static T[] CopyList<T>(IReadOnlyList<T> source, int maximum, Action guard, Func<T, T> copy)
    {
        ArgumentNullException.ThrowIfNull(source); guard(); var count = source.Count; guard();
        if (count < 0 || count > maximum) throw new NotSupportedException("The complete line/ring list exceeds its support bound.");
        var result = new T[count];
        for (var i = 0; i < count; ++i) { guard(); result[i] = copy(source[i]); guard(); }
        if (source.Count != count) throw new InvalidOperationException("Line/ring coverage changed during copying.");
        guard(); return result;
    }
    internal static SeekLineRingSourceSnapshot Copy(SeekLineRingSourceSnapshot raw, Action guard)
    {
        ArgumentNullException.ThrowIfNull(raw); ArgumentNullException.ThrowIfNull(guard); guard();
        var id = raw.Identity ?? throw new InvalidDataException("Line/ring source identity is absent.");
        if (id.CanvasCapture is null || id.CanvasPlan is null || id.Notes is null || id.Information == 0 || id.Canvas == 0 || id.LineControl == 0
            || !ReferenceEquals(id.CanvasCapture.Plan, id.CanvasPlan))
            throw new InvalidDataException("Line/ring provenance must keep its original actual Canvas capture, Plan and note source.");
        if (raw.DynamicLinesArray == 0 || raw.LineGroupsArray == 0 || raw.ControlBaseline is null)
            throw new InvalidDataException("Both complete actual line/ring control arrays and scalar baseline are required.");
        var dynamicLines = CopyList(raw.DynamicLines, NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences, guard, x => x);
        var groupSlots = CopyList(raw.LineGroups, NativeSeekLineRingSourceCapture.MaximumGroupOccurrences, guard, x => x);
        var lines = CopyList(raw.Lines, NativeSeekLineRingSourceCapture.MaximumLines, guard, x => x with { });
        var points = CopyList(raw.Points, NativeSeekLineRingSourceCapture.MaximumPoints, guard, x => x with { });
        var totalSegments = 0;
        var groups = CopyList(raw.Groups, NativeSeekLineRingSourceCapture.MaximumGroups, guard, group =>
        {
            var slots = CopyList(group.Lines, NativeSeekLineRingSourceCapture.MaximumGroupLineOccurrences, guard, x => x);
            totalSegments = checked(totalSegments + slots.Length);
            if (totalSegments > NativeSeekLineRingSourceCapture.MaximumGroupLineOccurrences)
                throw new NotSupportedException("The complete physical group-line occurrence budget is exceeded.");
            if (group.LinesArray == 0) throw new InvalidDataException("A physical group's complete lines array cannot be absent.");
            return group with { Lines = Array.AsReadOnly(slots) };
        });
        var selections = CopyList(raw.GroupBaselines, NativeSeekLineRingSourceCapture.MaximumGroups, guard, x => x with { });
        var transforms = CopyList(raw.Transforms, NativeSeekLineRingSourceCapture.MaximumTransforms, guard, x => x with { });
        var chart = CopyList(raw.ChartPoints, NativeSeekLineRingSourceCapture.MaximumChartPoints, guard, x => x with { });
        var joins = CopyList(raw.NoteJoins, NativeSeekLineRingSourceCapture.MaximumNoteJoins, guard,
            x => x with { Location = x.Location with { } });
        Unique(lines.Select(x => x.Reference)); Unique(points.Select(x => x.Reference)); Unique(groups.Select(x => x.Reference));
        Unique(selections.Select(x => x.Reference)); Unique(transforms.Select(x => x.Reference));
        var lineIds = lines.Select(x => x.Reference).ToHashSet(); var pointIds = points.Select(x => x.Reference).ToHashSet();
        var groupIds = groups.Select(x => x.Reference).ToHashSet(); var transformIds = transforms.Select(x => x.Reference).ToHashSet();
        void Present(long reference, HashSet<long> table)
        { if (reference != 0 && !table.Contains(reference)) throw new InvalidDataException("The complete line/ring graph omits an actual referenced object."); }
        foreach (var reference in dynamicLines) { guard(); Present(reference, lineIds); }
        foreach (var reference in groupSlots) { guard(); Present(reference, groupIds); }
        foreach (var group in groups)
        {
            guard(); Present(group.RingTransform, transformIds);
            foreach (var reference in group.Lines) { guard(); Present(reference, lineIds); }
        }
        foreach (var line in lines)
        {
            guard(); if (line.Transform == 0) throw new InvalidDataException("Every actual line requires its component Transform.");
            Present(line.Transform, transformIds); Present(line.EndTransform, transformIds);
            Present(line.StartPoint, pointIds); Present(line.EndPoint, pointIds); Present(line.EndLine, lineIds); Present(line.Group, groupIds);
        }
        if (!selections.Select(x => x.Reference).ToHashSet().SetEquals(groupIds))
            throw new InvalidDataException("Every actual physical group needs exactly one selection baseline.");
        var parents = transforms.ToDictionary(x => x.Reference, x => x.Parent);
        foreach (var pair in parents)
        {
            guard(); var path = new HashSet<long>(); var cursor = pair.Key;
            while (cursor != 0)
            {
                guard();
                if (!path.Add(cursor) || !parents.TryGetValue(cursor, out cursor))
                    throw new InvalidDataException("The line/ring parent closure is cyclic or incomplete.");
                if (path.Count > NativeSeekLineRingSourceCapture.MaximumParentDepth)
                    throw new NotSupportedException("The complete line/ring parent chain exceeds its support depth.");
            }
        }
        guard(); return new(id with { }, raw.DynamicLinesArray, Array.AsReadOnly(dynamicLines), raw.LineGroupsArray, Array.AsReadOnly(groupSlots),
            Array.AsReadOnly(lines), Array.AsReadOnly(points), Array.AsReadOnly(groups), raw.ControlBaseline with { },
            Array.AsReadOnly(selections), Array.AsReadOnly(transforms), Array.AsReadOnly(chart), Array.AsReadOnly(joins));
    }
    private static void Unique(IEnumerable<long> references)
    {
        var set = new HashSet<long>();
        foreach (var reference in references)
            if (reference == 0 || !set.Add(reference)) throw new InvalidDataException("A physical line/ring table contains an absent or duplicate identity.");
    }
    internal static bool Same(SeekLineRingSourceSnapshot a, SeekLineRingSourceSnapshot b, bool baseline)
    {
        if (!ReferenceEquals(a.Identity.CanvasCapture, b.Identity.CanvasCapture)
            || !ReferenceEquals(a.Identity.CanvasPlan, b.Identity.CanvasPlan) || a.Identity.Notes != b.Identity.Notes
            || a.Identity.Information != b.Identity.Information || a.Identity.Canvas != b.Identity.Canvas || a.Identity.LineControl != b.Identity.LineControl
            || a.DynamicLinesArray != b.DynamicLinesArray || a.LineGroupsArray != b.LineGroupsArray
            || !a.DynamicLines.SequenceEqual(b.DynamicLines) || !a.LineGroups.SequenceEqual(b.LineGroups)
            || !a.Lines.SequenceEqual(b.Lines) || a.Points.Count != b.Points.Count || a.Groups.Count != b.Groups.Count
            || a.ChartPoints.Count != b.ChartPoints.Count || !a.NoteJoins.SequenceEqual(b.NoteJoins)) return false;
        for (var i = 0; i < a.Points.Count; ++i) if (!Point(a.Points[i], b.Points[i])) return false;
        for (var i = 0; i < a.Groups.Count; ++i)
            if (a.Groups[i].Reference != b.Groups[i].Reference || a.Groups[i].RingTransform != b.Groups[i].RingTransform
                || a.Groups[i].LinesArray != b.Groups[i].LinesArray || !a.Groups[i].Lines.SequenceEqual(b.Groups[i].Lines)) return false;
        for (var i = 0; i < a.ChartPoints.Count; ++i)
        {
            var x = a.ChartPoints[i]; var y = b.ChartPoints[i];
            if (x.LineOrdinal != y.LineOrdinal || x.PointOrdinal != y.PointOrdinal || x.ChartLine != y.ChartLine
                || x.Reference != y.Reference || Bits(x.Time) != Bits(y.Time)) return false;
        }
        if (!baseline) return true;
        if (a.ControlBaseline != b.ControlBaseline || !a.GroupBaselines.SequenceEqual(b.GroupBaselines) || a.Transforms.Count != b.Transforms.Count) return false;
        for (var i = 0; i < a.Transforms.Count; ++i)
        {
            var x = a.Transforms[i]; var y = b.Transforms[i];
            if (x.Reference != y.Reference || x.Parent != y.Parent || !Vector(x.Position, y.Position) || !Vector(x.LocalPosition, y.LocalPosition)
                || !Rotation(x.LocalRotation, y.LocalRotation) || !Vector(x.LocalScale, y.LocalScale)) return false;
        }
        return true;
    }
    internal static SeekLineRingSourceCoverage Coverage(SeekLineRingSourceSnapshot source)
    {
        var work = new List<SeekLineRingRequiredWork>(); var lines = source.Lines.ToDictionary(x => x.Reference);
        var groups = source.Groups.ToDictionary(x => x.Reference); var segmentCount = 0; var physicalCount = 0;
        void Missing(string kind, long reference, string reason) => work.Add(new(kind, reference, reason));
        foreach (var reference in source.DynamicLines) if (reference == 0) Missing("NullDynamicSlot", 0, "An actual dynamic occurrence is null.");
        foreach (var reference in source.LineGroups)
            if (reference == 0) Missing("NullGroupSlot", 0, "An actual group occurrence is null.");
            else segmentCount = checked(segmentCount + groups[reference].Lines.Count);
        foreach (var group in source.Groups)
        {
            physicalCount = checked(physicalCount + group.Lines.Count);
            if (group.RingTransform == 0) Missing("MissingRingTransform", group.Reference, "The actual group has no ring Transform.");
            foreach (var reference in group.Lines) if (reference == 0) Missing("NullSegmentSlot", group.Reference, "An actual group segment occurrence is null.");
        }
        foreach (var line in source.Lines)
        {
            if (line.StartPoint == 0 || line.EndPoint == 0) Missing("MissingEndpoint", line.Reference, "An actual line endpoint is absent.");
            if (line.Group == 0) Missing("MissingGroup", line.Reference, "An actual line has no declared group association.");
            if (line.EndTransform == 0) Missing("MissingEndTransform", line.Reference, "An actual line has no end Transform.");
        }
        var proven = new List<SeekLineRingNoteJoin>();
        foreach (var join in source.NoteJoins)
        {
            if (!lines.TryGetValue(join.RuntimeLine, out var line))
            { Missing("RuntimeLineOutsideControlGraph", join.RuntimeLine, "A retained Note references a line outside the actual control/group closure."); continue; }
            if (join.StartPoint != line.StartPoint || join.EndPoint != line.EndPoint || join.Group != line.Group || join.IsStatic != line.IsStatic)
                throw new InvalidDataException("An exact Note/runtime line join contradicts the retained control graph.");
            proven.Add(join);
        }
        var joined = proven.Select(x => x.RuntimeLine).ToHashSet();
        var unjoined = source.Lines.Where(line => !joined.Contains(line.Reference)).Select(line => line.Reference).ToArray();
        foreach (var reference in unjoined) Missing("UnresolvedChartJoin", reference, "No actual Note proves this line's chart association; no ordinal inference is used.");
        Missing("TargetProjection", source.Identity.LineControl, "Complete line/ring target projection and explicit writers remain separate work.");
        Missing("EngineClosure", source.Identity.LineControl, "Producer/helper, activation, renderer and Unity effects are not closed by source capture.");
        return new(source.DynamicLines.Count, source.LineGroups.Count, segmentCount, source.Lines.Count,
            source.Lines.Count(x => x.IsStatic), Array.AsReadOnly(unjoined), Array.AsReadOnly(proven.ToArray()), Array.AsReadOnly(work.ToArray()))
        { PhysicalGroupLineOccurrences = physicalCount };
    }
}
