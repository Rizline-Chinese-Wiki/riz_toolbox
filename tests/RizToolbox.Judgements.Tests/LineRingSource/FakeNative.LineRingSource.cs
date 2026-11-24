using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Additional official-export storage for the real shared Canvas reader. This
// profile has no Hold-profile or Hold-instance dependency. The earlier fixture
// creates its reusable metadata; all Hold containers stay poisoned for reads.
sealed partial class FakeNative
{
    internal LineRingSourceProfileValue LineRingSourceProfile = null!;
    internal IntPtr LineRingControl, LineRingDynamicArray, LineRingGroupsArray;
    internal IntPtr LineRingOrphanGroup, LineRingOrphanStatic, LineRingOrphanDynamic;
    internal IntPtr LineRingLastBox;
    internal readonly List<IntPtr> LineRingLines = new(), LineRingGroups = new(), LineRingPoints = new();
    internal readonly List<(string Name, IntPtr Receiver)> LineRingGetterCalls = new();

    internal sealed class LineRingSourceProfileValue : ISeekLineRingSourceProfile
    {
        private readonly FakeNative _n;
        internal Action? OnVerify;
        internal string? WrongMember;
        internal LineRingSourceProfileValue(FakeNative n) => _n = n;
        private IntPtr C(string name, IntPtr value) => WrongMember == name ? _n.ClassOf(_n.Object) : value;
        private IntPtr M(string name, string key) => WrongMember == name ? _n._geometryMethods["set_localPosition"] : _n._geometryMethods[key];
        public IntPtr ChartClass => C(nameof(ChartClass), _n.SeekProfile.ChartClass);
        public IntPtr ProgressClass => C(nameof(ProgressClass), _n.SeekProfile.ProgressClass);
        public IntPtr InformationClass => C(nameof(InformationClass), _n.ClassOf(_n.SeekType("InLevel.LevelInformation")));
        public IntPtr LineControlClass => C(nameof(LineControlClass), _n.ClassOf(_n._geometryLineControlType));
        public IntPtr LineClass => C(nameof(LineClass), _n.ClassOf(_n._noteViewLineType));
        public IntPtr LinePointClass => C(nameof(LinePointClass), _n.ClassOf(_n._noteViewPointType));
        public IntPtr GroupClass => C(nameof(GroupClass), _n.ClassOf(_n._noteViewGroupType));
        public IntPtr TransformClass => C(nameof(TransformClass), _n.ClassOf(_n._geometryTransformType));
        public IntPtr UIClass => C(nameof(UIClass), _n.ClassOf(_n._geometryUiType));
        public IntPtr CameraClass => C(nameof(CameraClass), _n.ClassOf(_n._geometryCameraType));
        public IntPtr GetTransformMethod => M(nameof(GetTransformMethod), "get_transform");
        public IntPtr GetParentMethod => M(nameof(GetParentMethod), "get_parent");
        public IntPtr GetPositionMethod => M(nameof(GetPositionMethod), "get_position");
        public IntPtr GetLocalPositionMethod => M(nameof(GetLocalPositionMethod), "get_localPosition");
        public IntPtr GetLocalRotationMethod => M(nameof(GetLocalRotationMethod), "get_localRotation");
        public IntPtr GetLocalScaleMethod => M(nameof(GetLocalScaleMethod), "get_localScale");
        public void VerifyCurrent() { OnVerify?.Invoke(); _n.SeekProfile.VerifyCurrent(); }
    }

