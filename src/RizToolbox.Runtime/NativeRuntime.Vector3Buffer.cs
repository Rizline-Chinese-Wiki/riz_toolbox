using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal readonly record struct NativeVector3BufferBits(int X, int Y, int Z)
{
    internal static NativeVector3BufferBits FromSample(NativeVector3ArraySample sample)
        => new(BitConverter.SingleToInt32Bits(sample.X), BitConverter.SingleToInt32Bits(sample.Y),
            BitConverter.SingleToInt32Bits(sample.Z));
}

// Historical complete storage, not a live authorization or an atomic prestate.
internal sealed record NativeRootedVector3Buffer(IntPtr Array, IntPtr Class, IntPtr ElementClass,
    int Length, IReadOnlyList<NativeVector3BufferBits> Items)
{
    internal bool AtomicSnapshot => false;
}

internal sealed record NativeVector3BufferWriteObservation(NativeRootedVector3Buffer Before,
    NativeRootedVector3Buffer After, int PrefixLength)
{
    internal bool StoreEntered => PrefixLength != 0;
    internal bool AtomicConditionalWrite => false;
    internal bool ExternalConsumersKnown => false;
}

internal sealed class NativeVector3BufferConflict : InvalidOperationException
{
    internal NativeVector3BufferConflict(string message) : base(message) { }
}

internal sealed partial class NativeRuntime
{
    internal const int MaximumLineVector3BufferLength = 65536;

    /// <summary>Observe the entire original pinned array twice, including unused
    /// tail bits. No new array is allocated and no alias is substituted.</summary>
    internal NativeRootedVector3Buffer ReadRootedLineVector3Buffer(RootHandle original,
        IntPtr exactArrayClass, int maximum, Action guard)
        => WithLineVector3Buffer(original, exactArrayClass, maximum, guard, buffer => buffer.Read());

