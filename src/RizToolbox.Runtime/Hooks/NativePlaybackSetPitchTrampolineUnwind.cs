using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Independent unwind for 40 53 / 48 83 EC 30 followed by the
/// volatile R11 continuation. No other source prologue is admitted.</summary>
internal sealed class NativePlaybackSetPitchTrampolineUnwind
{
    private readonly IntPtr _entry, _block;
    private readonly NativePlaybackExecutableSeal.RangeSeal _seal;
    internal IntPtr InformationAddress => IntPtr.Add(_block, 16);
    private NativePlaybackSetPitchTrampolineUnwind(IntPtr entry)
    {
        _entry = entry;
        for (long delta = 0x10000; delta < 0x80000000; delta += 0x10000)
        {
            var address = checked((entry.ToInt64() + delta) & ~0xffffL);
            _block = VirtualAlloc((IntPtr)address, 4096, 0x3000, 4);
            if (_block != IntPtr.Zero) break;
        }
        if (_block == IntPtr.Zero) throw new UnsupportedSignatureException("No bounded independent SetPitch unwind allocation is available.");
        var bytes = new byte[24]; BitConverter.GetBytes(19u).CopyTo(bytes, 4);
        BitConverter.GetBytes(checked((uint)(_block.ToInt64() + 16 - entry.ToInt64()))).CopyTo(bytes, 8);
        // Prolog6: at offset2 PUSH RBX has completed; at offset6 SUB RSP,30
        // has completed. UWOP_ALLOC_SMALL(48) then UWOP_PUSH_NONVOL(RBX).
        new byte[] { 1, 6, 2, 0, 6, 0x52, 2, 0x30 }.CopyTo(bytes, 16);
        Marshal.Copy(bytes, 0, _block, bytes.Length);
        if (!VirtualProtect(_block, 4096, 2, out _) || !RtlAddFunctionTable(_block, 1, unchecked((ulong)entry.ToInt64())))
            throw new UnsupportedSignatureException("The SetPitch original unwind table was not registered read-only.");
        _seal = NativePlaybackExecutableSeal.RangeSeal.Capture(_block, bytes.Length, _block, false); Verify();
    }
    internal static NativePlaybackSetPitchTrampolineUnwind Register(IntPtr entry) => new(entry);
    internal void Verify()
    {
        _seal.Verify();
        foreach (var offset in new[] { 0, 2, 6, 16 })
        {
            var table = RtlLookupFunctionEntry(unchecked((ulong)(_entry.ToInt64() + offset)), out var imageBase, IntPtr.Zero);
            if (table != _block || imageBase != unchecked((ulong)_entry.ToInt64()))
                throw new UnsupportedSignatureException("The SetPitch original OS function-table owner changed.");
            VerifyBoundary(offset, imageBase);
        }
    }
    private unsafe void VerifyBoundary(int offset, ulong imageBase)
    {
        var raw = stackalloc byte[1264]; var aligned = (byte*)(((nuint)raw + 15) & ~(nuint)15);
        new Span<byte>(aligned, 1232).Clear(); var context = (IntPtr)aligned;
        var stack = stackalloc ulong[48]; new Span<ulong>(stack, 48).Clear();
        var entryRsp = (ulong)(stack + 16);
        const ulong returnPc = 0x1234567812345678, rbx = 0x3344334433443344;
        stack[16] = returnPc; stack[15] = rbx;
        Marshal.WriteInt32(context, 48, 0x10001f); Marshal.WriteInt64(context, 248, checked(_entry.ToInt64() + offset));
        var delta = (offset >= 2 ? 8ul : 0ul) + (offset >= 6 ? 48ul : 0ul);
        Marshal.WriteInt64(context, 152, unchecked((long)(entryRsp - delta)));
        Marshal.WriteInt64(context, 144, unchecked((long)(offset >= 2 ? 0xbadul : rbx)));
        foreach (var register in new[] { 160, 168, 176, 216, 224, 232, 240 }) Marshal.WriteInt64(context, register, register * 101L);
        new Span<byte>(aligned + 512, 160).Fill(0x5a);
        RtlVirtualUnwind(0, imageBase, checked(imageBase + (uint)offset), _block, context, out _, out _, IntPtr.Zero);
        if (unchecked((ulong)Marshal.ReadInt64(context, 248)) != returnPc || unchecked((ulong)Marshal.ReadInt64(context, 152)) != entryRsp + 8
            || unchecked((ulong)Marshal.ReadInt64(context, 144)) != rbx)
            throw new UnsupportedSignatureException("The actual OS SetPitch unwind did not recover RIP/RSP/RBX at boundary " + offset + ".");
        foreach (var register in new[] { 160, 168, 176, 216, 224, 232, 240 })
            if (Marshal.ReadInt64(context, register) != register * 101L) throw new UnsupportedSignatureException("SetPitch unwind changed another nonvolatile integer register.");
        foreach (var value in new Span<byte>(aligned + 512, 160))
            if (value != 0x5a) throw new UnsupportedSignatureException("SetPitch unwind changed a nonvolatile XMM register.");
    }
    [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("ntdll")] private static extern bool RtlAddFunctionTable(IntPtr table, uint count, ulong baseAddress);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
    [DllImport("ntdll")] private static extern IntPtr RtlVirtualUnwind(uint type, ulong imageBase, ulong pc, IntPtr function,
        IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);
}
