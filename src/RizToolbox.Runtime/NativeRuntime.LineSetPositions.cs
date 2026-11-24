using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

// Describes the exact generated instance ABI:
// void(self, UnityEngine.Vector3[] positions, int positionCount, MethodInfo*).
// Dispatch still goes through il2cpp_runtime_invoke so an IL2CPP exception is
// returned through its exception out pointer rather than crossing a CLR P/Invoke.
internal sealed record NativeLineSetPositionsSignature(NativeMetadataMethod Method,
    IntPtr LineClass, IntPtr ArrayClass, IntPtr VectorClass, IntPtr CountClass, IntPtr Module);

internal sealed partial class NativeRuntime
{
    internal const int ExactLineSetPositionsRva = 0x3fb160;
    internal const int ExactLineSetPositionsBodyLength = 288;

    /// <summary>Freshly enumerate only the exact two-parameter LineInformation
    /// declaration and validate its canonical element/array/scalar ABI and
    /// MethodInfo2022 entry. Every native metadata export is caller-guarded.</summary>
    internal NativeLineSetPositionsSignature InspectLineSetPositionsSignature(
        IntPtr lineClass, IntPtr module, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("The exact SetPositions module is absent.");
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        T M<T>(IntPtr method, Func<T> read) => O(() =>
        {
            VerifyLineSetPositionsReadable(method,
                checked((nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>()));
            return read();
        });

        _ = LineEndpointClassType(lineClass, "Assembly-CSharp.dll", "InLevel", "LineInformation", 18, false, guard);
        var vector = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
        var vectorType = LineEndpointClassType(vector, "UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", 17, true, guard);
        VerifyLineEndpointVector(vectorType, guard);
        var integer = LineEndpointClass("mscorlib.dll", "System", "Int32", guard);
        var integerType = LineEndpointClassType(integer, "mscorlib.dll", "System", "Int32", 8, true, guard);
        var integerLayout = LineRingLayout(integerType, guard);
        NativeAbi.ValidateValue(typeof(int), integerLayout);

        var arrayClassGet = O(() => ReadGraphExport<ArrayClassGet>("il2cpp_array_class_get"));
        var rank = O(() => ReadGraphExport<Flags>("il2cpp_class_get_rank"));
        var elementClass = O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_element_class"));
        var stride = O(() => ReadGraphExport<ArrayElementSize>("il2cpp_array_element_size"));
        var arrayClass = O(() => arrayClassGet(vector, 1));
        if (arrayClass == IntPtr.Zero || O(() => rank(arrayClass)) != 1
            || O(() => elementClass(arrayClass)) != vector || O(() => stride(arrayClass)) != 12
            || O(() => _isValueType(arrayClass)) || O(() => _isEnum(arrayClass)))
            throw new UnsupportedSignatureException("SetPositions requires the canonical rank-one twelve-byte Vector3 array.");
        var canonicalArrayType = O(() => _classType(arrayClass));
        if (canonicalArrayType == IntPtr.Zero || O(() => _typeClass(canonicalArrayType)) != arrayClass
            || O(() => _byRef(canonicalArrayType)) || O(() => _typeCode(canonicalArrayType)) != 29
            || LineRingTypeName(canonicalArrayType, guard) != "UnityEngine.Vector3[]")
            throw new UnsupportedSignatureException("The canonical SetPositions Vector3 array type changed.");

        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 4096; ++i)
            {
                var candidate = O(() => _methods(lineClass, iterator.Pointer));
                if (candidate == IntPtr.Zero) { terminated = true; break; }
                if (Utf8(M(candidate, () => _methodName(candidate))) != "SetPositions"
                    || M(candidate, () => _parameterCount(candidate)) != 2) continue;
                var first = M(candidate, () => _parameterType(candidate, 0));
                var second = M(candidate, () => _parameterType(candidate, 1));
                if (LineRingTypeName(first, guard) != "UnityEngine.Vector3[]"
                    || LineRingTypeName(second, guard) != "System.Int32") continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("The exact two-argument SetPositions declaration is ambiguous.");
                found = candidate;
            }
        if (!terminated || found == IntPtr.Zero || M(found, () => _methodClass(found)) != lineClass)
            throw new UnsupportedSignatureException("The exact LineInformation.SetPositions(Vector3[],Int32) is absent or foreign.");
        var arrayParameter = M(found, () => _parameterType(found, 0));
        if (O(() => _byRef(arrayParameter)) || O(() => _typeCode(arrayParameter)) != 29
            || O(() => _typeClass(arrayParameter)) != arrayClass
            || LineRingTypeName(arrayParameter, guard) != "UnityEngine.Vector3[]")
            throw new UnsupportedSignatureException("SetPositions has another array parameter ABI.");
        var countParameter = M(found, () => _parameterType(found, 1));
        var countLayout = LineRingLayout(countParameter, guard);
        if (O(() => _typeClass(countParameter)) != integer || countLayout != integerLayout)
            throw new UnsupportedSignatureException("SetPositions has another Int32 count ABI.");
        NativeAbi.ValidateValue(typeof(int), countLayout);
        var returnType = M(found, () => _returnType(found));
        VerifyLineEndpointVoid(returnType, guard);
        uint implementation = 0;
        var flags = M(found, () => _methodFlags(found, out implementation));
        var generic = M(found, () => _isGenericMethod(found));
        var inflated = M(found, () => _isInflatedMethod(found));
        if ((flags & (0x10u | 0x400u)) != 0 || generic || inflated)
            throw new UnsupportedSignatureException("SetPositions must be a concrete non-generic instance method.");

        guard(); VerifyLineSetPositionsReadable(found,
            checked((nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>()));
        var record = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(found); guard();
        HookTargetValidation.ValidateMetadata(record, M(found, () => _methodName(found)), lineClass,
            returnType, flags, 2, record.MethodPointer);
        if (record.ImplementationFlags != (ushort)implementation)
            throw new UnsupportedSignatureException("SetPositions implementation flags changed.");
        guard(); HookTargetValidation.VerifyExecutable(record.MethodPointer, module); guard();
        var rva = record.MethodPointer.ToInt64() - module.ToInt64();
        if (rva != ExactLineSetPositionsRva)
            throw new UnsupportedSignatureException("SetPositions is not the frozen two-argument entry.");
        var entry = new NativeMetadataEntry(record.MethodPointer, rva, record.Slot);
        var method = new NativeMetadataMethod(found, new(lineClass, "InLevel.LineInformation"),
            "SetPositions", "System.Void", Array.AsReadOnly(new[] { "UnityEngine.Vector3[]", "System.Int32" }),
            flags, implementation, generic, inflated, entry,
            "Exact current MethodInfo2022 and canonical Vector3[]/Int32 ABI; body pin belongs to the profile.");
        guard(); return new(method, lineClass, arrayClass, vector, integer, module);
    }

    /// <summary>Invoke the exact pinned method through il2cpp_runtime_invoke with
    /// caller-owned original roots and registered temporary pins. The final
    /// beforeEntry callback publishes responsibility before final revalidation.
    /// Native entry may save fields and then raise; the original roots and an
    /// entered flag remain attached even if temporary cleanup also fails.</summary>
    internal void InvokeLineSetPositionsWithPublicationAdmission(
        NativeLineSetPositionsSignature expected, RootHandle originalLine, RootHandle originalArray,
        int positionCount, Action guard, Action verifyProfile, Action verifySource, Action beforeEntry)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(originalLine); ArgumentNullException.ThrowIfNull(originalArray);
        ArgumentNullException.ThrowIfNull(guard); ArgumentNullException.ThrowIfNull(verifyProfile);
        ArgumentNullException.ThrowIfNull(verifySource); ArgumentNullException.ThrowIfNull(beforeEntry);
        if (positionCount < 0) throw new ArgumentOutOfRangeException(nameof(positionCount));
        var entered = false; var returned = false;
        try
        {
            void G0() { VerifyAccess(); originalLine.VerifyOwnerThread(); originalArray.VerifyOwnerThread(); guard(); }
            T O0<T>(Func<T> read) { G0(); var result = read(); G0(); return result; }
            var line = O0(() => originalLine.Target);
            var array = O0(() => originalArray.Target);
            nuint lineExtent = checked((nuint)(2 * IntPtr.Size));
            nuint arrayExtent = checked((nuint)(2 * IntPtr.Size));
            void G()
            {
                G0();
                VerifyLineSetPositionsReadable(expected.Method.MethodInfo,
                    checked((nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>()));
                VerifyLineSetPositionsReadable(line, lineExtent);
                VerifyLineSetPositionsReadable(array, arrayExtent);
            }
            T O<T>(Func<T> read) { G(); var result = read(); G(); return result; }
            T V<T>(IntPtr value, nuint extent, Func<T> read)
                => O(() => { VerifyLineSetPositionsReadable(value, extent); return read(); });

            var arguments = new[] { Arg.Ref(array), Arg.Value(positionCount) };
            NativePublicationCall call;
            try { call = new NativePublicationCall(VerifyAccess, arguments); }
            catch { foreach (var argument in arguments) argument.Dispose(); throw; }
            try
            {
                RootHandle Pin(IntPtr value) => call.Root(() =>
                {
                    G(); VerifyLineSetPositionsReadable(value, checked((nuint)(2 * IntPtr.Size)));
                    if (O(() => originalLine.Target) != line || O(() => originalArray.Target) != array)
                        throw new UnsupportedSignatureException("The original SetPositions roots changed before pin acquisition.");
                    var id = _newHandle(value, true);
                    return new RootHandle(id, _freeHandle, _handleTarget, VerifyAccess);
                });
                var linePin = Pin(line); var arrayPin = Pin(array);
                G(); verifySource(); G();
                var current = InspectLineSetPositionsSignature(expected.LineClass, expected.Module, G);
                if (!SameLineSetPositionsSignature(expected, current))
                    throw new UnsupportedSignatureException("The exact SetPositions signature/MethodInfo changed.");
                if (O(() => linePin.Target) != line || O(() => arrayPin.Target) != array
                    || O(() => originalLine.Target) != line || O(() => originalArray.Target) != array
                    || V(line, checked((nuint)(2 * IntPtr.Size)), () => _objectClass(line)) != expected.LineClass
                    || V(array, arrayExtent, () => _objectClass(array)) != expected.ArrayClass)
                    throw new UnsupportedSignatureException("The original LineInformation/Vector3 array pin or class changed.");
                var objectSize = O(() => ReadGraphExport<ArraySize>("il2cpp_object_get_size"));
                var lineSize = V(line, lineExtent, () => objectSize(line));
                if (lineSize < 0x94 || lineSize > 4096)
                    throw new UnsupportedSignatureException("The actual SetPositions LineInformation storage is outside its bounded fields.");
                lineExtent = checked((nuint)lineSize);
                G();
                VerifyLineSetPositionsWritable(IntPtr.Add(line, 0x88), 8);
                VerifyLineSetPositionsWritable(IntPtr.Add(line, 0x90), 4);
                var headerSize = O(() => ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size"));
                var header = O(() => headerSize());
                if (header < 2 * IntPtr.Size || header > 4096 || header % IntPtr.Size != 0)
                    throw new UnsupportedSignatureException("The original SetPositions array header extent changed.");
                arrayExtent = checked((nuint)header);
                var length = O(() => ReadGraphExport<ArraySize>("il2cpp_array_length"));
                var byteLength = O(() => ReadGraphExport<ArraySize>("il2cpp_array_get_byte_length"));
                var capacity = V(array, arrayExtent, () => length(array));
                var storageBytes = V(array, arrayExtent, () => byteLength(array));
                var total = V(array, arrayExtent, () => objectSize(array));
                if (capacity > 65536 || (ulong)capacity * 12 != storageBytes
                    || (ulong)header + storageBytes != total)
                    throw new UnsupportedSignatureException("The actual SetPositions scratch storage has inconsistent official bounds.");
                arrayExtent = checked((nuint)total);
                G();
                if ((uint)positionCount > capacity)
                    throw new ArgumentOutOfRangeException(nameof(positionCount), "The actual rooted scratch array is shorter than the selected count.");

                InvokeCore(expected.Method.MethodInfo, line, null, call, true, arguments,
                    () =>
                    {
                        G();
                        beforeEntry(); // publish responsibility before all final callbacks/checks
                        G(); verifyProfile(); G(); verifySource(); G();
                        if (O(() => linePin.Target) != line || O(() => arrayPin.Target) != array
                            || O(() => originalLine.Target) != line || O(() => originalArray.Target) != array
                            || V(line, lineExtent, () => _objectClass(line)) != expected.LineClass
                            || V(line, lineExtent, () => objectSize(line)) != lineSize
                            || V(array, arrayExtent, () => _objectClass(array)) != expected.ArrayClass
                            || O(() => headerSize()) != header
                            || V(array, arrayExtent, () => length(array)) != capacity
                            || V(array, arrayExtent, () => byteLength(array)) != storageBytes
                            || V(array, arrayExtent, () => objectSize(array)) != total
                            || (uint)positionCount > capacity)
                            throw new UnsupportedSignatureException("The SetPositions source/receiver/array/capacity changed before entry.");
                        VerifyLineSetPositionsWritable(IntPtr.Add(line, 0x88), 8);
                        VerifyLineSetPositionsWritable(IntPtr.Add(line, 0x90), 4);
                        VerifyLineSetPositionsReadable(array, arrayExtent);
                        entered = true;
                    }, G, G);
                returned = true;
            }
            catch (Exception error) { call.OperationFailed(error); throw; }
            finally { call.Dispose(); }
        }
        catch (Exception error)
        {
            error.Data["LineSetPositionsOriginalLineRoot"] = originalLine;
            error.Data["LineSetPositionsOriginalArrayRoot"] = originalArray;
            error.Data["LineSetPositionsPositionCount"] = positionCount;
            error.Data["LineSetPositionsNativeEntered"] = entered;
            error.Data["LineSetPositionsReturned"] = returned;
            throw;
        }
    }

    private static bool SameLineSetPositionsSignature(NativeLineSetPositionsSignature left,
        NativeLineSetPositionsSignature right)
    {
        var a = left.Method; var b = right.Method;
        return left.LineClass == right.LineClass && left.ArrayClass == right.ArrayClass
            && left.VectorClass == right.VectorClass && left.CountClass == right.CountClass
            && left.Module == right.Module && a.MethodInfo == b.MethodInfo
            && a.DeclaringClass == b.DeclaringClass && a.Name == b.Name && a.ReturnType == b.ReturnType
            && a.ParameterTypes.SequenceEqual(b.ParameterTypes) && a.Flags == b.Flags
            && a.ImplementationFlags == b.ImplementationFlags && a.GenericMethod == b.GenericMethod
            && a.InflatedMethod == b.InflatedMethod && a.Entry == b.Entry;
    }

    private static void VerifyLineSetPositionsReadable(IntPtr start, nuint bytes)
        => VerifyLineSetPositionsMemory(start, bytes, writable: false);

    private static void VerifyLineSetPositionsWritable(IntPtr start, nuint bytes)
        => VerifyLineSetPositionsMemory(start, bytes, writable: true);

    private static void VerifyLineSetPositionsMemory(IntPtr start, nuint bytes, bool writable)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || start == IntPtr.Zero || bytes == 0)
            throw new UnsupportedSignatureException("SetPositions requires Windows x64 readable native storage.");
        var cursor = unchecked((ulong)start.ToInt64()); var end = checked(cursor + (ulong)bytes);
        while (cursor < end)
        {
            if (CanvasCacheVirtualQuery(new IntPtr(unchecked((long)cursor)), out var region,
                    (nuint)Marshal.SizeOf<CanvasCacheMemoryRegion>()) == 0 || region.State != 0x1000
                || (region.Protect & 0x100) != 0)
                throw new UnsupportedSignatureException("The SetPositions native range is uncommitted or guarded.");
            var protection = region.Protect & 0xff;
            if (writable ? protection is not (0x04 or 0x08 or 0x40 or 0x80)
                : protection is not (0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80))
                throw new UnsupportedSignatureException("The SetPositions native range lacks its required memory permission.");
            var begin = unchecked((ulong)region.BaseAddress.ToInt64());
            var next = checked(begin + (ulong)region.RegionSize);
            if (cursor < begin || next <= cursor)
                throw new UnsupportedSignatureException("The SetPositions memory region is inconsistent.");
            cursor = Math.Min(end, next);
        }
    }
}
