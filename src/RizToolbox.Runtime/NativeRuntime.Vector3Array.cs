using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

// Owned by Runtime so the array writer does not depend on an Adapter value type.
// Explicit offsets also make the byte copy independent of compiler-generated fields.
[StructLayout(LayoutKind.Explicit, Size = 3 * sizeof(float))]
internal readonly struct NativeVector3ArraySample
{
    [FieldOffset(0)] internal readonly float X;
    [FieldOffset(4)] internal readonly float Y;
    [FieldOffset(8)] internal readonly float Z;

    internal NativeVector3ArraySample(float x, float y, float z) { X = x; Y = y; Z = z; }
}

internal sealed partial class NativeRuntime
{
    internal const int MaximumVector3ArraySamples = 4 * 1024 * 1024; // 48 MiB

    /// <summary>Creates an exact UnityEngine.Vector3[] and transfers its pinned root to
    /// the synchronous publisher before releasing the empty-array header probe root.
    /// The caller owns the published root even when notification or cleanup fails.</summary>
    internal RootHandle CreateVector3ArrayWithPublication(NativeVector3ArraySample[] samples,
        Action guard, Action<RootHandle> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(publish);
        var ownedBytes = SnapshotVector3ArrayBytes(samples);
        guard();
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var element = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
            return CreateVector3ArrayCore(ownedBytes, element, guard, publish, call);
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    // The class seam still verifies canonical identity and executes the production
    // metadata, allocation, copy and publication path against real export delegates.
    internal RootHandle CreateVector3ArrayForClassWithPublication(NativeVector3ArraySample[] samples,
        IntPtr elementClass, Action guard, Action<RootHandle> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(publish);
        var ownedBytes = SnapshotVector3ArrayBytes(samples);
        guard();
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try { return CreateVector3ArrayCore(ownedBytes, elementClass, guard, publish, call); }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private static byte[] SnapshotVector3ArrayBytes(NativeVector3ArraySample[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length > MaximumVector3ArraySamples)
            throw new ArgumentOutOfRangeException(nameof(samples), "Vector3 array exceeds the 48 MiB upload limit.");
        if (Marshal.SizeOf<NativeVector3ArraySample>() != 12
            || Marshal.OffsetOf<NativeVector3ArraySample>(nameof(NativeVector3ArraySample.X)).ToInt32() != 0
            || Marshal.OffsetOf<NativeVector3ArraySample>(nameof(NativeVector3ArraySample.Y)).ToInt32() != 4
            || Marshal.OffsetOf<NativeVector3ArraySample>(nameof(NativeVector3ArraySample.Z)).ToInt32() != 8)
            throw new UnsupportedSignatureException("Runtime Vector3 sample storage is not exactly three adjacent Singles.");
        // Clone before any caller guard or native export can mutate the input array.
        var owned = (NativeVector3ArraySample[])samples.Clone();
        foreach (var value in owned)
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
                throw new ArgumentException("Vector3 samples cannot contain NaN or infinity.", nameof(samples));
        var bytes = MemoryMarshal.AsBytes(owned.AsSpan());
        if (bytes.Length != checked(owned.Length * 12))
            throw new UnsupportedSignatureException("Runtime Vector3 sample stride is not twelve bytes.");
        return bytes.ToArray(); // preserves each float's raw bits, including negative zero
    }

    private RootHandle CreateVector3ArrayCore(byte[] ownedBytes, IntPtr elementClass,
        Action guard, Action<RootHandle> publish, NativePublicationCall call)
    {
        T O<T>(Func<T> read) { VerifyAccess(); guard(); var value = read(); VerifyAccess(); guard(); return value; }
        void G() { VerifyAccess(); guard(); }
        G();
        if (elementClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("Vector3 array element class is absent.");
        var canonical = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
        if (elementClass != canonical)
            throw new UnsupportedSignatureException("Vector3 array element class is not canonical UnityEngine.CoreModule.dll.");
        var elementType = LineEndpointClassType(elementClass, "UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", 17, true, guard);
        VerifyLineEndpointVector(elementType, guard); G();

        var arrayClassGet = O(() => ReadGraphExport<ArrayClassGet>("il2cpp_array_class_get"));
        var arrayClass = O(() => arrayClassGet(elementClass, 1));
        if (arrayClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("Canonical Vector3 array class is absent.");
        var rank = O(() => ReadGraphExport<Flags>("il2cpp_class_get_rank"));
        var arrayElement = O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_element_class"));
        var elementSize = O(() => ReadGraphExport<ArrayElementSize>("il2cpp_array_element_size"));
        var arrayType = O(() => _classType(arrayClass));
        if (arrayType == IntPtr.Zero || O(() => _typeClass(arrayType)) != arrayClass
            || O(() => _byRef(arrayType)) || O(() => _typeCode(arrayType)) != 29
            || O(() => _isValueType(arrayClass)) || O(() => _isEnum(arrayClass))
            || LineRingTypeName(arrayType, guard) != "UnityEngine.Vector3[]"
            || O(() => rank(arrayClass)) != 1 || O(() => arrayElement(arrayClass)) != elementClass
            || O(() => elementSize(arrayClass)) != 12)
            throw new UnsupportedSignatureException("Vector3[] rank, element class or twelve-byte storage changed.");

        var create = O(() => ReadGraphExport<ArrayNew>("il2cpp_array_new"));
        var length = O(() => ReadGraphExport<ArraySize>("il2cpp_array_length"));
        var byteLength = O(() => ReadGraphExport<ArraySize>("il2cpp_array_get_byte_length"));
        var objectSize = O(() => ReadGraphExport<ArraySize>("il2cpp_object_get_size"));
        RootHandle Pin(ulong count)
        {
            G(); var value = create(elementClass, count); G();
            RequireObject(value);
            // Register the handle in NativePublicationCall before a post-export
            // guard can throw. This preserves cleanup responsibility if a native
            // callback withdraws admission during gchandle_new.
            G(); var handle = _newHandle(value, true);
            return new RootHandle(handle, _freeHandle, _handleTarget, VerifyAccess);
        }

        var empty = call.Root(() => Pin(0));
        G();
        var emptyTarget = O(() => empty.Target);
        if (O(() => _objectClass(emptyTarget)) != arrayClass)
            throw new UnsupportedSignatureException("Vector3 array allocation returned another class.");
        var header = O(() => objectSize(emptyTarget));
        if (O(() => length(emptyTarget)) != 0 || O(() => byteLength(emptyTarget)) != 0
            || header < 2 * IntPtr.Size || header > 4096)
            throw new UnsupportedSignatureException("Empty Vector3 array does not expose a bounded consistent header.");

        var count = ownedBytes.Length / 12;
        var expectedBytes = checked((uint)ownedBytes.Length);
        var expectedObjectSize = checked(header + expectedBytes);
        var output = call.Root(() => Pin(checked((ulong)count)));
        G();
        var target = O(() => output.Target);
        if (O(() => _objectClass(target)) != arrayClass)
            throw new UnsupportedSignatureException("Vector3 array allocation returned another class.");
        if (O(() => length(target)) != (uint)count || O(() => byteLength(target)) != expectedBytes
            || O(() => objectSize(target)) != expectedObjectSize)
            throw new UnsupportedSignatureException("Vector3 array length, bytes and object size disagree.");
        G();
        if (ownedBytes.Length != 0) Marshal.Copy(ownedBytes, 0, IntPtr.Add(target, checked((int)header)), ownedBytes.Length);
        G();
        call.TransferRoot(output); // responsibility leaves the temporary owner before notification
        call.BeginPublication(output);
        try { publish(output); call.PublicationAccepted(); }
        catch { call.PublicationFailed(); throw; }
        return output;
    }
}
