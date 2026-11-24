using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal enum FingerMetadataFault { Namespace, Name, NotInflated, ArrayRank, ArrayElement, ArrayWidth,
        StorageOverlap, StaticFinger, ReadonlyFinger, FingerOffset, IntWidth }
    internal FingerMetadataFault? FingerMetadataError;
    internal Action<string, IntPtr>? AfterFingerMetadata;
    internal Action<string, IntPtr>? AfterFingerArrayExport;
    internal uint? FingerHeaderOverride, FingerLengthOverride;
    private void ConfigureFingerExports()
    {
        Add("il2cpp_class_get_name", "Ptr1", nameof(FingerClassName));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(FingerFindClass));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(FingerNamespace));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(FingerClassParent));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(FingerAssignable));
        Add("il2cpp_class_is_generic", "Predicate", nameof(FingerClassGeneric));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(FingerClassInflated));
        Add("il2cpp_class_get_rank", "Flags", nameof(FingerArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(FingerArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(FingerArrayElementSize));
        Add("il2cpp_array_class_get", "ArrayClassGet", nameof(FingerArrayClass));
        Add("il2cpp_array_new", "ArrayNew", nameof(FingerNewArray));
        Add("il2cpp_array_length", "ArraySize", nameof(FingerArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(FingerArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(FingerObjectSize));
        Add("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(FingerArrayHeader));
        Add("il2cpp_field_get_flags", "Flags", nameof(FingerFieldFlags));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(FingerMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(FingerNamedMethod));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(FingerInvoke));
        Add("il2cpp_object_new", "Ptr1", nameof(FingerObjectNew));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(FingerSetFieldValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(FingerSetFieldObject));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(FingerRoot));
        Add("il2cpp_value_box", "ValueBox", nameof(FingerValueBox));
    }
    private void FingerMetadata(string operation, IntPtr subject) => AfterFingerMetadata?.Invoke(operation, subject);
    private IntPtr FingerFindClass(IntPtr image, string ns, string name) => _gameplayImages[image] == "mscorlib.dll" && ns == "System" && name == "Array"
        ? ClassOf(_fingerSystemArrayType) : FlagFindClass(image, ns, name);
    private bool FingerAssignable(IntPtr target, IntPtr source)
        => target == ClassOf(_fingerSystemArrayType) && source == ClassOf(_fingerArrayType) || CursorAssignable(target, source);
    private IntPtr FingerClassName(IntPtr cls)
    {
        FingerMetadata("class.name", cls);
        return cls == ClassOf(_fingerListType) ? GameplayText(FingerMetadataError == FingerMetadataFault.Name ? "Stack`1" : "List`1") : JudgeClassName(cls);
    }
    private IntPtr FingerNamespace(IntPtr cls)
    {
        FingerMetadata("class.namespace", cls);
        return cls == ClassOf(_fingerListType) ? GameplayText(FingerMetadataError == FingerMetadataFault.Namespace ? "Foreign" : "System.Collections.Generic") : JudgeNamespace(cls);
    }
    private IntPtr FingerClassParent(IntPtr cls)
    {
        FingerMetadata("class.parent", cls);
        return cls == ClassOf(_fingerListType) ? ClassOf(Object)
            : cls == ClassOf(_fingerArrayType) ? ClassOf(_fingerSystemArrayType)
                : cls == ClassOf(_fingerSystemArrayType) ? ClassOf(Object) : JudgeClassParent(cls);
    }
    private bool FingerClassGeneric(IntPtr cls) => cls == ClassOf(_fingerListType) ? false : JudgeClassGeneric(cls);
    private bool FingerClassInflated(IntPtr cls) => cls == ClassOf(_fingerListType)
        ? FingerMetadataError != FingerMetadataFault.NotInflated : JudgeClassInflated(cls);
    private uint FingerArrayRank(IntPtr cls) => cls == ClassOf(_fingerArrayType)
        ? FingerMetadataError == FingerMetadataFault.ArrayRank ? 2u : 1u : JudgeArrayRank(cls);
    private IntPtr FingerArrayElement(IntPtr cls) => cls == ClassOf(_fingerArrayType)
        ? ClassOf(FingerMetadataError == FingerMetadataFault.ArrayElement ? Single : Int32) : JudgeArrayElement(cls);
    private int FingerArrayElementSize(IntPtr cls) => cls == ClassOf(_fingerArrayType)
        ? FingerMetadataError == FingerMetadataFault.ArrayWidth ? 8 : sizeof(int) : JudgeArrayElementSize(cls);
    private IntPtr FingerArrayClass(IntPtr element, uint rank)
        => element == ClassOf(Int32) && rank == 1 ? ClassOf(_fingerArrayType) : ArrayClass(element, rank);
    private IntPtr FingerNewArray(IntPtr element, ulong count)
    {
        if (element != ClassOf(Int32)) return NewArray(element, count);
        var value = FingerArray(new int[checked((int)count)]); ++ArrayAllocations; LastArray = value; return value;
    }
    private uint FingerArrayLength(IntPtr array)
    {
        if (!_fingerArrays.TryGetValue(array, out var slots)) return ArrayLength(array);
        var result = FingerLengthOverride ?? checked((uint)slots.Length); AfterFingerArrayExport?.Invoke("length", array); return result;
    }
    private uint FingerArrayHeader() => FingerHeaderOverride ?? checked((uint)ArrayHeaderBytes);
    private uint FingerArrayBytes(IntPtr array)
    {
        if (!_fingerArrays.TryGetValue(array, out var slots)) return ArrayBytes(array);
        var result = checked((uint)slots.Length * sizeof(int) + (CorruptArrayByteLength && slots.Length != 0 ? 1u : 0u));
        AfterFingerArrayExport?.Invoke("bytes", array); return result;
    }
    private uint FingerObjectSize(IntPtr array)
    {
        if (!_fingerArrays.TryGetValue(array, out var slots)) return ObjectSize(array);
        var result = checked((uint)(ArrayHeaderBytes + slots.Length * sizeof(int)) + (CorruptArrayObjectSize && slots.Length != 0 ? 4u : 0u));
        AfterFingerArrayExport?.Invoke("size", array); return result;
    }
    private uint FingerFieldFlags(IntPtr field)
    {
        FingerMetadata("field.flags", field);
        if (field == _fingerField)
            return FingerMetadataError == FingerMetadataFault.StaticFinger ? 0x10u
                : FingerMetadataError == FingerMetadataFault.ReadonlyFinger ? 0x20u : 0;
        return JudgeFieldFlags(field);
    }
    internal void FingerMutateMetadata(FingerMetadataFault fault)
    {
        FingerMetadataError = fault;
        if (fault == FingerMetadataFault.FingerOffset)
            _fieldMetadata[_fingerField] = ("fingerIds", _fingerListType, 0x60);
        if (fault == FingerMetadataFault.StorageOverlap)
            _fieldMetadata[FingerListField("_version")] = ("_version", Int32, 0x18);
        if (fault == FingerMetadataFault.IntWidth)
            _fieldMetadata[FingerListField("_version")] = ("_version", AddType("System.Int32", 8, true, 8), 0x1c);
    }
    private IntPtr FingerMethods(IntPtr cls, IntPtr iterator) => cls == ClassOf(_fingerListType) || cls == ClassOf(_fingerArrayType) || cls == ClassOf(_fingerSystemArrayType)
        ? GameplayMethods(cls, iterator) : JudgeQueueMethods(cls, iterator);
    private IntPtr FingerNamedMethod(IntPtr cls, string name, int count) => cls == ClassOf(_fingerListType) || cls == ClassOf(_fingerArrayType) || cls == ClassOf(_fingerSystemArrayType)
        ? _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name && row.Value.Parameters.Length == count).Key
        : JudgeQueueNamedMethod(cls, name, count);
}
