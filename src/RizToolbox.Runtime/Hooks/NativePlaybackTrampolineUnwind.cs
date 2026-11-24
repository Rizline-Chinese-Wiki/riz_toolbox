using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Process-retained unwind for precisely MOV [RSP+8],RBX; PUSH RDI;
/// MOV R11,target+6; JMP R11. Only ABI-volatile R11 is changed by the tail.</summary>
internal sealed class NativePlaybackTrampolineUnwind
{
    private readonly IntPtr _entry, _block;
    private readonly NativePlaybackExecutableSeal.RangeSeal _seal;
    internal IntPtr InformationAddress => IntPtr.Add(_block, 16);
    private NativePlaybackTrampolineUnwind(IntPtr entry, IntPtr reservedMetadata)
    {
        _entry = entry;
        if (reservedMetadata != IntPtr.Add(entry, 4096))
            throw new UnsupportedSignatureException("GetTime unwind metadata must use the page reserved with its exact private thunk.");
        _block = reservedMetadata;
        var bytes = new byte[28];
        BitConverter.GetBytes(19u).CopyTo(bytes, 4);
        BitConverter.GetBytes(checked((uint)(_block.ToInt64() + 16 - entry.ToInt64()))).CopyTo(bytes, 8);
        // At offset 5 RBX is still unchanged, so recovery is safely deferred
        // until offset 6. Once PUSH completes, restore RBX at RSP+16 before
        // undoing PUSH RDI. This order is verified at all actual PC boundaries.
        new byte[] { 1, 6, 3, 0, 6, 0x34, 2, 0, 6, 0x70, 0, 0 }.CopyTo(bytes, 16);
        Marshal.Copy(bytes, 0, _block, bytes.Length);
        if (!VirtualProtect(_block, 4096, 2, out _) || !RtlAddFunctionTable(_block, 1, unchecked((ulong)entry.ToInt64())))
            throw new UnsupportedSignatureException("The private GetTime relocation unwind table was not registered read-only.");
        _seal = NativePlaybackExecutableSeal.RangeSeal.Capture(_block, bytes.Length, entry, false);
        Verify();
    }
    internal static NativePlaybackTrampolineUnwind Register(IntPtr entry, IntPtr reservedMetadata) => new(entry, reservedMetadata);
    internal void Verify()
    {
        _seal.Verify();
        foreach (var offset in new[] { 0, 5, 6, 16 })
        {
            var table = RtlLookupFunctionEntry(unchecked((ulong)(_entry.ToInt64() + offset)), out var imageBase, IntPtr.Zero);
            if (table != _block || imageBase != unchecked((ulong)_entry.ToInt64()))
                throw new UnsupportedSignatureException("The OS GetTime relocation function-table owner changed.");
            VerifyBoundary(offset, imageBase);
        }
    }
    private unsafe void VerifyBoundary(int offset, ulong imageBase)
    {
        var raw = stackalloc byte[1264]; var aligned = (byte*)(((nuint)raw + 15) & ~(nuint)15);
        new Span<byte>(aligned, 1232).Clear(); var context = (IntPtr)aligned;
        var stack = stackalloc ulong[48]; new Span<ulong>(stack, 48).Clear();
        var entryRsp = (ulong)(stack + 16);
        const ulong returnPc = 0x1234567812345678, rbx = 0x3344334433443344, rdi = 0x7788778877887788;
        stack[16] = returnPc; stack[17] = rbx; stack[15] = rdi;
        Marshal.WriteInt32(context, 48, 0x10001f);
        Marshal.WriteInt64(context, 248, checked(_entry.ToInt64() + offset));
        Marshal.WriteInt64(context, 152, unchecked((long)(entryRsp - (offset >= 6 ? 8ul : 0ul))));
        Marshal.WriteInt64(context, 144, unchecked((long)(offset >= 6 ? 0xbadul : rbx)));
        Marshal.WriteInt64(context, 176, unchecked((long)(offset >= 6 ? 0xbadul : rdi)));
        foreach (var register in new[] { 160, 168, 216, 224, 232, 240 }) Marshal.WriteInt64(context, register, register * 101L);
        new Span<byte>(aligned + 512, 160).Fill(0x5a);
        RtlVirtualUnwind(0, imageBase, checked(imageBase + (uint)offset), _block, context, out _, out _, IntPtr.Zero);
        if (unchecked((ulong)Marshal.ReadInt64(context, 248)) != returnPc || unchecked((ulong)Marshal.ReadInt64(context, 152)) != entryRsp + 8
            || unchecked((ulong)Marshal.ReadInt64(context, 144)) != rbx || unchecked((ulong)Marshal.ReadInt64(context, 176)) != rdi)
            throw new UnsupportedSignatureException("The actual OS GetTime relocation unwind did not recover RIP/RSP/RBX/RDI at boundary " + offset + ".");
        foreach (var register in new[] { 160, 168, 216, 224, 232, 240 })
            if (Marshal.ReadInt64(context, register) != register * 101L) throw new UnsupportedSignatureException("GetTime unwind changed another nonvolatile integer register.");
        foreach (var value in new Span<byte>(aligned + 512, 160))
            if (value != 0x5a) throw new UnsupportedSignatureException("GetTime unwind changed a nonvolatile XMM register.");
    }
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("ntdll")] private static extern bool RtlAddFunctionTable(IntPtr table, uint count, ulong baseAddress);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
    [DllImport("ntdll")] private static extern IntPtr RtlVirtualUnwind(uint type, ulong imageBase, ulong pc, IntPtr function,
        IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);
}