    /// <summary>Copy only a selected prefix into this original array after
    /// observing its complete expected storage. Original NaN payloads/-0 can be
    /// restored as raw bits; forward target finiteness belongs to the producer.
    /// Repeated checks do not make this a native compare-and-swap.</summary>
    internal NativeVector3BufferWriteObservation WriteRootedLineVector3BufferPrefix(RootHandle original,
        IntPtr exactArrayClass, NativeRootedVector3Buffer expected,
        IReadOnlyList<NativeVector3BufferBits> prefix, Action guard,
        Action verifySource, Action beforeStore)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(prefix); ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(verifySource); ArgumentNullException.ThrowIfNull(beforeStore);
        guard();
        // Copy complete managed inputs before native acquisition; no caller
        // collection can later change which bytes are expected or published.
        var beforeBits = CopyLineVectorBits(expected.Items, guard);
        var nextBits = CopyLineVectorBits(prefix, guard);
        if (expected.Length != beforeBits.Length || nextBits.Length > beforeBits.Length)
            throw new ArgumentOutOfRangeException(nameof(prefix), "The prefix must fit the complete expected array.");
        NativeRootedVector3Buffer? before = null;
        NativeVector3BufferWriteObservation? completed = null;
        var entered = false;
        try
        {
            return WithLineVector3Buffer(original, exactArrayClass, expected.Length, guard, buffer =>
            {
            if (expected.Array != buffer.Array || expected.Class != exactArrayClass
                || expected.ElementClass != buffer.ElementClass || expected.Length != buffer.Length)
                throw new NativeVector3BufferConflict("The expected buffer belongs to another original array or storage shape.");
                buffer.Storage(); verifySource(); guard();
                before = buffer.Read(); RequireLineVectorBits(before.Items, beforeBits);
                if (nextBits.Length == 0) return completed = new(before, before, 0);
                var bytes = new byte[checked(nextBits.Length * 12)];
                for (var i = 0; i < nextBits.Length; ++i)
                {
                    BitConverter.TryWriteBytes(bytes.AsSpan(i * 12, 4), nextBits[i].X);
                    BitConverter.TryWriteBytes(bytes.AsSpan(i * 12 + 4, 4), nextBits[i].Y);
                    BitConverter.TryWriteBytes(bytes.AsSpan(i * 12 + 8, 4), nextBits[i].Z);
                }
                buffer.Storage(); verifySource(); guard();
                RequireLineVectorBits(buffer.Read().Items, beforeBits);
                VerifyLineVectorBufferWritable(buffer.Data, bytes.Length);
                beforeStore(); guard();
                // A callback that visibly changes the array/association cannot
                // become an implicit permission to overwrite its new value.
                buffer.Storage(); verifySource(); guard();
                RequireLineVectorBits(buffer.Read().Items, beforeBits);
                // The callback may also alter page protection. Verify every
                // region of this actual prefix again immediately before entry.
                VerifyLineVectorBufferWritable(buffer.Data, bytes.Length);
                entered = true;
                Marshal.Copy(bytes, 0, buffer.Data, bytes.Length);
                guard();
                var after = buffer.Read();
                var wanted = (NativeVector3BufferBits[])beforeBits.Clone();
                Array.Copy(nextBits, wanted, nextBits.Length);
                RequireLineVectorBits(after.Items, wanted);
                return completed = new(before, after, nextBits.Length);
            });
        }
        catch (Exception error)
        {
            // Keep the original storage responsibility even when the operation
            // succeeded and its temporary pin then failed to free in finally.
            // NativePublicationCall keeps its own unknown-free owner; it must
            // not replace or lose the already-entered buffer responsibility.
            error.Data["LineVector3BufferOriginalRoot"] = original;
            error.Data["LineVector3BufferStoreEntered"] = entered;
            if (before is not null) error.Data["LineVector3BufferBefore"] = before;
            if (completed is not null) error.Data["LineVector3BufferWriteObservation"] = completed;
            throw;
        }
    }

    private static NativeVector3BufferBits[] CopyLineVectorBits(IReadOnlyList<NativeVector3BufferBits> input, Action guard)
    {
        guard(); var count = input.Count; guard();
        if (count < 0 || count > MaximumLineVector3BufferLength)
            throw new NotSupportedException("The complete Vector3 buffer exceeds its bound.");
        var copy = new NativeVector3BufferBits[count];
        for (var i = 0; i < count; ++i) { guard(); copy[i] = input[i]; guard(); }
        if (input.Count != count) throw new InvalidOperationException("The complete Vector3 input changed while copying.");
        guard(); return copy;
    }

    private static void RequireLineVectorBits(IReadOnlyList<NativeVector3BufferBits> actual,
        IReadOnlyList<NativeVector3BufferBits> expected)
    {
        if (actual.Count != expected.Count) throw new NativeVector3BufferConflict("The complete Vector3 array length changed.");
        for (var i = 0; i < actual.Count; ++i)
            if (actual[i] != expected[i]) throw new NativeVector3BufferConflict("A complete Vector3 slot differs at index " + i + ".");
    }

    private T WithLineVector3Buffer<T>(RootHandle original, IntPtr exactArrayClass, int maximum,
        Action guard, Func<LineVector3BufferAccess, T> action)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(guard);
        if (maximum < 0 || maximum > MaximumLineVector3BufferLength) throw new ArgumentOutOfRangeException(nameof(maximum));
        void G() { VerifyAccess(); original.VerifyOwnerThread(); guard(); }
        TRead O<TRead>(Func<TRead> read) { G(); var value = read(); G(); return value; }
        G(); var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = O(() => original.Target);
            nuint readableBytes = checked((nuint)(2 * IntPtr.Size));
            // O calls an arbitrary admission guard. Check the actual object
            // inside its callback, after that guard, before native exports
            // which may themselves dereference the object/array header.
            TRead V<TRead>(Func<TRead> read) => O(() =>
            {
                VerifyLineVectorBufferMemory(value, readableBytes, writable: false);
                return read();
            });
            var pin = call.Root(() =>
            {
                G(); VerifyLineVectorBufferMemory(value, readableBytes, writable: false);
                var id = _newHandle(value, true);
                // Store ownership before the first post-export caller guard.
                return new RootHandle(id, _freeHandle, _handleTarget, VerifyAccess);
            });
            G();
            var element = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
            var arrayClassGet = O(() => ReadGraphExport<ArrayClassGet>("il2cpp_array_class_get"));
            var rank = O(() => ReadGraphExport<Flags>("il2cpp_class_get_rank"));
            var getElement = O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_element_class"));
            var stride = O(() => ReadGraphExport<ArrayElementSize>("il2cpp_array_element_size"));
            void Metadata()
            {
                var type = LineEndpointClassType(element, "UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", 17, true, guard);
                VerifyLineEndpointVector(type, guard);
                if (exactArrayClass == IntPtr.Zero || O(() => arrayClassGet(element, 1)) != exactArrayClass)
                    throw new UnsupportedSignatureException("The original buffer has another canonical Vector3 array class.");
                var arrayType = O(() => _classType(exactArrayClass));
                if (arrayType == IntPtr.Zero || O(() => _typeClass(arrayType)) != exactArrayClass
                    || O(() => _byRef(arrayType)) || O(() => _typeCode(arrayType)) != 29
                    || O(() => _isValueType(exactArrayClass)) || O(() => _isEnum(exactArrayClass))
                    || LineRingTypeName(arrayType, guard) != "UnityEngine.Vector3[]"
                    || O(() => rank(exactArrayClass)) != 1 || O(() => getElement(exactArrayClass)) != element
                    || O(() => stride(exactArrayClass)) != 12
                    || O(() => pin.Target) != value || O(() => original.Target) != value
                    || V(() => _objectClass(value)) != exactArrayClass)
                    throw new UnsupportedSignatureException("The complete original Vector3 buffer class or root changed.");
            }
            Metadata();
            var length = O(() => ReadGraphExport<ArraySize>("il2cpp_array_length"));
            var byteLength = O(() => ReadGraphExport<ArraySize>("il2cpp_array_get_byte_length"));
            var objectSize = O(() => ReadGraphExport<ArraySize>("il2cpp_object_get_size"));
            var headerSize = O(() => ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size"));
            var header = O(() => headerSize());
            if (header < 2 * IntPtr.Size || header > 4096 || header % IntPtr.Size != 0)
                throw new UnsupportedSignatureException("The original Vector3 array has an invalid official header extent.");
            readableBytes = header;
            var count = V(() => length(value));
            var bytes = V(() => byteLength(value)); var total = V(() => objectSize(value));
            if (count > maximum || (ulong)count * 12 != bytes || (ulong)header + bytes != total)
                throw new UnsupportedSignatureException("The original Vector3 array has inconsistent official storage bounds.");
            readableBytes = total;
            void Storage()
            {
                Metadata();
                if (O(() => headerSize()) != header || V(() => length(value)) != count
                    || V(() => byteLength(value)) != bytes || V(() => objectSize(value)) != total)
                    throw new UnsupportedSignatureException("The complete Vector3 array shape changed.");
                G(); VerifyLineVectorBufferMemory(value, total, writable: false);
                if (Marshal.ReadIntPtr(value) != exactArrayClass)
                    throw new UnsupportedSignatureException("The original array header class differs from its official class.");
                G();
            }
            Storage();
            return action(new(value, exactArrayClass, element, checked((int)count),
                IntPtr.Add(value, checked((int)header)), Storage, G));
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private sealed class LineVector3BufferAccess
    {
        internal readonly IntPtr Array, Class, ElementClass, Data;
        internal readonly int Length;
        internal readonly Action Storage;
        private readonly Action _guard;
        internal LineVector3BufferAccess(IntPtr array, IntPtr cls, IntPtr element, int length,
            IntPtr data, Action storage, Action guard)
        { Array = array; Class = cls; ElementClass = element; Length = length; Data = data; Storage = storage; _guard = guard; }

        private NativeVector3BufferBits[] ReadOnce()
        {
            var items = new NativeVector3BufferBits[Length];
            for (var i = 0; i < items.Length; ++i)
            {
                _guard(); var offset = checked(i * 12);
                // Caller guards are callbacks too: recheck this actual slot's
                // protection after the guard and before any unmanaged read.
                VerifyLineVectorBufferMemory(IntPtr.Add(Data, offset), 12, writable: false);
                items[i] = new(Marshal.ReadInt32(Data, offset), Marshal.ReadInt32(Data, offset + 4), Marshal.ReadInt32(Data, offset + 8));
                _guard();
            }
            return items;
        }

        internal NativeRootedVector3Buffer Read()
        {
            Storage(); var first = ReadOnce(); Storage(); var second = ReadOnce(); Storage();
            RequireLineVectorBits(first, second);
            return new(Array, Class, ElementClass, Length, System.Array.AsReadOnly(second));
        }
    }

    private static void VerifyLineVectorBufferWritable(IntPtr start, int count)
        => VerifyLineVectorBufferMemory(start, checked((nuint)count), writable: true);

    private static void VerifyLineVectorBufferMemory(IntPtr start, nuint count, bool writable)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || start == IntPtr.Zero)
            throw new UnsupportedSignatureException("Vector3 buffer access requires Windows x64 bounded memory.");
        var cursor = unchecked((ulong)start.ToInt64()); var end = checked(cursor + (ulong)count);
        while (cursor < end)
        {
            if (CanvasCacheVirtualQuery(new IntPtr(unchecked((long)cursor)), out var region,
                    (nuint)Marshal.SizeOf<CanvasCacheMemoryRegion>()) == 0 || region.State != 0x1000
                || (region.Protect & 0x100) != 0)
                throw new UnsupportedSignatureException("The complete Vector3 memory range is not committed unguarded storage.");
            var protection = region.Protect & 0xff;
            if (writable ? protection is not (0x04 or 0x08 or 0x40 or 0x80)
                : protection is not (0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80))
                throw new UnsupportedSignatureException("The complete Vector3 memory range lacks the required read/write permission.");
            var begin = unchecked((ulong)region.BaseAddress.ToInt64()); var next = checked(begin + (ulong)region.RegionSize);
            if (cursor < begin || next <= cursor) throw new UnsupportedSignatureException("The Vector3 memory region is inconsistent.");
            cursor = Math.Min(end, next);
        }
    }
}
