using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed record NativeRootedSingleArray(IntPtr Array, IntPtr Class, IntPtr ElementClass,
    int Length, IReadOnlyList<float> Items);

internal sealed partial class NativeRuntime
{
    internal const int MaximumGeometryArrayLength = 65536;

    /// <summary>Exact bounded Single vector observation. Pin ownership precedes
    /// header/slot access; the second complete bitwise pass preserves NaN payloads
    /// and signed zero. Equal observations do not exclude an unobserved ABA.</summary>
    internal NativeRootedSingleArray ReadRootedGeometrySingleArray(RootHandle array, IntPtr exactArrayClass, int maximum, Action? guard = null,
        bool canvasCache = false)
    {
        VerifyAccess(); guard?.Invoke(); ArgumentNullException.ThrowIfNull(array);
        T Observe<T>(Func<T> operation) { guard?.Invoke(); var result = operation(); guard?.Invoke(); return result; }
        if (maximum < 0 || maximum > MaximumGeometryArrayLength) throw new ArgumentOutOfRangeException(nameof(maximum));
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = Observe(() => array.Target);
            var pinned = Observe(() => call.Root(() => new RootHandle(_newHandle(value, true), _freeHandle, _handleTarget, VerifyAccess)));
            var single = Observe(() => Class("mscorlib.dll", "System", "Single"));
            if (Observe(() => pinned.Target) != value || Observe(() => _objectClass(value)) != exactArrayClass)
                throw new UnsupportedSignatureException("The rooted geometry Single array differs from its exact class.");
            VerifyGeometrySingleArrayClass(exactArrayClass, single, guard);
            guard?.Invoke();
            var length = ReadGraphExport<ArraySize>("il2cpp_array_length");
            var byteLength = ReadGraphExport<ArraySize>("il2cpp_array_get_byte_length");
            var objectSize = ReadGraphExport<ArraySize>("il2cpp_object_get_size");
            var header = Observe(() => ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size")());
            var count = Observe(() => length(value)); var bytes = Observe(() => byteLength(value)); var total = Observe(() => objectSize(value));
            if (count > maximum || header < 2 * IntPtr.Size || header > 128 || header % IntPtr.Size != 0
                || (ulong)count * sizeof(float) != bytes || (ulong)header + bytes != total)
                throw new UnsupportedSignatureException("The geometry Single array has inconsistent complete storage bounds.");
            HookTargetValidation.VerifyReadable(value, total);
            if (Marshal.ReadIntPtr(value) != exactArrayClass)
                throw new UnsupportedSignatureException("The pinned geometry array header differs from its official class.");
            var items = new float[checked((int)count)];
            Marshal.Copy(IntPtr.Add(value, checked((int)header)), items, 0, items.Length);
            if (Observe(() => array.Target) != value || Observe(() => pinned.Target) != value || Observe(() => _objectClass(value)) != exactArrayClass
                || Marshal.ReadIntPtr(value) != exactArrayClass || Observe(() => length(value)) != count || Observe(() => byteLength(value)) != bytes
                || Observe(() => objectSize(value)) != total || Observe(() => ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size")()) != header)
                throw new UnsupportedSignatureException("The complete rooted geometry Single array changed during observation.");
            VerifyGeometrySingleArrayClass(exactArrayClass, single, guard);
            guard?.Invoke();
            for (var i = 0; i < items.Length; ++i)
            {
                var observed = Marshal.ReadInt32(value, checked((int)header + i * sizeof(float)));
                var expected = BitConverter.SingleToInt32Bits(items[i]);
                if (observed != expected)
                {
                    if (canvasCache)
                    {
                        // Every first-pass slot was already copied. The second
                        // prefix has already compared equal, so retain its known
                        // bits without rereading native storage after conflict.
                        var samples = new NativeCanvasCacheObservation[checked(items.Length + i + 1)];
                        for (var k = 0; k < items.Length; ++k)
                            samples[k] = new(value.ToInt64(), k, "Single[]", BitConverter.SingleToInt32Bits(items[k]));
                        for (var k = 0; k < i; ++k) samples[items.Length + k] = samples[k];
                        samples[^1] = new(value.ToInt64(), i, "Single[]", observed);
                        throw NativeCanvasCacheConflict.Error(value.ToInt64(), i, "Single[]", expected, observed, true, samples);
                    }
                    throw new UnsupportedSignatureException("A geometry Single slot changed during the complete bitwise observation.");
                }
            }
            return new(value, exactArrayClass, single, items.Length, Array.AsReadOnly(items));
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
    private void VerifyGeometrySingleArrayClass(IntPtr arrayClass, IntPtr single, Action? guard = null)
    {
        T Observe<T>(Func<T> operation) { guard?.Invoke(); var value = operation(); guard?.Invoke(); return value; }
        var type = Observe(() => _classType(arrayClass)); var elementType = Observe(() => _classType(single));
        if (arrayClass == IntPtr.Zero || type == IntPtr.Zero || elementType == IntPtr.Zero
            || Observe(() => _byRef(type)) || Observe(() => _typeCode(type)) != 29
            || Observe(() => _typeClass(type)) != arrayClass || Observe(() => _typeClass(elementType)) != single
            || Observe(() => _isValueType(arrayClass)) || !Observe(() => _isValueType(single))
            || Observe(() => _byRef(elementType)) || Observe(() => _typeCode(elementType)) != 12
            || Observe(() => _valueSize(single, out _)) != sizeof(float)
            || Observe(() => MetadataTypeName(elementType)) != "System.Single"
            || Observe(() => ReadGraphExport<Flags>("il2cpp_class_get_rank")(arrayClass)) != 1
            || Observe(() => ReadGraphExport<Ptr1>("il2cpp_class_get_element_class")(arrayClass)) != single
            || Observe(() => ReadGraphExport<ArrayElementSize>("il2cpp_array_element_size")(arrayClass)) != sizeof(float)
            || Observe(() => MetadataTypeName(type)) != "System.Single[]")
            throw new UnsupportedSignatureException("The geometry array is not an exact Single vector.");
    }
}
