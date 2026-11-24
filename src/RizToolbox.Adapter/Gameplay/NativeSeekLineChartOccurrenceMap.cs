namespace RizToolbox.Adapter.Gameplay;

internal enum SeekLineChartResolution
{
    Exact,
    MissingEndpoint,
    EndpointTimeBitsMismatch,
    NoAdjacentChartPair,
    AmbiguousChartLineIdentity,
    AmbiguousChartOccurrence
}

internal enum SeekLinePhysicalOccurrenceKind { DynamicSlot, GroupSegmentSlot }

internal sealed record SeekLineChartSegmentCandidate(int LineOrdinal, long ChartLine,
    int SegmentOrdinal, long StartReference, long EndReference,
    int StartTimeBits, int EndTimeBits);

internal sealed record SeekLineChartLineMapping(long PhysicalLine,
    long StartReference, long EndReference, SeekLineChartResolution Resolution,
    IReadOnlyList<SeekLineChartSegmentCandidate> Candidates, string Reason)
{
    internal SeekLineChartSegmentCandidate? Exact => Resolution == SeekLineChartResolution.Exact ? Candidates[0] : null;
}

internal sealed record SeekLineChartGroupOccurrence(int GroupSlot, long GroupReference)
{
    internal string? Reason => GroupReference == 0 ? "The actual group control occurrence is null." : null;
}

internal sealed record SeekLineChartOutsideJoin(SeekLineRingNoteJoin Join, string Reason);

internal sealed record SeekLineChartPhysicalOccurrence(SeekLinePhysicalOccurrenceKind Kind,
    int ControlSlot, int SegmentSlot, long GroupReference, long PhysicalLine,
    SeekLineChartLineMapping? Mapping)
{
    internal string? Reason => PhysicalLine == 0 ? "The actual physical line occurrence is null."
        : Mapping?.Reason;
}

/// <summary>
/// Pure mapping of actual LineInformation endpoint objects to adjacent chart
/// LinePoint occurrences. A line has an exact mapping only when its reference
/// pair occurs once in the complete chart and both point times agree bitwise.
/// Physical array slots are preserved separately from unique line objects.
/// This is neither a live source verification nor complete Seek admission.
/// </summary>
internal sealed class NativeSeekLineChartOccurrenceMap
{
    [ThreadStatic] private static bool _projecting;

    internal IReadOnlyList<SeekLineChartLineMapping> Lines { get; }
    internal IReadOnlyList<SeekLineChartGroupOccurrence> GroupOccurrences { get; }
    internal IReadOnlyList<SeekLineChartPhysicalOccurrence> PhysicalOccurrences { get; }
    internal IReadOnlyList<SeekLineChartOutsideJoin> NoteJoinsOutsideControlGraph { get; }
    internal bool CompleteSeekPermitted => false;

    private NativeSeekLineChartOccurrenceMap(SeekLineChartLineMapping[] lines,
        SeekLineChartGroupOccurrence[] groups, SeekLineChartPhysicalOccurrence[] occurrences,
        SeekLineChartOutsideJoin[] externalJoins)
    {
        Lines = Array.AsReadOnly(lines);
        GroupOccurrences = Array.AsReadOnly(groups);
        PhysicalOccurrences = Array.AsReadOnly(occurrences);
        NoteJoinsOutsideControlGraph = Array.AsReadOnly(externalJoins);
    }

