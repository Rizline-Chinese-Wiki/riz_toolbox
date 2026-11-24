using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Only the pinned ProgressControl.set_Clip leaf: MOV [RCX+30],RDX;
/// ADD RCX,30; E9 exact tail. The tail's semantic name/ABI is deliberately unknown.
/// The broker supplies its module and issuer-owned tail; callers cannot opt other
/// source roles out of their existing image RF/xdata requirements.</summary>
internal sealed class NativePlaybackProgressClipLeafProof
{
    private readonly SourceLeafSeal _source;
    private readonly NativePlaybackExecutableSeal.RangeSeal _relocation, _thunk, _unwind;
    private readonly IntPtr _table;
    internal IntPtr OriginalAddress { get; }
    internal IntPtr InformationAddress => IntPtr.Add(_table, 16);
    internal NativePlaybackProgressClipLeafProof(IntPtr target, ReadOnlySpan<byte> body,
        IntPtr dobbyOriginal, IntPtr module, IntPtr tail)
    {
        _source = new(target, body, module, tail);
        _relocation = NativePlaybackExecutableSeal.RangeSeal.Capture(dobbyOriginal, 22, IntPtr.Zero, false, true);
        var actual = _relocation.Bytes;
        if (!actual.AsSpan(0, 8).SequenceEqual(body[..8])
            || !actual.AsSpan(8, 6).SequenceEqual(new byte[] { 0xff, 0x25, 0, 0, 0, 0 })
            || BitConverter.ToInt64(actual, 14) != checked(target.ToInt64() + 8))
            throw new UnsupportedSignatureException("The Progress resource Dobby original is not its exact eight-byte leaf prefix and target+8 continuation.");
        var code = new byte[21]; body[..8].CopyTo(code);
        code[8] = 0x49; code[9] = 0xbb;
        BitConverter.GetBytes(checked(target.ToInt64() + 8)).CopyTo(code, 10);
        code[18] = 0x41; code[19] = 0xff; code[20] = 0xe3;
        OriginalAddress = VirtualAlloc(IntPtr.Zero, 8192, 0x3000, 4);
        if (OriginalAddress == IntPtr.Zero) throw new UnsupportedSignatureException("The exact Progress resource leaf thunk allocation failed.");
        Marshal.Copy(code, 0, OriginalAddress, code.Length);
        if (!VirtualProtect(OriginalAddress, 4096, 0x20, out _) || !FlushInstructionCache((IntPtr)(-1), OriginalAddress, 21))
            throw new UnsupportedSignatureException("The exact Progress resource leaf thunk was not sealed RX and flushed.");
        _thunk = NativePlaybackExecutableSeal.RangeSeal.Capture(OriginalAddress, 21, OriginalAddress, false, true);
        _table = IntPtr.Add(OriginalAddress, 4096);
        // Empty unwind: this exact prefix changes no RSP/nonvolatile state.
        // It is not the PUSH/ALLOC evidence of the other stock roles.
        var metadata = new byte[20];
        BitConverter.GetBytes(21u).CopyTo(metadata, 4);
        BitConverter.GetBytes(4112u).CopyTo(metadata, 8); metadata[16] = 1;
        Marshal.Copy(metadata, 0, _table, metadata.Length);
        if (!VirtualProtect(_table, 4096, 2, out _) || !RtlAddFunctionTable(_table, 1, unchecked((ulong)OriginalAddress.ToInt64())))
            throw new UnsupportedSignatureException("The exact empty Progress resource leaf unwind was not registered read-only.");
        _unwind = NativePlaybackExecutableSeal.RangeSeal.Capture(_table, metadata.Length, OriginalAddress, false);
        Verify();
    }
    internal void Verify()
    {
        _source.Verify(); _relocation.Verify(); _thunk.Verify(); _unwind.Verify();
        foreach (var boundary in new[] { 0, 4, 8, 18 }) VerifyBoundary(boundary);
    }
    private unsafe void VerifyBoundary(int offset)
    {
        var pc = checked(OriginalAddress.ToInt64() + offset);
        if (RtlLookupFunctionEntry(unchecked((ulong)pc), out var imageBase, IntPtr.Zero) != _table
            || imageBase != unchecked((ulong)OriginalAddress.ToInt64()))
            throw new UnsupportedSignatureException("The exact Progress leaf thunk lost its empty unwind lookup identity.");
        var storage = stackalloc byte[1264]; var contextBytes = (byte*)(((nuint)storage + 15) & ~(nuint)15);
        new Span<byte>(contextBytes, 1232).Clear(); var context = (IntPtr)contextBytes;
        var stack = stackalloc ulong[8]; new Span<ulong>(stack, 8).Clear();
        const ulong returnPc = 0x1234567812345678;
        stack[4] = returnPc; var rsp = (ulong)(stack + 4);
        Marshal.WriteInt32(context, 48, 0x10001f); Marshal.WriteInt32(context, 68, 0x246);
        Marshal.WriteInt64(context, 152, unchecked((long)rsp)); Marshal.WriteInt64(context, 248, pc);
        foreach (var register in new[] { 144, 160, 168, 176, 216, 224, 232, 240 })
            Marshal.WriteInt64(context, register, register * 101L);
        new Span<byte>(contextBytes + 512, 160).Fill(0x5a);
        RtlVirtualUnwind(0, imageBase, unchecked((ulong)pc), _table, context, out _, out _, IntPtr.Zero);
        if (unchecked((ulong)Marshal.ReadInt64(context, 248)) != returnPc
            || unchecked((ulong)Marshal.ReadInt64(context, 152)) != rsp + 8 || Marshal.ReadInt32(context, 68) != 0x246)
            throw new UnsupportedSignatureException("The exact Progress leaf unwind changed RIP/RSP/flags at boundary " + offset);
        foreach (var register in new[] { 144, 160, 168, 176, 216, 224, 232, 240 })
            if (Marshal.ReadInt64(context, register) != register * 101L)
                throw new UnsupportedSignatureException("The exact Progress leaf unwind changed a nonvolatile register.");
        foreach (var value in new Span<byte>(contextBytes + 512, 160))
            if (value != 0x5a) throw new UnsupportedSignatureException("The exact Progress leaf unwind changed a nonvolatile XMM register.");
    }

