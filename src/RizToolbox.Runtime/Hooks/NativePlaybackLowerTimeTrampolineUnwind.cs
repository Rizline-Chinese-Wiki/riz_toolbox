using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Dedicated OS unwind for MOV [RSP+10h],RBX; PUSH RBP and the
/// private R11 continuation. Two fixed runtime functions cover the complete
/// 19-byte thunk and preserve both partial and completed PUSH contexts.</summary>
internal sealed class NativePlaybackLowerTimeTrampolineUnwind
{
    private readonly IntPtr _entry, _block;
    private readonly NativePlaybackExecutableSeal.RangeSeal _seal;
    internal IntPtr InformationAddress => IntPtr.Add(_block, 32);

    internal NativePlaybackLowerTimeTrampolineUnwind(IntPtr entry, IntPtr metadata)
    {
        if (metadata != IntPtr.Add(entry, 4096))
            throw new UnsupportedSignatureException("Lower-time unwind metadata must share its exact reserved thunk allocation.");
        (_entry, _block) = (entry, metadata);
        var bytes = new byte[60]; BitConverter.GetBytes(6u).CopyTo(bytes, 4);
        BitConverter.GetBytes(0x1020u).CopyTo(bytes, 8);
        BitConverter.GetBytes(6u).CopyTo(bytes, 12); BitConverter.GetBytes(19u).CopyTo(bytes, 16);
        BitConverter.GetBytes(0x1030u).CopyTo(bytes, 20);
        new byte[] { 1, 5, 2, 0, 5, 0x34, 2, 0 }.CopyTo(bytes, 32);
        new byte[] { 1, 0, 3, 0, 0, 0x50, 0, 0x34, 3, 0, 0, 0 }.CopyTo(bytes, 48);
        Marshal.Copy(bytes, 0, _block, bytes.Length);
        if (!VirtualProtect(_block, 4096, 2, out _) || !RtlAddFunctionTable(_block, 2, unchecked((ulong)entry.ToInt64())))
            throw new UnsupportedSignatureException("The lower-time original's dedicated OS unwind table was not registered read-only.");
        _seal = NativePlaybackExecutableSeal.RangeSeal.Capture(_block, bytes.Length, entry, false);
        Verify();
    }

    internal void Verify()
    {
        _seal.Verify();
        foreach (var offset in new[] { 0, 5, 6, 16, 18 })
        {
            var pc = unchecked((ulong)(_entry.ToInt64() + offset));
            var function = IntPtr.Add(_block, offset >= 6 ? 12 : 0);
            if (RtlLookupFunctionEntry(pc, out var imageBase, IntPtr.Zero) != function || imageBase != unchecked((ulong)_entry.ToInt64()))
                throw new UnsupportedSignatureException("The lower-time original's actual OS runtime-function owner changed.");
            VerifyBoundary(offset, imageBase, function);
        }
    }

    private unsafe void VerifyBoundary(int offset, ulong imageBase, IntPtr function)
    {
        var storage = stackalloc byte[1264]; var aligned = (byte*)(((nuint)storage + 15) & ~(nuint)15);
        new Span<byte>(aligned, 1232).Clear(); var context = (IntPtr)aligned;
        var stack = stackalloc ulong[64]; new Span<ulong>(stack, 64).Clear();
        var entryRsp = (ulong)(stack + 32);
        const ulong returnPc = 0x1234567812345678;
        const long rbx = 0x3344334433443344;
        const long rbp = 0x5566556655665566;
        stack[31] = unchecked((ulong)rbp); stack[32] = returnPc; stack[34] = unchecked((ulong)rbx);
        Marshal.WriteInt32(context, 48, 0x10001f); Marshal.WriteInt32(context, 68, 0x246);
        Marshal.WriteInt64(context, 248, checked(_entry.ToInt64() + offset));
        Marshal.WriteInt64(context, 152, unchecked((long)(offset >= 6 ? entryRsp - 8 : entryRsp)));
        Marshal.WriteInt64(context, 144, offset >= 5 ? 0xbad : rbx);
        Marshal.WriteInt64(context, 160, offset >= 6 ? 0xbad : rbp);
        foreach (var register in new[] { 168, 176, 216, 224, 232, 240 }) Marshal.WriteInt64(context, register, register * 101L);
        new Span<byte>(aligned + 512, 160).Fill(0x5a);
        RtlVirtualUnwind(0, imageBase, checked(imageBase + (uint)offset), function, context, out _, out _, IntPtr.Zero);
        if (unchecked((ulong)Marshal.ReadInt64(context, 248)) != returnPc
            || unchecked((ulong)Marshal.ReadInt64(context, 152)) != entryRsp + 8
            || Marshal.ReadInt64(context, 144) != rbx || Marshal.ReadInt64(context, 160) != rbp
            || Marshal.ReadInt32(context, 68) != 0x246)
            throw new UnsupportedSignatureException("The actual lower-time OS unwind did not restore RIP/RSP/RBX/RBP/flags at boundary " + offset + ".");
        foreach (var register in new[] { 168, 176, 216, 224, 232, 240 })
            if (Marshal.ReadInt64(context, register) != register * 101L)
                throw new UnsupportedSignatureException("The lower-time unwind changed an unrelated nonvolatile integer register.");
        foreach (var value in new Span<byte>(aligned + 512, 160))
            if (value != 0x5a) throw new UnsupportedSignatureException("The lower-time unwind changed a nonvolatile XMM register.");
    }
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("ntdll")] private static extern bool RtlAddFunctionTable(IntPtr table, uint count, ulong baseAddress);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
    [DllImport("ntdll")] private static extern IntPtr RtlVirtualUnwind(uint type, ulong imageBase, ulong pc, IntPtr function,
        IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);
}
