using System.Runtime.InteropServices;
using System.Reflection.PortableExecutable;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Mechanical relocation evidence for the finite stock source roles.
/// This object does not admit a target, issue a group lease or prove a stock
/// occurrence. The concrete broker still owns metadata, full-body and image pins.</summary>
internal sealed class NativePlaybackSourceTrampolineProof
{
    private readonly NativePlaybackExecutableSeal.RangeSeal _relocation = null!, _thunk = null!;
    private readonly NativePlaybackProgressPlayerLeafProof? _leaf;
    private readonly NativePlaybackProgressClipLeafProof? _resourceLeaf;
    private readonly NativePlaybackLowerPitchTrampolineProof? _lowerPitch;
    private readonly NativePlaybackEnginePitchPatchPair.MemberReceipt? _nativePairMember;
    private readonly NativePlaybackSourceTrampolineUnwind _unwind = null!;
    private readonly SourceFunctionSeal _sourceFunctions = null!;
    internal IntPtr OriginalAddress { get; }
    internal IntPtr DobbyOriginalAddress { get; }
    internal IntPtr InformationAddress => _nativePairMember?.InformationAddress ?? _lowerPitch?.InformationAddress ?? _leaf?.InformationAddress ?? _resourceLeaf?.InformationAddress ?? _unwind.InformationAddress;
    internal int RelocatedByteCount { get; }
    internal int OriginalCodeLength => RelocatedByteCount + 13;

    private NativePlaybackSourceTrampolineProof(ExactHookBroker.PlaybackSourceOwner owner,
        NativePlaybackEnginePitchPatchPair.MemberReceipt member)
    {
        member.AdoptIntoProof(owner); _nativePairMember = member;
        OriginalAddress = member.OriginalAddress; DobbyOriginalAddress = member.DobbyOriginalAddress;
        RelocatedByteCount = member.RelocatedByteCount;
        member.Verify();
    }
    internal static NativePlaybackSourceTrampolineProof AdoptNativePair(ExactHookBroker.PlaybackSourceOwner owner,
        NativePlaybackEnginePitchPatchPair.MemberReceipt member) => new(owner, member);

    private NativePlaybackSourceTrampolineProof(IntPtr target, NativePlaybackSourceRole role,
        ReadOnlySpan<byte> originalBody, IntPtr dobbyOriginal, IntPtr module, IntPtr leafTail,
        NativePlaybackLowerPitchTarget? lowerPitch)
    {
        if (role == NativePlaybackSourceRole.LowerSetPitch)
        {
            if (lowerPitch is null || lowerPitch.Address != target || lowerPitch.Module != module)
                throw new UnsupportedSignatureException("Only the fixed native-code lower target receipt can own this relocation.");
            _lowerPitch = new(lowerPitch, originalBody, dobbyOriginal);
            OriginalAddress = _lowerPitch.OriginalAddress; DobbyOriginalAddress = dobbyOriginal;
            RelocatedByteCount = NativePlaybackLowerPitchTrampolineProof.PrefixLength; return;
        }
        if (lowerPitch is not null) throw new UnsupportedSignatureException("A native lower receipt cannot replace a MethodInfo source role.");
        if (role == NativePlaybackSourceRole.ProgressMusicPlayer)
        {
            _leaf = new(target, originalBody, dobbyOriginal, module, leafTail);
            OriginalAddress = _leaf.OriginalAddress; DobbyOriginalAddress = dobbyOriginal; RelocatedByteCount = 8; return;
        }
        if (role == NativePlaybackSourceRole.ProgressClip)
        {
            _resourceLeaf = new(target, originalBody, dobbyOriginal, module, leafTail);
            OriginalAddress = _resourceLeaf.OriginalAddress; DobbyOriginalAddress = dobbyOriginal; RelocatedByteCount = 8; return;
        }
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || target == IntPtr.Zero || dobbyOriginal == IntPtr.Zero
            || target == dobbyOriginal)
            throw new UnsupportedSignatureException("Source relocation requires distinct actual Windows x64 target and Dobby original.");
        var prefix = NativePlaybackSourcePrologue.ForRole(role);
        prefix.RequirePrefix(originalBody);
        RelocatedByteCount = prefix.Length; DobbyOriginalAddress = dobbyOriginal;
        HookTargetValidation.VerifyReadable(target, (nuint)prefix.Length);
        if (!NativePlaybackExecutableSeal.RangeSeal.Read(target, prefix.Length).AsSpan().SequenceEqual(originalBody[..prefix.Length]))
            throw new UnsupportedSignatureException("The source changed before its actual relocation could be proved.");
        _sourceFunctions = new(target, originalBody.Length, prefix.Length, role);
        _relocation = NativePlaybackExecutableSeal.RangeSeal.Capture(dobbyOriginal, prefix.Length + 14, IntPtr.Zero, false, true);
        var actual = _relocation.Bytes;
        if (!actual.AsSpan(0, prefix.Length).SequenceEqual(originalBody[..prefix.Length])
            || !actual.AsSpan(prefix.Length, 6).SequenceEqual(new byte[] { 0xff, 0x25, 0, 0, 0, 0 })
            || BitConverter.ToInt64(actual, prefix.Length + 6) != checked(target.ToInt64() + prefix.Length))
            throw new UnsupportedSignatureException("The actual Dobby source original is not the exact complete prefix plus its target continuation.");