    internal static NativeSeekLineChartOccurrenceMap Project(SeekLineRingSourceSnapshot source, Action guard)
    {
        if (_projecting) throw new InvalidOperationException("Chart occurrence projection cannot synchronously reenter.");
        _projecting = true;
        try
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(guard);
            guard();
            // This existing bounded copy owns all current physical and chart
            // records. No caller list is read again after this point.
            var owned = SeekLineRingSourceState.Copy(source, guard);
            guard();

            var points = new Dictionary<long, SeekLineRingPoint>(owned.Points.Count);
            foreach (var point in owned.Points) { guard(); points.Add(point.Reference, point); }
            var lines = new Dictionary<long, SeekLineRingLine>(owned.Lines.Count);
            foreach (var line in owned.Lines) { guard(); lines.Add(line.Reference, line); }
            var groups = new Dictionary<long, SeekLineRingGroup>(owned.Groups.Count);
            foreach (var group in owned.Groups) { guard(); groups.Add(group.Reference, group); }
            var chartTimes = new Dictionary<long, int>();
            var conflictingChartTimes = new HashSet<long>();
            var firstByPair = new Dictionary<(long Start, long End), SeekLineChartSegmentCandidate>();
            var duplicatePairs = new Dictionary<(long Start, long End), List<SeekLineChartSegmentCandidate>>();
            SeekLineRingChartPoint? previous = null;
            var chartLineOrdinals = new Dictionary<long, int>();
            var reusedChartLines = new HashSet<long>();
            foreach (var point in owned.ChartPoints)
            {
                guard();
                if (point.LineOrdinal < 0 || point.LineOrdinal >= NativeSeekRuntimeNoteView.MaximumLines
                    || point.PointOrdinal < 0 || point.PointOrdinal >= NativeSeekRuntimeNoteView.MaximumLinePoints
                    || point.ChartLine == 0 || point.Reference == 0
                    || !float.IsFinite(point.Time))
                    throw new InvalidDataException("A chart point occurrence has an invalid ordinal, reference or time.");
                if (previous is null || point.LineOrdinal != previous.LineOrdinal)
                {
                    if (point.PointOrdinal != 0 || previous is not null && point.LineOrdinal <= previous.LineOrdinal)
                        throw new InvalidDataException("Complete chart point occurrences are out of line/point order.");
                }
                else if (point.PointOrdinal != previous.PointOrdinal + 1 || point.ChartLine != previous.ChartLine)
                    throw new InvalidDataException("Complete chart point occurrences are nonconsecutive or change chart Line identity.");

                if (chartLineOrdinals.TryGetValue(point.ChartLine, out var priorOrdinal))
                {
                    if (priorOrdinal != point.LineOrdinal) reusedChartLines.Add(point.ChartLine);
                }
                else chartLineOrdinals.Add(point.ChartLine, point.LineOrdinal);
                var timeBits = BitConverter.SingleToInt32Bits(point.Time);
                if (chartTimes.TryGetValue(point.Reference, out var priorBits))
                {
                    if (priorBits != timeBits) conflictingChartTimes.Add(point.Reference);
                }
                else chartTimes.Add(point.Reference, timeBits);

                if (previous is not null && previous.LineOrdinal == point.LineOrdinal)
                {
                    var pair = (Start: previous.Reference, End: point.Reference);
                    var candidate = new SeekLineChartSegmentCandidate(previous.LineOrdinal, point.ChartLine,
                        previous.PointOrdinal, pair.Start, pair.End,
                        BitConverter.SingleToInt32Bits(previous.Time), timeBits);
                    if (firstByPair.TryGetValue(pair, out var first))
                    {
                        if (!duplicatePairs.TryGetValue(pair, out var duplicates))
                            duplicatePairs.Add(pair, duplicates = new List<SeekLineChartSegmentCandidate> { first });
                        duplicates.Add(candidate);
                    }
                    else firstByPair.Add(pair, candidate);
                }
                previous = point;
            }

            // Materialize only pairs used by physical lines. A one-million-point
            // chart therefore never allocates one List for every unique pair.
            var frozenPairs = new Dictionary<(long Start, long End), IReadOnlyList<SeekLineChartSegmentCandidate>>();
            var mappings = new SeekLineChartLineMapping[owned.Lines.Count];
            var byLine = new Dictionary<long, SeekLineChartLineMapping>(mappings.Length);
            for (var i = 0; i < mappings.Length; ++i)
            {
                guard(); var line = owned.Lines[i];
                var pair = (line.StartPoint, line.EndPoint);
                if (!frozenPairs.TryGetValue(pair, out var candidates))
                {
                    candidates = duplicatePairs.TryGetValue(pair, out var duplicates)
                        ? Array.AsReadOnly(duplicates.ToArray())
                        : firstByPair.TryGetValue(pair, out var single)
                            ? Array.AsReadOnly(new[] { single }) : Array.Empty<SeekLineChartSegmentCandidate>();
                    frozenPairs.Add(pair, candidates);
                }
                var resolution = Resolve(line, points, chartTimes, conflictingChartTimes, reusedChartLines, candidates);
                var reason = resolution switch
                {
                    SeekLineChartResolution.Exact => "The physical endpoint references identify exactly one adjacent chart segment occurrence.",
                    SeekLineChartResolution.MissingEndpoint => "The physical line has an absent start or end LinePoint reference.",
                    SeekLineChartResolution.EndpointTimeBitsMismatch => "A chart occurrence of a physical endpoint reference has different time bits.",
                    SeekLineChartResolution.NoAdjacentChartPair => "The physical endpoint reference pair is absent from adjacent chart point occurrences.",
                    SeekLineChartResolution.AmbiguousChartLineIdentity => "The chart Line reference occurs under multiple chart line ordinals.",
                    _ => "The physical endpoint reference pair identifies multiple chart segment occurrences."
                };
                mappings[i] = new(line.Reference, line.StartPoint, line.EndPoint, resolution, candidates, reason);
                byLine.Add(line.Reference, mappings[i]);
            }

            var externalJoins = new List<SeekLineChartOutsideJoin>();
            foreach (var join in owned.NoteJoins)
            {
                guard();
                if (!lines.TryGetValue(join.RuntimeLine, out var line))
                {
                    externalJoins.Add(new(join,
                        "A retained runtime Note references a Line outside the physical control/group closure."));
                    continue;
                }
                if (join.StartPoint != line.StartPoint || join.EndPoint != line.EndPoint
                    || join.Group != line.Group || join.IsStatic != line.IsStatic)
                    throw new InvalidDataException("An exact Note/runtime Line join contradicts the retained physical control graph.");
                var mapping = byLine[line.Reference];
                if (!ContainsJoinCandidate(mapping.Candidates, join))
                    throw new InvalidDataException("An exact Note/runtime Line join contradicts the complete chart point occurrences.");
            }

            var groupSlots = new SeekLineChartGroupOccurrence[owned.LineGroups.Count];
            // Expansion of repeated group slots is also bounded. Exceeding the
            // bound refuses the whole map; no prefix is ever published.
            var maximumOccurrences = checked(NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences
                + NativeSeekLineRingSourceCapture.MaximumGroupLineOccurrences);
            var occurrenceCount = owned.DynamicLines.Count;
            for (var i = 0; i < groupSlots.Length; ++i)
            {
                guard(); var reference = owned.LineGroups[i];
                groupSlots[i] = new(i, reference);
                if (reference == 0) continue;
                occurrenceCount = checked(occurrenceCount + groups[reference].Lines.Count);
                if (occurrenceCount > maximumOccurrences)
                    throw new NotSupportedException("Complete expanded physical line occurrences exceed their supported bound.");
            }
            var occurrences = new SeekLineChartPhysicalOccurrence[occurrenceCount];
            var next = 0;
            for (var i = 0; i < owned.DynamicLines.Count; ++i)
            {
                guard(); var reference = owned.DynamicLines[i];
                occurrences[next++] = new(SeekLinePhysicalOccurrenceKind.DynamicSlot, i, -1, 0, reference,
                    reference == 0 ? null : byLine[reference]);
            }
            for (var groupSlot = 0; groupSlot < owned.LineGroups.Count; ++groupSlot)
            {
                guard(); var groupReference = owned.LineGroups[groupSlot];
                if (groupReference == 0) continue;
                var group = groups[groupReference];
                for (var segmentSlot = 0; segmentSlot < group.Lines.Count; ++segmentSlot)
                {
                    guard(); var reference = group.Lines[segmentSlot];
                    if (reference != 0 && lines[reference].Group != groupReference)
                        throw new InvalidDataException("A physical group segment occurrence contradicts its line's declared group.");
                    occurrences[next++] = new(SeekLinePhysicalOccurrenceKind.GroupSegmentSlot,
                        groupSlot, segmentSlot, groupReference, reference,
                        reference == 0 ? null : byLine[reference]);
                }
            }
            guard();
            return new(mappings, groupSlots, occurrences, externalJoins.ToArray());
        }
        finally { _projecting = false; }
    }

    private static SeekLineChartResolution Resolve(SeekLineRingLine line,
        IReadOnlyDictionary<long, SeekLineRingPoint> points,
        IReadOnlyDictionary<long, int> chartTimes, IReadOnlySet<long> conflictingChartTimes,
        IReadOnlySet<long> reusedChartLines,
        IReadOnlyList<SeekLineChartSegmentCandidate> candidates)
    {
        if (line.StartPoint == 0 || line.EndPoint == 0)
            return SeekLineChartResolution.MissingEndpoint;
        // SourceState.Copy has already proved both non-null endpoints exist.
        if (conflictingChartTimes.Contains(line.StartPoint) || conflictingChartTimes.Contains(line.EndPoint)
            || (chartTimes.TryGetValue(line.StartPoint, out var startBits)
                && startBits != BitConverter.SingleToInt32Bits(points[line.StartPoint].Time))
            || (chartTimes.TryGetValue(line.EndPoint, out var endBits)
                && endBits != BitConverter.SingleToInt32Bits(points[line.EndPoint].Time)))
            return SeekLineChartResolution.EndpointTimeBitsMismatch;
        if (candidates.Count == 0) return SeekLineChartResolution.NoAdjacentChartPair;
        if (candidates.Count != 1) return SeekLineChartResolution.AmbiguousChartOccurrence;
        if (reusedChartLines.Contains(candidates[0].ChartLine)) return SeekLineChartResolution.AmbiguousChartLineIdentity;
        return SeekLineChartResolution.Exact;
    }

    private static bool ContainsJoinCandidate(IReadOnlyList<SeekLineChartSegmentCandidate> candidates,
        SeekLineRingNoteJoin join)
    {
        // Candidate vectors are in original chart line/point order. Binary
        // search avoids scanning every repeated pair for each runtime Note.
        var low = 0; var high = candidates.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var candidate = candidates[middle];
            if (candidate.LineOrdinal < join.Location.LineOrdinal
                || candidate.LineOrdinal == join.Location.LineOrdinal
                    && candidate.SegmentOrdinal < join.SegmentOrdinal)
            { low = middle + 1; continue; }
            if (candidate.LineOrdinal > join.Location.LineOrdinal
                || candidate.SegmentOrdinal > join.SegmentOrdinal)
            { high = middle - 1; continue; }
            return candidate.ChartLine == join.ChartLine && candidate.StartReference == join.StartPoint
                && candidate.EndReference == join.EndPoint;
        }
        return false;
    }

    /// <summary>Refuses every unresolved line, null source slot, ambiguous
    /// candidate pair, and Note join outside the physical control closure.</summary>
    internal void StrictResolve()
    {
        if (Lines.Any(line => line.Resolution != SeekLineChartResolution.Exact)
            || GroupOccurrences.Any(group => group.GroupReference == 0)
            || PhysicalOccurrences.Any(occurrence => occurrence.PhysicalLine == 0)
            || NoteJoinsOutsideControlGraph.Count != 0)
            throw new InvalidOperationException("The complete physical line to chart occurrence map is not exact.");
    }
}
