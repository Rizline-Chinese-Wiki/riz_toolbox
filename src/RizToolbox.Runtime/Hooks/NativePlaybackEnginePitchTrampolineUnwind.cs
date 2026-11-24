using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Independent OS unwind for MOV RAX,RSP; MOV [RAX+18h],RBX and
/// the private R11 continuation. The wrapper's PUSH/SUB unwind is inapplicable.</summary>
internal sealed class NativePlaybackEnginePitchTrampolineUnwind
{
    private readonly IntPtr _entry, _block;
    private readonly NativePlaybackExecutableSeal.RangeSeal _seal;
    internal IntPtr InformationAddress => IntPtr.Add(_block, 16);
    internal NativePlaybackEnginePitchTrampolineUnwind(IntPtr entry, IntPtr metadata)
    {
        if (metadata != IntPtr.Add(entry, 4096)) throw new UnsupportedSignatureException("Native engine unwind metadata must share its exact reserved thunk allocation.");
        (_entry, _block) = (entry, metadata);
        var bytes = new byte[24]; BitConverter.GetBytes(20u).CopyTo(bytes, 4);
        BitConverter.GetBytes(0x1010u).CopyTo(bytes, 8);
        // At offset 7 SAVE_NONVOL(RBX, entry-RSP+18h) has completed. No PUSH
        // or stack allocation has occurred in this relocated prefix.
        new byte[] { 1, 7, 2, 0, 7, 0x34, 3, 0 }.CopyTo(bytes, 16);
        Marshal.Copy(bytes, 0, _block, bytes.Length);
        if (!VirtualProtect(_block, 4096, 2, out _) || !RtlAddFunctionTable(_block, 1, unchecked((ulong)entry.ToInt64())))
            throw new UnsupportedSignatureException("The native-engine original's dedicated OS unwind table was not registered read-only.");
        _seal = NativePlaybackExecutableSeal.RangeSeal.Capture(_block, bytes.Length, entry, false); Verify();
    }
    internal void Verify()
    {
        _seal.Verify();
        foreach (var offset in new[] { 0, 3, 7, 17 })
        {
            var pc = unchecked((ulong)(_entry.ToInt64() + offset));
            if (RtlLookupFunctionEntry(pc, out var imageBase, IntPtr.Zero) != _block || imageBase != unchecked((ulong)_entry.ToInt64()))
                throw new UnsupportedSignatureException("The native-engine original's actual OS runtime-function owner changed.");
            VerifyBoundary(offset, imageBase);
        }
    }
    private unsafe void VerifyBoundary(int offset, ulong imageBase)
    {
        var storage = stackalloc byte[1264]; var aligned = (byte*)(((nuint)storage + 15) & ~(nuint)15);
        new Span<byte>(aligned, 1232).Clear(); var context = (IntPtr)aligned;
        var stack = stackalloc ulong[64]; new Span<ulong>(stack, 64).Clear();
        var entryRsp = (ulong)(stack + 32);
        const ulong returnPc = 0x1234567812345678;
        const long rbx = 0x3344334433443344;
        stack[32] = returnPc; stack[35] = unchecked((ulong)rbx);
        Marshal.WriteInt32(context, 48, 0x10001f); Marshal.WriteInt32(context, 68, 0x246);
        Marshal.WriteInt64(context, 248, checked(_entry.ToInt64() + offset));
        Marshal.WriteInt64(context, 152, unchecked((long)entryRsp));
        Marshal.WriteInt64(context, 144, offset >= 7 ? 0xbad : rbx);
        foreach (var register in new[] { 160, 168, 176, 216, 224, 232, 240 }) Marshal.WriteInt64(context, register, register * 101L);
        new Span<byte>(aligned + 512, 160).Fill(0x5a);
        RtlVirtualUnwind(0, imageBase, checked(imageBase + (uint)offset), _block, context, out _, out _, IntPtr.Zero);
        if (unchecked((ulong)Marshal.ReadInt64(context, 248)) != returnPc
            || unchecked((ulong)Marshal.ReadInt64(context, 152)) != entryRsp + 8
            || Marshal.ReadInt64(context, 144) != rbx || Marshal.ReadInt32(context, 68) != 0x246)
            throw new UnsupportedSignatureException("The actual native-engine original OS unwind did not restore RIP/RSP/RBX/flags at boundary " + offset + ".");
        foreach (var register in new[] { 160, 168, 176, 216, 224, 232, 240 })
            if (Marshal.ReadInt64(context, register) != register * 101L)
                throw new UnsupportedSignatureException("The native-engine unwind changed an unrelated nonvolatile integer register.");
        foreach (var value in new Span<byte>(aligned + 512, 160))
            if (value != 0x5a) throw new UnsupportedSignatureException("The native-engine unwind changed a nonvolatile XMM register.");
    }
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("ntdll")] private static extern bool RtlAddFunctionTable(IntPtr table, uint count, ulong baseAddress);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
    [DllImport("ntdll")] private static extern IntPtr RtlVirtualUnwind(uint type, ulong imageBase, ulong pc, IntPtr function,
        IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);
}
