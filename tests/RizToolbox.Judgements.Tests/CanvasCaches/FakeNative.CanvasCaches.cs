using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Official-export inputs for the real RunValue cache access. Array stores are
// performed by production code; this fixture only samples their native bytes at
// export boundaries. It does not replace INativeSeekCanvasCacheAccess.
sealed partial class FakeNative
{
    internal sealed record CanvasCacheObservation(SeekCanvasCacheKind Kind, IntPtr Owner, int Index, int Bits);
    internal sealed record CanvasCacheScalarCall(string Name, int Bits, bool Stored);
    internal readonly List<CanvasCacheObservation> CanvasCacheObservedStores = new();
    internal readonly List<CanvasCacheScalarCall> CanvasCacheScalarCalls = new();
    private readonly Dictionary<(IntPtr Array, int Index), int> _canvasCacheObserved = new();
    private readonly Dictionary<string, int> _canvasCacheObservedScalars = new();
    internal Action<string>? BeforeCanvasCacheScalar, AfterCanvasCacheScalar;
    internal Action<CanvasCacheObservation>? AfterCanvasCacheObservedStore;
    internal int FailCanvasCacheScalarBefore, FailCanvasCacheScalarAfter;
    internal bool CanvasCacheStoresPinned = true, CanvasCacheScalarsRooted = true;
    internal static readonly string[] CanvasCacheNames = { "floorPositions", "floorSpeeds", "xPositions", "nowTime", "speed" };

