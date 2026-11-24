using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// Adds the original-array seam to the existing real unmanaged-memory IL2CPP fake.
// The SDK still resolves and invokes its production export delegates.
sealed partial class FakeNative
{
    internal IntPtr Vector3BufferArray { get; private set; }
    internal IntPtr Vector3BufferClass => ClassOf(_vector3ArrayType);
    internal uint? Vector3BufferHeaderSizeOverride;
    internal uint Vector3BufferLengthExtra;

    internal (NativeRuntime Runtime, NativeRuntime.RootHandle Original, IntPtr ExactClass)
        CaptureVector3Buffer(params NativeVector3BufferBits[] items)
        => CaptureVector3BufferCore(items, IntPtr.Zero);

    internal (NativeRuntime Runtime, NativeRuntime.RootHandle Original, IntPtr ExactClass, Vector3BufferPage Page)
        CapturePageVector3Buffer(params NativeVector3BufferBits[] items)
    {
        var page = new Vector3BufferPage();
        try
        {
            var (runtime, original, cls) = CaptureVector3BufferCore(items, page.Pointer);
            return (runtime, original, cls, page);
        }
        catch { page.Dispose(); throw; }
    }

    private (NativeRuntime Runtime, NativeRuntime.RootHandle Original, IntPtr ExactClass)
        CaptureVector3BufferCore(NativeVector3BufferBits[] items, IntPtr page)
    {
        ArgumentNullException.ThrowIfNull(items);
        var runtime = Vector3ArrayCaptureApi();
        Add("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(Vector3BufferHeaderSize));
        Add("il2cpp_array_length", "ArraySize", nameof(Vector3BufferLength));
        if (page == IntPtr.Zero) Vector3BufferArray = NewVectorArray(Vector3ArrayElementClass, checked((ulong)items.Length));
        else
        {
            var storage = checked(ArrayHeaderBytes + items.Length * 12 + 8);
            if (storage > Vector3BufferPage.Size) throw new ArgumentOutOfRangeException(nameof(items));
            Vector3BufferArray = page;
            for (var i = 0; i < storage; i++) Marshal.WriteByte(page, i, 0x5a);
            _boxedTypes[page] = _vector3ArrayType;
            _arrays[page] = checked((uint)items.Length);
            ++ArrayAllocations; LastArray = page;
        }
        // The other array-publication tests never dereference the native object
        // header. This reader does, so make its raw class agree with the export.
        Marshal.WriteIntPtr(Vector3BufferArray, Vector3BufferClass);
        for (var i = 0; i < items.Length; i++) WriteVector3BufferItem(i, items[i]);
        var original = new NativeRuntime.RootHandle(NewHandle(Vector3BufferArray, true), FreeHandle, HandleTarget);
        return (runtime, original, Vector3BufferClass);
    }

    internal NativeRuntime.RootHandle AliasVector3BufferRoot()
        => new(NewHandle(Vector3BufferArray, true), FreeHandle, HandleTarget);

    internal NativeVector3BufferBits ReadVector3BufferItem(int index)
    {
        var offset = checked(ArrayHeaderBytes + index * 12);
        return new(Marshal.ReadInt32(Vector3BufferArray, offset),
            Marshal.ReadInt32(Vector3BufferArray, offset + 4),
            Marshal.ReadInt32(Vector3BufferArray, offset + 8));
    }

    internal void WriteVector3BufferItem(int index, NativeVector3BufferBits bits)
    {
        var offset = checked(ArrayHeaderBytes + index * 12);
        Marshal.WriteInt32(Vector3BufferArray, offset, bits.X);
        Marshal.WriteInt32(Vector3BufferArray, offset + 4, bits.Y);
        Marshal.WriteInt32(Vector3BufferArray, offset + 8, bits.Z);
    }

    internal void ReportWrongVector3BufferClass() => _boxedTypes[Vector3BufferArray] = Object;
    internal void PatchVector3BufferHeaderClass(IntPtr cls) => Marshal.WriteIntPtr(Vector3BufferArray, cls);

    private uint Vector3BufferHeaderSize() => Vector3BufferHeaderSizeOverride ?? checked((uint)ArrayHeaderBytes);
    private uint Vector3BufferLength(IntPtr array) => checked(_arrays[array] + Vector3BufferLengthExtra);

    internal sealed class Vector3BufferPage : IDisposable
    {
        internal const int Size = 4096;
        private IntPtr _pointer;
        internal IntPtr Pointer => _pointer;
        internal Vector3BufferPage()
        {
            _pointer = VirtualAlloc(IntPtr.Zero, Size, 0x3000, 0x04);
            if (_pointer == IntPtr.Zero) throw new InvalidOperationException("VirtualAlloc failed for the buffer fixture.");
        }
        internal uint Protect(uint protection)
        {
            if (!VirtualProtect(_pointer, Size, protection, out var previous))
                throw new InvalidOperationException("VirtualProtect failed for the buffer fixture.");
            return previous;
        }
        public void Dispose()
        {
            var pointer = Interlocked.Exchange(ref _pointer, IntPtr.Zero);
            if (pointer != IntPtr.Zero && !VirtualFree(pointer, 0, 0x8000))
                throw new InvalidOperationException("VirtualFree failed for the buffer fixture.");
        }
    }

    [DllImport("kernel32", SetLastError = true)] private static extern IntPtr VirtualAlloc(
        IntPtr address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint previous);
    [DllImport("kernel32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(IntPtr address, nuint size, uint freeType);
}
