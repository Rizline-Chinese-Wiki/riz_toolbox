using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed record NativeInt32ListLayout(IntPtr Class, IntPtr ArrayClass, IntPtr ElementClass,
    NativeExactReadField BackingField, NativeExactReadField SizeField, NativeExactReadField VersionField);
internal sealed record NativeRootedInt32Array(IntPtr Array, IntPtr Class, IntPtr ElementClass, int Length,
    uint ByteLength, uint ObjectSize, uint HeaderSize, IReadOnlyList<int> Items);

internal sealed partial class NativeRuntime
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ValueBox(IntPtr klass, IntPtr value);
    internal const int MaximumReadOnlyInt32ArrayLength = 65536;

    /// <summary>The closed class must come from an actual declared field. Generic dump
    /// offsets are not layouts: require the official exact Int32 backing/count/version.</summary>
    internal NativeInt32ListLayout InspectInt32ListClass(IntPtr actualClosedClass)
    {
        VerifyAccess();
        var integer = Class("mscorlib.dll", "System", "Int32");
        var objectClass = Class("mscorlib.dll", "System", "Object");
        if (actualClosedClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("The actual Int32 list class is absent.");
        var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image");
        var type = _classType(actualClosedClass);
        if (type == IntPtr.Zero || _typeClass(type) != actualClosedClass || _byRef(type) || _typeCode(type) != 21
            || _isValueType(actualClosedClass) || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(actualClosedClass)
            || !ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(actualClosedClass)
            || _classParent(actualClosedClass) != objectClass || image(actualClosedClass) != image(objectClass)
            || Utf8(_classNamespace(actualClosedClass)) != "System.Collections.Generic"
            || Utf8(_className(actualClosedClass)) != "List`1"
            || MetadataTypeName(type) != "System.Collections.Generic.List<System.Int32>")
            throw new UnsupportedSignatureException("The actual input collection is not the exact closed mscorlib Int32 list.");
        var backing = InspectExactDeclaredReadField(actualClosedClass, "_items", "System.Int32[]", false);
        var size = InspectExactDeclaredReadField(actualClosedClass, "_size", "System.Int32", false);
        var version = InspectExactDeclaredReadField(actualClosedClass, "_version", "System.Int32", false);
        if (size.FieldClass != integer || version.FieldClass != integer || size.Size != sizeof(int) || version.Size != sizeof(int)
            || backing.Size != IntPtr.Size || Overlaps(backing, size) || Overlaps(backing, version) || Overlaps(size, version))
            throw new UnsupportedSignatureException("The actual Int32 list has overlapping or different backing/count/version storage.");
        VerifyInt32ArrayClass(backing.FieldClass, integer);
        return new(actualClosedClass, backing.FieldClass, integer, backing, size, version);
    }

    /// <summary>Copies the complete pinned capacity, including signed values and unused
    /// tail slots. Official layout bounds and a second observation reject a partial scan;
    /// this is not an atomic snapshot or an ABA exclusion proof. No raw writes are made.</summary>
    internal NativeRootedInt32Array ReadRootedInt32ArrayWithPublication(RootHandle array,
        IntPtr exactArrayClass, int maximum)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(array);
        if (maximum < 0 || maximum > MaximumReadOnlyInt32ArrayLength)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Int32 array observation exceeds its complete bound.");
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = array.Target;
            var pinned = call.Root(() => new RootHandle(_newHandle(value, true), _freeHandle, _handleTarget, VerifyAccess));
            var integer = Class("mscorlib.dll", "System", "Int32");
            if (pinned.Target != value || _objectClass(value) != exactArrayClass)
                throw new UnsupportedSignatureException("The rooted Int32 array differs from its exact required class.");
            VerifyInt32ArrayClass(exactArrayClass, integer);
            var length = ReadGraphExport<ArraySize>("il2cpp_array_length");
            var byteLength = ReadGraphExport<ArraySize>("il2cpp_array_get_byte_length");
            var objectSize = ReadGraphExport<ArraySize>("il2cpp_object_get_size");
            var header = ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size")();
            var count = length(value); var bytes = byteLength(value); var total = objectSize(value);
            if (count > maximum || header < 2 * IntPtr.Size || header > 128 || header % IntPtr.Size != 0
                || (ulong)count * sizeof(int) != bytes || (ulong)header + bytes != total)
                throw new UnsupportedSignatureException("Int32 array length, official header and complete storage bounds disagree.");
            HookTargetValidation.VerifyReadable(value, total);
            if (Marshal.ReadIntPtr(value) != exactArrayClass)
                throw new UnsupportedSignatureException("The pinned Int32 array header differs from its official object class.");
            var items = new int[checked((int)count)];
            Marshal.Copy(IntPtr.Add(value, checked((int)header)), items, 0, items.Length);
            if (array.Target != value || pinned.Target != value || _objectClass(value) != exactArrayClass
                || Marshal.ReadIntPtr(value) != exactArrayClass || length(value) != count || byteLength(value) != bytes
                || objectSize(value) != total || ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size")() != header)
                throw new UnsupportedSignatureException("The actual Int32 array changed during its rooted observation.");
            VerifyInt32ArrayClass(exactArrayClass, integer);
            for (var index = 0; index < items.Length; ++index)
                if (Marshal.ReadInt32(value, checked((int)header + index * sizeof(int))) != items[index])
                    throw new UnsupportedSignatureException("An Int32 array slot changed during its complete observation.");
            return new(value, exactArrayClass, integer, items.Length, bytes, total, header, Array.AsReadOnly(items));
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    /// <summary>Restores one slot through official value_box and Array.SetValue(Object,
    /// Int32). The Int32 is a boxed reference, never a scalar pretending to be Object.
    /// All temporary roots have bounded unknown-free responsibility. The final managed
    /// guard runs immediately before SetValue; boxing alone never changes array storage.</summary>
    internal void SetInt32ArrayElementGuarded(RootHandle array, IntPtr exactArrayClass,
        int index, int value, Action beforeDispatch)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(array); ArgumentNullException.ThrowIfNull(beforeDispatch);
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var receiver = call.Root(() => Root(array.Target));
            var integer = Class("mscorlib.dll", "System", "Int32");
            VerifyInt32ArrayClass(exactArrayClass, integer);
            var target = receiver.Target;
            if (_objectClass(target) != exactArrayClass)
                throw new UnsupportedSignatureException("Int32 slot restoration requires the exact array receiver.");
            var count = ReadGraphExport<ArraySize>("il2cpp_array_length")(target);
            if (count > MaximumReadOnlyInt32ArrayLength || index < 0 || (uint)index >= count)
                throw new ArgumentOutOfRangeException(nameof(index), "Int32 slot restoration is outside the bounded complete array.");
            var arrayClass = Class("mscorlib.dll", "System", "Array");
            var method = Method(arrayClass, "SetValue", 2, parameterTypes: new[] { "System.Object", "System.Int32" });
            var returns = Describe(_returnType(method));
            if (_methodClass(method) != arrayClass || returns.Kind != NativeValueKind.Void || returns.ByRef)
                throw new UnsupportedSignatureException("Int32 slot restoration requires the official exact Array.SetValue method.");
            var box = ReadGraphExport<ValueBox>("il2cpp_value_box");
            using var input = Arg.Value(value);
            var boxed = call.Root(() => Root(box(integer, input.Pointer)));
            var boxedTarget = boxed.Target;
            if (_objectClass(boxedTarget) != integer || ReadValue<int>(_unbox(boxedTarget)) != value)
                throw new UnsupportedSignatureException("Official Int32 boxing returned another class or value.");
            using var reference = Arg.Ref(boxedTarget);
            using var position = Arg.Value(index);
            InvokeCore(method, target, null, call, false, new[] { reference, position }, beforeDispatch);
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private void VerifyInt32ArrayClass(IntPtr arrayClass, IntPtr integer)
    {
        if (arrayClass == IntPtr.Zero || integer == IntPtr.Zero)
            throw new UnsupportedSignatureException("An exact Int32 array and element class are required.");
        var type = _classType(arrayClass); var elementType = _classType(integer);
        if (type == IntPtr.Zero || elementType == IntPtr.Zero || _byRef(type) || _typeCode(type) != 29
            || _typeClass(type) != arrayClass || _typeClass(elementType) != integer || _isValueType(arrayClass)
            || !_isValueType(integer) || _byRef(elementType) || _typeCode(elementType) != 8
            || _valueSize(integer, out _) != sizeof(int) || MetadataTypeName(elementType) != "System.Int32"
            || ReadGraphExport<Flags>("il2cpp_class_get_rank")(arrayClass) != 1
            || ReadGraphExport<Ptr1>("il2cpp_class_get_element_class")(arrayClass) != integer
            || ReadGraphExport<ArrayElementSize>("il2cpp_array_element_size")(arrayClass) != sizeof(int)
            || MetadataTypeName(type) != "System.Int32[]")
            throw new UnsupportedSignatureException("The actual array is not the required one-dimensional Int32 element class.");
    }
}