        // FF25 can be classified as an epilogue while this prefix still owns a
        // stack frame. A volatile R11 MOV/JMP tail has explicit unwind metadata
        // and preserves CMP flags, all argument registers and nonvolatile state.
        var code = new byte[OriginalCodeLength]; originalBody[..prefix.Length].CopyTo(code);
        code[prefix.Length] = 0x49; code[prefix.Length + 1] = 0xbb;
        BitConverter.GetBytes(checked(target.ToInt64() + prefix.Length)).CopyTo(code, prefix.Length + 2);
        code[prefix.Length + 10] = 0x41; code[prefix.Length + 11] = 0xff; code[prefix.Length + 12] = 0xe3;
        // Reserve the metadata page with its code. Searching a separate 2GB
        // address window can fail under a valid ASLR layout or reserved arena.
        OriginalAddress = VirtualAlloc(IntPtr.Zero, 8192, 0x3000, 4);
        if (OriginalAddress == IntPtr.Zero) throw new UnsupportedSignatureException("The exact source original thunk allocation failed.");
        Marshal.Copy(code, 0, OriginalAddress, code.Length);
        if (!VirtualProtect(OriginalAddress, 4096, 0x20, out _) || !FlushInstructionCache((IntPtr)(-1), OriginalAddress, (nuint)code.Length))
            throw new UnsupportedSignatureException("The exact source original thunk was not sealed RX and flushed.");
        _thunk = NativePlaybackExecutableSeal.RangeSeal.Capture(OriginalAddress, code.Length, OriginalAddress, false, true);
        _unwind = NativePlaybackSourceTrampolineUnwind.Register(OriginalAddress, prefix, IntPtr.Add(OriginalAddress, 4096));
        Verify();
    }
    internal static NativePlaybackSourceTrampolineProof Create(IntPtr target, NativePlaybackSourceRole role,
        ReadOnlySpan<byte> originalBody, IntPtr dobbyOriginal, IntPtr module, IntPtr leafTail,
        NativePlaybackLowerPitchTarget? lowerPitch = null)
        => new(target, role, originalBody, dobbyOriginal, module, leafTail, lowerPitch);
    internal void Verify()
        => Verify(null);
    internal void Verify(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal)
    {
        if (_nativePairMember is not null) { _nativePairMember.Verify(traversal); return; }
        if (_lowerPitch is not null) { _lowerPitch.Verify(traversal); return; }
        if (_leaf is not null) { _leaf.Verify(); return; }
        if (_resourceLeaf is not null) { _resourceLeaf.Verify(); return; }
        _relocation.Verify(); _thunk.Verify(); _sourceFunctions.Verify(); _unwind.Verify();
    }
    internal void VerifyOperation(ExactHookBroker.PlaybackSourceOwner owner)
    {
        if (_nativePairMember is not null) { _nativePairMember.VerifyOperation(owner); return; }
        if (_lowerPitch is not null) { _lowerPitch.VerifyOperation(); return; }
        if (_leaf is not null) { _leaf.Verify(); return; }
        if (_resourceLeaf is not null) { _resourceLeaf.Verify(); return; }
        _relocation.Verify(); _thunk.Verify(); _sourceFunctions.Verify(); _unwind.Verify();
    }

    /// <summary>Retain the actual SEC_IMAGE RF/xdata and lookup identity for
    /// the source entry and continuation. A foreign dynamic table cannot become
    /// valid merely because it describes some stack frame.</summary>
    internal sealed class SourceFunctionSeal
    {
        private readonly IntPtr _continuation;
        private readonly ulong _imageBase;
        private readonly FileStream _file;
        private readonly PEReader _reader;
        private readonly List<NativePlaybackExecutableSeal.RangeSeal> _ranges = new();
        private readonly List<(IntPtr Function, uint Begin, uint End)> _functions = new();
        internal SourceFunctionSeal(IntPtr target, int bodyLength, int prefixLength, NativePlaybackSourceRole? role = null)
        {
            _continuation = IntPtr.Add(target, prefixLength);
            var first = RtlLookupFunctionEntry(unchecked((ulong)target.ToInt64()), out _imageBase, IntPtr.Zero);
            if (first == IntPtr.Zero || _imageBase == 0 || _imageBase > long.MaxValue || bodyLength < prefixLength || bodyLength > 65536)
                throw new UnsupportedSignatureException("The actual source entry has no image-owned Windows runtime function.");
            var module = (IntPtr)checked((long)_imageBase);
            HookTargetValidation.VerifyExecutable(target, module);
            if (RtlLookupFunctionEntry(unchecked((ulong)_continuation.ToInt64()), out var nextBase, IntPtr.Zero) != first || nextBase != _imageBase)
                throw new UnsupportedSignatureException("The source continuation does not retain its original first runtime function.");
            _file = new FileStream(NativePlaybackExecutableSeal.ModulePath(module), FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new PEReader(_file, PEStreamOptions.LeaveOpen);
            var header = _reader.PEHeaders.PEHeader;
            if (header is null || header.Magic != PEMagic.PE32Plus || _reader.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                throw new UnsupportedSignatureException("Source runtime-function evidence requires a pinned AMD64 PE file.");
            var bodyRva = checked((uint)(target.ToInt64() - module.ToInt64()));
            var bodyEnd = checked(bodyRva + (uint)bodyLength);
            if (bodyEnd > header.SizeOfImage)
                throw new UnsupportedSignatureException("The complete source body leaves its pinned image.");
            var directory = header.ExceptionTableDirectory;
            if (directory.RelativeVirtualAddress <= 0 || directory.Size <= 0 || directory.Size % 12 != 0)
                throw new UnsupportedSignatureException("The source image has no complete file-backed exception table.");
            // Read the bounded table from the retained PEReader. Each source still
            // checks every row, without materializing a separate table copy.
            System.Reflection.Metadata.BlobReader table;
            try { table = _reader.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, directory.Size); }
            catch (Exception error) when (error is ArgumentException or BadImageFormatException or OverflowException)
            { throw new UnsupportedSignatureException("Source runtime-function metadata leaves its pinned file section."); }
            var seenUnwind = new HashSet<uint>();
            uint previousEnd = 0;
            for (var at = 0; at < table.Length; at += 12)
            {
                var begin = table.ReadUInt32();
                var end = table.ReadUInt32();
                var unwind = table.ReadUInt32();
                if (begin >= end || begin < previousEnd || end > header.SizeOfImage)
                    throw new UnsupportedSignatureException("The file-backed exception table is reversed, overlapping or outside its image.");
                previousEnd = end;
                if (begin >= bodyEnd || end <= bodyRva) continue;
                if (begin < bodyRva || end > bodyEnd)
                    throw new UnsupportedSignatureException("A source runtime function crosses its complete pinned body.");
                var function = IntPtr.Add(module, checked(directory.RelativeVirtualAddress + at));
                CaptureCanonical(checked((uint)(directory.RelativeVirtualAddress + at)), 12, module);
                _functions.Add((function, begin, end));
                CaptureUnwind(unwind, module, seenUnwind);
            }
            if (_functions.Count == 0 || _functions[0].Begin != bodyRva || _functions[0].End <= bodyRva + prefixLength || _functions[0].Function != first)
                throw new UnsupportedSignatureException("The actual source entry differs from its complete file-backed runtime-function list.");
            if (role == NativePlaybackSourceRole.PlayerConstructor && bodyRva == 0x36c3e0)
            {
                // The shared stock body has a 49-byte runtime function inside
                // its canonical 64-byte code pin. Keep the exact original RF
                // and UW identity in addition to the generic file/OS seal.
                var rfRva = checked((uint)(first.ToInt64() - module.ToInt64()));
                var rf = FileBytes(rfRva, 12);
                var uw = FileBytes(0x1ce4d20, 12);
                if (_functions.Count != 1 || BitConverter.ToUInt32(rf, 0) != 0x36c3e0
                    || BitConverter.ToUInt32(rf, 4) != 0x36c411
                    || BitConverter.ToUInt32(rf, 8) != 0x1ce4d20
                    || !uw.AsSpan().SequenceEqual(new byte[] { 1, 10, 4, 0, 10, 0x34, 6, 0, 10, 0x32, 6, 0x70 }))
                    throw new UnsupportedSignatureException("The shared constructor original runtime-function/unwind pin changed.");
            }
            Verify();
        }
        private byte[] FileBytes(uint rva, int length)
        {
            try { return _reader.GetSectionData(checked((int)rva)).GetContent(0, length).ToArray(); }
            catch (Exception error) when (error is ArgumentException or BadImageFormatException or OverflowException)
            { throw new UnsupportedSignatureException("Source runtime-function metadata leaves its pinned file section."); }
        }
        private byte[] CaptureCanonical(uint rva, int length, IntPtr module)
        {
            var expected = FileBytes(rva, length);
            var seal = NativePlaybackExecutableSeal.RangeSeal.Capture(IntPtr.Add(module, checked((int)rva)), length, module, true);
            // RUNTIME_FUNCTION and UNWIND_INFO fields are relative RVAs or
            // packed opcodes, not absolute addresses needing ASLR rebasing.
            if (!seal.Bytes.AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("Source RF/xdata was changed before capture and differs from its pinned file.");
            _ranges.Add(seal); return expected;
        }
        private void CaptureUnwind(uint unwind, IntPtr module, HashSet<uint> captured)
        {
            var chain = new HashSet<uint>();
            for (var depth = 0; ; ++depth)
            {
                if (depth >= 16 || unwind == 0 || !chain.Add(unwind))
                    throw new UnsupportedSignatureException("The file-backed source unwind chain is absent, cyclic or unbounded.");
                if (captured.Contains(unwind)) return;
                var header = FileBytes(unwind, 4); var version = header[0] & 7; var flags = header[0] >> 3;
                if (version != 1 || flags > 4 || (flags & 4) != 0 && (flags & 3) != 0)
                    throw new UnsupportedSignatureException("The pinned source unwind header has an unsupported exact form.");
                var length = checked(4 + ((header[2] * 2 + 3) & ~3) + ((flags & 4) != 0 ? 12 : (flags & 3) != 0 ? 4 : 0));
                var data = CaptureCanonical(unwind, length, module); captured.Add(unwind);
                if ((flags & 4) == 0) return;
                var tail = length - 12;
                if (BitConverter.ToUInt32(data, tail) >= BitConverter.ToUInt32(data, tail + 4))
                    throw new UnsupportedSignatureException("The pinned source chained runtime function is empty or reversed.");
                unwind = BitConverter.ToUInt32(data, tail + 8);
            }
        }
        internal void Verify()
        {
            foreach (var range in _ranges) range.Verify();
            foreach (var row in _functions)
                foreach (var pc in new[] { _imageBase + row.Begin, _imageBase + row.End - 1 })
                    if (RtlLookupFunctionEntry(pc, out var imageBase, IntPtr.Zero) != row.Function || imageBase != _imageBase)
                        throw new UnsupportedSignatureException("A source OS runtime-function lookup differs from its exact file-backed entry.");
            if (RtlLookupFunctionEntry(unchecked((ulong)_continuation.ToInt64()), out var continuationBase, IntPtr.Zero) != _functions[0].Function
                || continuationBase != _imageBase)
                throw new UnsupportedSignatureException("The source continuation OS runtime-function owner changed.");
        }
    }
    [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocationType, uint protection);
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint size);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
}

