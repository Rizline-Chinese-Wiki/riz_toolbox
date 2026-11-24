using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal enum NativeTweenQueueKind { List, Stack }
internal sealed record NativeTweenQueueLayout(IntPtr Class, NativeTweenQueueKind Kind, IntPtr ArrayClass,
    IntPtr ElementClass, NativeExactReadField BackingField, NativeExactReadField SizeField, NativeExactReadField VersionField);
internal sealed record NativeRootedReferenceArray(IntPtr Array, IntPtr Class, IntPtr ElementClass, int Length,
    uint ByteLength, uint ObjectSize, uint HeaderSize, IReadOnlyList<IntPtr> Items);

internal sealed partial class NativeRuntime
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReferenceArrayHeaderSize();
    internal const int MaximumReadOnlyReferenceArrayLength = 4096;

    /// <summary>Uses the already closed class obtained from a real manager field.
    /// Generic dump offsets are not layouts: every backing/count/version field must
    /// be officially declared by this exact class and fit its actual instance size.</summary>
    internal NativeTweenQueueLayout InspectTweenQueueClass(IntPtr actualClosedClass, NativeTweenQueueKind kind, IntPtr tweenInfoClass)
    {
        VerifyAccess();
        if (kind is not (NativeTweenQueueKind.List or NativeTweenQueueKind.Stack)
            || tweenInfoClass != Class("Assembly-CSharp.dll", "", "TweenInfo") || actualClosedClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("Tween queues require the official TweenInfo element and a supported container kind.");
        var name = kind == NativeTweenQueueKind.List ? "List" : "Stack";
        var objectClass = Class("mscorlib.dll", "System", "Object");
        var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image");
        var type = _classType(actualClosedClass);
        if (type == IntPtr.Zero || _typeClass(type) != actualClosedClass || _byRef(type) || _typeCode(type) != 21
            || _isValueType(actualClosedClass) || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(actualClosedClass)
            || !ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(actualClosedClass)
            || _classParent(actualClosedClass) != objectClass || image(actualClosedClass) != image(objectClass)
            || Utf8(_classNamespace(actualClosedClass)) != "System.Collections.Generic" || Utf8(_className(actualClosedClass)) != name + "`1"
            || MetadataTypeName(type) != "System.Collections.Generic." + name + "<TweenInfo>")
            throw new UnsupportedSignatureException("The actual Tween queue is not the supported closed mscorlib class.");
        var backing = InspectExactDeclaredReadField(actualClosedClass, kind == NativeTweenQueueKind.List ? "_items" : "_array", "TweenInfo[]", false);
        var size = InspectExactDeclaredReadField(actualClosedClass, "_size", "System.Int32", false);
        var version = InspectExactDeclaredReadField(actualClosedClass, "_version", "System.Int32", false);
        var integer = Class("mscorlib.dll", "System", "Int32");
        if (size.FieldClass != integer || version.FieldClass != integer || size.Size != sizeof(int) || version.Size != sizeof(int)
            || backing.Size != IntPtr.Size || Overlaps(backing, size) || Overlaps(backing, version) || Overlaps(size, version))
            throw new UnsupportedSignatureException("The actual Tween queue has overlapping or different count/version/backing storage.");
        VerifyReferenceArrayClass(backing.FieldClass, tweenInfoClass);
        return new(actualClosedClass, kind, backing.FieldClass, tweenInfoClass, backing, size, version);
    }

    /// <summary>Copies every capacity slot, including nulls and duplicate identities.
    /// Each reference is published immediately after its load, before any inspection.
    /// The caller records an independent root before retaining/inspecting that token.
    /// This is an observation, never a queue-drained or producer-closed receipt.</summary>
    internal NativeRootedReferenceArray ReadRootedReferenceArrayWithPublication(RootHandle array,
        IntPtr exactArrayClass, IntPtr exactElementClass, int maximum, Action<int, IntPtr> publish)
        => ReadRootedReferenceArrayCore(array, exactArrayClass, exactElementClass, maximum,
            MaximumReadOnlyReferenceArrayLength, publish);

    private NativeRootedReferenceArray ReadRootedReferenceArrayCore(RootHandle array,
        IntPtr exactArrayClass, IntPtr exactElementClass, int maximum, int permittedMaximum, Action<int, IntPtr> publish, Action? guard = null)
    {
        VerifyAccess(); guard?.Invoke(); ArgumentNullException.ThrowIfNull(array); ArgumentNullException.ThrowIfNull(publish);
        T Observe<T>(Func<T> operation) { guard?.Invoke(); var result = operation(); guard?.Invoke(); return result; }
        if (maximum < 0 || maximum > permittedMaximum)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Reference array observation exceeds its complete bound.");
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = Observe(() => array.Target);
            // Pinning is actual native GC-handle responsibility, registered before
            // any address arithmetic. A failed free retains the publication owner.
            var pinned = Observe(() => call.Root(() => new RootHandle(_newHandle(value, true), _freeHandle, _handleTarget, VerifyAccess)));
            if (Observe(() => pinned.Target) != value || Observe(() => _objectClass(value)) != exactArrayClass)
                throw new UnsupportedSignatureException("The rooted reference array differs from its exact required class.");
            VerifyReferenceArrayClass(exactArrayClass, exactElementClass, guard);
            guard?.Invoke();
            var length = ReadGraphExport<ArraySize>("il2cpp_array_length");
            var byteLength = ReadGraphExport<ArraySize>("il2cpp_array_get_byte_length");
            var objectSize = ReadGraphExport<ArraySize>("il2cpp_object_get_size");
            var header = Observe(() => ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size")());
            var count = Observe(() => length(value)); var bytes = Observe(() => byteLength(value)); var total = Observe(() => objectSize(value));
            if (count > maximum || header < 2 * IntPtr.Size || header > 128 || header % IntPtr.Size != 0
                || (ulong)count * (uint)IntPtr.Size != bytes || (ulong)header + bytes != total)
                throw new UnsupportedSignatureException("Reference array length, official header and complete storage bounds disagree.");
            HookTargetValidation.VerifyReadable(value, total);
            if (Marshal.ReadIntPtr(value) != exactArrayClass)
                throw new UnsupportedSignatureException("The pinned array header differs from its official object class.");
            var items = new IntPtr[checked((int)count)];
            for (var index = 0; index < items.Length; ++index)
            {
                guard?.Invoke();
                var token = Marshal.ReadIntPtr(value, checked((int)header + index * IntPtr.Size));
                call.BeginPublication(token);
                try { publish(index, token); call.PublicationAccepted(); }
                catch { call.PublicationFailed(); throw; }
                // Ownership acceptance and admission are separate: withdrawal
                // after a known adoption must not manufacture an unknown token.
                guard?.Invoke();
                items[index] = token;
            }
            // No partial scan is accepted as a complete working graph. Array
            // slots have no version field, so compare the entire ordered vector.
            if (Observe(() => array.Target) != value || Observe(() => pinned.Target) != value || Observe(() => _objectClass(value)) != exactArrayClass
                || Marshal.ReadIntPtr(value) != exactArrayClass || Observe(() => length(value)) != count || Observe(() => byteLength(value)) != bytes
                || Observe(() => objectSize(value)) != total || Observe(() => ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size")()) != header)
                throw new UnsupportedSignatureException("The actual reference array changed during its rooted observation.");
            VerifyReferenceArrayClass(exactArrayClass, exactElementClass, guard);
            guard?.Invoke();
            for (var index = 0; index < items.Length; ++index)
                if (Marshal.ReadIntPtr(value, checked((int)header + index * IntPtr.Size)) != items[index])
                    throw new UnsupportedSignatureException("A reference array slot changed during its complete observation.");
            return new(value, exactArrayClass, exactElementClass, items.Length, bytes, total, header, Array.AsReadOnly(items));
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private void VerifyReferenceArrayClass(IntPtr arrayClass, IntPtr elementClass, Action? guard = null)
    {
        T O<T>(Func<T> read) { guard?.Invoke(); var result = read(); guard?.Invoke(); return result; }
        if (arrayClass == IntPtr.Zero || elementClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("An exact array and element class are required.");
        var type = O(() => _classType(arrayClass)); var elementType = O(() => _classType(elementClass));
        if (type == IntPtr.Zero || elementType == IntPtr.Zero || O(() => _byRef(type)) || O(() => _typeCode(type)) != 29
            || O(() => _typeClass(type)) != arrayClass || O(() => _typeClass(elementType)) != elementClass || O(() => _isValueType(arrayClass))
            || O(() => ReadGraphExport<Flags>("il2cpp_class_get_rank")(arrayClass)) != 1
            || O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_element_class")(arrayClass)) != elementClass
            || O(() => _isValueType(elementClass)) || O(() => _byRef(elementType))
            || O(() => ReadGraphExport<ArrayElementSize>("il2cpp_array_element_size")(arrayClass)) != IntPtr.Size
            || (guard is null ? MetadataTypeName(type) : LineRingTypeName(type, guard))
                != (guard is null ? MetadataTypeName(elementType) : LineRingTypeName(elementType, guard)) + "[]")
            throw new UnsupportedSignatureException("The actual array is not the required one-dimensional reference element class.");
    }

    private static bool Overlaps(NativeExactReadField first, NativeExactReadField second)
        => first.Offset < (long)second.Offset + second.Size && second.Offset < (long)first.Offset + first.Size;
}