    internal sealed class SourceLeafSeal
    {
        private static readonly byte[] Prefix = { 0x48, 0x89, 0x51, 0x30, 0x48, 0x83, 0xc1, 0x30 };
        private readonly IntPtr _target, _module, _tail;
        private readonly NativePlaybackExecutableSeal.RangeSeal _table;
        private readonly byte[] _branch;
        private readonly FileStream _file;
        private readonly PEReader _reader;
        internal SourceLeafSeal(IntPtr target, ReadOnlySpan<byte> body, IntPtr module, IntPtr tail)
        {
            (_target, _module, _tail) = (target, module, tail);
            if (body.Length != 13 || !body[..8].SequenceEqual(Prefix) || body[8] != 0xe9
                || checked(target.ToInt64() + 13 + BitConverter.ToInt32(body[9..13])) != tail.ToInt64())
                throw new UnsupportedSignatureException("Progress resource leaf requires exactly MOV field30, ADD30 and its issuer-owned E9 tail.");
            HookTargetValidation.VerifyExecutable(target, module); HookTargetValidation.VerifyExecutable(tail, module);
            _branch = body[8..].ToArray();
            _file = new(NativePlaybackExecutableSeal.ModulePath(module), FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new(_file, PEStreamOptions.LeaveOpen);
            var header = _reader.PEHeaders.PEHeader;
            if (header is null || header.Magic != PEMagic.PE32Plus || _reader.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                throw new UnsupportedSignatureException("Progress resource leaf evidence requires an AMD64 image.");
            var rva = checked((uint)(target.ToInt64() - module.ToInt64()));
            if ((ulong)rva + 13 > (ulong)header.SizeOfImage
                || !_reader.GetSectionData(checked((int)rva)).GetContent(0, 13).AsSpan().SequenceEqual(body))
                throw new UnsupportedSignatureException("The complete Progress resource leaf differs from its exact pinned file body.");
            var directory = header.ExceptionTableDirectory;
            if (directory.RelativeVirtualAddress <= 0 || directory.Size <= 0 || directory.Size % 12 != 0)
                throw new UnsupportedSignatureException("The Progress leaf image has no complete file-backed RF table to exclude coverage.");
            var table = _reader.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, directory.Size).ToArray();
            uint previousEnd = 0;
            for (var at = 0; at < table.Length; at += 12)
            {
                var begin = BitConverter.ToUInt32(table, at); var end = BitConverter.ToUInt32(table, at + 4);
                if (begin >= end || begin < previousEnd || end > header.SizeOfImage || begin < rva + 13 && end > rva)
                    throw new UnsupportedSignatureException("The exact Progress resource leaf is covered by an RF, or its file RF table is malformed.");
                previousEnd = end;
            }
            _table = NativePlaybackExecutableSeal.RangeSeal.Capture(IntPtr.Add(module, directory.RelativeVirtualAddress), directory.Size, module, true);
            if (!_table.Bytes.AsSpan().SequenceEqual(table))
                throw new UnsupportedSignatureException("Progress resource RF exclusion changed before its file-backed capture.");
            Verify();
        }
        internal void Verify()
        {
            _table.Verify(); HookTargetValidation.VerifyExecutable(_target, _module); HookTargetValidation.VerifyExecutable(_tail, _module);
            if (!NativePlaybackExecutableSeal.RangeSeal.Read(IntPtr.Add(_target, 8), 5).AsSpan().SequenceEqual(_branch))
                throw new UnsupportedSignatureException("The exact Progress resource E9 continuation changed.");
            // The native leaf has no stack frame or image/dynamic runtime-function
            // entry at any byte. No other role is allowed through this path.
            for (var at = 0; at < 13; ++at)
                if (RtlLookupFunctionEntry(unchecked((ulong)(_target.ToInt64() + at)), out _, IntPtr.Zero) != IntPtr.Zero)
                    throw new UnsupportedSignatureException("An unexpected runtime function now covers the exact Progress resource leaf.");
        }
    }
    [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint bytes, uint allocation, uint protection);
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint bytes, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint bytes);
    [DllImport("ntdll")] private static extern bool RtlAddFunctionTable(IntPtr table, uint count, ulong imageBase);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
    [DllImport("ntdll")] private static extern IntPtr RtlVirtualUnwind(uint type, ulong imageBase, ulong pc, IntPtr function,
        IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);
}
