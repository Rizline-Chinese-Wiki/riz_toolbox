using System.Buffers.Binary;

namespace RizToolbox.Runtime.Hooks;

// Publication V4 is a separate protocol from Kernel transport ABI 2 and its
// scalar records (version 1). Keep an owned immutable copy, never a live view.
internal sealed class NativeEnginePublication
{
    internal const int Version = 4, ByteSize = 4096, ModuleOffset = 128, ModuleBytes = 96;
    internal const int RowOffset = 512, RowBytes = 320, EventOffset = 1792, EventBytes = 32;
    internal const uint OwnedReadyAdmission = 31, ProductionFactsAdmission = 0x7f00, ProductionReadyAdmission = 0xff00;
    private readonly byte[] _bytes;
    private NativeEnginePublication(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();
    internal static NativeEnginePublication Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteSize) throw new UnsupportedSignatureException("Engine publication requires its exact 4096-byte V4 record.");
        return new(bytes);
    }
    internal ReadOnlySpan<byte> Bytes => _bytes;
    internal uint UInt32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(at, 4));
    internal ulong UInt64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(at, 8));
    internal uint RecordVersion => UInt32(0);
    internal uint Size => UInt32(4);
    internal uint State => UInt32(8);
    internal uint Failure => UInt32(12);
    internal ulong Epoch => UInt64(16);
    internal uint OwnerThread => UInt32(24);
    internal uint ProcessId => UInt32(28);
    internal uint AttachReason => UInt32(32);
    internal uint StartupStatic => UInt32(36);
    internal uint Claimed => UInt32(40);
    internal uint Profile => UInt32(44);
    internal uint ConfiguredMask => UInt32(48);
    internal uint BoundMask => UInt32(52);
    internal uint CommittedMask => UInt32(56);
    internal uint SourceWriteAttempts => UInt32(60);
    internal uint SourceWriteCompletions => UInt32(64);
    internal uint ProtectionOutstanding => UInt32(68);
    internal uint EventCount => UInt32(72);
    internal uint Overflow => UInt32(76);
    internal ulong ReservedPointer => UInt64(80);
    internal ulong BootstrapModule => UInt64(88);
    internal ulong KernelModule => UInt64(96);
    internal ulong TargetModule => UInt64(104);
    internal ulong ExecutableModule => UInt64(112);
    internal uint AdmissionFlags => UInt32(120);
    internal uint VerifyFailures => UInt32(124);
    internal Module ModuleAt(int index) => new(_bytes.AsSpan(ModuleOffset + CheckedIndex(index) * ModuleBytes, ModuleBytes));
    internal Row RowAt(NativeEngineNodeKind kind) => new(_bytes.AsSpan(RowOffset + CheckedIndex((int)kind) * RowBytes, RowBytes));
    private static int CheckedIndex(int index) => index is >= 0 and < 4 ? index : throw new ArgumentOutOfRangeException(nameof(index));
    internal bool SameImmutableRecord(NativeEnginePublication other) => other is not null && Bytes.SequenceEqual(other.Bytes);

    internal sealed class Module
    {
        private readonly byte[] _bytes;
        internal Module(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();
        private uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(at, 4));
        private ulong Q(int at) => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(at, 8));
        internal uint Kind => U(0);
        internal uint Machine => U(4);
        internal uint ImageSize => U(8);
        internal uint TimeDateStamp => U(12);
        internal ulong Address => Q(16);
        internal ulong FileBytes => Q(24);
        internal ulong FileIndex => Q(32);
        internal uint VolumeSerial => U(40);
        internal uint Flags => U(44);
        internal ReadOnlySpan<byte> Sha256 => _bytes.AsSpan(48, 32);
        internal ulong HeldFile => Q(80);
        internal uint EntryRva => U(88);
        internal uint ExceptionRva => U(92);
    }

    internal sealed class Row
    {
        private readonly byte[] _bytes;
        internal Row(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();
        private uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(at, 4));
        private ulong Q(int at) => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(at, 8));
        internal uint Version => U(0);
        internal uint Size => U(4);
        internal uint Kind => U(8);
        internal uint PrefixBytes => U(12);
        internal ulong Module => Q(16);
        internal ulong Target => Q(24);
        internal ulong Original => Q(32);
        internal ulong Continuation => Q(40);
        internal ulong Entry => Q(48);
        internal ulong Epoch => Q(56);
        internal uint Rva => U(64);
        internal uint OriginalProtection => U(68);
        internal uint ObservedProtection => U(72);
        internal uint Flags => U(76);
        internal uint ProtectAttempts => U(80);
        internal uint ProtectSucceeded => U(84);
        internal uint SourceWriteAttempts => U(88);
        internal uint SourceBytesWritten => U(92);
        internal uint ReadbackAttempts => U(96);
        internal uint ReadbackVerified => U(100);
        internal uint RestoreAttempts => U(104);
        internal uint RestoreSucceeded => U(108);
        internal uint FlushAttempts => U(112);
        internal uint FlushSucceeded => U(116);
        internal uint ProtectionOutstanding => U(120);
        internal uint LastWin32Error => U(124);
        internal ulong HelperEpoch => Q(128);
        internal uint HelperSlot => U(136);
        internal uint Reserved => U(140);
        internal ReadOnlySpan<byte> OriginalBytes => _bytes.AsSpan(144, 32);
        internal ReadOnlySpan<byte> PatchBytes => _bytes.AsSpan(176, 32);
        internal ReadOnlySpan<byte> ObservedBytes => _bytes.AsSpan(208, 32);
        internal ReadOnlySpan<byte> ModuleSha256 => _bytes.AsSpan(240, 32);
        internal ReadOnlySpan<byte> BodySha256 => _bytes.AsSpan(272, 32);
        internal uint RuntimeFunctionCount => U(304);
        internal bool TailReservedZero => IsZero(_bytes.AsSpan(308, 12));
        internal NativeEngineNodeChannelIdentity Identity => new((NativeEngineNodeKind)Kind, HelperSlot,
            Pointer(Target), Pointer(Original), Pointer(Entry), HelperEpoch);
    }

    internal static IntPtr Pointer(ulong address) => new(unchecked((long)address));
    private static readonly byte[][] Prefixes = {
        Convert.FromHexString("48895C2408488974241848897C2420"),
        Convert.FromHexString("405553574156488D6C24884881EC78010000"),
        Convert.FromHexString("40534883EC20FF4960488BD983796000"),
        Convert.FromHexString("48896C2420574883EC20488BF933ED") };
    internal static ReadOnlySpan<byte> Prefix(NativeEngineNodeKind kind) => Prefixes[CheckedIndex((int)kind)];
    internal static bool IsZero(ReadOnlySpan<byte> bytes) { foreach (var value in bytes) if (value != 0) return false; return true; }
    private static bool Within(ulong address, uint bytes, Module module)
        => address >= module.Address && address - module.Address <= module.ImageSize
            && bytes <= module.ImageSize - (address - module.Address);

    // This is only a transport/semantic validator. A private process owner must
    // separately prove loaded images, independent hashes, actual source memory,
    // registered unwind families, native VerifyV4 and exact Kernel identities.
    internal static bool HasReadyShape(NativeEnginePublication value, uint expectedProfile)
    {
        if (value.RecordVersion != Version || value.Size != ByteSize || value.State != 4 || value.Failure != 0
            || value.Epoch != 1 || value.OwnerThread == 0 || value.ProcessId == 0 || value.AttachReason != 1
            || value.StartupStatic != 1 || value.Claimed != 1 || expectedProfile is not (1 or 2) || value.Profile != expectedProfile
            || value.ConfiguredMask != 15 || value.BoundMask != 15 || value.CommittedMask != 15
            || value.SourceWriteAttempts != 4 || value.SourceWriteCompletions != 4 || value.ProtectionOutstanding != 0
            // This is the opaque real DllEntry lpReserved startup witness,
            // not unused padding. Never dereference it or infer game admission.
            || value.EventCount is 0 or > 64 || value.Overflow != 0 || value.ReservedPointer == 0
            || value.AdmissionFlags != (expectedProfile == 1 ? OwnedReadyAdmission : ProductionReadyAdmission)
            || value.VerifyFailures != 0 || !IsZero(value.Bytes[3840..])) return false;
        var addresses = new[] { value.BootstrapModule, value.KernelModule, value.TargetModule, value.ExecutableModule };
        if (addresses.Any(x => x == 0) || addresses.Distinct().Count() != 4) return false;
        for (var index = 0; index < 4; ++index)
        {
            var module = value.ModuleAt(index);
            if (module.Kind != index || module.Machine != 0x8664 || module.ImageSize < 4096 || module.TimeDateStamp == 0
                || module.Address != addresses[index] || module.FileBytes == 0 || module.FileIndex == 0 || module.VolumeSerial == 0
                || module.HeldFile == 0 || module.Flags != (index == 0 ? 7u : 15u) || IsZero(module.Sha256)
                || module.ExceptionRva == 0 || module.ExceptionRva >= module.ImageSize
                || (index == 1 ? module.EntryRva != 0 : module.EntryRva == 0 || module.EntryRva >= module.ImageSize)) return false;
        }
        var target = value.ModuleAt(2); var bootstrap = value.ModuleAt(0); var kernel = value.ModuleAt(1);
        for (var index = 0; index < 4; ++index)
        {
            var kind = (NativeEngineNodeKind)index; var row = value.RowAt(kind); var prefix = Prefix(kind);
            if (row.Version != Version || row.Size != RowBytes || row.Kind != index || row.PrefixBytes != prefix.Length
                || row.Module != target.Address || row.Target != target.Address + row.Rva || row.Rva >= target.ImageSize
                || !Within(row.Target, 32, target) || !Within(row.Original, checked((uint)prefix.Length + 10), bootstrap)
                || !Within(row.Entry, 1, kernel) || row.Continuation != row.Target + row.PrefixBytes
                || row.Epoch != value.Epoch || row.OriginalProtection != 0x20 || row.ObservedProtection != 0x20 || row.Flags != 7
                || row.ProtectAttempts != 1 || row.ProtectSucceeded != 1 || row.SourceWriteAttempts != 1
                || row.SourceBytesWritten != row.PrefixBytes || row.ReadbackAttempts != 1 || row.ReadbackVerified != 1
                || row.RestoreAttempts != 1 || row.RestoreSucceeded != 1 || row.FlushAttempts != 1 || row.FlushSucceeded != 1
                || row.ProtectionOutstanding != 0 || row.LastWin32Error != 0 || row.HelperEpoch == 0 || row.HelperSlot != 28
                || row.Reserved != 0 || !row.TailReservedZero || !row.Identity.IsComplete || row.RuntimeFunctionCount is 0 or > 64
                || !row.OriginalBytes[..prefix.Length].SequenceEqual(prefix) || !row.ModuleSha256.SequenceEqual(target.Sha256)
                || IsZero(row.BodySha256)) return false;
            var patch = row.OriginalBytes.ToArray(); patch[0] = 0xff; patch[1] = 0x25; Array.Clear(patch, 2, 4);
            BinaryPrimitives.WriteUInt64LittleEndian(patch.AsSpan(6, 8), row.Entry);
            for (var at = 14; at < prefix.Length; ++at) patch[at] = 0x90;
            if (!row.PatchBytes.SequenceEqual(patch) || !row.ObservedBytes.SequenceEqual(patch)) return false;
        }
        for (var index = 0; index < value.EventCount; ++index)
        {
            var at = EventOffset + index * EventBytes;
            if (value.UInt64(at) != (ulong)index + 1 || value.UInt32(at + 8) == 0
                || value.UInt32(at + 12) > 4 || value.UInt32(at + 16) > 4 || value.UInt32(at + 20) != 0
                || value.UInt32(at + 24) != 0 || value.UInt32(at + 28) != 0) return false;
        }
        return IsZero(value.Bytes.Slice(EventOffset + checked((int)value.EventCount) * EventBytes,
            (64 - checked((int)value.EventCount)) * EventBytes));
    }
}
