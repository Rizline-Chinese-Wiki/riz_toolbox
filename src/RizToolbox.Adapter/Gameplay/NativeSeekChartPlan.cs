using RizToolbox.Adapter.Judgements;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal readonly record struct GameplaySeekTargetSeconds(double Value);
// This profile verifies read-only fixed metadata/code. It issues no control receipt.
// The production implementation is the existing broker-aware clock profile.
internal interface ISeekChartNativeProfile
{
    IntPtr ChartClass { get; }
    IntPtr ProgressClass { get; }
    void VerifyCurrent();
}
internal sealed record SeekChartIdentity(ObservedJudgementRun Run, long Chart, long Progress, long Lines);
internal sealed record SeekChartBpmPoint(long Reference, float Time, float Value, int EaseType, float FloorPosition);
internal sealed record SeekChartMapping(float Bpm, long ShiftList, int DeclaredShiftCount, IReadOnlyList<SeekChartBpmPoint> Shifts);
internal sealed record SeekChartRawNote(long Reference, long InformationList, int Type, float Time, float FloorPosition,
    IReadOnlyList<float> OtherInformations);
internal sealed record SeekChartRawLine(long Reference, long NotesList, int DeclaredNoteCount,
    IReadOnlyList<SeekChartRawNote> Notes);

/// <summary>Reads an independently retained, complete chart graph. It issues no write,
/// scheduling, judgement, or native playback receipt. Counts are read from the graph;
/// an observed-judgement registry is not a complete-chart enumeration source.</summary>
internal interface IRetainedSeekChartReader : IDisposable
{
    void VerifyAccess();
    SeekChartIdentity ReadIdentity();
    int ReadLineCount();
    SeekChartMapping ReadMapping();
    SeekChartRawLine ReadLine(int ordinal);
    float ChartTimeToGameSeconds(double chartCoordinate);
    // Must use managed state only, with no native getters, callbacks or invocation.
    // This closes local disposal/run changes, not unobserved graph-write windows.
    void VerifyLocal(SeekChartIdentity expected);
}

internal sealed record SeekChartNote(ChartNoteLocation Location, int RawType,
    float ChartCoordinate, float? HoldEndChartCoordinate, float StartGameSeconds, float? HoldEndGameSeconds);

/// <summary>Input for a future real note/judge/hold/cursor reset consumer. This plan
/// partitions every retained chart location by an explicit game-seconds target.
/// It does not reset NoteInformation, stop/recreate coroutines, alter score/input,
/// or authorize GameplayControlAction.Seek. CrossingHolds are deliberately
/// unresolved: classifying one does not resume or reconstruct its native state.</summary>
internal sealed class NativeSeekChartPlan : IDisposable
{
    internal const int MaximumLines = 4096, MaximumNotes = 65536, MaximumBpmShifts = 32768;
    private IRetainedSeekChartReader? _source;
    private readonly SourceSnapshot _baseline;
    internal GameplaySeekTargetSeconds Target { get; }
    internal GameplayControlValueUnit Unit => GameplayControlValueUnit.GameSeconds;
    internal ObservedJudgementRun Run => _baseline.Identity.Run;
    internal IReadOnlyList<SeekChartNote> Notes { get; }
    internal IReadOnlyList<SeekChartNote> OrderedByStart { get; }
    internal IReadOnlyList<SeekChartNote> Past { get; }
    internal IReadOnlyList<SeekChartNote> Pending { get; }
    internal IReadOnlyList<SeekChartNote> CrossingHolds { get; }
    // This indexes OrderedByStart only. It is not either native NoteControl cursor.
    internal int PendingStartIndex { get; }

    private NativeSeekChartPlan(IRetainedSeekChartReader source, SourceSnapshot baseline,
        GameplaySeekTargetSeconds target, SeekChartNote[] notes)
    {
        _source = source; _baseline = baseline; Target = target;
        Notes = Array.AsReadOnly(notes);
        var ordered = notes.OrderBy(n => n.StartGameSeconds)
            .ThenBy(n => n.Location.LineOrdinal).ThenBy(n => n.Location.NoteOrdinal).ToArray();
        OrderedByStart = Array.AsReadOnly(ordered);
        PendingStartIndex = LowerBound(ordered, target.Value);
        Pending = Array.AsReadOnly(ordered.Skip(PendingStartIndex).ToArray());
        Past = Array.AsReadOnly(ordered.Where(n => n.StartGameSeconds < target.Value
            && (n.HoldEndGameSeconds is null || n.HoldEndGameSeconds <= target.Value)).ToArray());
        CrossingHolds = Array.AsReadOnly(ordered.Where(n => n.StartGameSeconds < target.Value
            && n.HoldEndGameSeconds is float end && target.Value < end).ToArray());
        if (Past.Count + Pending.Count + CrossingHolds.Count != notes.Length)
            throw new InvalidDataException("The target partition lost a chart location.");
    }

