using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekRuntimeNoteSourceIdentity(SeekChartIdentity Chart, long NoteControl,
    long RenderNotes, long AllNotes);
internal sealed record SeekRuntimeChartPoint(long Reference, float Time);
internal sealed record SeekRuntimeChartLine(SeekChartRawLine Data, long PointsList,
    int DeclaredPointCount, IReadOnlyList<SeekRuntimeChartPoint> Points);
internal sealed record SeekRuntimeLine(long Reference, long StartPoint, long EndPoint,
    long LinesGroup, bool IsStatic);
internal sealed record SeekRuntimeRawNote(long Reference, long NoteData, SeekRuntimeLine Line);
internal sealed record SeekRuntimeNote(long Reference, long NoteData, ChartNoteLocation Location,
    long ChartLine, int SegmentOrdinal, SeekRuntimeLine Line);
internal enum SeekRuntimeNoteCoverage { EmptyChart, NoRuntimeNotes, PartialChart, CompleteChart }
internal sealed record SeekRuntimeNoteCoverageReport(SeekRuntimeNoteCoverage Level, int ChartLocationCount,
    int CoveredLocationCount, int RuntimeInstanceCount, IReadOnlyList<ChartNoteLocation> MissingLocations,
    IReadOnlyList<ChartNoteLocation> MultipleInstanceLocations);

/// <summary>Owns independent roots for the current complete chart and both actual
/// NoteControl arrays. Publication and local checks use managed lifecycle state.
/// Implementations keep root-free failures as explicit cleanup responsibility.</summary>
internal interface IRetainedSeekRuntimeNoteReader : IDisposable
{
    void VerifyOwnerThread();
    void VerifyAccess();
    void BindLifetimeGuard(Action guard);
    SeekRuntimeNoteSourceIdentity ReadIdentity();
    int ReadLineCount();
    SeekRuntimeChartLine ReadChartLine(int ordinal);
    IReadOnlyList<SeekRuntimeRawNote> ReadRuntimeNotes(bool renderNotes);
    // Pure managed state only, including owner thread and exact run revision.
    void VerifyLocal(SeekRuntimeNoteSourceIdentity expected);
}

/// <summary>Immutable, bounded input for future Note/Judge preparation. References
/// remain rooted until disposal and require VerifySource before consumption.
/// Coverage describes distinct chart locations in the sampled allNotes array.
/// Each future writer owns its separate preparation and restoration protocol.</summary>
internal sealed class NativeSeekRuntimeNoteView : IDisposable
{
    internal const int MaximumLines = NativeSeekChartPlan.MaximumLines;
    internal const int MaximumNotes = NativeSeekChartPlan.MaximumNotes;
    internal const int MaximumLinePoints = 65536;
    private readonly IRetainedSeekRuntimeNoteReader _source;
    private readonly Snapshot _baseline;
    private bool _disposed, _faulted, _released, _releasing;
    private Action? _canvasUseGuard;
    private int _inFlight, _physicalOwners;
    internal ObservedJudgementRun Run => _baseline.Identity.Chart.Run;
    internal SeekRuntimeNoteSourceIdentity Identity => _baseline.Identity;
    internal IReadOnlyList<SeekRuntimeChartLine> ChartLines { get; }
    // Preserve each array's actual slot order. RenderNotes indexes Notes by identity.
    internal IReadOnlyList<SeekRuntimeNote> Notes { get; }
    internal IReadOnlyList<long> RenderNotes { get; }
    internal SeekRuntimeNoteCoverageReport Coverage { get; }
    internal string? CleanupFailure { get; private set; }

    private NativeSeekRuntimeNoteView(IRetainedSeekRuntimeNoteReader source, Snapshot snapshot)
    {
        _source = source;
        _baseline = snapshot;
        ChartLines = Array.AsReadOnly(snapshot.Lines);
        var joined = Join(snapshot);
        Notes = Array.AsReadOnly(joined.Notes);
        RenderNotes = Array.AsReadOnly(snapshot.Render.Select(note => note.Reference).ToArray());
        Coverage = joined.Coverage;
    }

