using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>One conditional four-byte store into an independently owned
    /// exact Single[] cache. Official metadata/storage bounds and a registered
    /// pin precede all pointer arithmetic. Raw bits permit original NaN/-0
    /// restoration; the caller separately validates finite forward targets.</summary>
    internal void WriteRootedCanvasCacheSingle(RootHandle array, IntPtr exactArrayClass, int expectedLength,
        int index, int expectedBits, int bits, Action guard, Action beforeDispatch, Action verifySource)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard); guard();
        ArgumentNullException.ThrowIfNull(beforeDispatch); ArgumentNullException.ThrowIfNull(array);
        ArgumentNullException.ThrowIfNull(verifySource);
        if (expectedLength < 0 || expectedLength > 65536 || (uint)index >= (uint)expectedLength)
            throw new ArgumentOutOfRangeException(nameof(index));
        T Observe<T>(Func<T> action) { guard(); var result = action(); guard(); return result; }
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = Observe(() => array.Target);
            var pin = Observe(() => call.Root(() => new RootHandle(_newHandle(value, true), _freeHandle, _handleTarget, VerifyAccess)));
            var single = Observe(VerifyCanvasCacheSingle);
            void Metadata()
            {
                guard(); VerifyGeometrySingleArrayClass(exactArrayClass, single, guard); guard();
                if (Observe(() => _objectClass(value)) != exactArrayClass || Observe(() => pin.Target) != value
                    || Observe(() => array.Target) != value)
                    throw new UnsupportedSignatureException("The actual Canvas cache pin/class/source identity changed.");
            }
            Metadata();
            var length = ReadGraphExport<ArraySize>("il2cpp_array_length");
            var bytes = ReadGraphExport<ArraySize>("il2cpp_array_get_byte_length");
            var size = ReadGraphExport<ArraySize>("il2cpp_object_get_size");
            var headerSize = ReadGraphExport<ReferenceArrayHeaderSize>("il2cpp_array_object_header_size");
            var header = Observe(() => headerSize());
            var count = Observe(() => length(value)); var byteCount = Observe(() => bytes(value)); var total = Observe(() => size(value));
            if (count != expectedLength || header < 2 * IntPtr.Size || header > 128 || header % IntPtr.Size != 0
                || (ulong)count * sizeof(float) != byteCount || (ulong)header + byteCount != total)
                throw new UnsupportedSignatureException("The complete Canvas cache array storage differs from its official bounds.");
            HookTargetValidation.VerifyReadable(value, total);
            if (Marshal.ReadIntPtr(value) != exactArrayClass)
                throw new UnsupportedSignatureException("The actual pinned Canvas cache header differs from its class.");
            var offset = checked((int)header + index * sizeof(float));
            var slot = IntPtr.Add(value, offset); VerifyCanvasCacheWritable(slot);
            void Storage()
            {
                Metadata();
                if (Observe(() => length(value)) != count || Observe(() => bytes(value)) != byteCount
                    || Observe(() => size(value)) != total || Observe(() => headerSize()) != header
                    || Marshal.ReadIntPtr(value) != exactArrayClass)
                    throw new UnsupportedSignatureException("The complete pinned Canvas cache storage changed before or after its conditional store.");
                guard();
            }
            Storage();
            // Metadata and pin acquisition may replace a Canvas field without
            // changing its managed run revision. Recheck current associations
            // after those callbacks, before the last raw-value observation.
            verifySource();
            guard();
            var before = Marshal.ReadInt32(slot);
            if (before != expectedBits)
                throw NativeCanvasCacheConflict.Error(value.ToInt64(), index, "Single[]", expectedBits, before);
            beforeDispatch(); guard();
            Marshal.WriteInt32(slot, bits);
            guard(); Storage();
            var after = Marshal.ReadInt32(slot);
            if (after != bits)
            {
                if (after != expectedBits) throw NativeCanvasCacheConflict.Error(value.ToInt64(), index, "Single[]", bits, after);
                throw new InvalidOperationException("The actual Canvas cache slot differs from its commanded raw bits.");
            }
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CanvasCacheMemoryRegion
    {
        internal IntPtr BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal nuint RegionSize;
        internal uint State, Protect, Type;
    }
    [DllImport("kernel32", EntryPoint = "VirtualQuery")]
    private static extern nuint CanvasCacheVirtualQuery(IntPtr address, out CanvasCacheMemoryRegion region, nuint length);
    private static void VerifyCanvasCacheWritable(IntPtr address)
    {
        if (!OperatingSystem.IsWindows() || CanvasCacheVirtualQuery(address, out var region,
                (nuint)Marshal.SizeOf<CanvasCacheMemoryRegion>()) == 0 || region.State != 0x1000
            || (region.Protect & 0x100) != 0 || (region.Protect & 0xff) is not (0x04 or 0x08 or 0x40 or 0x80)
            || (ulong)address.ToInt64() < (ulong)region.BaseAddress.ToInt64()
            || (ulong)address.ToInt64() - (ulong)region.BaseAddress.ToInt64() + sizeof(float) > (ulong)region.RegionSize)
            throw new UnsupportedSignatureException("The actual pinned Canvas cache slot lacks writable four-byte storage.");
    }
}