    /// <summary>Takes ownership of source on success and failure. All declared lines
    /// and note slots are copied twice, including references and mapping inputs.
    /// Conversion calls may reenter. A partial or observed inconsistent pass is
    /// refused. Equal samples do not establish atomicity against unobserved
    /// in-place, tail or double-ABA changes; this is not a reset permit.</summary>
    internal static NativeSeekChartPlan Capture(IRetainedSeekChartReader source, GameplaySeekTargetSeconds target)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            source.VerifyAccess();
            if (!double.IsFinite(target.Value))
                throw new ArgumentOutOfRangeException(nameof(target), "The target must be finite game seconds.");
            var before = ReadSnapshot(source);
            var notes = new List<SeekChartNote>(before.NoteCount);
            for (var line = 0; line < before.Lines.Length; ++line)
                for (var ordinal = 0; ordinal < before.Lines[line].Notes.Count; ++ordinal)
                {
                    var note = before.Lines[line].Notes[ordinal];
                    source.VerifyAccess();
                    var start = Convert(source, note.Time);
                    float? holdCoordinate = note.Type == 2 ? note.OtherInformations[0] : null;
                    float? end = holdCoordinate is float coordinate ? Convert(source, coordinate) : null;
                    if (end < start)
                        throw new InvalidDataException("A hold ends before its actual chart start.");
                    notes.Add(new(new(line, ordinal), note.Type, note.Time, holdCoordinate, start, end));
                }
            var after = ReadSnapshot(source);
            if (!Same(before, after))
                throw new InvalidOperationException("The retained run, chart graph, note data or BPM mapping changed during capture.");
            // Keep the final boundary free of native target lookups/invocations.
            var plan = new NativeSeekChartPlan(source, before, target, notes.ToArray());
            source.VerifyLocal(after.Identity);
            return plan;
        }
        catch { source.Dispose(); throw; }
    }

    /// <summary>Future reset consumers must verify this still-retained source before
    /// preparing or applying their own real state changes. This is a read, not a
    /// reset permit, and does not prove absence of unobserved double ABA writers.</summary>
    internal void VerifySource()
    {
        var source = _source ?? throw new ObjectDisposedException(nameof(NativeSeekChartPlan));
        source.VerifyAccess();
        if (!Same(_baseline, ReadSnapshot(source)))
            throw new InvalidOperationException("This complete chart plan no longer matches its retained source.");
        source.VerifyLocal(_baseline.Identity);
    }

    private static float Convert(IRetainedSeekChartReader source, float coordinate)
    {
        var value = source.ChartTimeToGameSeconds(coordinate);
        if (!float.IsFinite(value)) throw new InvalidDataException("The fixed chart converter returned nonfinite game seconds.");
        return value;
    }
    private static int LowerBound(SeekChartNote[] notes, double target)
    {
        var left = 0; var right = notes.Length;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (notes[middle].StartGameSeconds < target) left = middle + 1;
            else right = middle;
        }
        return left;
    }

    private sealed record SourceSnapshot(SeekChartIdentity Identity, SeekChartMapping Mapping,
        SeekChartRawLine[] Lines, int NoteCount);
    private static SourceSnapshot ReadSnapshot(IRetainedSeekChartReader source)
    {
        source.VerifyAccess();
        var identity = source.ReadIdentity() ?? throw new InvalidDataException("The current retained run is absent.");
        if (identity.Chart == 0 || identity.Progress == 0 || identity.Lines == 0
            || identity.Run is null || string.IsNullOrWhiteSpace(identity.Run.SessionId)
            || string.IsNullOrWhiteSpace(identity.Run.RunId) || identity.Run.Information == 0
            || identity.Run.Controller == 0 || identity.Run.SceneGeneration < 0 || identity.Run.Revision < 0)
            throw new InvalidDataException("The chart source lacks its actual retained run/graph identity.");
        var mapping = CopyMapping(source.ReadMapping());
        var count = source.ReadLineCount();
        if (count is < 0 or > MaximumLines) throw new InvalidDataException("The actual chart line count exceeds its bound.");
        var lines = new SeekChartRawLine[count]; var total = 0;
        for (var line = 0; line < count; ++line)
        {
            source.VerifyAccess();
            var input = source.ReadLine(line) ?? throw new InvalidDataException("A complete chart line was not read.");
            if (input.Reference == 0 || input.NotesList == 0 || input.Notes is null
                || input.DeclaredNoteCount is < 0 or > MaximumNotes
                || input.Notes.Count != input.DeclaredNoteCount || input.DeclaredNoteCount > MaximumNotes - total)
                throw new InvalidDataException("The complete declared note list was not copied within its bound.");
            var copied = new SeekChartRawNote[input.Notes.Count];
            for (var ordinal = 0; ordinal < copied.Length; ++ordinal)
            {
                var note = input.Notes[ordinal] ?? throw new InvalidDataException("The actual chart contains a null note.");
                if (note.Reference == 0 || note.InformationList == 0 || note.Type is < 0 or > 2
                    || !float.IsFinite(note.Time) || !float.IsFinite(note.FloorPosition)
                    || note.OtherInformations is null || note.OtherInformations.Count is not (0 or 3)
                    || note.Type == 2 && note.OtherInformations.Count != 3
                    || note.OtherInformations.Any(v => !float.IsFinite(v)))
                    throw new InvalidDataException("The actual note is outside the fixed chart format.");
                copied[ordinal] = note with { OtherInformations = Array.AsReadOnly(note.OtherInformations.ToArray()) };
            }
            total += copied.Length;
            lines[line] = input with { Notes = Array.AsReadOnly(copied) };
        }
        if (source.ReadLineCount() != count || source.ReadIdentity() != identity)
            throw new InvalidOperationException("The actual run or chart topology changed during traversal.");
        source.VerifyLocal(identity);
        return new(identity, mapping, lines, total);
    }
    private static SeekChartMapping CopyMapping(SeekChartMapping input)
    {
        if (input is null || !float.IsFinite(input.Bpm) || input.Bpm <= 0 || input.ShiftList == 0
            || input.Shifts is null || input.DeclaredShiftCount is < 0 or > MaximumBpmShifts
            || input.Shifts.Count != input.DeclaredShiftCount)
            throw new InvalidDataException("The complete current BPM source was not captured.");
        var points = input.Shifts.ToArray();
        if (points.Any(p => p is null || p.Reference == 0 || !float.IsFinite(p.Time)
            || !float.IsFinite(p.Value) || p.Value <= 0 || !float.IsFinite(p.FloorPosition)))
            throw new InvalidDataException("The current BPM mapping contains an unsupported value.");
        return input with { Shifts = Array.AsReadOnly(points) };
    }
    private static bool Same(SourceSnapshot first, SourceSnapshot second)
    {
        if (first.Identity != second.Identity || first.NoteCount != second.NoteCount
            || !SameFloat(first.Mapping.Bpm, second.Mapping.Bpm) || first.Mapping.ShiftList != second.Mapping.ShiftList
            || first.Mapping.Shifts.Count != second.Mapping.Shifts.Count || first.Lines.Length != second.Lines.Length)
            return false;
        for (var i = 0; i < first.Mapping.Shifts.Count; ++i)
        {
            var a = first.Mapping.Shifts[i]; var b = second.Mapping.Shifts[i];
            if (a.Reference != b.Reference || a.EaseType != b.EaseType
                || !SameFloat(a.Time, b.Time) || !SameFloat(a.Value, b.Value) || !SameFloat(a.FloorPosition, b.FloorPosition))
                return false;
        }
        for (var line = 0; line < first.Lines.Length; ++line)
        {
            var a = first.Lines[line]; var b = second.Lines[line];
            if (a.Reference != b.Reference || a.NotesList != b.NotesList || a.DeclaredNoteCount != b.DeclaredNoteCount)
                return false;
            for (var ordinal = 0; ordinal < a.Notes.Count; ++ordinal)
            {
                var n = a.Notes[ordinal]; var m = b.Notes[ordinal];
                if (n.Reference != m.Reference || n.InformationList != m.InformationList || n.Type != m.Type
                    || !SameFloat(n.Time, m.Time) || !SameFloat(n.FloorPosition, m.FloorPosition)
                    || n.OtherInformations.Count != m.OtherInformations.Count)
                    return false;
                for (var i = 0; i < n.OtherInformations.Count; ++i)
                    if (!SameFloat(n.OtherInformations[i], m.OtherInformations[i])) return false;
            }
        }
        return true;
    }
    private static bool SameFloat(float first, float second)
        => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);
    public void Dispose()
    {
        var source = _source;
        if (source is null) return;
        // Dispose enforces the owner thread without requiring the run/backend to
        // remain admitted. Its independently owned roots must remain releasable.
        source.Dispose(); _source = null;
    }
}
