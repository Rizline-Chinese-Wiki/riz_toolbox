using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal enum GeometryArrayFault { Rank, Element, Width, Header, Length, Bytes, Total, HeaderClass }
    internal GeometryArrayFault? GeometryArrayError;
    internal Action<string, IntPtr>? AfterGeometryArrayExport;
    private void ConfigureGeometryExports()
    {
        Add("il2cpp_class_get_methods", "Ptr2", nameof(GeometryMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(GeometryNamedMethod));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(GeometryClassParent));
        Add("il2cpp_class_get_image", "Ptr1", nameof(GeometryClassImage));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(GeometryAssignable));
        Add("il2cpp_object_get_class", "Ptr1", nameof(GeometryObjectClass));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(GeometryInvoke));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(GeometryRoot));
        Add("il2cpp_class_get_rank", "Flags", nameof(GeometryArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(GeometryArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(GeometryArrayWidth));
        Add("il2cpp_array_length", "ArraySize", nameof(GeometryArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(GeometryArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(GeometryArraySize));
        Add("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(GeometryArrayHeader));
        Add("il2cpp_field_static_get_value", "StaticGet", nameof(GeometryStaticRead));
    }
    private bool GeometryArrayClass(IntPtr cls) => _classes.TryGetValue(cls, out var type) && _geometryArrayElements.ContainsKey(type);
    private bool GeometrySingleClass(IntPtr cls) => _classes.TryGetValue(cls, out var type)
        && _geometryArrayElements.TryGetValue(type, out var element) && element == Single;
    private uint GeometryArrayRank(IntPtr cls) => GeometryArrayClass(cls)
        ? GeometrySingleClass(cls) && GeometryArrayError == GeometryArrayFault.Rank ? 2u : 1u : JudgeArrayRank(cls);
    private IntPtr GeometryArrayElement(IntPtr cls) => GeometryArrayClass(cls)
        ? GeometrySingleClass(cls) && GeometryArrayError == GeometryArrayFault.Element ? ClassOf(Int32) : ClassOf(_geometryArrayElements[_classes[cls]])
        : JudgeArrayElement(cls);
    private int GeometryArrayWidth(IntPtr cls) => GeometryArrayClass(cls)
        ? GeometrySingleClass(cls) ? GeometryArrayError == GeometryArrayFault.Width ? 8 : sizeof(float) : IntPtr.Size
        : JudgeArrayElementSize(cls);
    private uint GeometryArrayLength(IntPtr array)
    {
        if (!_referenceArrays.TryGetValue(array, out var slots)) return ArrayLength(array);
        var length = _geometrySingleArrays.ContainsKey(array) && GeometryArrayError == GeometryArrayFault.Length ? 65537u : checked((uint)slots.Length);
        AfterGeometryArrayExport?.Invoke("length", array); return length;
    }
    private uint GeometryArrayBytes(IntPtr array)
    {
        if (!_referenceArrays.TryGetValue(array, out var slots)) return ArrayBytes(array);
        var bytes = checked((uint)slots.Length * (_geometrySingleArrays.ContainsKey(array) ? sizeof(float) : (uint)IntPtr.Size));
        if (_geometrySingleArrays.ContainsKey(array) && GeometryArrayError == GeometryArrayFault.Bytes) ++bytes;
        AfterGeometryArrayExport?.Invoke("bytes", array); return bytes;
    }
    private uint GeometryArraySize(IntPtr array)
    {
        if (!_referenceArrays.TryGetValue(array, out var slots)) return ObjectSize(array);
        var bytes = checked((uint)(ArrayHeaderBytes + slots.Length * (_geometrySingleArrays.ContainsKey(array) ? sizeof(float) : IntPtr.Size)));
        if (_geometrySingleArrays.ContainsKey(array) && GeometryArrayError == GeometryArrayFault.Total) ++bytes;
        AfterGeometryArrayExport?.Invoke("size", array); return bytes;
    }
    private uint GeometryArrayHeader() => GeometryArrayError == GeometryArrayFault.Header ? 17u : checked((uint)ArrayHeaderBytes);
    private void GeometryStaticRead(IntPtr field, IntPtr output)
    {
        if (!_gameplayStorage.TryGetValue((IntPtr.Zero, field), out var value)) { ReadStatic(field, output); return; }
        ++FieldReads; Marshal.Copy(value, 0, output, value.Length); AfterTargetFieldRead?.Invoke(_fieldMetadata[field].Name, IntPtr.Zero);
    }
    internal void GeometrySingleSlot(IntPtr array, int index, float value)
    {
        _geometrySingleArrays[array][index] = value; Marshal.WriteInt32(array, ArrayHeaderBytes + index * sizeof(float), BitConverter.SingleToInt32Bits(value));
    }
    internal IntPtr GeometryFloorArray => SeekValue<IntPtr>(GeometryCanvasControl, "floorPositions");
    internal IntPtr GeometryFloorClass => ClassOf(_boxedTypes[GeometryFloorArray]);
    internal void GeometryRemoveEase()
        => _gameplayStorage[(IntPtr.Zero, _gameplayFields["geometry.ease.static"].Field)] = new byte[IntPtr.Size];
    internal void GeometryNullEaseRow()
    {
        var bytes = _gameplayStorage[(IntPtr.Zero, _gameplayFields["geometry.ease.static"].Field)];
        var array = new IntPtr(BitConverter.ToInt64(bytes));
        _referenceArrays[array][0] = IntPtr.Zero;
        Marshal.WriteIntPtr(array, ArrayHeaderBytes, IntPtr.Zero);
    }
    internal void GeometryBreakArrayHeader() => Marshal.WriteIntPtr(GeometryFloorArray, ClassOf(Object));
}