    /// <summary>Takes source ownership after the owner-thread check. Two complete
    /// copied samples must agree. Sampling records observed changes, while native
    /// registration serials and unobserved double ABA remain separate evidence.</summary>
    internal static NativeSeekRuntimeNoteView Capture(IRetainedSeekRuntimeNoteReader source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.VerifyOwnerThread();
        try
        {
            var before = ReadSnapshot(source);
            var after = ReadSnapshot(source);
            if (!Same(before, after))
                throw new InvalidOperationException("The chart, runtime note arrays or reference associations changed during capture.");
            var view = new NativeSeekRuntimeNoteView(source, before);
            source.BindLifetimeGuard(view.VerifyLifetime);
            // All graph/native calls precede this final local publication guard.
            source.VerifyLocal(after.Identity);
            view.VerifyLifetime();
            return view;
        }
        catch (Exception error)
        {
            try { source.Dispose(); }
            catch (Exception cleanup)
            {
                // Keep the failed owner's wrappers reachable with the original
                // operation failure. The cleanup owner records unknown frees.
                error.Data["SeekRuntimeNoteCleanupOwner"] = source;
                error.Data["SeekRuntimeNoteCleanupFailure"] = cleanup;
            }
            throw;
        }
    }

    internal void VerifySource()
    {
        _source.VerifyOwnerThread();
        VerifyLifetime();
        if (_inFlight != 0) throw new InvalidOperationException("Runtime-note source verification is already in progress.");
        ++_inFlight;
        Exception? operationFailure = null;
        try
        {
            if (!Same(_baseline, ReadSnapshot(_source)))
                throw new InvalidOperationException("The retained runtime-note view has changed.");
            _source.VerifyLocal(_baseline.Identity);
            VerifyLifetime();
        }
        catch (Exception error)
        {
            if (!SeekCanvasBasisCallerWithdrawal.Is(error)) _faulted = true;
            operationFailure = error; throw;
        }
        finally
        {
            --_inFlight;
            if (_disposed && _inFlight == 0 && _physicalOwners == 0)
            {
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (operationFailure is null) throw;
                    operationFailure.Data["SeekRuntimeNoteCleanupOwner"] = _source;
                    operationFailure.Data["SeekRuntimeNoteCleanupFailure"] = cleanup;
                }
            }
        }
    }

    internal IDisposable RetainPhysicalSource()
    {
        _source.VerifyOwnerThread(); VerifyLifetime();
        if (_physicalOwners == 256) throw new NotSupportedException("The bounded runtime-note source lease capacity is exhausted.");
        var lease = new PhysicalSourceLease(this); ++_physicalOwners; return lease;
    }
    private sealed class PhysicalSourceLease : IDisposable
    {
        private NativeSeekRuntimeNoteView? _view;
        internal PhysicalSourceLease(NativeSeekRuntimeNoteView view) => _view = view;
        public void Dispose()
        {
            var view = _view;
            if (view is null) return;
            view._source.VerifyOwnerThread(); _view = null; --view._physicalOwners;
            if (view._disposed && view._inFlight == 0 && view._physicalOwners == 0) view.Release();
        }
    }

    // A dependent scalar dispatch owns the complete captured source until its
    // physical call exits, even if a callback closes the logical NoteView.
    internal void WithRetainedSource(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation); _source.VerifyOwnerThread(); VerifyLifetime();
        if (_inFlight != 0) throw new InvalidOperationException("The runtime-note source is already in use.");
        ++_inFlight; Exception? operationFailure = null;
        try { operation(); VerifyLifetime(); }
        catch (Exception error) { operationFailure = error; throw; }
        finally
        {
            --_inFlight;
            if (_disposed && _inFlight == 0 && _physicalOwners == 0)
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (operationFailure is null) throw;
                    operationFailure.Data["SeekRuntimeNoteCleanupOwner"] = _source;
                    operationFailure.Data["SeekRuntimeNoteCleanupFailure"] = cleanup;
                }
        }
    }

    // Canvas sharing needs cancellation at the reader's deepest export guards.
    // This adds no root/lease and does not rebind the source's lifetime callback.
    internal void BindCanvasUseGuard(Action guard)
    {
        _source.VerifyOwnerThread(); ArgumentNullException.ThrowIfNull(guard);
        if (_canvasUseGuard is not null)
            throw new InvalidOperationException("The Canvas source use guard attaches only once.");
        VerifyLifetime(); _canvasUseGuard = guard; VerifyLifetime();
    }

    private void VerifyLifetime()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekRuntimeNoteView));
        if (_faulted) throw new InvalidOperationException("This runtime-note view was already observed expired.");
        _canvasUseGuard?.Invoke();
    }

    private sealed record Snapshot(SeekRuntimeNoteSourceIdentity Identity, SeekRuntimeChartLine[] Lines,
        SeekRuntimeRawNote[] Render, SeekRuntimeRawNote[] All);

    private static Snapshot ReadSnapshot(IRetainedSeekRuntimeNoteReader source)
    {
        source.VerifyAccess();
        var identity = source.ReadIdentity() ?? throw new InvalidDataException("The current runtime-note identity is absent.");
        var chart = identity.Chart;
        if (chart is null || chart.Run is null || chart.Chart == 0 || chart.Progress == 0 || chart.Lines == 0
            || string.IsNullOrWhiteSpace(chart.Run.SessionId) || string.IsNullOrWhiteSpace(chart.Run.RunId)
            || chart.Run.Information == 0 || chart.Run.Controller == 0 || chart.Run.SceneGeneration < 0 || chart.Run.Revision < 0
            || identity.NoteControl == 0 || identity.RenderNotes == 0 || identity.AllNotes == 0)
            throw new InvalidDataException("The view requires the actual retained chart, arrays and exact run identity.");
        var count = source.ReadLineCount();
        if (count is < 0 or > MaximumLines) throw new InvalidDataException("The complete chart line count exceeds its bound.");
        var lines = new SeekRuntimeChartLine[count];
        var noteCount = 0;
        var pointCount = 0;
        for (var ordinal = 0; ordinal < count; ++ordinal)
        {
            source.VerifyAccess();
            var input = source.ReadChartLine(ordinal) ?? throw new InvalidDataException("A complete chart line is absent.");
            var data = input.Data;
            if (data is null || data.Reference == 0 || data.NotesList == 0 || data.Notes is null
                || data.DeclaredNoteCount is < 0 or > MaximumNotes || data.DeclaredNoteCount != data.Notes.Count
                || data.DeclaredNoteCount > MaximumNotes - noteCount || input.PointsList == 0 || input.Points is null
                || input.DeclaredPointCount is < 0 or > MaximumLinePoints || input.DeclaredPointCount != input.Points.Count
                || input.DeclaredPointCount > MaximumLinePoints - pointCount)
                throw new InvalidDataException("A complete chart line or its point/note list exceeds its bound.");
            var notes = new SeekChartRawNote[data.Notes.Count];
            for (var i = 0; i < notes.Length; ++i)
            {
                var note = data.Notes[i];
                if (note is null || note.Reference == 0 || note.InformationList == 0 || note.Type is < 0 or > 2
                    || !float.IsFinite(note.Time) || !float.IsFinite(note.FloorPosition) || note.OtherInformations is null
                    || note.OtherInformations.Count is not (0 or 3) || note.Type == 2 && note.OtherInformations.Count != 3
                    || note.OtherInformations.Any(value => !float.IsFinite(value)))
                    throw new InvalidDataException("The current chart Note differs from the supported fixed format.");
                notes[i] = note with { OtherInformations = Array.AsReadOnly(note.OtherInformations.ToArray()) };
            }
            var points = input.Points.ToArray();
            if (points.Any(point => point is null || point.Reference == 0 || !float.IsFinite(point.Time)))
                throw new InvalidDataException("The current chart contains an absent or nonfinite LinePoint.");
            lines[ordinal] = input with
            {
                Data = data with { Notes = Array.AsReadOnly(notes) },
                Points = Array.AsReadOnly(points)
            };
            noteCount += notes.Length;
            pointCount += points.Length;
        }
        var render = CopyRuntime(source.ReadRuntimeNotes(true));
        var all = CopyRuntime(source.ReadRuntimeNotes(false));
        if (source.ReadLineCount() != count || source.ReadIdentity() != identity)
            throw new InvalidOperationException("The runtime-note source topology or exact run changed during traversal.");
        source.VerifyLocal(identity);
        return new(identity, lines, render, all);
    }

    private static SeekRuntimeRawNote[] CopyRuntime(IReadOnlyList<SeekRuntimeRawNote> input)
    {
        if (input is null || input.Count is < 0 or > MaximumNotes)
            throw new InvalidDataException("The actual runtime-note array exceeds its complete bound.");
        var copied = input.ToArray();
        var references = new HashSet<long>();
        foreach (var note in copied)
            if (note is null || note.Reference == 0 || note.NoteData == 0 || note.Line is null
                || note.Line.Reference == 0 || note.Line.StartPoint == 0 || note.Line.EndPoint == 0 || note.Line.LinesGroup == 0
                || !references.Add(note.Reference))
                throw new InvalidDataException("The actual runtime-note array has a missing reference or duplicate object slot.");
        return copied;
    }

    private static (SeekRuntimeNote[] Notes, SeekRuntimeNoteCoverageReport Coverage) Join(Snapshot snapshot)
    {
        var chartNotes = new Dictionary<long, (ChartNoteLocation Location, SeekRuntimeChartLine Line)>();
        var chartLines = new HashSet<long>();
        var segments = new Dictionary<(long Line, long Start, long End), int>();
        for (var line = 0; line < snapshot.Lines.Length; ++line)
        {
            var raw = snapshot.Lines[line];
            if (!chartLines.Add(raw.Data.Reference))
                throw new InvalidDataException("The complete chart contains a duplicate Line identity.");
            for (var point = 0; point + 1 < raw.Points.Count; ++point)
                if (!segments.TryAdd((raw.Data.Reference, raw.Points[point].Reference, raw.Points[point + 1].Reference), point))
                    throw new InvalidDataException("A chart LinePoint pair occurs in multiple segment slots.");
            for (var note = 0; note < raw.Data.Notes.Count; ++note)
                if (!chartNotes.TryAdd(raw.Data.Notes[note].Reference, (new(line, note), raw)))
                    throw new InvalidDataException("A chart Note reference belongs to multiple chart locations.");
        }
        var joined = new SeekRuntimeNote[snapshot.All.Length];
        var all = new Dictionary<long, SeekRuntimeRawNote>();
        var runtimeLines = new Dictionary<long, (SeekRuntimeLine Line, long ChartLine)>();
        var instanceKeys = new HashSet<(long Line, long NoteData)>();
        var coverage = new Dictionary<ChartNoteLocation, int>();
        for (var slot = 0; slot < snapshot.All.Length; ++slot)
        {
            var note = snapshot.All[slot];
            if (!chartNotes.TryGetValue(note.NoteData, out var chart))
                throw new InvalidDataException("A runtime noteData reference is absent from the current complete chart.");
            if (!segments.TryGetValue((chart.Line.Data.Reference, note.Line.StartPoint, note.Line.EndPoint), out var segment))
                throw new InvalidDataException("The actual runtime LineInformation point pair belongs to another chart Line.");
            if (runtimeLines.TryGetValue(note.Line.Reference, out var existing)
                && (existing.Line != note.Line || existing.ChartLine != chart.Line.Data.Reference))
                throw new InvalidDataException("A runtime LineInformation identity has inconsistent chart or point associations.");
            runtimeLines[note.Line.Reference] = (note.Line, chart.Line.Data.Reference);
            if (!instanceKeys.Add((note.Line.Reference, note.NoteData)))
                throw new InvalidDataException("Multiple runtime objects claim the same actual line segment and chart Note.");
            all.Add(note.Reference, note);
            coverage.TryGetValue(chart.Location, out var instances);
            coverage[chart.Location] = instances + 1;
            joined[slot] = new(note.Reference, note.NoteData, chart.Location, chart.Line.Data.Reference, segment, note.Line);
        }
        var rendered = new HashSet<long>();
        foreach (var note in snapshot.Render)
        {
            if (!all.TryGetValue(note.Reference, out var original) || note != original || note.Line.IsStatic)
                throw new InvalidDataException("A render-array object is missing from allNotes or has a different/static line association.");
            rendered.Add(note.Reference);
        }
        foreach (var note in snapshot.All)
            if (!note.Line.IsStatic && !rendered.Contains(note.Reference))
                throw new InvalidDataException("A dynamic-line allNotes object is missing from the actual render array.");
        var missing = chartNotes.Values.Select(note => note.Location).Where(location => !coverage.ContainsKey(location))
            .OrderBy(location => location.LineOrdinal).ThenBy(location => location.NoteOrdinal).ToArray();
        var multiple = coverage.Where(entry => entry.Value > 1).Select(entry => entry.Key)
            .OrderBy(location => location.LineOrdinal).ThenBy(location => location.NoteOrdinal).ToArray();
        var level = chartNotes.Count == 0 ? SeekRuntimeNoteCoverage.EmptyChart
            : coverage.Count == 0 ? SeekRuntimeNoteCoverage.NoRuntimeNotes
            : missing.Length == 0 ? SeekRuntimeNoteCoverage.CompleteChart : SeekRuntimeNoteCoverage.PartialChart;
        return (joined, new(level, chartNotes.Count, coverage.Count, joined.Length,
            Array.AsReadOnly(missing), Array.AsReadOnly(multiple)));
    }

    private static bool Same(Snapshot first, Snapshot second)
    {
        if (first.Identity != second.Identity || first.Lines.Length != second.Lines.Length
            || !first.Render.SequenceEqual(second.Render) || !first.All.SequenceEqual(second.All)) return false;
        for (var line = 0; line < first.Lines.Length; ++line)
        {
            var a = first.Lines[line];
            var b = second.Lines[line];
            if (a.Data.Reference != b.Data.Reference || a.Data.NotesList != b.Data.NotesList
                || a.PointsList != b.PointsList || a.Points.Count != b.Points.Count || a.Data.Notes.Count != b.Data.Notes.Count) return false;
            for (var point = 0; point < a.Points.Count; ++point)
                if (a.Points[point].Reference != b.Points[point].Reference || !SameFloat(a.Points[point].Time, b.Points[point].Time)) return false;
            for (var note = 0; note < a.Data.Notes.Count; ++note)
            {
                var n = a.Data.Notes[note];
                var m = b.Data.Notes[note];
                if (n.Reference != m.Reference || n.InformationList != m.InformationList || n.Type != m.Type
                    || !SameFloat(n.Time, m.Time) || !SameFloat(n.FloorPosition, m.FloorPosition)
                    || n.OtherInformations.Count != m.OtherInformations.Count) return false;
                for (var value = 0; value < n.OtherInformations.Count; ++value)
                    if (!SameFloat(n.OtherInformations[value], m.OtherInformations[value])) return false;
            }
        }
        return true;
    }
    private static bool SameFloat(float first, float second)
        => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);

    public void Dispose()
    {
        _source.VerifyOwnerThread();
        _disposed = true;
        if (_inFlight == 0 && _physicalOwners == 0) Release();
    }
    private void Release()
    {
        if (_released || _releasing || _inFlight != 0 || _physicalOwners != 0) return;
        _releasing = true;
        try { _source.Dispose(); _released = true; }
        catch (Exception error) { CleanupFailure = error.Message; throw; }
        finally { _releasing = false; }
    }
}
