using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Concrete retained SEC_IMAGE identity. The file stays write/delete
/// denied; every immutable mapped section and its exact page identity are read
/// again. Private playback members and fixed V5 windows require their concrete,
/// independently verified retained owners.</summary>
internal sealed partial class NativePlaybackExecutableSeal
{
    [ThreadStatic] private static long _threadFullImageAudits;
    // Counts actual VerifyCore entries for every playback game/helper image
    // on this thread. This is observational and grants no installation proof.
    internal static long ReadThreadFullImageAudits() => _threadFullImageAudits;
    private readonly FileStream _file;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly List<RangeSeal> _ranges = new();
    private readonly List<MemoryFact> _groupExecutableMemory = new();
    private readonly ExactHookBroker.PlaybackSourceInstallGroup? _group;
    private readonly NativePlaybackEnginePitchPatchPair? _pitchPair;
    private readonly NativeEngineTransportFirstInstall? _transport;
    private readonly List<(uint Rva, byte[] Bytes)> _executableFileRanges = new();
    private readonly ExpectedSectionBufferPool _expectedBuffers = new();
    internal string FileSha256 { get; }
    internal IntPtr Module => _module;
    private NativePlaybackExecutableSeal(IntPtr module, string expectedSha256, IntPtr source, int sourceLength,
        ExactHookBroker.PlaybackSourceInstallGroup? group = null, NativePlaybackEnginePitchPatchPair? pitchPair = null)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || module == IntPtr.Zero)
            throw new UnsupportedSignatureException("Playback image authority requires Windows x64 SEC_IMAGE memory.");
        if (group is not null && pitchPair is not null)
            throw new UnsupportedSignatureException("A complete playback group and its native patch pair require separate retained issuers.");
        _module = module; _path = ModulePath(module); _group = group; _pitchPair = pitchPair;
        group?.VerifySealIssuer(module);
        pitchPair?.VerifySealIssuer(module);
        _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using (var sha = SHA256.Create()) FileSha256 = Convert.ToHexString(sha.ComputeHash(_file)).ToLowerInvariant();
        if (!FileSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("The playback image differs from its required file fingerprint.");
        _transport = NativeEngineTransportFirstInstall.RetainPlaybackImage(module, FileSha256);
        _file.Position = 0;
        using var reader = new BinaryReader(_file, Encoding.UTF8, true);
        if (reader.ReadUInt16() != 0x5a4d) throw new UnsupportedSignatureException("Playback image is not a PE image.");
        _file.Position = 0x3c; var pe = reader.ReadInt32();
        if (pe < 64 || pe > _file.Length - 264) throw new UnsupportedSignatureException("Playback PE header is outside its pinned file.");
        _file.Position = pe;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664)
            throw new UnsupportedSignatureException("Playback image requires an actual AMD64 PE.");
        var sections = reader.ReadUInt16(); _file.Position = pe + 20; var optionalSize = reader.ReadUInt16();
        _file.Position = pe + 24;
        if (reader.ReadUInt16() != 0x20b || sections is 0 or > 96 || optionalSize < 112)
            throw new UnsupportedSignatureException("Playback image optional header or sections differ.");
        _file.Position = pe + 24 + 56; var imageSize = reader.ReadUInt32(); var headerSize = reader.ReadUInt32();
        if (imageSize == 0 || imageSize > int.MaxValue || headerSize < pe + 24 + optionalSize + sections * 40 || headerSize > imageSize)
            throw new UnsupportedSignatureException("Playback image has an invalid bounded mapped size.");
        _ranges.Add(RangeSeal.Capture(module, checked((int)headerSize), module, true));
        // Independently compare the mapped immutable headers with the pinned file.
        _file.Position = 0; var diskHeaders = reader.ReadBytes(checked((int)headerSize));
        // Windows can rewrite only this header field to the actual ASLR base,
        // including DONT_RESOLVE_DLL_REFERENCES mappings. Canonicalize that
        // one known loader edit, then seal its actual value for later reads.
        var imageBaseOffset = pe + 24 + 24;
        var preferredBase = BitConverter.ToInt64(diskHeaders, imageBaseOffset);
        var mappedBase = BitConverter.ToInt64(_ranges[0].Bytes, imageBaseOffset);
        if (mappedBase != preferredBase && mappedBase != module.ToInt64())
            throw new UnsupportedSignatureException("Playback mapped PE ImageBase is neither its preferred nor actual allocation base.");
        BitConverter.GetBytes(mappedBase).CopyTo(diskHeaders, imageBaseOffset);
        if (!diskHeaders.AsSpan().SequenceEqual(_ranges[0].Bytes))
            throw new UnsupportedSignatureException("Playback mapped PE headers differ from the pinned file.");
        var sourceOffset = source == IntPtr.Zero ? -1L : source.ToInt64() - module.ToInt64();
        if (sourceOffset >= 0 && (sourceLength <= 0 || sourceOffset > imageSize - sourceLength))
            throw new UnsupportedSignatureException("Playback source is outside its exact mapped image.");
        var bodies = group?.ImageBodies(module) ?? pitchPair?.ImageBodies()
            ?? (sourceOffset < 0 ? Array.Empty<(IntPtr Address, int Length)>() : new[] { (source, sourceLength) });
        var intervals = bodies.Select(value => (Start: value.Address.ToInt64() - module.ToInt64(), value.Length))
            .OrderBy(value => value.Start).ToArray();
        for (var index = 0; index < intervals.Length; ++index)
            if (intervals[index].Start < 0 || intervals[index].Length <= 0 || intervals[index].Start > imageSize - intervals[index].Length
                || index > 0 && intervals[index - 1].Start + intervals[index - 1].Length > intervals[index].Start)
                throw new UnsupportedSignatureException("Private playback member ranges overlap or leave the pinned image.");
        _transport?.VerifyPlaybackMembersDisjoint(module, FileSha256, bodies);
        var coveredMembers = 0;
        for (var index = 0; index < sections; ++index)
        {
            _file.Position = pe + 24 + optionalSize + index * 40 + 8;
            var size = reader.ReadUInt32(); var rva = reader.ReadUInt32();
            _file.Position += 20; var flags = reader.ReadUInt32();
            if (size == 0) continue;
            if (rva > imageSize || size > imageSize - rva) throw new UnsupportedSignatureException("Playback mapped section exceeds its image.");
            if ((flags & 0x20000000) == 0 && ((flags & 0x40000000) == 0 || (flags & 0x80000000) != 0)) continue;
            var canonicalGroupExecutable = (_group is not null || _pitchPair is not null || _transport is not null) && (flags & 0x20000000) != 0;
            var end = (long)rva + size;
            var cursor = (long)rva;
            foreach (var member in intervals.Where(value => value.Start >= rva && value.Start < end))
            {
                if ((flags & 0x20000000) == 0 || member.Length > end - member.Start)
                    throw new UnsupportedSignatureException("Playback member crosses an executable section.");
                Add(checked((uint)cursor), checked((int)(member.Start - cursor)), canonicalGroupExecutable);
                cursor = member.Start + member.Length; ++coveredMembers;
            }
            Add(checked((uint)cursor), checked((int)(end - cursor)), canonicalGroupExecutable);
        }
        if (coveredMembers != intervals.Length) throw new UnsupportedSignatureException("A private playback member is absent from executable sections.");
        VerifyExecutableFile(reader, pe, optionalSize, sections, imageSize);
        // A loader reference is retained independently of the caller's handle.
        if (!GetModuleHandleEx(4, module, out var held) || held != module)
            throw new UnsupportedSignatureException("Playback SEC_IMAGE cannot retain its actual loader owner.");
        Verify();

        void Add(uint offset, int count, bool canonicalGroupExecutable)
        {
            if (count <= 0) return;
            var address = IntPtr.Add(module, checked((int)offset));
            // Group, pair and V5 images retain and recompose every executable section
            // from the pinned file plus DIR64 on every Verify. Keep the original
            // page identity here; that complete comparison owns all byte checks.
            if (canonicalGroupExecutable)
                _groupExecutableMemory.AddRange(MemoryFact.Read(address, count, module, true, false));
            else _ranges.Add(RangeSeal.Capture(address, count, module, true));
        }
    }
    internal static NativePlaybackExecutableSeal Capture(IntPtr module, string expectedSha256, IntPtr source = default, int sourceLength = 0)
        => new(module, expectedSha256, source, sourceLength);
    internal static NativePlaybackExecutableSeal CaptureGroup(ExactHookBroker.PlaybackSourceInstallGroup group)
        => new(group.Module, group.ImageSha256, IntPtr.Zero, 0, group);
    internal static NativePlaybackExecutableSeal CaptureLowerGroup(ExactHookBroker.PlaybackSourceInstallGroup group)
        => group.HasLowerPitch ? new(group.LowerModule, group.LowerImageSha256, IntPtr.Zero, 0, group)
            : throw new UnsupportedSignatureException("Only the fixed dual-image group can issue its lower image seal.");
    internal static NativePlaybackExecutableSeal CaptureEnginePitchPair(NativePlaybackEnginePitchPatchPair pair)
        => new(pair.Module, pair.ImageSha256, IntPtr.Zero, 0, pitchPair: pair);
    internal void Verify()
    {
        if (_group?.TryVerifyInstallationImage(this) == true) return;
        VerifyCore(null);
    }
    internal void VerifyForRetiredGroup(ExactHookBroker.PlaybackSourceInstallGroup group)
    {
        if (!ReferenceEquals(_group, group))
            throw new UnsupportedSignatureException("Retired activity requires this exact group image.");
        group.VerifyRetiredImage(this);
        VerifyCore(null, retired: true);
        group.VerifyRetiredImage(this);
    }
    internal void VerifyInstallationRead(ExactHookBroker.PlaybackSourceInstallGroup group, Action read)
    {
        if (!ReferenceEquals(_group, group))
            throw new UnsupportedSignatureException("Installation transport sharing requires this exact retained group image.");
        group.VerifySealIssuer(_module);
        if (_transport is null) read();
        else _transport.VerifyPlaybackInstallationRead(_module, FileSha256, read);
    }
    // Running channels retain their loader/file owner. This checks that actual
    // owner without repeating every executable/read-only section comparison.
    // Full Verify remains the installation and explicit diagnostic contract.
    internal void VerifyOperationModule()
    {
        if (_transport?.Unknown == true)
            throw new UnsupportedSignatureException("The running playback image lost its retained V5 transport authority.");
        if (_file.SafeFileHandle.IsClosed || _file.SafeFileHandle.IsInvalid
            || !ModulePath(_module).Equals(_path, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("The running channel lost its retained module/file owner.");
        HookTargetValidation.VerifyReadable(_module, 64);
    }
    internal void VerifyForGroupTraversal(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        if (_group is not null) traversal.VerifyIssuer(_group);
        else if (_pitchPair is not null) _pitchPair.VerifySealIssuer(_module, traversal);
        else throw new UnsupportedSignatureException("Only the traversal's actual group or native pair image can share target validation.");
        VerifyCore(traversal);
    }
    internal void VerifyForTargetFamily(PlaybackSourceGroupTargets.TargetFamilyVerification family)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (_pitchPair is null || _group is not null)
            throw new UnsupportedSignatureException("Only a complete native pair image can share TargetFamily ancestors.");
        _pitchPair.VerifySealIssuer(_module, null, family);
        VerifyCore(null, family);
    }
    private void VerifyCore(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
        PlaybackSourceGroupTargets.TargetFamilyVerification? family = null, bool retired = false)
    {
        ++_threadFullImageAudits;
        if (!ModulePath(_module).Equals(_path, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("Playback module path or retained image identity changed.");
        foreach (var range in _ranges) range.Verify();
        foreach (var memory in _groupExecutableMemory) memory.Verify();
        _group?.VerifySealIssuer(_module);
        _pitchPair?.VerifySealIssuer(_module, traversal, family);
        ulong transportWindows = 0;
        foreach (var (rva, bytes) in _executableFileRanges)
        {
            using var lease = _expectedBuffers.Rent(bytes);
            var expected = lease.Buffer;
            if (_transport is not null)
                transportWindows |= _transport.ComposeVerifiedPlaybackWindows(_module, FileSha256, rva, expected);
            if (_group is not null)
            {
                if (retired) _group.ComposeVerifiedRetiredMembers(_module, rva, expected);
                else if (traversal is null) _group.ComposeVerifiedMembers(_module, rva, expected);
                else _group.ComposeVerifiedMembersForTraversal(_module, rva, expected, traversal);
            }
            _pitchPair?.ComposeVerifiedMembers(rva, expected, traversal, family);
            if (!RangeSeal.TryMatchSealedRange(IntPtr.Add(_module, checked((int)rva)), expected,
                    out var first, out var actualWindow))
            {
                var count = Math.Min(16, expected.Length - first);
                throw new UnsupportedSignatureException("The full playback executable image differs from the pinned file, DIR64 or exact retained member patches. "
                    + $"module={Path.GetFileName(_path)}; sectionRVA=0x{rva:x}; firstDiffRVA=0x{checked(rva + (uint)first):x}; "
                    + $"expected={Convert.ToHexString(expected.AsSpan(first, count))}; actual={actualWindow}.");
            }
            lease.CompleteComparison();
        }
        if (_transport is not null && transportWindows != NativeEngineTransportPublicationLayout.AllWindows)
            throw new UnsupportedSignatureException("Playback executable verification did not cover all 36 retained V5 transport windows.");
    }
    // This pool stores only cleared scratch arrays, never verified evidence.
    // Each exact-length lease is removed before the canonical bytes are fully
    // copied into it. A nested or concurrent Verify therefore gets a distinct
    // array; composition's byte[] reference identity remains local to one
    // complete section comparison. A failed composition may leave a section
    // object holding the array until traversal exit, so that lease is discarded
    // without touching the old array or lending it to a nested verification.
    internal sealed class ExpectedSectionBufferPool
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, byte[]> _idle = new();

        internal Lease Rent(byte[] canonical)
        {
            ArgumentNullException.ThrowIfNull(canonical);
            byte[] buffer;
            lock (_gate)
            {
                if (_idle.TryGetValue(canonical.Length, out var idle))
                {
                    _idle.Remove(canonical.Length);
                    buffer = idle;
                }
                else buffer = new byte[canonical.Length];
            }
            // The entire exact section is restored before any member sees it.
            canonical.AsSpan().CopyTo(buffer);
            return new Lease(this, buffer);
        }

        private void Return(byte[] buffer, bool completed)
        {
            if (!completed) return;
            Array.Clear(buffer, 0, buffer.Length);
            lock (_gate) _idle.TryAdd(buffer.Length, buffer); // one idle array per exact length
        }

        internal sealed class Lease : IDisposable
        {
            private readonly ExpectedSectionBufferPool _owner;
            private byte[]? _buffer;
            private int _completed;

            internal Lease(ExpectedSectionBufferPool owner, byte[] buffer)
                => (_owner, _buffer) = (owner, buffer);
            internal byte[] Buffer => Volatile.Read(ref _buffer)
                ?? throw new ObjectDisposedException(nameof(Lease));
            internal void CompleteComparison()
            {
                if (Volatile.Read(ref _buffer) is null || Interlocked.Exchange(ref _completed, 1) != 0)
                    throw new InvalidOperationException("An expected section lease cannot complete twice or after disposal.");
            }
            public void Dispose()
            {
                var buffer = Interlocked.Exchange(ref _buffer, null);
                if (buffer is not null) _owner.Return(buffer, Volatile.Read(ref _completed) == 1);
            }
        }
    }
    internal static string ModulePath(IntPtr module)
    {
        Span<char> path = stackalloc char[260];
        var count = ReadModulePath(module, path);
        if (count >= path.Length)
        {
            path = new char[32768];
            count = ReadModulePath(module, path);
        }
        if (count == 0 || count >= path.Length)
            throw new UnsupportedSignatureException("Playback loaded image path is unavailable.");
        return Path.GetFullPath(new string(path[..(int)count]));
    }
    private static unsafe uint ReadModulePath(IntPtr module, Span<char> path)
    {
        fixed (char* buffer = path)
            return GetModuleFileName(module, buffer, path.Length);
    }
    [DllImport("kernel32", EntryPoint = "GetModuleFileNameW", ExactSpelling = true)]
    private static extern unsafe uint GetModuleFileName(IntPtr module, char* path, int size);
    [DllImport("kernel32", EntryPoint = "GetModuleHandleExW")] private static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);

    internal sealed class RangeSeal
    {
        private readonly IntPtr _address;
        private readonly MemoryFact[] _memory;
        internal byte[] Bytes { get; }
        internal uint StartProtection => _memory[0].Protect;
        private RangeSeal(IntPtr address, int length, IntPtr allocation, bool image, bool executable)
        {
            _address = address; _memory = MemoryFact.Read(address, length, allocation, image, executable);
            Bytes = Read(address, length);
        }
        internal static RangeSeal Capture(IntPtr address, int length, IntPtr allocation, bool image, bool executable = false)
            => new(address, length, allocation, image, executable);
        internal void Verify()
        {
            VerifyMemoryIdentity();
            if (!TryMatchSealedRange(_address, Bytes, out var first, out var actualWindow))
            {
                var memory = _memory[0];
                var image = memory.Type == 0x1000000 && memory.AllocationBase != IntPtr.Zero;
                var module = image ? Path.GetFileName(ModulePath(memory.AllocationBase)) : "private-allocation";
                var relative = _address.ToInt64() - memory.AllocationBase.ToInt64();
                var count = Math.Min(16, Bytes.Length - first);
                throw new UnsupportedSignatureException("A sealed playback image, relay or relocation byte changed. module=" + module
                    + "; rangeOffset=0x" + relative.ToString("x") + "; rangeLength=" + Bytes.Length
                    + "; firstDiffOffset=0x" + (relative + first).ToString("x")
                    + "; expected=" + Convert.ToHexString(Bytes.AsSpan(first, count))
                    + "; actual=" + actualWindow + ".");
            }
        }
        internal void VerifyMemoryIdentity()
        { foreach (var memory in _memory) memory.Verify(); }
        internal static byte[] Read(IntPtr address, int length)
        {
            var value = new byte[length]; Marshal.Copy(address, value, 0, value.Length); return value;
        }
        // Keep each comparison's actual bytes local to this one sealed range.
        // A bounded stack snapshot avoids a full managed actual[] while
        // preserving the first mismatching window even if later pages change.
        // Callers retain their existing page/member checks before this read.
        internal static unsafe bool TryMatchSealedRange(IntPtr address, ReadOnlySpan<byte> expected,
            out int firstDifference, out string actualWindow)
        {
            const int chunkLength = 4096;
            const int diagnosticLength = 16;
            firstDifference = -1;
            actualWindow = string.Empty;
            var actual = new ReadOnlySpan<byte>(address.ToPointer(), expected.Length);
            Span<byte> snapshot = stackalloc byte[chunkLength + diagnosticLength - 1];
            for (var offset = 0; offset < expected.Length;)
            {
                var compareLength = Math.Min(chunkLength, expected.Length - offset);
                var copyLength = Math.Min(snapshot.Length, expected.Length - offset);
                actual.Slice(offset, copyLength).CopyTo(snapshot);
                if (!snapshot[..compareLength].SequenceEqual(expected.Slice(offset, compareLength)))
                {
                    for (var index = 0; index < compareLength; ++index)
                    {
                        if (snapshot[index] == expected[offset + index]) continue;
                        firstDifference = offset + index;
                        actualWindow = Convert.ToHexString(snapshot.Slice(index,
                            Math.Min(diagnosticLength, expected.Length - firstDifference)));
                        return false;
                    }
                    // A concurrent change during comparison must fail closed.
                    // The current chunk may have changed after the bounded copy.
                    throw new UnsupportedSignatureException("A playback executable section changed during its bounded comparison.");
                }
                offset += compareLength;
            }
            return true;
        }
    }
    private sealed record MemoryFact(IntPtr Address, IntPtr End, IntPtr AllocationBase, uint AllocationProtect,
        uint State, uint Protect, uint Type)
    {
        internal static MemoryFact[] Read(IntPtr address, int length, IntPtr allocation, bool image, bool executable)
        {
            if (address == IntPtr.Zero || length <= 0 || address.ToInt64() > long.MaxValue - length)
                throw new UnsupportedSignatureException("A playback code range is invalid.");
            var facts = new List<MemoryFact>(); var next = address.ToInt64(); var end = next + length;
            while (next < end)
            {
                var current = (IntPtr)next;
                if (VirtualQuery(current, out var value, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0
                    || value.State != 0x1000 || (value.Protect & 0x101) != 0 || value.RegionSize == 0
                    || allocation != IntPtr.Zero && value.AllocationBase != allocation || image && value.Type != 0x1000000
                    || executable && (value.Protect & 0xf0) == 0)
                    throw new UnsupportedSignatureException("A playback code range lost its exact committed image/executable owner.");
                var regionEnd = checked(value.BaseAddress.ToInt64() + (long)value.RegionSize);
                if (regionEnd <= next) throw new UnsupportedSignatureException("Playback memory region did not advance.");
                var coveredEnd = Math.Min(end, regionEnd);
                facts.Add(new(current, (IntPtr)coveredEnd, value.AllocationBase, value.AllocationProtect,
                    value.State, value.Protect, value.Type)); next = coveredEnd;
            }
            return facts.ToArray();
        }
        internal void Verify()
        {
            // VirtualQuery may split or merge a larger region when adjacent
            // heap pages are committed/protected. Only our covered interval
            // confers authority; every byte/page of it must retain the same
            // allocation, allocation protection, state, protection and type.
            for (var next = Address.ToInt64(); next < End.ToInt64();)
            {
                if (VirtualQuery((IntPtr)next, out var value, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0
                    || value.AllocationBase != AllocationBase || value.AllocationProtect != AllocationProtect
                    || value.State != State || value.Protect != Protect || value.Type != Type)
                    throw new UnsupportedSignatureException("A covered playback range's allocation or page protection changed at 0x" + next.ToString("x") + ".");
                var regionEnd = checked(value.BaseAddress.ToInt64() + (long)value.RegionSize);
                if (value.BaseAddress.ToInt64() > next || regionEnd <= next)
                    throw new UnsupportedSignatureException("A covered playback memory query did not contain or advance its exact address.");
                next = Math.Min(End.ToInt64(), regionEnd);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    {
        internal IntPtr BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal nuint RegionSize;
        internal uint State, Protect, Type;
    }
    [DllImport("kernel32")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation info, nuint length);
}
