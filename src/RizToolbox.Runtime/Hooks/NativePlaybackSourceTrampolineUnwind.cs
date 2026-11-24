using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Actual Windows unwind for the private R11 relocation. It covers
/// every instruction boundary of one closed source prefix and its tail.</summary>
internal sealed class NativePlaybackSourceTrampolineUnwind
{
    private readonly IntPtr _entry, _block;
    private readonly NativePlaybackSourcePrologue _prefix;
    private readonly NativePlaybackExecutableSeal.RangeSeal _seal;
    internal IntPtr InformationAddress => IntPtr.Add(_block, 16);
    private NativePlaybackSourceTrampolineUnwind(IntPtr entry, NativePlaybackSourcePrologue prefix, IntPtr reservedMetadata)
    {
        (_entry, _prefix) = (entry, prefix);
        if (reservedMetadata != IntPtr.Add(entry, 4096))
            throw new UnsupportedSignatureException("The source unwind metadata must use the page reserved with this exact private thunk.");
        _block = reservedMetadata;
        var unwind = prefix.UnwindBytes;
        var bytes = new byte[16 + unwind.Length];
        BitConverter.GetBytes(checked((uint)(prefix.Length + 13))).CopyTo(bytes, 4);
        BitConverter.GetBytes(checked((uint)(_block.ToInt64() + 16 - entry.ToInt64()))).CopyTo(bytes, 8);
        unwind.CopyTo(bytes, 16); Marshal.Copy(bytes, 0, _block, bytes.Length);
        if (!VirtualProtect(_block, 4096, 2, out _) || !RtlAddFunctionTable(_block, 1, unchecked((ulong)entry.ToInt64())))
            throw new UnsupportedSignatureException("The exact source unwind table was not registered read-only.");
        _seal = NativePlaybackExecutableSeal.RangeSeal.Capture(_block, bytes.Length, entry, false); Verify();
    }
    internal static NativePlaybackSourceTrampolineUnwind Register(IntPtr entry, NativePlaybackSourcePrologue prefix, IntPtr reservedMetadata)
        => new(entry, prefix, reservedMetadata);
    internal void Verify()
    {
        _seal.Verify();
        foreach (var offset in _prefix.Boundaries)
        {
            var pc = checked(_entry.ToInt64() + offset);
            if (RtlLookupFunctionEntry(unchecked((ulong)pc), out var imageBase, IntPtr.Zero) != _block || imageBase != unchecked((ulong)_entry.ToInt64()))
                throw new UnsupportedSignatureException("The exact source relocation OS runtime-function owner changed.");
            VerifyBoundary(offset, imageBase);
        }
    }
    private unsafe void VerifyBoundary(int offset, ulong imageBase)
    {
        var contextStorage = stackalloc byte[1264]; var aligned = (byte*)(((nuint)contextStorage + 15) & ~(nuint)15);
        new Span<byte>(aligned, 1232).Clear(); var context = (IntPtr)aligned;
        var stack = stackalloc ulong[64]; new Span<ulong>(stack, 64).Clear();
        var entryRsp = (ulong)(stack + 32);
        const ulong returnPc = 0x1234567812345678;
        const long rbx = 0x3344334433443344, rdi = 0x7788778877887788, rbp = 0x5566556655665566, rsi = 0x6677667766776677;
        stack[32] = returnPc; stack[33] = rbx; stack[35] = rbp;
        var pushed = _prefix.PushRegister != 0 && offset >= (_prefix.SaveRbxBeforePush || _prefix.SaveRbpBeforePushRsi ? 6 : 2);
        stack[31] = unchecked((ulong)(_prefix.PushRegister == 3 ? rbx : _prefix.PushRegister == 6 ? rsi : rdi));
        var allocated = !_prefix.SaveRbxBeforePush && !_prefix.SaveRbpBeforePushRsi && offset >= (_prefix.IsProgress ? 4 : 6);
        var delta = (pushed ? 8UL : 0UL) + (allocated ? checked((ulong)_prefix.Allocation) : 0UL);
        Marshal.WriteInt32(context, 48, 0x10001f); Marshal.WriteInt32(context, 68, 0x246);
        Marshal.WriteInt64(context, 248, checked(_entry.ToInt64() + offset));
        Marshal.WriteInt64(context, 152, unchecked((long)(entryRsp - delta)));
        Marshal.WriteInt64(context, 144, pushed && (_prefix.PushRegister == 3 || _prefix.SaveRbxBeforePush) ? 0xbad : rbx);
        Marshal.WriteInt64(context, 176, pushed && _prefix.PushRegister == 7 ? 0xbad : rdi);
        foreach (var register in new[] { 216, 224, 232, 240 }) Marshal.WriteInt64(context, register, register * 101L);
        Marshal.WriteInt64(context, 160, _prefix.SaveRbpBeforePushRsi && offset >= 6 ? 0xbad : rbp);
        Marshal.WriteInt64(context, 168, _prefix.PushRegister == 6 && pushed ? 0xbad : rsi);
        new Span<byte>(aligned + 512, 160).Fill(0x5a);
        RtlVirtualUnwind(0, imageBase, checked(imageBase + (uint)offset), _block, context, out _, out _, IntPtr.Zero);
        if (unchecked((ulong)Marshal.ReadInt64(context, 248)) != returnPc || unchecked((ulong)Marshal.ReadInt64(context, 152)) != entryRsp + 8
            || Marshal.ReadInt64(context, 144) != rbx || Marshal.ReadInt64(context, 176) != rdi
            || Marshal.ReadInt64(context, 160) != rbp || Marshal.ReadInt64(context, 168) != rsi || Marshal.ReadInt32(context, 68) != 0x246)
            throw new UnsupportedSignatureException("The source unwind did not recover exact RIP/RSP/RBX/RBP/RSI/RDI/flags at instruction boundary " + offset + ".");
        foreach (var register in new[] { 216, 224, 232, 240 })
            if (Marshal.ReadInt64(context, register) != register * 101L)
                throw new UnsupportedSignatureException("The source unwind changed an unrelated nonvolatile integer register.");
        foreach (var value in new Span<byte>(aligned + 512, 160))
            if (value != 0x5a) throw new UnsupportedSignatureException("The source unwind changed a nonvolatile XMM register.");
    }
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("ntdll")] private static extern bool RtlAddFunctionTable(IntPtr table, uint count, ulong baseAddress);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
    [DllImport("ntdll")] private static extern IntPtr RtlVirtualUnwind(uint type, ulong imageBase, ulong pc, IntPtr function,
        IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);
}
