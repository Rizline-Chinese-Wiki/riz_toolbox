using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal readonly List<IntPtr> GeometryNoteTransforms = new(), GeometryEndTransforms = new(), GeometryLineTransforms = new();
    internal readonly List<IntPtr> GeometryCanvasParents = new(), GeometryBasisLines = new(), GeometryRings = new();
    internal IntPtr GeometryUi, GeometryCanvasControl, GeometryCamera, GeometryUiRoot, GeometryCanvasArray;
    private IntPtr _geometryUiType, _geometryCanvasType, _geometryCameraType, _geometryLineControlType, _geometryEaseType, _geometryEaseInfoType;
    private readonly Dictionary<IntPtr, IntPtr> _geometryArrayElements = new();
    private readonly Dictionary<IntPtr, float[]> _geometrySingleArrays = new();
    private readonly Dictionary<IntPtr, IntPtr> _geometryGroupRings = new();
    private readonly Dictionary<IntPtr, IntPtr> _geometryLineEnds = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _geometryOther = new();
    internal GeometryProfileValue GeometryProfile = null!;
    internal sealed class GeometryProfileValue : ISeekHoldGeometryProfile
    {
        private readonly FakeNative _n; internal Action? OnVerify;
        internal GeometryProfileValue(FakeNative n) => _n = n;
        public IntPtr ChartClass => _n.HoldStateProfile.ChartClass;
        public IntPtr ProgressClass => _n.HoldStateProfile.ProgressClass;
        public IntPtr InformationClass => _n.HoldStateProfile.InformationClass;
        public IntPtr NoteClass => _n.HoldStateProfile.NoteClass;
        public IntPtr JudgeClass => _n.HoldStateProfile.JudgeClass;
        public IntPtr HoldClass => _n.HoldStateProfile.HoldClass;
        public IntPtr ChallengeTimeClass => _n.HoldStateProfile.ChallengeTimeClass;
        public IntPtr NoteListClass => _n.HoldStateProfile.NoteListClass;
        public IntPtr ClearMethod => _n.HoldStateProfile.ClearMethod;
        public IntPtr AddMethod => _n.HoldStateProfile.AddMethod;
        public IntPtr UIClass => _n.ClassOf(_n._geometryUiType);
        public IntPtr CanvasClass => _n.ClassOf(_n._geometryCanvasType);
        public IntPtr CameraClass => _n.ClassOf(_n._geometryCameraType);
        public IntPtr LineClass => _n.ClassOf(_n._noteViewLineType);
        public IntPtr LinePointClass => _n.ClassOf(_n._noteViewPointType);
        public IntPtr GroupClass => _n.ClassOf(_n._noteViewGroupType);
        public IntPtr EaseClass => _n.ClassOf(_n._geometryEaseType);
        public IntPtr EaseInfoClass => _n.ClassOf(_n._geometryEaseInfoType);
        public IntPtr TransformClass => _n.ClassOf(_n._geometryTransformType);
        public IntPtr GetTransformMethod => _n._geometryMethods["get_transform"];
        public IntPtr GetParentMethod => _n._geometryMethods["get_parent"];
        public IntPtr GetPositionMethod => _n._geometryMethods["get_position"];
        public IntPtr GetLocalPositionMethod => _n._geometryMethods["get_localPosition"];
        public IntPtr GetLocalRotationMethod => _n._geometryMethods["get_localRotation"];
        public IntPtr GetLocalScaleMethod => _n._geometryMethods["get_localScale"];
        public IntPtr SetParentMethod => _n._geometryMethods["SetParent"];
        public IntPtr SetPositionMethod => _n._geometryMethods["set_position"];
        public IntPtr SetLocalPositionMethod => _n._geometryMethods["set_localPosition"];
        public IntPtr SetLocalRotationMethod => _n._geometryMethods["set_localRotation"];
        public IntPtr SetLocalScaleMethod => _n._geometryMethods["set_localScale"];
        public IntPtr TransformPointMethod => _n._geometryMethods["TransformPoint"];
        public IntPtr InverseTransformPointMethod => _n._geometryMethods["InverseTransformPoint"];
        public void VerifyCurrent() { OnVerify?.Invoke(); _n.HoldStateProfile.VerifyCurrent(); }
    }
    internal void ConfigureHoldGeometry(NoteViewShape shape = NoteViewShape.Complete)
    {
        ConfigureHoldState(shape); ConfigureGeometryTransforms();
        _geometryUiType = SeekType("InLevel.UIControl"); _geometryCanvasType = SeekType("InLevel.CanvasControl");
        _geometryCameraType = SeekType("InLevel.CameraControl"); _geometryLineControlType = SeekType("InLevel.LineControl");
        _geometryEaseType = SeekType("InLevel.GetEase"); _geometryEaseInfoType = SeekType("InLevel.EaseInfor");
        GeometryUi = SeekObject(_geometryUiType); GeometryCanvasControl = SeekObject(_geometryCanvasType); GeometryCamera = SeekObject(_geometryCameraType);
        GeometryComponentTransforms[GeometryCamera] = GeometryNewTransform(IntPtr.Zero, new(0, 0, -10), new(0, 0, 0, 1), new(1, 1, 1));
        var lineControl = SeekObject(_geometryLineControlType); var information = SeekType("InLevel.LevelInformation");
        GeometryUiRoot = GeometryNewTransform(IntPtr.Zero, new(10, -20, 30), new(0, 0, 1, 0), new(2, 3, 4));
        var canvas = GeometryNewTransform(GeometryUiRoot, new(1, 2, 3), new(0, 0, 0, 1), new(1, 1, 1));
        GeometryCanvasParents.Add(canvas); GeometryCanvasArray = GeometryArray(_geometryTransformType, new[] { canvas });
        SeekField("geometry.info.ui", information, "uIControl", _geometryUiType, GameplayInformation, GeometryUi, 0x68);
        SeekField("geometry.info.canvas", information, "canvasControl", _geometryCanvasType, GameplayInformation, GeometryCanvasControl, 0x70);
        SeekField("geometry.info.camera", information, "cameraControl", _geometryCameraType, GameplayInformation, GeometryCamera, 0x78);
        SeekField("geometry.info.line", information, "lineControl", _geometryLineControlType, GameplayInformation, lineControl, 0x38);
        SeekField("geometry.ui.root", _geometryUiType, "gameSceneCanvas", _geometryTransformType, GeometryUi, GeometryUiRoot, 0x20);
        SeekField("geometry.canvas.info", _geometryCanvasType, "levelInformation", information, GeometryCanvasControl, GameplayInformation, 0x20);
        SeekField("geometry.canvas.transforms", _geometryCanvasType, "canvasTransforms", _boxedTypes[GeometryCanvasArray], GeometryCanvasControl, GeometryCanvasArray, 0x30);
        var moves = GeometryArray(SeekType("InLevel.CanvasMove"), Array.Empty<IntPtr>());
        SeekField("geometry.canvas.moves", _geometryCanvasType, "canvasMoveData", _boxedTypes[moves], GeometryCanvasControl, moves, 0x28);
        foreach (var (name, offset, value) in new[] { ("floorPositions", 0x38, 2f), ("floorSpeeds", 0x40, 3f), ("xPositions", 0x48, 4f) })
        {
            var array = GeometryArray(Single, new[] { Box(Single, BitConverter.GetBytes(value)) });
            SeekField("geometry.canvas." + name, _geometryCanvasType, name, _boxedTypes[array], GeometryCanvasControl, array, offset);
        }
        foreach (var (name, offset, value) in new[] { ("nowTime", 0x50, -999f), ("speed", 0x54, 5f), ("screenWidth", 0x58, 1920f) })
            SeekField("geometry.canvas." + name, _geometryCanvasType, name, Single, GeometryCanvasControl, value, offset);
        SeekField("geometry.camera.info", _geometryCameraType, "levelInformation", information, GeometryCamera, GameplayInformation, 0x20);
        SeekField("geometry.camera.x", _geometryCameraType, "xPosition", Single, GeometryCamera, 7f, 0x30);
        SeekField("geometry.camera.scale", _geometryCameraType, "scale", Single, GeometryCamera, 2f, 0x34);
        for (var ordinal = 0; ordinal < HoldStateNotes.Count; ++ordinal)
        {
            var note = HoldStateNotes[ordinal]; var runtimeLine = SeekValue<IntPtr>(note, "lineInformation");
            if (!GeometryComponentTransforms.TryGetValue(runtimeLine, out var line))
                GeometryComponentTransforms[runtimeLine] = line = GeometryNewTransform(GeometryUiRoot, new(4, 5, 6), new(0, 0, 0, 1), new(1, 1, 1));
            if (!_geometryLineEnds.TryGetValue(runtimeLine, out var endLine))
                _geometryLineEnds[runtimeLine] = endLine = GeometryNewTransform(GeometryUiRoot, new(9, 15, 6), new(0, 0, 0, 1), new(1, 1, 1));
            var group = SeekValue<IntPtr>(runtimeLine, "linesGroup");
            if (!_geometryGroupRings.TryGetValue(group, out var ring))
                _geometryGroupRings[group] = ring = GeometryNewTransform(GeometryUiRoot, new(-3, 4, 2), new(0, 0, 1, 0), new(2, 1, 1));
            var noteTransform = GeometryNewTransform(canvas, new(1, 2 + ordinal, 3), new(0, 0, 0, 1), new(1, 1, 1));
            var endTransform = GeometryNewTransform(canvas, new(1, 8 + ordinal, 3), new(0, 0, 0, 1), new(1, 1, 1));
            var holdTransform = GeometryNewTransform(noteTransform, new(0, 0, 0), new(0, 0, 0, 1), new(3, 4, 5));
            GeometryNoteTransforms.Add(noteTransform); GeometryEndTransforms.Add(endTransform); GeometryLineTransforms.Add(holdTransform);
            GeometryBasisLines.Add(line); GeometryRings.Add(ring); GeometryComponentTransforms[note] = noteTransform;
            TargetSet(note, "holdEndFlag", endTransform); TargetSet(note, "holdLine", holdTransform);
            SeekField("geometry.line.canvas." + ordinal, _noteViewLineType, "canvasIndex", Int32, runtimeLine, 0, 0x24);
            SeekField("geometry.line.endtransform." + ordinal, _noteViewLineType, "endTransform", _geometryTransformType, runtimeLine, endLine, 0x40);
            SeekField("geometry.line.endline." + ordinal, _noteViewLineType, "endLineInformation", _noteViewLineType, runtimeLine, runtimeLine, 0x58);
            SeekField("geometry.group.ring." + ordinal, _noteViewGroupType, "judgeRingTransform", _geometryTransformType, group, ring, 0x20);
            SeekField("geometry.group.active." + ordinal, _noteViewGroupType, "ringActive", Boolean, group, true, 0x29);
            SeekField("geometry.group.index." + ordinal, _noteViewGroupType, "nowLineIndex", Int32, group, 0, 0x70);
            var start = SeekValue<IntPtr>(runtimeLine, "startLinePoint"); var end = SeekValue<IntPtr>(runtimeLine, "endLinePoint");
            foreach (var point in new[] { start, end })
            foreach (var (name, type, offset, value) in new[] { ("xPosition", Single, 0x14, (object)0f), ("easeType", Int32, 0x1c, (object)0),
                         ("canvasIndex", Int32, 0x20, (object)0), ("floorPosition", Single, 0x24, (object)0f) })
                SeekField("geometry.point." + point + name, _noteViewPointType, name, type, point, value, offset);
        }
        var progress = GeometryArray(Single, Enumerable.Range(0, 101).Select(i => Box(Single, BitConverter.GetBytes(i / 100f))).ToArray());
        var ease = SeekObject(_geometryEaseInfoType); SeekField("geometry.ease.code", _geometryEaseInfoType, "code", Int32, ease, 0, 0x10);
        SeekField("geometry.ease.progress", _geometryEaseInfoType, "progress", _boxedTypes[progress], ease, progress, 0x18);
        var eases = GeometryArray(_geometryEaseInfoType, new[] { ease });
        SeekField("geometry.ease.static", _geometryEaseType, "EaseInfos", _boxedTypes[eases], IntPtr.Zero, eases, 0);
        _judgeFlags[_gameplayFields["geometry.ease.static"].Field] = 0x10;
        ConfigureGeometryExports(); GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread(); GeometryProfile = new(this);
        GeometryRememberOther();
    }
    private IntPtr GeometryArray(IntPtr element, IntPtr[] values)
    {
        var name = _types[element].Name + "[]"; var type = _types.FirstOrDefault(row => row.Value.Name == name).Key;
        if (type == IntPtr.Zero) type = AddType(name, 29, false, IntPtr.Size);
        _geometryArrayElements[type] = element;
        var width = element == Single ? sizeof(float) : IntPtr.Size;
        var array = GameplayAllocate(ArrayHeaderBytes + values.Length * width); Marshal.WriteIntPtr(array, ClassOf(type));
        _boxedTypes[array] = type; _referenceArrays[array] = values.ToArray(); _referenceRanks[array] = 1;
        if (element == Single)
        {
            var singles = values.Select(value => BitConverter.ToSingle(RenderedValue(value))).ToArray(); _geometrySingleArrays.Add(array, singles);
            Marshal.Copy(singles, 0, IntPtr.Add(array, ArrayHeaderBytes), singles.Length);
        }
        else for (var i = 0; i < values.Length; ++i) Marshal.WriteIntPtr(array, ArrayHeaderBytes + i * IntPtr.Size, values[i]);
        return array;
    }
    internal void GeometryRememberOther()
    { _geometryOther.Clear(); foreach (var row in _gameplayStorage) _geometryOther.Add(row.Key, row.Value.ToArray()); }
    internal bool GeometryOtherUnchanged() => _geometryOther.All(row => _gameplayStorage.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value));
    internal void GeometrySetEndFloor(int ordinal, float value)
    {
        var data = HoldStateData(HoldStateNotes[ordinal]); var list = SeekValue<IntPtr>(data, "otherInformations");
        var values = _seekLists[list].ToArray(); values[2] = value; _seekLists[list] = values;
    }
    internal void GeometrySetTailCanvas(int ordinal, float value)
    {
        var data = HoldStateData(HoldStateNotes[ordinal]); var list = SeekValue<IntPtr>(data, "otherInformations");
        var values = _seekLists[list].ToArray(); values[1] = value; _seekLists[list] = values;
    }
    internal IntPtr GeometryLineFor(int ordinal) => SeekValue<IntPtr>(HoldStateNotes[ordinal], "lineInformation");
    internal IntPtr GeometryGroupFor(int ordinal) => SeekValue<IntPtr>(GeometryLineFor(ordinal), "linesGroup");
    internal void GeometryMetadata(IntPtr owner, string name, int offset)
    { var field = TargetField(owner, name); var prior = _fieldMetadata[field]; _fieldMetadata[field] = (prior.Name, prior.Type, offset); }
}
