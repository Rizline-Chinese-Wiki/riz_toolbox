using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class HoldAccess
        {
            internal sealed partial class GeometryAccess
            {
                private readonly Dictionary<int, (long Extra, long EndLine)> _noteSources = new();
                private readonly Dictionary<long, long> _parents = new();
                private readonly Dictionary<long, HashSet<long>> _children = new();
                public SeekHoldGeometrySnapshot ReadSnapshot(int? selectedOrdinal = null) => Observe(() =>
                {
                    VerifyCurrent(fullGraph: selectedOrdinal is null);
                    if (selectedOrdinal is int ordinal && ((uint)ordinal >= (uint)_source._holds.Count || _context is null))
                        throw new ArgumentOutOfRangeException(nameof(selectedOrdinal));
                    var first = ReadGeometryOnce(selectedOrdinal);
                    if (selectedOrdinal is null && _context is null)
                    {
                        _context = first;
                        foreach (var transform in first.Transforms) _allTransforms.Add(transform.Reference);
                    }
                    var second = ReadGeometryOnce(selectedOrdinal);
                    if (!SeekHoldGeometryState.Same(first, second)) throw Changed("Actual Hold geometry changed between independent observations.");
                    foreach (var transform in second.Transforms) RememberParent(transform.Reference, transform.Parent);
                    VerifyLocal(); return second;
                });
                private SeekHoldGeometrySnapshot ReadGeometryOnce(int? selected)
                {
                    var context = _context;
                    SeekHoldRawState hold;
                    long[] canvases; float[] floors; long ease; IReadOnlyList<SeekHoldGeometryEaseTable>? tables;
                    if (selected is null)
                    {
                        hold = _source.ReadSnapshot();
                        var basis = ReadBasis(); canvases = basis.Canvases; floors = basis.Floors; ease = basis.Ease; tables = basis.Tables;
                    }
                    else
                    {
                        hold = new(context!.Identity, context.Lists, context.Control, Array.Empty<SeekHoldNoteTarget>());
                        canvases = context.CanvasTransforms.ToArray(); floors = context.FloorPositions.ToArray();
                        ease = context.EaseTableReference; tables = context.EaseTables;
                    }
                    var notes = selected is int ordinal ? new[] { ReadGeometryNote(ordinal, canvases) }
                        : Enumerable.Range(0, _source._holds.Count).Select(i => ReadGeometryNote(i, canvases)).ToArray();
                    var seeds = new HashSet<long> { Address(_uiRoot), Address(_cameraTransform) };
                    if (selected is null) { seeds.UnionWith(canvases); seeds.UnionWith(_allTransforms); }
                    foreach (var note in notes)
                    {
                        seeds.UnionWith(new[] { note.NoteTransform, note.EndFlagTransform, note.HoldLineTransform, note.CanvasParent,
                            note.EndFlagCanvasParent, note.LineTransform, note.EndLineTransform, note.RingTransform });
                        // Only commanded targets require descendant propagation, not every child of a shared UI ancestor.
                        var pending = new Queue<long>(new[] { note.NoteTransform, note.EndFlagTransform, note.HoldLineTransform });
                        var visited = new HashSet<long>();
                        while (pending.Count != 0)
                        {
                            var next = pending.Dequeue(); if (!visited.Add(next)) continue; seeds.Add(next);
                            if (_children.TryGetValue(next, out var children)) foreach (var child in children) pending.Enqueue(child);
                        }
                    }
                    var transforms = ReadTransformClosure(seeds);
                    if (selected is int index) _selectedTransforms[index] = transforms.Select(x => x.Reference).ToHashSet();
                    VerifyLocal();
                    return new(hold.Identity, Address(_uiRoot), Address(_cameraTransform), Address(_canvas), Array.AsReadOnly(canvases),
                        Array.AsReadOnly(floors), ease, tables, hold.Lists, hold.Control, Array.AsReadOnly(notes), Array.AsReadOnly(transforms));
                }
                private SeekHoldGeometryNoteState ReadGeometryNote(int ordinal, IReadOnlyList<long> canvases)
                {
                    var c = _source._core; var sourceOrdinal = _source._ordinals[ordinal]; c.VerifyNote(sourceOrdinal);
                    var actual = _source._holds[ordinal]; var note = c._notes[sourceOrdinal]; var nc = c._owner._note;
                    var data = c.TargetReference(note, nc, "noteData", "InLevel.Note", 0x28);
                    var line = c.TargetReference(note, nc, "lineInformation", "InLevel.LineInformation", 0x20);
                    var start = c.TargetReference(line, _profile.LineClass, "startLinePoint", "InLevel.LinePoint", 0x30);
                    var end = c.TargetReference(line, _profile.LineClass, "endLinePoint", "InLevel.LinePoint", 0x38);
                    var group = c.TargetReference(line, _profile.LineClass, "linesGroup", "InLevel.LinesGroupInformation", 0x60);
                    var endLine = c.TargetReference(line, _profile.LineClass, "endLineInformation", "InLevel.LineInformation", 0x58);
                    if (Address(data) != actual.NoteData || Address(line) != actual.Line.Reference || Address(start) != actual.Line.StartPoint
                        || Address(end) != actual.Line.EndPoint || Address(group) != actual.Line.LinesGroup)
                        throw Changed("The actual Hold data/segment identity changed.");
                    var extra = c.TargetReference(data, c._owner._noteData, "otherInformations", "System.Collections.Generic.List<System.Single>", 0x20);
                    var identity = (Address(extra), Address(endLine));
                    if (_noteSources.TryGetValue(ordinal, out var old) && old != identity) throw Changed("The Hold extra/end-line source identity changed.");
                    _noteSources[ordinal] = identity;
                    if (c.TargetCount(extra, 3) != 3) throw new InvalidDataException("Actual Hold geometry requires all three other-information values.");
                    var cls = c.Native(() => c._owner._api.ObjectClass(c.Target(extra)));
                    var method = c.Native(() => c._owner._api.Method(cls, "get_Item", 1, parameterTypes: new[] { "System.Int32" }));
                    var values = new float[3];
                    for (var i = 0; i < values.Length; ++i)
                    {
                        var slot = i;
                        values[i] = c.Native(() => { using var argument = A.Value(slot); return c._owner._api.InvokeValueWithPublication<float>(method, c.Target(extra), argument); });
                    }
                    if (c.TargetCount(extra, 3) != 3 || values.Any(v => !float.IsFinite(v))) throw Changed("The actual Hold extra data is incomplete or nonfinite.");
                    var canvas = c.ValueField<int>(line, _profile.LineClass, "canvasIndex", "System.Int32", 0x24);
                    // LevelControl.Start adds Single 0.5 before native truncation; this is not banker's rounding.
                    var roundedCanvas = values[1] + .5f;
                    if (!float.IsFinite(roundedCanvas) || (double)roundedCanvas < int.MinValue || (double)roundedCanvas > int.MaxValue)
                        throw new InvalidDataException("The actual Hold tail canvas index is not representable.");
                    var tailCanvas = (int)roundedCanvas;
                    if ((uint)canvas >= (uint)canvases.Count || (uint)tailCanvas >= (uint)canvases.Count)
                        throw new InvalidDataException("Actual Hold canvas indices are outside the complete canvas array.");
                    var head = InvokeReference(_profile.GetTransformMethod, note, true)!;
                    var flag = TransformField(note, nc, "holdEndFlag", 0x40) ?? throw Changed("The actual Hold end flag is absent.");
                    var body = TransformField(note, nc, "holdLine", 0x48) ?? throw Changed("The actual Hold line is absent.");
                    var lineTransform = InvokeReference(_profile.GetTransformMethod, line, true)!;
                    var endTransform = TransformField(line, _profile.LineClass, "endTransform", 0x40) ?? throw Changed("The actual line end Transform is absent.");
                    var ring = TransformField(group, _profile.GroupClass, "judgeRingTransform", 0x20) ?? throw Changed("The actual judge ring is absent.");
                    var state = _source.ReadHold(ordinal);
                    var result = new SeekHoldGeometryNoteState(actual.Reference,
                        c.ValueField<float>(data, c._owner._noteData, "time", "System.Single", 0x14), values[0],
                        c.ValueField<float>(data, c._owner._noteData, "floorPosition", "System.Single", 0x18),
                        c.ValueField<float>(start, _profile.LinePointClass, "floorPosition", "System.Single", 0x24),
                        c.ValueField<int>(start, _profile.LinePointClass, "easeType", "System.Int32", 0x1c), values[2],
                        c.ValueField<bool>(note, nc, "isActive", "System.Boolean", 0x30), c.ValueField<bool>(note, nc, "missed", "System.Boolean", 0x55),
                        state.IsStaticNote, state.LastFrameHoldLineLength, Address(head), Address(flag), Address(body), canvases[canvas], canvases[tailCanvas],
                        Address(lineTransform), Address(endTransform), Address(ring), c.ValueField<bool>(group, _profile.GroupClass, "ringActive", "System.Boolean", 0x29));
                    c.VerifyNote(sourceOrdinal); VerifyCurrent(); return result;
                }
                private SeekHoldGeometryTransformState[] ReadTransformClosure(IEnumerable<long> seeds)
                {
                    var parents = new Dictionary<long, long>(); var pending = new Queue<long>(seeds);
                    while (pending.Count != 0)
                    {
                        var reference = pending.Dequeue(); if (parents.ContainsKey(reference)) continue;
                        if (parents.Count >= NativeSeekHoldGeometryCommands.MaximumTransforms) throw new NotSupportedException("The actual Transform closure exceeds its bound.");
                        if (_context is not null && !_allTransforms.Contains(reference)) throw Changed("An actual Transform moved outside its independently captured closure.");
                        var root = Root(reference); VerifyTransform(root);
                        var parent = Address(InvokeReference(_profile.GetParentMethod, root, false)); parents.Add(reference, parent);
                        if (parent != 0) pending.Enqueue(parent);
                    }
                    var colors = new Dictionary<long, int>();
                    foreach (var reference in parents.Keys)
                    {
                        var chain = new List<long>(); var cursor = reference;
                        while (cursor != 0 && (!colors.TryGetValue(cursor, out var color) || color != 2))
                        {
                            if (colors.TryGetValue(cursor, out color) && color == 1) throw Changed("The actual Transform parent graph is cyclic.");
                            colors[cursor] = 1; chain.Add(cursor); cursor = parents[cursor];
                        }
                        foreach (var item in chain) colors[item] = 2;
                    }
                    var result = new List<SeekHoldGeometryTransformState>(parents.Count);
                    foreach (var pair in parents.OrderBy(x => x.Key))
                    {
                        var root = Root(pair.Key);
                        var value = new SeekHoldGeometryTransformState(pair.Key, pair.Value,
                            Value<NativeVector3>(_profile.GetPositionMethod, root), Value<NativeVector3>(_profile.GetLocalPositionMethod, root),
                            Value<NativeRendererQuaternion>(_profile.GetLocalRotationMethod, root), Value<NativeVector3>(_profile.GetLocalScaleMethod, root));
                        if (Address(InvokeReference(_profile.GetParentMethod, root, false)) != pair.Value) throw Changed("An actual Transform parent changed during vector reads.");
                        result.Add(value);
                    }
                    return result.ToArray();
                }
                private void RememberParent(long reference, long parent)
                {
                    if (_parents.TryGetValue(reference, out var old) && old != 0 && _children.TryGetValue(old, out var previous)) previous.Remove(reference);
                    _parents[reference] = parent;
                    if (parent == 0) return;
                    if (!_children.TryGetValue(parent, out var children)) _children[parent] = children = new();
                    children.Add(reference);
                }
            }
        }
    }
}