    internal void ConfigureLineRingSource(bool preserveHolds = false)
    {
        ConfigureCanvasCaches(positions: true);
        Add("il2cpp_array_class_get", "ArrayClassGet", nameof(LineRingArrayClass));
        // Quaternion is now freshly enumerated through the official field
        // parent export, as Vector3 already was in the older Canvas fixture.
        foreach (var field in _structFields[ClassOf(_geometryQuaternionType)])
            _gameplayFieldParents[field] = ClassOf(_geometryQuaternionType);
        LineRingControl = SeekValue<IntPtr>(GameplayInformation, "lineControl");
        LineRingSourceProfile = new(this);
        // Convert the complete chart to ordinary Notes before the real capture.
        foreach (var data in preserveHolds ? Array.Empty<IntPtr>() : SeekNotes.ToArray())
        {
            TargetSet(data, "type", 0);
            _seekLists[SeekValue<IntPtr>(data, "otherInformations")] = Array.Empty<object>();
        }
        foreach (var points in NoteViewPoints)
        foreach (var point in points) LineRingCompletePoint(point);
        foreach (var note in NoteViewRuntimeNotes)
        {
            var line = SeekValue<IntPtr>(note, "lineInformation");
            if (!LineRingLines.Contains(line)) LineRingCompleteLine(line);
        }
        LineRingGroups.AddRange(NoteViewGroups);
        LineRingOrphanGroup = SeekObject(_noteViewGroupType);
        LineRingGroups.Add(LineRingOrphanGroup);
        var first = LineRingNewPoint(20f, -2f, 1, 2, 31f);
        var second = LineRingNewPoint(30f, 8f, 0, 1, 47f);
        LineRingOrphanStatic = LineRingNewLine(LineRingOrphanGroup, first, second, true);
        LineRingOrphanDynamic = LineRingNewLine(LineRingOrphanGroup, first, second, false);
        foreach (var group in LineRingGroups)
        {
            var members = LineRingLines.Where(line => SeekValue<IntPtr>(line, "linesGroup") == group).ToArray();
            LineRingCompleteGroup(group, members);
        }
        LineRingSetDynamic(LineRingLines.Where(line => !TargetBoolean(line, "lineIsStatic")).ToArray());
        LineRingSetGroups(LineRingGroups.ToArray());
        var information = SeekType("InLevel.LevelInformation");
        SeekField("linering.info.sceneWidth", information, "sceneWidth", Single,
            GameplayInformation, 1920f, 0xc4);
        SeekField("linering.control.information", _geometryLineControlType, "levelInformation", information,
            LineRingControl, GameplayInformation, 0x20);
        SeekField("linering.control.canvas", _geometryLineControlType, "_canvasControl", _geometryCanvasType,
            LineRingControl, GeometryCanvasControl, 0x38);
        foreach (var (name, offset, bits) in new[]
        {
            ("_nowTime", 0x40, unchecked((int)0x80000000)),
            ("_sceneWidth", 0x44, unchecked((int)0x7fc01234)),
            ("_speed", 0x48, BitConverter.SingleToInt32Bits(-7.25f)),
            ("_gameSceneScale", 0x4c, BitConverter.SingleToInt32Bits(1.5f))
        }) SeekField("linering.control." + name, _geometryLineControlType, name, Single,
            LineRingControl, BitConverter.Int32BitsToSingle(bits), offset);
        CanvasSourcePointers.UnionWith(LineRingLines.Concat(LineRingGroups).Concat(LineRingPoints).Append(LineRingControl));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(LineRingSourceInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        CanvasSourceRemember();
    }

    private void LineRingCompletePoint(IntPtr point)
    {
        if (!LineRingPoints.Contains(point)) LineRingPoints.Add(point);
        var key = "linering.point." + point;
        var time = SeekValue<float>(point, "time");
        SeekField(key + ".time", _noteViewPointType, "time", Single, point, time, 0x10);
        SeekField(key + ".x", _noteViewPointType, "xPosition", Single, point, time + .125f, 0x14);
        SeekField(key + ".ease", _noteViewPointType, "easeType", Int32, point, 1, 0x1c);
        SeekField(key + ".canvas", _noteViewPointType, "canvasIndex", Int32, point, 0, 0x20);
        SeekField(key + ".floor", _noteViewPointType, "floorPosition", Single, point, time * 3f + 2f, 0x24);
    }
    internal IntPtr LineRingNewPoint(float time, float x, int ease, int canvas, float floor)
    {
        var point = SeekObject(_noteViewPointType); var key = "linering.point." + point;
        SeekField(key + ".time", _noteViewPointType, "time", Single, point, time, 0x10);
        LineRingCompletePoint(point); TargetSet(point, "xPosition", x); TargetSet(point, "easeType", ease);
        TargetSet(point, "canvasIndex", canvas); TargetSet(point, "floorPosition", floor);
        CanvasSourcePointers.Add(point); return point;
    }
    private void LineRingCompleteLine(IntPtr line)
    {
        if (!LineRingLines.Contains(line)) LineRingLines.Add(line);
        var key = "linering.line." + line;
        if (!GeometryComponentTransforms.ContainsKey(line)) GeometryComponentTransforms[line] =
            GeometryNewTransform(GeometryUiRoot, new(2, 3, 4), new(0, 0, 0, 1), new(1, 2, 1));
        if (!_geometryLineEnds.TryGetValue(line, out var end)) _geometryLineEnds[line] = end =
            GeometryNewTransform(GeometryUiRoot, new(9, 11, 4), new(0, 0, 0, 1), new(1, 1, 1));
        SeekField(key + ".canvas", _noteViewLineType, "canvasIndex", Int32, line, 0, 0x24);
        SeekField(key + ".endtransform", _noteViewLineType, "endTransform", _geometryTransformType, line, end, 0x40);
        SeekField(key + ".endline", _noteViewLineType, "endLineInformation", _noteViewLineType, line, line, 0x58);
        CanvasSourcePointers.Add(line);
    }
    internal IntPtr LineRingNewLine(IntPtr group, IntPtr start, IntPtr end, bool isStatic)
    {
        var line = SeekObject(_noteViewLineType); var key = "linering.line." + line;
        SeekField(key + ".start", _noteViewLineType, "startLinePoint", _noteViewPointType, line, start, 0x30);
        SeekField(key + ".end", _noteViewLineType, "endLinePoint", _noteViewPointType, line, end, 0x38);
        SeekField(key + ".group", _noteViewLineType, "linesGroup", _noteViewGroupType, line, group, 0x60);
        SeekField(key + ".static", _noteViewLineType, "lineIsStatic", Boolean, line, isStatic, 0x20);
        LineRingCompleteLine(line); return line;
    }
    private void LineRingCompleteGroup(IntPtr group, IntPtr[] lines)
    {
        var key = "linering.group." + group;
        if (!_geometryGroupRings.TryGetValue(group, out var ring)) _geometryGroupRings[group] = ring =
            GeometryNewTransform(GeometryUiRoot, new(-5, 4, 2), new(0, 0, 1, 0), new(2, 1, 1));
        SeekField(key + ".ring", _noteViewGroupType, "judgeRingTransform", _geometryTransformType, group, ring, 0x20);
        SeekField(key + ".active", _noteViewGroupType, "ringActive", Boolean, group, true, 0x29);
        SeekField(key + ".index", _noteViewGroupType, "nowLineIndex", Int32, group, -17, 0x70);
        LineRingSetGroupLines(group, lines); CanvasSourcePointers.Add(group);
    }
    internal void LineRingSetDynamic(params IntPtr[] lines)
    {
        LineRingDynamicArray = CanvasArray(_noteViewLineType, lines);
        SeekField("linering.control.dynamic", _geometryLineControlType, "dynamicLines", _boxedTypes[LineRingDynamicArray],
            LineRingControl, LineRingDynamicArray, 0x28);
    }
    internal void LineRingSetGroups(params IntPtr[] groups)
    {
        LineRingGroupsArray = CanvasArray(_noteViewGroupType, groups);
        SeekField("linering.control.groups", _geometryLineControlType, "lineGroups", _boxedTypes[LineRingGroupsArray],
            LineRingControl, LineRingGroupsArray, 0x30);
    }
    internal void LineRingSetGroupLines(IntPtr group, params IntPtr[] lines)
    {
        var array = CanvasArray(_noteViewLineType, lines);
        SeekField("linering.group." + group + ".lines", _noteViewGroupType, "lines", _boxedTypes[array], group, array, 0x68);
    }
    internal IntPtr LineRingGroupArray(IntPtr group) => SeekValue<IntPtr>(group, "lines");
    internal IntPtr LineRingLineTransform(IntPtr line) => GeometryComponentTransforms[line];
    internal IntPtr LineRingEndTransform(IntPtr line) => SeekValue<IntPtr>(line, "endTransform");
    internal IntPtr LineRingRing(IntPtr group) => SeekValue<IntPtr>(group, "judgeRingTransform");
    internal long[] LineRingEndpointReferences() => LineRingLines.SelectMany(line => new[]
        { SeekValue<IntPtr>(line, "startLinePoint").ToInt64(), SeekValue<IntPtr>(line, "endLinePoint").ToInt64() })
        .Where(reference => reference != 0).Distinct().ToArray();
    internal void LineRingSet(IntPtr owner, string name, object value)
    { TargetSet(owner, name, value); CanvasSourceRemember(); }
    internal void LineRingSetObjectClass(IntPtr value, IntPtr cls)
    { _boxedTypes[value] = _classes[cls]; Marshal.WriteIntPtr(value, cls); }
    internal void LineRingSpoofReferenceField(IntPtr owner, string name)
    {
        var field = TargetField(owner, name); var row = _fieldMetadata[field]; var type = _types[row.Type];
        var foreign = AddType(type.Name, type.Code, type.ValueType, type.Size);
        _fieldMetadata[field] = (row.Name, foreign, row.Offset);
    }
    private IntPtr LineRingArrayClass(IntPtr element, uint rank)
    {
        var type = _geometryArrayElements.FirstOrDefault(row => ClassOf(row.Value) == element).Key;
        return rank == 1 && type != IntPtr.Zero ? ClassOf(type) : FingerArrayClass(element, rank);
    }
    internal void LineRingSetSlot(IntPtr array, int slot, IntPtr value)
    { _referenceArrays[array][slot] = value; Marshal.WriteIntPtr(array, ArrayHeaderBytes + slot * IntPtr.Size, value); CanvasSourceRemember(); }
    internal void LineRingNoNotes(bool emptyChart = false)
    {
        CursorAllArray = CursorArray(Array.Empty<IntPtr>()); CursorRenderArray = CursorArray(Array.Empty<IntPtr>());
        SetGameplayField("cursor.all", CursorAllArray); SetGameplayField("cursor.render", CursorRenderArray);
        if (emptyChart) foreach (var line in NoteViewChartLines)
            _seekLists[SeekValue<IntPtr>(line, "notes")] = Array.Empty<object>();
        CanvasSourceRemember();
    }
    internal void LineRingMultipleInstance()
    {
        var firstNote = NoteViewRuntimeNotes[0];
        var line = NoteViewRuntimeLine(0, 1, false); LineRingCompleteLine(line);
        var note = SeekObject(SeekType("InLevel.NoteInformation"));
        SeekField("linering.extra.note.data", SeekType("InLevel.NoteInformation"), "noteData", _seekNoteType,
            note, SeekValue<IntPtr>(firstNote, "noteData"), 0x28);
        SeekField("linering.extra.note.line", SeekType("InLevel.NoteInformation"), "lineInformation", _noteViewLineType, note, line, 0x20);
        NoteViewRuntimeNotes.Add(note);
        CursorAllArray = CursorArray(NoteViewRuntimeNotes.ToArray());
        CursorRenderArray = CursorArray(NoteViewRuntimeNotes.Where(value => !TargetBoolean(SeekValue<IntPtr>(value, "lineInformation"), "lineIsStatic")).ToArray());
        SetGameplayField("cursor.all", CursorAllArray); SetGameplayField("cursor.render", CursorRenderArray);
        var group = SeekValue<IntPtr>(line, "linesGroup");
        LineRingSetGroupLines(group, CanvasSourceSlots(LineRingGroupArray(group)).Append(line).ToArray());
        LineRingSetDynamic(CanvasSourceSlots(LineRingDynamicArray).Append(line).ToArray()); CanvasSourceRemember();
    }
    internal void LineRingAliasGraph()
    {
        var line = LineRingOrphanDynamic; var shared = LineRingLineTransform(line);
        LineRingSetDynamic(line, line, LineRingLines[0], line);
        LineRingSetGroups(LineRingOrphanGroup, LineRingGroups[0], LineRingOrphanGroup, LineRingGroups[1]);
        LineRingSetGroupLines(LineRingOrphanGroup, line, LineRingOrphanStatic, line);
        TargetSet(line, "endLinePoint", SeekValue<IntPtr>(line, "startLinePoint"));
        TargetSet(line, "endTransform", shared); TargetSet(LineRingOrphanGroup, "judgeRingTransform", shared);
        CanvasSourceRemember();
    }
    internal void LineRingAddParentChain(int count, bool cycle = false)
    {
        var line = LineRingLineTransform(LineRingOrphanDynamic); var parent = IntPtr.Zero;
        for (var i = 0; i < count; ++i)
            parent = GeometryNewTransform(parent, new(0, 0, 0), new(0, 0, 0, 1), new(1, 1, 1));
        if (cycle && parent != IntPtr.Zero) GeometryTransforms[parent] = GeometryTransforms[parent] with { Parent = parent };
        GeometryTransforms[line] = GeometryTransforms[line] with { Parent = parent };
    }
    internal void LineRingGetterMetadata(string name, string mode)
    {
        var method = _geometryMethods[name]; var row = _gameplayMethods[method];
        _gameplayMethods[method] = mode switch
        {
            "static" => row with { Static = true }, "owner" => row with { Class = ClassOf(Object) },
            "name" => row with { Name = "get_rotation" }, "return" => row with { Return = Single },
            "parameters" => row with { Parameters = new[] { Int32 } },
            "byref-return" => row with { Return = AddType(_types[row.Return].Name, _types[row.Return].Code,
                _types[row.Return].ValueType, _types[row.Return].Size, byRef: true) },
            "same-name-reference" => row with { Return = AddType(_types[row.Return].Name, _types[row.Return].Code,
                _types[row.Return].ValueType, _types[row.Return].Size) },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
    private IntPtr LineRingSourceInvoke(IntPtr method, IntPtr receiver, IntPtr args, IntPtr exception)
    {
        CanvasSourceObserveExport("il2cpp_runtime_invoke");
        var getter = _geometryMethods.FirstOrDefault(row => row.Value == method).Key;
        if (getter is "get_transform" or "get_parent" or "get_position" or "get_localPosition" or "get_localRotation" or "get_localScale")
        {
            LineRingGetterCalls.Add((getter, receiver));
            var result = GeometryInvoke(method, receiver, args, exception);
            if (getter is not ("get_transform" or "get_parent")) LineRingLastBox = result;
            if (getter == "get_localPosition") CanvasPositionLastBox = result;
            return result;
        }
        return CanvasPositionInvoke(method, receiver, args, exception);
    }
}