    internal void ConfigureCanvasCaches(bool positions = false)
    {
        if (positions) ConfigureCanvasPositions(); else ConfigureCanvasSource();
        for (var kind = 0; kind < 3; ++kind)
            TargetSet(GeometryCanvasControl, CanvasCacheNames[kind], CanvasSourceSampleArray(
                Enumerable.Range(0, 3).Select(i => (kind + 1) * 100f + i + 1).ToArray()));
        CanvasSourceRemember(); CanvasCacheRememberObserved();
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(CanvasCacheWriteScalar));
        CanvasSourceInstrumentExports();
        foreach (var row in _exports.ToArray())
        {
            var type = row.Value.GetType();
            var args = type.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            var observe = Expression.Call(Expression.Constant(this), GetType().GetMethod(nameof(CanvasCacheObserveStorage), BindingFlags.NonPublic | BindingFlags.Instance)!);
            _exports[row.Key] = Expression.Lambda(type,
                Expression.TryFinally(Expression.Invoke(Expression.Constant(row.Value, type), args), observe), args).Compile();
        }
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    internal IntPtr CanvasCacheArray(int kind) => SeekValue<IntPtr>(GeometryCanvasControl, CanvasCacheNames[kind]);
    internal IntPtr CanvasCacheField(int kind) => TargetField(GeometryCanvasControl, CanvasCacheNames[kind]);
    internal int CanvasCacheBits(int kind, int index = -1) => kind < 3
        ? Marshal.ReadInt32(CanvasCacheArray(kind), ArrayHeaderBytes + index * sizeof(float))
        : BitConverter.ToInt32(_gameplayStorage[(GeometryCanvasControl, TargetField(GeometryCanvasControl, CanvasCacheNames[kind]))]);
    internal int[] CanvasCacheSnapshot()
    {
        var result = new List<int>();
        for (var kind = 0; kind < 3; ++kind)
            for (var i = 0; i < _geometrySingleArrays[CanvasCacheArray(kind)].Length; ++i) result.Add(CanvasCacheBits(kind, i));
        result.Add(CanvasCacheBits(3)); result.Add(CanvasCacheBits(4)); return result.ToArray();
    }
    internal void CanvasCacheSetBits(int kind, int index, int bits)
    {
        if (kind < 3) GeometrySingleSlot(CanvasCacheArray(kind), index, BitConverter.Int32BitsToSingle(bits));
        else _gameplayStorage[(GeometryCanvasControl, TargetField(GeometryCanvasControl, CanvasCacheNames[kind]))] = BitConverter.GetBytes(bits);
        CanvasCacheRememberObserved(); CanvasSourceRemember();
    }
    internal void CanvasCacheReplace(int kind, IntPtr array)
    {
        TargetSet(GeometryCanvasControl, CanvasCacheNames[kind], array);
        CanvasCacheRememberObserved(); CanvasSourceRemember();
    }
    internal void CanvasCacheAlias(int kind, int source) => CanvasCacheReplace(kind, CanvasCacheArray(source));
    internal void CanvasCacheEmpty()
    {
        CanvasSourceEmpty();
        for (var kind = 0; kind < 3; ++kind) CanvasCacheReplace(kind, CanvasSourceSampleArray());
    }
    internal void CanvasCacheEqualTargets()
    {
        foreach (var move in CanvasSourceMoves.Distinct())
        foreach (var speed in new[] { false, true })
        {
            var list = SeekValue<IntPtr>(move, speed ? "speedKeyPoints" : "xPositionKeyPoints");
            foreach (var point in _referenceArrays[CanvasSourceBacking(list)])
            {
                TargetSet(point, "value", 0f); TargetSet(point, "floorPosition", 0f);
            }
        }
        CanvasSourceRemember();
    }
    internal void CanvasCacheMetadata(int kind, string mode)
    {
        if (mode == "readonly") _judgeFlags[TargetField(GeometryCanvasControl, CanvasCacheNames[kind])] = 0x20u;
        else CanvasSourceMetadata(GeometryCanvasControl, CanvasCacheNames[kind], mode);
    }
    private void CanvasCacheRememberObserved()
    {
        _canvasCacheObserved.Clear(); _canvasCacheObservedScalars.Clear();
        for (var kind = 0; kind < 3; ++kind)
        {
            var array = CanvasCacheArray(kind);
            // Valid fixtures have exactly three complete physical slots. Bound
            // export-boundary sampling for the deliberately oversized reject
            // fixture; that case compares its entire native vector separately.
            for (var i = 0; i < Math.Min(4, _geometrySingleArrays[array].Length); ++i)
                _canvasCacheObserved[(array, i)] = Marshal.ReadInt32(array, ArrayHeaderBytes + i * sizeof(float));
        }
        for (var kind = 3; kind < 5; ++kind) _canvasCacheObservedScalars[CanvasCacheNames[kind]] = CanvasCacheBits(kind);
    }
    private void CanvasCacheObserveStorage()
    {
        foreach (var slot in _canvasCacheObserved.Keys.ToArray())
        {
            var bits = Marshal.ReadInt32(slot.Array, ArrayHeaderBytes + slot.Index * sizeof(float));
            if (_canvasCacheObserved[slot] == bits) continue;
            _canvasCacheObserved[slot] = bits;
            CanvasCacheStoresPinned &= _handles.Any(root => root.Value == slot.Array
                && NoteViewRootLog.Any(row => row.Handle == root.Key && row.Pinned));
            var kind = Enumerable.Range(0, 3).First(k => CanvasCacheArray(k) == slot.Array);
            var row = new CanvasCacheObservation((SeekCanvasCacheKind)kind, slot.Array, slot.Index, bits);
            CanvasCacheObservedStores.Add(row); AfterCanvasCacheObservedStore?.Invoke(row);
        }
        for (var kind = 3; kind < 5; ++kind)
        {
            var name = CanvasCacheNames[kind]; var bits = CanvasCacheBits(kind);
            if (!_canvasCacheObservedScalars.TryGetValue(name, out var prior) || prior == bits) continue;
            _canvasCacheObservedScalars[name] = bits;
            var row = new CanvasCacheObservation((SeekCanvasCacheKind)kind, GeometryCanvasControl, -1, bits);
            CanvasCacheObservedStores.Add(row); AfterCanvasCacheObservedStore?.Invoke(row);
        }
    }
    private void CanvasCacheWriteScalar(IntPtr owner, IntPtr field, IntPtr value)
    {
        var name = _fieldMetadata[field].Name;
        if (owner != GeometryCanvasControl || name is not ("nowTime" or "speed")
            || _fieldMetadata[field].Type != Single || _types[Single].Size != sizeof(float)
            || _fieldMetadata[field].Offset != (name == "nowTime" ? 0x50 : 0x54)
            || _judgeFlags.GetValueOrDefault(field) != 0)
        { ++CanvasSourceForbidden; throw new InvalidOperationException("Only exact mutable Canvas nowTime/speed scalar storage is writable."); }
        ++ValueFieldWrites; CanvasCacheScalarsRooted &= HasRootFor(owner);
        var call = CanvasCacheScalarCalls.Count; var bits = Marshal.ReadInt32(value);
        CanvasCacheScalarCalls.Add(new(name, bits, false)); BeforeCanvasCacheScalar?.Invoke(name);
        if (FailCanvasCacheScalarBefore == call + 1) throw new IOException("Synthetic Canvas scalar failure before storage.");
        _gameplayStorage[(owner, field)] = BitConverter.GetBytes(bits);
        CanvasCacheScalarCalls[call] = new(name, bits, true); CanvasCacheObserveStorage();
        AfterCanvasCacheScalar?.Invoke(name);
        if (FailCanvasCacheScalarAfter == call + 1) throw new IOException("Synthetic Canvas scalar failure after storage.");
    }
    internal bool CanvasCacheUnrelatedUnchanged()
    {
        bool CacheField((IntPtr Owner, IntPtr Field) key) => key.Owner == GeometryCanvasControl && CanvasCacheNames.Contains(_fieldMetadata[key.Field].Name);
        var arrays = Enumerable.Range(0, 3).Select(CanvasCacheArray).ToHashSet();
        return _canvasSourceStorage.Where(row => !CacheField(row.Key)).All(row =>
                _gameplayStorage.TryGetValue(row.Key, out var bytes) && bytes.SequenceEqual(row.Value))
            && _canvasSourceArrays.All(row => _referenceArrays.TryGetValue(row.Key, out var values) && values.SequenceEqual(row.Value))
            && _canvasSourceSingleBytes.Where(row => !arrays.Contains(row.Key)).All(row =>
            {
                var bytes = new byte[row.Value.Length]; Marshal.Copy(IntPtr.Add(row.Key, ArrayHeaderBytes), bytes, 0, bytes.Length);
                return bytes.SequenceEqual(row.Value);
            });
    }
}