/// <summary>Closed instruction shapes, chosen by the verified physical role.
/// No caller can supply unwind opcodes, a branch target or an arbitrary prefix.</summary>
internal sealed class NativePlaybackSourcePrologue
{
    private readonly byte[] _prefix;
    internal int Length => _prefix.Length;
    internal int Allocation { get; }
    internal int PushRegister { get; }
    internal bool SaveRbxBeforePush { get; }
    internal bool SaveRbpBeforePushRsi { get; }
    internal bool IsProgress { get; }
    internal int[] Boundaries => SaveRbpBeforePushRsi || SaveRbxBeforePush ? new[] { 0, 5, 6, 16 }
        : IsProgress ? new[] { 0, 4, 7, 17 } : new[] { 0, 2, 6, 16 };
    internal byte[] UnwindBytes => SaveRbpBeforePushRsi ? new byte[] { 1, 6, 3, 0, 6, 0x54, 4, 0, 6, 0x60, 0, 0 }
        : SaveRbxBeforePush ? new byte[] { 1, 6, 3, 0, 6, 0x34, 2, 0, 6, 0x70, 0, 0 }
        : IsProgress ? new byte[] { 1, 4, 1, 0, 4, 0x42, 0, 0 }
        : new byte[] { 1, 6, 2, 0, 6, checked((byte)(((Allocation / 8 - 1) << 4) | 2)), 2, checked((byte)(PushRegister << 4)) };
    private NativePlaybackSourcePrologue(byte[] prefix, int allocation, int pushRegister, bool saveRbx = false, bool saveRbpRsi = false, bool progress = false)
        => (_prefix, Allocation, PushRegister, SaveRbxBeforePush, SaveRbpBeforePushRsi, IsProgress)
            = (prefix, allocation, pushRegister, saveRbx, saveRbpRsi, progress);
    internal static NativePlaybackSourcePrologue ForRole(NativePlaybackSourceRole role) => role switch
    {
        NativePlaybackSourceRole.SetPitch or NativePlaybackSourceRole.SetTime or NativePlaybackSourceRole.PlayScheduled or NativePlaybackSourceRole.AudioPlayDelayed
            => new(new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x30 }, 0x30, 3),
        NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.PauseLeaf or NativePlaybackSourceRole.AudioPlay or NativePlaybackSourceRole.AudioStop
            => new(new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x20 }, 0x20, 3),
        NativePlaybackSourceRole.Replay
            => new(new byte[] { 0x40, 0x53, 0x48, 0x83, 0xec, 0x40 }, 0x40, 3),
        NativePlaybackSourceRole.Resume
            => new(new byte[] { 0x40, 0x57, 0x48, 0x83, 0xec, 0x50 }, 0x50, 7),
        NativePlaybackSourceRole.SetClip or NativePlaybackSourceRole.PlayerConstructor or NativePlaybackSourceRole.AudioPlayHelper
            or NativePlaybackSourceRole.AudioStopBoolean or NativePlaybackSourceRole.AudioSetLoop
            => new(new byte[] { 0x48, 0x89, 0x5c, 0x24, 0x08, 0x57 }, 0, 7, true),
        NativePlaybackSourceRole.ProgressPlay
            => new(new byte[] { 0x48, 0x83, 0xec, 0x28, 0x38, 0x51, 0x42 }, 0x28, 0, progress: true),
        NativePlaybackSourceRole.ProgressStartMoveNext
            => new(new byte[] { 0x48, 0x89, 0x6c, 0x24, 0x18, 0x56 }, 0, 6, saveRbpRsi: true),
        _ => throw new UnsupportedSignatureException("This physical source role has no exact proved relocation shape.")
    };
    internal void RequirePrefix(ReadOnlySpan<byte> body)
    {
        if (body.Length < _prefix.Length || !body[.._prefix.Length].SequenceEqual(_prefix))
            throw new UnsupportedSignatureException("The actual source prefix differs from its role-specific complete instruction shape.");
    }
}
