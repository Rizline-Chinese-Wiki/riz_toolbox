using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Official-export storage for the actual retained LineRingReader. The array
// itself is one physical value-element IL2CPP object, not a managed substitute.
sealed partial class FakeNative
{
    private IntPtr _lineBufferArrayType;
    private readonly Dictionary<IntPtr, int> _lineBufferLengths = new();
    internal IntPtr LineBufferArray;

    internal void ConfigureLineBuffer(params NativeVector3BufferBits[] initial)
    {
        _lineBufferArrayType = AddType("UnityEngine.Vector3[]", 29, false, IntPtr.Size);
        _geometryArrayElements[_lineBufferArrayType] = _geometryVectorType;
        LineBufferArray = LineBufferNewArray(initial);
        SeekField("linering.control.linePositions", _geometryLineControlType,
            "linePositions", _lineBufferArrayType, LineRingControl, LineBufferArray, 0x78);
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(LineBufferElementSize));
        Add("il2cpp_array_length", "ArraySize", nameof(LineBufferLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(LineBufferByteLength));
        Add("il2cpp_object_get_size", "ArraySize", nameof(LineBufferObjectSize));
        CanvasSourceRemember();
    }

    internal IntPtr LineBufferNewArray(params NativeVector3BufferBits[] items)
    {
        var array = GameplayAllocate(checked(ArrayHeaderBytes + items.Length * 12));
        for (var i = 0; i < ArrayHeaderBytes + items.Length * 12; ++i)
            Marshal.WriteByte(array, i, 0);
        Marshal.WriteIntPtr(array, ClassOf(_lineBufferArrayType));
        _boxedTypes[array] = _lineBufferArrayType;
        _referenceRanks[array] = 1;
        _lineBufferLengths.Add(array, items.Length);
        CanvasSourcePointers.Add(array);
        for (var i = 0; i < items.Length; ++i)
        {
            var offset = checked(ArrayHeaderBytes + i * 12);
            Marshal.WriteInt32(array, offset, items[i].X);
            Marshal.WriteInt32(array, offset + 4, items[i].Y);
            Marshal.WriteInt32(array, offset + 8, items[i].Z);
        }
        return array;
    }

    internal NativeVector3BufferBits[] LineBufferReadBits(IntPtr array)
    {
        var count = _lineBufferLengths[array];
        var result = new NativeVector3BufferBits[count];
        for (var i = 0; i < count; ++i)
        {
            var offset = checked(ArrayHeaderBytes + i * 12);
            result[i] = new(Marshal.ReadInt32(array, offset), Marshal.ReadInt32(array, offset + 4),
                Marshal.ReadInt32(array, offset + 8));
        }
        return result;
    }

    internal IntPtr LineBufferAssociation => SeekValue<IntPtr>(LineRingControl, "linePositions");
    internal void LineBufferReplaceAssociation(IntPtr array) => TargetSet(LineRingControl, "linePositions", array);
    internal uint LineBufferNewestHandle => _nextHandle;
    internal void LineBufferFailNewestFree() => FailFreeHandle = _nextHandle;
    internal uint[] LineBufferActiveHandles(IntPtr array)
        => _handles.Where(row => row.Value == array).Select(row => row.Key).OrderBy(value => value).ToArray();
    internal uint LineBufferFailActiveTemporaryPin(IntPtr array, uint permanentRoot)
    {
        var active = LineBufferActiveHandles(array);
        if (active.Length != 2 || active[0] != permanentRoot || active[1] <= permanentRoot)
            throw new InvalidOperationException("The exact original array's active temporary pin is absent.");
        FailFreeHandle = active[1];
        return active[1];
    }
    internal void LineBufferOverwritePrefix(IntPtr array, params NativeVector3BufferBits[] prefix)
    {
        if (prefix.Length > _lineBufferLengths[array]) throw new ArgumentOutOfRangeException(nameof(prefix));
        for (var i = 0; i < prefix.Length; ++i)
        {
            var offset = checked(ArrayHeaderBytes + i * 12);
            Marshal.WriteInt32(array, offset, prefix[i].X);
            Marshal.WriteInt32(array, offset + 4, prefix[i].Y);
            Marshal.WriteInt32(array, offset + 8, prefix[i].Z);
        }
    }

    private int LineBufferElementSize(IntPtr cls)
        => cls == ClassOf(_lineBufferArrayType) ? 12 : CanvasArrayWidth(cls);
    private uint LineBufferLength(IntPtr array)
        => _lineBufferLengths.TryGetValue(array, out var length) ? checked((uint)length) : CanvasArrayLength(array);
    private uint LineBufferByteLength(IntPtr array)
        => _lineBufferLengths.TryGetValue(array, out var length) ? checked((uint)(length * 12)) : CanvasArrayBytes(array);
    private uint LineBufferObjectSize(IntPtr array)
        => _lineBufferLengths.TryGetValue(array, out var length)
            ? checked((uint)(ArrayHeaderBytes + length * 12)) : CanvasArraySize(array);
}
