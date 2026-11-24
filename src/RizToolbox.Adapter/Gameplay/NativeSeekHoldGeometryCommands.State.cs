using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal static class SeekHoldGeometryState
{
    internal static bool Same(NativeVector3 a, NativeVector3 b) => SeekHoldState.Same(a.X, b.X)
        && SeekHoldState.Same(a.Y, b.Y) && SeekHoldState.Same(a.Z, b.Z);
    internal static bool Same(NativeRendererQuaternion a, NativeRendererQuaternion b) => SeekHoldState.Same(a.X, b.X)
        && SeekHoldState.Same(a.Y, b.Y) && SeekHoldState.Same(a.Z, b.Z) && SeekHoldState.Same(a.W, b.W);
    internal static bool Finite(NativeVector3 a) => float.IsFinite(a.X) && float.IsFinite(a.Y) && float.IsFinite(a.Z);
    internal static bool Same(SeekHoldGeometryTransformState a, SeekHoldGeometryTransformState b)
        => a.Reference == b.Reference && a.Parent == b.Parent && Same(a.WorldPosition, b.WorldPosition)
            && Same(a.LocalPosition, b.LocalPosition) && Same(a.LocalRotation, b.LocalRotation) && Same(a.LocalScale, b.LocalScale);
    internal static bool Same(SeekHoldGeometryNoteState a, SeekHoldGeometryNoteState b)
        => a.Reference == b.Reference && SeekHoldState.Same(a.HeadChartTime, b.HeadChartTime)
            && SeekHoldState.Same(a.TailChartTime, b.TailChartTime) && SeekHoldState.Same(a.NoteFloorPosition, b.NoteFloorPosition)
            && SeekHoldState.Same(a.StartFloorPosition, b.StartFloorPosition) && a.EaseType == b.EaseType
            && SeekHoldState.Same(a.EndFloorPosition, b.EndFloorPosition) && a.IsActive == b.IsActive && a.Missed == b.Missed
            && a.IsStaticNote == b.IsStaticNote && SeekHoldState.Same(a.LastFrameHoldLineLength, b.LastFrameHoldLineLength)
            && a.NoteTransform == b.NoteTransform && a.EndFlagTransform == b.EndFlagTransform && a.HoldLineTransform == b.HoldLineTransform
            && a.CanvasParent == b.CanvasParent && a.EndFlagCanvasParent == b.EndFlagCanvasParent
            && a.LineTransform == b.LineTransform && a.EndLineTransform == b.EndLineTransform
            && a.RingTransform == b.RingTransform && a.RingActive == b.RingActive;
    private static T[] Copy<T>(IReadOnlyList<T> input, int maximum, Action guard)
    {
        ArgumentNullException.ThrowIfNull(input); guard(); var count = input.Count; guard();
        if (count < 0 || count > maximum) throw new InvalidDataException("The complete Hold geometry input exceeds its retained bound.");
        var copy = new T[count];
        for (var i = 0; i < count; ++i) { guard(); copy[i] = input[i]; guard(); }
        guard(); if (input.Count != count) throw new InvalidOperationException("A complete Hold geometry input changed during copying."); guard(); return copy;
    }
    internal static SeekHoldGeometrySnapshot Copy(SeekHoldGeometrySnapshot raw, Action guard)
    {
        guard();
        if (raw is null || raw.Identity is null || raw.Identity.Notes?.Chart?.Run is null
            || raw.Identity.HoldControl == 0 || raw.UiRootTransform == 0 || raw.CameraTransform == 0 || raw.CanvasControl == 0)
            throw new InvalidDataException("The exact Hold geometry source identity and basis are required.");
        var canvases = Copy(raw.CanvasTransforms, NativeSeekHoldGeometryCommands.MaximumNotes, guard);
        var floors = Copy(raw.FloorPositions, NativeSeekHoldGeometryCommands.MaximumNotes, guard);
        if (canvases.Length != floors.Length || canvases.Any(value => value == 0))
            throw new InvalidDataException("The complete actual canvas transform and floor arrays disagree.");
        var notes = Copy(raw.Notes, NativeSeekHoldGeometryCommands.MaximumNotes, guard); var seen = new HashSet<long>();
        foreach (var note in notes)
            if (note is null || note.Reference == 0 || !seen.Add(note.Reference))
                throw new InvalidDataException("The actual Hold geometry note subset contains an absent or duplicate identity.");
        var transforms = Copy(raw.Transforms, NativeSeekHoldGeometryCommands.MaximumTransforms, guard); seen.Clear();
        foreach (var value in transforms)
            if (value is null || value.Reference == 0 || !seen.Add(value.Reference))
                throw new InvalidDataException("The Hold geometry transform closure contains an absent or duplicate identity.");
        SeekHoldGeometryEaseTable[]? tables = null;
        if (raw.EaseTables is not null)
        {
            var rows = Copy(raw.EaseTables, 65536, guard); tables = new SeekHoldGeometryEaseTable[rows.Length];
            for (var i = 0; i < rows.Length; ++i)
            {
                var row = rows[i];
                if (row is null) throw new InvalidDataException("The initialized ease table has an absent row.");
                var progress = Copy(row.Progress, 101, guard);
                if (progress.Length != 101 || progress.Any(value => !float.IsFinite(value)))
                    throw new InvalidDataException("An initialized ease table must retain all 101 finite native samples.");
                tables[i] = row with { Progress = Array.AsReadOnly(progress) };
            }
        }
        if ((raw.EaseTableReference == 0) != (tables is null)) throw new InvalidDataException("The actual initialized ease table reference and copied rows disagree.");
        var lists = SeekHoldState.Copy(raw.Lists, guard); var control = SeekHoldState.Copy(raw.Control, guard, false);
        guard(); return raw with { CanvasTransforms = Array.AsReadOnly(canvases), FloorPositions = Array.AsReadOnly(floors),
            EaseTables = tables is null ? null : Array.AsReadOnly(tables), Notes = Array.AsReadOnly(notes.Select(row => row with { }).ToArray()),
            Transforms = Array.AsReadOnly(transforms.Select(row => row with { }).ToArray()), Lists = lists, Control = control };
    }
    internal static SeekHoldGeometrySnapshot CopySelected(SeekHoldGeometrySnapshot raw, SeekHoldGeometrySnapshot context, Action guard)
    {
        guard();
        if (raw is null || raw.Identity != context.Identity || raw.UiRootTransform != context.UiRootTransform
            || raw.CameraTransform != context.CameraTransform || raw.CanvasControl != context.CanvasControl
            || raw.EaseTableReference != context.EaseTableReference)
            throw new InvalidOperationException("The selected actual Hold geometry has another source identity.");
        var notes = Copy(raw.Notes, 1, guard); var transforms = Copy(raw.Transforms, NativeSeekHoldGeometryCommands.MaximumTransforms, guard);
        if (notes.Length != 1 || notes[0] is null) throw new InvalidDataException("A selected Hold geometry snapshot must contain its exact note once.");
        var references = new HashSet<long>();
        foreach (var row in transforms)
            if (row is null || row.Reference == 0 || !references.Add(row.Reference))
                throw new InvalidDataException("The selected actual Hold transform closure has an absent or duplicate identity.");
        // Global arrays/List storage are immutable phase context, explicitly not
        // reobserved by a selected read. Do not rescan them at every scalar step.
        guard(); return context with { Notes = Array.AsReadOnly(notes.Select(row => row with { }).ToArray()),
            Transforms = Array.AsReadOnly(transforms.Select(row => row with { }).ToArray()) };
    }
    internal static bool SameBasis(SeekHoldGeometrySnapshot a, SeekHoldGeometrySnapshot b)
        => a.Identity == b.Identity && a.UiRootTransform == b.UiRootTransform && a.CameraTransform == b.CameraTransform
            && a.CanvasControl == b.CanvasControl && a.CanvasTransforms.SequenceEqual(b.CanvasTransforms)
            && a.FloorPositions.Count == b.FloorPositions.Count && !a.FloorPositions.Where((value, i) => !SeekHoldState.Same(value, b.FloorPositions[i])).Any()
            && a.EaseTableReference == b.EaseTableReference && SameEase(a.EaseTables, b.EaseTables)
            && SeekHoldState.Same(a.Lists, b.Lists) && SeekHoldState.Same(a.Control, b.Control);
    private static bool SameEase(IReadOnlyList<SeekHoldGeometryEaseTable>? a, IReadOnlyList<SeekHoldGeometryEaseTable>? b)
        => a is null ? b is null : b is not null && a.Count == b.Count && !a.Where((row, i) => row.Code != b[i].Code
            || row.Progress.Count != b[i].Progress.Count || row.Progress.Where((value, j) => !SeekHoldState.Same(value, b[i].Progress[j])).Any()).Any();
    internal static bool Same(SeekHoldGeometrySnapshot a, SeekHoldGeometrySnapshot b)
        => SameBasis(a, b) && a.Notes.Count == b.Notes.Count && !a.Notes.Where((row, i) => !Same(row, b.Notes[i])).Any()
            && a.Transforms.Count == b.Transforms.Count && !a.Transforms.Where((row, i) => !Same(row, b.Transforms[i])).Any();
    internal static bool IsProjection(SeekHoldGeometrySnapshot full, SeekHoldGeometrySnapshot selected)
    {
        if (!SameBasis(full, selected)) return false;
        var notes = full.Notes.ToDictionary(row => row.Reference); var transforms = full.Transforms.ToDictionary(row => row.Reference);
        return selected.Notes.All(row => notes.TryGetValue(row.Reference, out var expected) && Same(row, expected))
            && selected.Transforms.All(row => transforms.TryGetValue(row.Reference, out var expected) && Same(row, expected));
    }
    internal static SeekHoldGeometrySnapshot Merge(SeekHoldGeometrySnapshot full, SeekHoldGeometrySnapshot selected)
    {
        var transforms = selected.Transforms.ToDictionary(row => row.Reference);
        return full with { Transforms = Array.AsReadOnly(full.Transforms.Select(row => transforms.TryGetValue(row.Reference, out var value) ? value : row).ToArray()) };
    }
    internal static void Validate(SeekHoldGeometrySnapshot full)
    {
        var transforms = full.Transforms.ToDictionary(row => row.Reference); var notes = full.Notes.ToDictionary(row => row.Reference);
        if (!transforms.ContainsKey(full.UiRootTransform) || !transforms.ContainsKey(full.CameraTransform)
            || full.CanvasTransforms.Any(reference => !transforms.ContainsKey(reference)))
            throw new InvalidDataException("The actual UI/camera/canvas transform basis is incomplete.");
        foreach (var row in full.Notes)
        {
            if (!float.IsFinite(row.HeadChartTime) || !float.IsFinite(row.TailChartTime) || row.TailChartTime < row.HeadChartTime
                || !float.IsFinite(row.NoteFloorPosition) || !float.IsFinite(row.StartFloorPosition) || !float.IsFinite(row.EndFloorPosition)
                || new[] { row.NoteTransform, row.EndFlagTransform, row.HoldLineTransform, row.CanvasParent,
                    row.EndFlagCanvasParent, row.LineTransform, row.EndLineTransform, row.RingTransform }.Any(reference => !transforms.ContainsKey(reference)))
                throw new InvalidDataException("An actual Hold has incomplete finite chart data or transform basis.");
            if (!full.CanvasTransforms.Contains(row.CanvasParent) || !full.CanvasTransforms.Contains(row.EndFlagCanvasParent))
                throw new InvalidDataException("A Hold canvas parent is not in the independently retained actual canvas array.");
        }
        foreach (var list in new[] { full.Lists.HoldNotes, full.Lists.RunningHoldNotes })
            if (list.CapacitySlots.Take(list.Size).Any(reference => !notes.ContainsKey(reference)))
                throw new InvalidDataException("An active Hold geometry List occurrence is outside the actual Hold subset.");
        var uiX = transforms[full.UiRootTransform].LocalScale.X; var cameraX = transforms[full.CameraTransform].LocalScale.X;
        if (!float.IsFinite(uiX) || uiX == 0 || !float.IsFinite(cameraX) || cameraX == 0 || !float.IsFinite(uiX * cameraX))
            throw new NotSupportedException("The captured UI/camera scale cannot support finite target Hold geometry.");
    }
}
