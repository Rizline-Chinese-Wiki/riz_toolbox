using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Only new synthetic official-export inputs. This is not a product reader, a
// target Transform writer, or an alternative managed implementation of capture.
sealed partial class FakeNative
{
    internal CanvasSourceProfileValue CanvasSourceProfile = null!;
    internal IntPtr CanvasSourceMovesArray, CanvasSourceEaseArray;
    internal readonly List<IntPtr> CanvasSourceMoves = new(), CanvasSourceLists = new(), CanvasSourceSamples = new();
    internal readonly HashSet<IntPtr> CanvasSourcePointers = new();
    internal readonly List<(string Name, IntPtr Owner)> CanvasSourceReads = new();
    internal readonly List<(double From, double To, float Result)> CanvasSourceRanges = new();
    internal readonly List<IntPtr> CanvasSourceCountReads = new();
    internal readonly List<(IntPtr List, int Slot, IntPtr Value)> CanvasSourceItemReads = new();
    private readonly Dictionary<(IntPtr List, int Slot), IntPtr> _canvasSourceItemOverrides = new();
    internal Action? AfterCanvasSourceRange;
    internal Action<string, IntPtr>? AfterCanvasSourceRead;
    internal Action<uint, IntPtr, bool>? AfterCanvasSourceRoot;
    internal int CanvasSourceForbidden, CanvasSourceRangeAttempts, CanvasSourceCountBias, CanvasSourceRangeExceptionWrites;
    internal bool CanvasSourcePublished = true, CanvasSourceNoHold = true;
    internal float? CanvasSourceRangeResult;
    internal IntPtr CanvasSourceRangeException;
    private IntPtr _canvasSourceMoveType;
    private readonly Dictionary<(IntPtr, IntPtr), byte[]> _canvasSourceStorage = new();
    private readonly Dictionary<IntPtr, IntPtr[]> _canvasSourceArrays = new();
    private readonly Dictionary<IntPtr, byte[]> _canvasSourceSingleBytes = new();
    internal sealed class CanvasSourceProfileValue : ISeekCanvasNativeProfile
    {
        private readonly FakeNative _n;
        internal Action? OnVerify;
        internal string? WrongMember;
        internal IntPtr Range;
        internal CanvasSourceProfileValue(FakeNative n, IntPtr range) { _n = n; Range = range; }
        private IntPtr P(string name, IntPtr value) => WrongMember == name ? _n.ClassOf(_n.Object) : value;
        public IntPtr ChartClass => P(nameof(ChartClass), _n.SeekProfile.ChartClass);
        public IntPtr ProgressClass => P(nameof(ProgressClass), _n.SeekProfile.ProgressClass);
        public IntPtr InformationClass => P(nameof(InformationClass), _n.TargetProfile.InformationClass);
        public IntPtr CanvasClass => P(nameof(CanvasClass), _n.ClassOf(_n._geometryCanvasType));
        public IntPtr CanvasMoveClass => P(nameof(CanvasMoveClass), _n.ClassOf(_n._canvasSourceMoveType));
        public IntPtr KeyPointClass => P(nameof(KeyPointClass), _n.ClassOf(_n._seekKeyType));
        public IntPtr TransformClass => P(nameof(TransformClass), _n.ClassOf(_n._geometryTransformType));
        public IntPtr VectorClass => P(nameof(VectorClass), _n.ClassOf(_n._geometryVectorType));
        public IntPtr EaseClass => P(nameof(EaseClass), _n.ClassOf(_n._geometryEaseType));
        public IntPtr EaseInfoClass => P(nameof(EaseInfoClass), _n.ClassOf(_n._geometryEaseInfoType));
        public IntPtr ChartTimeRangeMethod => Range;
        public void VerifyCurrent() { OnVerify?.Invoke(); _n.SeekProfile.VerifyCurrent(); }
    }
    internal void ConfigureCanvasSource()
    {
        ConfigureHoldGeometry(); _canvasSourceMoveType = SeekType("InLevel.CanvasMove");
        _types[_seekKeyListType] = _types[_seekKeyListType] with { Code = 21 };
        var a = CanvasMove(999,
            new[] { (0f, 2f, 3f), (5f, 4f, 30f), (5f, 6f, 36f) },
            new[] { (0f, -1f, 0), (5f, 3f, 0), (5f, 9f, 0) });
        var b = CanvasMove(-5,
            new[] { (0f, 1f, 1f), (4f, 9f, 90f), (1f, 3f, 20f), (9f, 8f, 80f) },
            new[] { (0f, 1f, 1), (4f, 10f, 1), (1f, 5f, 1), (9f, 13f, 1) });
        CanvasSourceMoves.AddRange(new[] { a, b, a });
        CanvasSourceMovesArray = CanvasArray(_canvasSourceMoveType, CanvasSourceMoves.ToArray());
        TargetSet(GeometryCanvasControl, "canvasMoveData", CanvasSourceMovesArray);
        var transforms = new[] { GeometryCanvasParents[0],
            GeometryNewTransform(GeometryUiRoot, new(3, 4, 5), new(0, 0, 0, 1), new(1, 1, 1)),
            GeometryNewTransform(GeometryUiRoot, new(6, 7, 8), new(0, 0, 0, 1), new(1, 1, 1)) };
        GeometryCanvasArray = CanvasArray(_geometryTransformType, transforms);
        TargetSet(GeometryCanvasControl, "canvasTransforms", GeometryCanvasArray);
        TargetSet(GeometryCanvasControl, "screenWidth", 4f);
        foreach (var list in _seekLists.Keys.Where(p => _boxedTypes[p] == _seekKeyListType).ToArray())
            if (!_gameplayStorage.ContainsKey((list, TargetField(list, "_size"))))
                CanvasListFields(list, _seekLists[list].Cast<IntPtr>().ToArray());
        CanvasStaticVector("downVector", 0x24, new(-2f, .5f, 4f));
        CanvasStaticVector("rightVector", 0x3c, new(.25f, -3f, -.5f));
        var eases = new List<IntPtr>();
        for (var code = 0; code < 2; ++code)
        {
            var samples = Enumerable.Range(0, 101).Select(i => code == 0 ? .25f : i == 50 ? .375f : i == 51 ? .875f : .75f).ToArray();
            var array = CanvasArray(Single, samples.Select(v => Box(Single, BitConverter.GetBytes(v))).ToArray());
            CanvasSourceSamples.Add(array);
            var info = CanvasObject(_geometryEaseInfoType); var key = "canvas.source.ease." + code;
            SeekField(key + ".code", _geometryEaseInfoType, "code", Int32, info, code, 0x10);
            SeekField(key + ".progress", _geometryEaseInfoType, "progress", _boxedTypes[array], info, array, 0x18);
            eases.Add(info);
        }
        CanvasSourceEaseArray = CanvasArray(_geometryEaseInfoType, eases.ToArray());
        _gameplayStorage[(IntPtr.Zero, _gameplayFields["geometry.ease.static"].Field)] = BitConverter.GetBytes(CanvasSourceEaseArray.ToInt64());
        CanvasSourcePointers.UnionWith(new[] { GameplayInformation, GameplayProgress, GeometryCanvasControl });
        // Poison unavailable Hold containers: Canvas capture may not use them.
        TargetSet(GameplayInformation, "holdControl", IntPtr.Zero);
        TargetSet(TargetHold, "holdNotes", IntPtr.Zero); TargetSet(TargetHold, "runningHoldNotes", IntPtr.Zero);
        var range = GameplayMethodAdd(SeekType("InLevel.ProgressControl"), "ChartTimeRageToRealTime", false, Single, _gameplayDouble, _gameplayDouble);
        CanvasSourceProfile = new(this, range); ConfigureCanvasSourceExports();
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread(); CanvasSourceRemember();
    }
    private IntPtr CanvasObject(IntPtr type)
    { var p = SeekObject(type); CanvasSourcePointers.Add(p); return p; }
    private IntPtr CanvasArray(IntPtr type, IntPtr[] slots)
    { var p = GeometryArray(type, slots); CanvasSourcePointers.Add(p); foreach (var slot in slots) if (slot != IntPtr.Zero && type != Single) CanvasSourcePointers.Add(slot); return p; }
    private IntPtr CanvasPoint(float time, float value, int ease, float floor)
    {
        var p = CanvasObject(_seekKeyType); var key = "canvas.source.point." + p;
        SeekField(key + ".time", _seekKeyType, "time", Single, p, time, 0x10);
        SeekField(key + ".value", _seekKeyType, "value", Single, p, value, 0x14);
        SeekField(key + ".ease", _seekKeyType, "easeType", Int32, p, ease, 0x18);
        SeekField(key + ".floor", _seekKeyType, "floorPosition", Single, p, floor, 0x1c);
        return p;
    }
    private IntPtr CanvasList(params IntPtr[] points)
    {
        var p = SeekList(_seekKeyListType, _seekKeyType, points.Cast<object>().ToArray());
        CanvasSourcePointers.Add(p); CanvasListFields(p, points); CanvasSourceLists.Add(p); return p;
    }
    private void CanvasListFields(IntPtr list, IntPtr[] points)
    {
        var array = CanvasArray(_seekKeyType, points); var key = "canvas.source.list." + list;
        SeekField(key + ".items", _seekKeyListType, "_items", _boxedTypes[array], list, array, 0x10);
        SeekField(key + ".size", _seekKeyListType, "_size", Int32, list, points.Length, 0x18);
        SeekField(key + ".version", _seekKeyListType, "_version", Int32, list, -17, 0x1c);
    }
    private IntPtr CanvasMove(int index, (float Time, float Value, float Floor)[] speeds, (float Time, float Value, int Ease)[] xs)
    {
        var p = CanvasObject(_canvasSourceMoveType); var key = "canvas.source.move." + p;
        var speed = CanvasList(speeds.Select(v => CanvasPoint(v.Time, v.Value, 0, v.Floor)).ToArray());
        var x = CanvasList(xs.Select(v => CanvasPoint(v.Time, v.Value, v.Ease, 0)).ToArray());
        SeekField(key + ".index", _canvasSourceMoveType, "index", Int32, p, index, 0x10);
        SeekField(key + ".speed", _canvasSourceMoveType, "speedKeyPoints", _seekKeyListType, p, speed, 0x20);
        SeekField(key + ".x", _canvasSourceMoveType, "xPositionKeyPoints", _seekKeyListType, p, x, 0x18);
        return p;
    }
    private void CanvasStaticVector(string name, int offset, NativeVector3 value)
    {
        var key = "canvas.source.vector." + name;
        SeekField(key, _geometryVectorType, name, _geometryVectorType, IntPtr.Zero, 0f, offset);
        var field = _gameplayFields[key].Field; _gameplayStorage[(IntPtr.Zero, field)] = GeometryBytes(value); _judgeFlags[field] = 0x30u;
        foreach (var f in _structFields[ClassOf(_geometryVectorType)]) _gameplayFieldParents[f] = ClassOf(_geometryVectorType);
    }
    internal IntPtr CanvasSourceList(int move, bool speed) => SeekValue<IntPtr>(CanvasSourceMoves[move], speed ? "speedKeyPoints" : "xPositionKeyPoints");
    internal IntPtr CanvasSourceBacking(IntPtr list) => SeekValue<IntPtr>(list, "_items");
    internal void CanvasSourceOverrideItem(IntPtr list, int slot, IntPtr value) => _canvasSourceItemOverrides[(list, slot)] = value;
    internal IntPtr CanvasSourcePoint(int move, bool speed, int slot) => _referenceArrays[CanvasSourceBacking(CanvasSourceList(move, speed))][slot];
    internal void CanvasSourceSlot(IntPtr array, int slot, IntPtr value)
    { _referenceArrays[array][slot] = value; Marshal.WriteIntPtr(array, ArrayHeaderBytes + slot * IntPtr.Size, value); CanvasSourceRemember(); }
    internal void CanvasSourceRemember()
    {
        _canvasSourceStorage.Clear(); foreach (var row in _gameplayStorage) _canvasSourceStorage.Add(row.Key, row.Value.ToArray());
        _canvasSourceArrays.Clear(); foreach (var row in _referenceArrays) _canvasSourceArrays.Add(row.Key, row.Value.ToArray());
        _canvasSourceSingleBytes.Clear();
        foreach (var row in _geometrySingleArrays)
        {
            var bytes = new byte[row.Value.Length * sizeof(float)];
            Marshal.Copy(IntPtr.Add(row.Key, ArrayHeaderBytes), bytes, 0, bytes.Length);
            _canvasSourceSingleBytes.Add(row.Key, bytes);
        }
        GeometryRememberOther();
    }
    private bool CanvasSourceSinglesUnchanged()
    {
        foreach (var row in _canvasSourceSingleBytes)
        {
            var bytes = new byte[row.Value.Length]; Marshal.Copy(IntPtr.Add(row.Key, ArrayHeaderBytes), bytes, 0, bytes.Length);
            if (!bytes.SequenceEqual(row.Value)) return false;
        }
        return true;
    }
    internal void CanvasSourceSetEase(IntPtr array)
    { _gameplayStorage[(IntPtr.Zero, _gameplayFields["geometry.ease.static"].Field)] = BitConverter.GetBytes(array.ToInt64()); CanvasSourceRemember(); }
    internal bool CanvasSourceUnchanged() => _canvasSourceStorage.All(row => _gameplayStorage.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value))
        && _canvasSourceArrays.All(row => _referenceArrays.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value)) && CanvasSourceSinglesUnchanged();
    internal void CanvasSourceSet(IntPtr owner, string name, object value)
    { TargetSet(owner, name, value); CanvasSourceRemember(); }
    internal void CanvasSourceSetVector(string name, NativeVector3 value)
    { _gameplayStorage[(IntPtr.Zero, _gameplayFields["canvas.source.vector." + name].Field)] = GeometryBytes(value); CanvasSourceRemember(); }
    internal void CanvasSourceInterpolate()
    {
        var list = CanvasSourceList(0, false);
        CanvasListFields(list, new[] { CanvasPoint(0, 1, 1, 0), CanvasPoint(100, 5, 1, 0) }); CanvasSourceRemember();
    }
    internal void CanvasSourceEmpty()
    {
        TargetSet(GeometryCanvasControl, "canvasTransforms", CanvasArray(_geometryTransformType, Array.Empty<IntPtr>()));
        TargetSet(GeometryCanvasControl, "canvasMoveData", CanvasArray(_canvasSourceMoveType, Array.Empty<IntPtr>())); CanvasSourceRemember();
    }
    internal IntPtr[] CanvasSourceSlots(IntPtr array) => _referenceArrays[array].ToArray();
    internal IntPtr CanvasSourceCloneArray(IntPtr array)
    {
        var element = _geometryArrayElements[_boxedTypes[array]];
        return element == Single ? CanvasSourceSampleArray(_geometrySingleArrays[array]) : CanvasArray(element, _referenceArrays[array].ToArray());
    }
    internal IntPtr CanvasSourceResizeArray(IntPtr array, int count)
    {
        var slots = _referenceArrays[array];
        return CanvasArray(_geometryArrayElements[_boxedTypes[array]], Enumerable.Range(0, count).Select(i => slots[i % slots.Length]).ToArray());
    }
    internal IntPtr CanvasSourceSampleArray(params float[] samples)
        => CanvasArray(Single, samples.Select(v => Box(Single, BitConverter.GetBytes(v))).ToArray());
    internal IntPtr CanvasSourceCloneObject(IntPtr original)
    {
        var copy = CanvasObject(_boxedTypes[original]);
        foreach (var row in _gameplayStorage.Where(row => row.Key.Item1 == original).ToArray())
            _gameplayStorage[(copy, row.Key.Item2)] = row.Value.ToArray();
        return copy;
    }
    internal IntPtr CanvasSourceCloneRange()
    {
        var original = _gameplayMethods[CanvasSourceProfile.Range];
        return GameplayMethodAdd(_classes[original.Class], original.Name, original.Static, original.Return, original.Parameters);
    }
    internal void CanvasSourceWarmMethodLayouts()
    {
        GameplayApi.InspectCanvasSourceMethod(CanvasSourceProfile.ProgressClass, "ChartTimeRageToRealTime",
            new[] { "System.Double", "System.Double" }, "System.Single", IntPtr.Zero);
        GameplayApi.InspectCanvasSourceMethod(ClassOf(_seekKeyListType), "get_Count", Array.Empty<string>(), "System.Int32", IntPtr.Zero);
        GameplayApi.InspectCanvasSourceMethod(ClassOf(_seekKeyListType), "get_Item", new[] { "System.Int32" }, "InLevel.KeyPoint", IntPtr.Zero);
    }
    internal void CanvasSourceScalarWidth(string name, int width)
    {
        var type = name switch { "System.Double" => _gameplayDouble, "System.Single" => Single,
            "System.Int32" => Int32, _ => throw new ArgumentOutOfRangeException(nameof(name)) };
        _types[type] = _types[type] with { Size = width };
    }
    internal string CanvasSourceActiveRootDiagnostics() => string.Join(",", _handles.OrderBy(row => row.Key).Take(12).Select(row =>
        row.Key + ":0x" + row.Value.ToInt64().ToString("x") + ":"
        + (_boxedTypes.TryGetValue(row.Value, out var type) ? _types[type].Name : "unknown")));
}
