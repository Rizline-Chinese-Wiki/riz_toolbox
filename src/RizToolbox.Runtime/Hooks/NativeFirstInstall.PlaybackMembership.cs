using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFirstInstallPlaybackAttestation
{
    internal uint Version, Size, State, Unknown;
    internal IntPtr Token;
    internal ulong Epoch;
    internal IntPtr Module, Helper, Dobby;
    internal uint ImageSize, OriginalCount, RangeCount, CompletedWrites;
    internal uint SupportedOriginals, LookupChecks, BoundaryChecks, Reserved;
    internal ulong FileSha0, FileSha1, FileSha2, FileSha3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFirstInstallPlaybackRange
{
    internal uint Version, Size, Kind, Id;
    internal IntPtr Address, Original, Entry;
    internal ulong Epoch;
    internal IntPtr FunctionTable;
    internal ulong FunctionTableBase;
    internal uint Length, Protection, MovedByteCount, OriginalByteCount;
    internal uint FunctionCount, UnwindByteCount, LookupChecks, BoundaryChecks;
    internal ulong Reserved0, Reserved1;
    internal ulong Before0, Before1, Before2, Before3, After0, After1, After2, After3;
    internal IntPtr Continuation;
    internal void CopyBefore(Span<byte> destination) => CopyWords(destination, Before0, Before1, Before2, Before3);
    internal void CopyAfter(Span<byte> destination) => CopyWords(destination, After0, After1, After2, After3);
    private static void CopyWords(Span<byte> destination, ulong a, ulong b, ulong c, ulong d)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, a);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], b);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], c);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], d);
    }
}

internal sealed partial class NativeFirstInstall
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadPlaybackAttestation(ref NativeFirstInstallPlaybackAttestation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadPlaybackSnapshot(IntPtr token, ref NativeFirstInstallPlaybackAttestation info, IntPtr ranges, uint count);
    private PlaybackMembership? _playbackMembership;

    /// <summary>Only the loaded private FirstInstall owner can issue this member.
    /// The new native protocol retains its file baseline before Prepare and
    /// revalidates every actual relocation, continuation, unwind and source write.
    /// Public V7 packets and copied native rows cannot create this object.</summary>
    internal PlaybackMembership RequirePlaybackGroupAdoptionEvidence()
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_owner, this))
                throw new UnsupportedSignatureException("Foundation adoption requires the actual private loaded FirstInstall producer.");
            VerifyReady();
            _playbackMembership ??= PlaybackMembership.Create(this);
            _playbackMembership.VerifyCurrent(); return _playbackMembership;
        }
    }

    internal sealed class PlaybackMembership
    {
        private readonly NativeFirstInstall _producer;
        private readonly ReadPlaybackSnapshot _read;
        private readonly NativeFirstInstallPlaybackAttestation _identity;
        private readonly NativeFirstInstallPlaybackRange[] _ranges;
        private int _unknown;
        internal IntPtr Module => _producer.Module;
        internal ulong Epoch => _identity.Epoch;
        internal string ImageSha256 => GameFingerprint;

        private PlaybackMembership(NativeFirstInstall producer, ReadPlaybackAttestation get, ReadPlaybackSnapshot read)
        {
            _producer = producer; _read = read;
            _identity = ReadHeader(get);
            _ranges = new NativeFirstInstallPlaybackRange[64];
            ReadRanges(_ranges);
        }
        internal static PlaybackMembership Create(NativeFirstInstall producer)
        {
            if (!ReferenceEquals(_owner, producer))
                throw new UnsupportedSignatureException("A foundation playback member requires the registered private FirstInstall producer.");
            if (Marshal.SizeOf<NativeFirstInstallPlaybackAttestation>() != 120 || Marshal.SizeOf<NativeFirstInstallPlaybackRange>() != 184
                || !NativeLibrary.TryGetExport(producer.HelperModule, "RizFirstInstall_GetPlaybackAttestationV1", out var get)
                || !NativeLibrary.TryGetExport(producer.HelperModule, "RizFirstInstall_ReadPlaybackAttestedSnapshotV1", out var read))
                throw new UnsupportedSignatureException("The loaded FirstInstall helper has no complete private pre-Prepare playback attestation protocol.");
            HookTargetValidation.VerifyExecutable(get, producer.HelperModule); HookTargetValidation.VerifyExecutable(read, producer.HelperModule);
            return new(producer, Marshal.GetDelegateForFunctionPointer<ReadPlaybackAttestation>(get), Marshal.GetDelegateForFunctionPointer<ReadPlaybackSnapshot>(read));
        }
        private NativeFirstInstallPlaybackAttestation ReadHeader(ReadPlaybackAttestation get)
        {
            var info = new NativeFirstInstallPlaybackAttestation { Version = 1, Size = 120 };
            if (get(ref info) != 0 || info.Version != 1 || info.Size != 120 || info.State != 3 || info.Unknown != 0
                || info.Token == IntPtr.Zero || info.Epoch != 1 || info.Module != _producer.Module || info.Helper != _producer.HelperModule
                || info.Dobby == IntPtr.Zero || info.ImageSize != 0x243c000 || info.OriginalCount != 28 || info.SupportedOriginals != 28
                || info.RangeCount != 64 || info.CompletedWrites != 64 || info.LookupChecks != 155 || info.BoundaryChecks != 155 || info.Reserved != 0)
                throw new UnsupportedSignatureException("FirstInstall has no complete private 28-original/64-write pre-Prepare playback credentials.");
            var sha = new byte[32]; BitConverter.GetBytes(info.FileSha0).CopyTo(sha, 0); BitConverter.GetBytes(info.FileSha1).CopyTo(sha, 8);
            BitConverter.GetBytes(info.FileSha2).CopyTo(sha, 16); BitConverter.GetBytes(info.FileSha3).CopyTo(sha, 24);
            if (!Convert.ToHexString(sha).Equals(GameFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The native private FirstInstall file pin belongs to another image.");
            return info;
        }
        private unsafe void ReadRanges(Span<NativeFirstInstallPlaybackRange> ranges)
        {
            var identities = new HashSet<IntPtr>();
            // The native reader validates the complete retained owner before
            // and after one bounded copy. Repeating a complete 64-row native
            // validation for each individual row made startup quadratic.
            // All fields are unmanaged. Pin only for this synchronous read
            // so the native snapshot goes straight into its final array.
            for (var index = 0; index < ranges.Length; ++index)
                ranges[index] = new NativeFirstInstallPlaybackRange { Version = 1, Size = 184 };
            var header = new NativeFirstInstallPlaybackAttestation { Version = 1, Size = 120 };
            fixed (NativeFirstInstallPlaybackRange* buffer = ranges)
            {
                if (_read(_identity.Token, ref header, (IntPtr)buffer, (uint)ranges.Length) != 0 || !header.Equals(_identity))
                    throw new UnsupportedSignatureException("The current complete native FirstInstall range read is unavailable or unknown.");
            }
            Span<byte> after = stackalloc byte[32];
            Span<byte> actual = stackalloc byte[32];
            for (var index = 0; index < ranges.Length; ++index)
            {
                var row = ranges[index];
                if (row.Version != 1 || row.Size != 184 || row.Id != index
                    || row.Address == IntPtr.Zero || row.Length is 0 or > 32 || row.Reserved0 != 0 || row.Reserved1 != 0
                    || !NativeListenerPublication.ExecutableProtection(row.Protection))
                    throw new UnsupportedSignatureException("A native private FirstInstall write row is incomplete or unknown.");
                var rva = row.Address.ToInt64() - Module.ToInt64();
                if (rva < 0 || (ulong)rva > _identity.ImageSize || row.Length > _identity.ImageSize - (ulong)rva)
                    throw new UnsupportedSignatureException("A private FirstInstall write is outside its pinned executable image.");
                HookTargetValidation.VerifyExecutable(row.Address, Module);
                VerifyProtection(row.Address, row.Protection);
                HookTargetValidation.VerifyReadable(row.Address, row.Length);
                row.CopyAfter(after);
                var length = checked((int)row.Length);
                new ReadOnlySpan<byte>(row.Address.ToPointer(), length).CopyTo(actual);
                if (!actual[..length].SequenceEqual(after[..length]))
                    throw new UnsupportedSignatureException("A private FirstInstall source no longer equals its actual native write.");
                foreach (ref readonly var other in ranges[..index])
                {
                    if (other.Address.ToInt64() < row.Address.ToInt64() + row.Length
                        && row.Address.ToInt64() < other.Address.ToInt64() + other.Length)
                        throw new UnsupportedSignatureException("Private FirstInstall source writes overlap.");
                }
                if (row.Kind == 1)
                {
                    var identity = DetourIdentity(row.Address);
                    if (row.Length != 32 || row.Original != identity.Original || row.Entry != identity.Entry || row.Epoch != identity.Epoch
                        || row.Original == IntPtr.Zero || row.Entry == IntPtr.Zero || row.Epoch == 0 || !identities.Add(row.Address)
                        || row.FunctionTable == IntPtr.Zero || row.FunctionTableBase != unchecked((ulong)row.Original.ToInt64())
                        || row.MovedByteCount is < 6 or > 10 || row.OriginalByteCount != row.MovedByteCount + 14
                        || row.FunctionCount is < 3 or > 6 || row.UnwindByteCount == 0
                        || row.LookupChecks != row.FunctionCount + 2 || row.BoundaryChecks != row.LookupChecks
                        || row.Continuation != IntPtr.Add(row.Address, checked((int)row.MovedByteCount)))
                        throw new UnsupportedSignatureException("A native attestation differs from the private FirstInstall original/entry/epoch or exact relocation.");
                    VerifyExecutable(row.Original); HookTargetValidation.VerifyExecutable(row.Entry, _producer.HelperModule);
                }
                else if (row.Kind != 2 || row.Original != IntPtr.Zero || row.Entry != IntPtr.Zero || row.Epoch != 0
                    || row.FunctionTable != IntPtr.Zero || row.FunctionTableBase != 0 || row.MovedByteCount != 0 || row.OriginalByteCount != 0
                    || row.FunctionCount != 0 || row.UnwindByteCount != 0 || row.LookupChecks != 0 || row.BoundaryChecks != 0 || row.Continuation != IntPtr.Zero)
                    throw new UnsupportedSignatureException("A native FirstInstall inline write masquerades as an original credential.");
            }
            if (identities.Count != 28) throw new UnsupportedSignatureException("The private FirstInstall attestation omits an actual Dobby original.");
        }
        private (IntPtr Original, IntPtr Entry, ulong Epoch) DetourIdentity(IntPtr target)
        {
            if (target == _producer.RuntimeTarget) return (_producer.RuntimeOriginal, _producer.RuntimeEntry, _producer.RuntimeEpoch);
            if (target == _producer.UnityTarget) return (_producer.UnityOriginal, _producer.UnityEntry, _producer.UnityEpoch);
            for (uint index = 0; index < NativeOperationPublication.Count; ++index)
            {
                var row = NativeOperationPublication.Row(_producer._operationPublication, index);
                if (row.Target == target) return (row.Original, row.Entry, row.Epoch);
            }
            throw new UnsupportedSignatureException("The native write is absent from the actual private FirstInstall operation registry.");
        }
        internal void VerifyCurrent()
        {
            lock (Gate)
            {
                VerifyPrivateIssuer();
                try
                {
                    _producer.VerifyReady();
                    Span<NativeFirstInstallPlaybackRange> current = stackalloc NativeFirstInstallPlaybackRange[64];
                    ReadRanges(current);
                    if (!MemoryMarshal.AsBytes(current).SequenceEqual(MemoryMarshal.AsBytes(_ranges.AsSpan())))
                        throw new UnsupportedSignatureException("The native FirstInstall private pre-Prepare identity or exact write registry changed.");
                }
                catch (Exception error)
                {
                    _producer.RememberFailure(nameof(PlaybackMembership) + "." + nameof(VerifyCurrent), error.Message);
                    Interlocked.Exchange(ref _unknown, 1);
                    throw;
                }
            }
        }
        private void VerifyPrivateIssuer()
        {
            _producer.RequireKnown();
            if (Volatile.Read(ref _unknown) != 0
                || !ReferenceEquals(_owner, _producer) || !ReferenceEquals(_producer._playbackMembership, this))
                throw new UnsupportedSignatureException("This foundation playback member is not the actual retained private issuer.");
        }
        internal void VerifyRuntimeOwnership()
        {
            VerifyPrivateIssuer();
            _producer.VerifyPlaybackRuntimeChannelOwnership();
            VerifyPrivateIssuer();
        }
        internal VerificationScope BeginVerification(ExactHookBroker.PlaybackSourceInstallGroup group, long? installation = null)
            => VerificationScope.Create(this, group, installation);

        // Only one locked group traversal owns this scope. Its entry and exit
        // both perform the complete native/producer read; no successful
        // check is ambient, inherited by nesting or retained by the group.
        internal sealed class VerificationScope : IDisposable
        {
            private readonly PlaybackMembership _member;
            private readonly ExactHookBroker.PlaybackSourceInstallGroup _group;
            private readonly long _generation;
            private readonly long? _installation;
            private readonly int _thread;
            private readonly IntPtr _token;
            private readonly ulong _epoch;
            private bool _closed;
            private VerificationScope(PlaybackMembership member, ExactHookBroker.PlaybackSourceInstallGroup group, long generation, long? installation)
            {
                (_member, _group, _generation, _thread, _token, _epoch) =
                    (member, group, generation, Environment.CurrentManagedThreadId, member._identity.Token, member._identity.Epoch);
                _installation = installation;
                VerifyIssuer(group);
            }
            internal static VerificationScope Create(PlaybackMembership member, ExactHookBroker.PlaybackSourceInstallGroup group, long? installation = null)
            {
                var generation = group.VerifyFoundationVerificationIssuer(member, installation: installation);
                member.VerifyCurrent();
                return new(member, group, generation, installation);
            }
            internal void VerifyIssuer(ExactHookBroker.PlaybackSourceInstallGroup group)
            {
                if (_closed || _thread != Environment.CurrentManagedThreadId || !ReferenceEquals(group, _group)
                    || _token == IntPtr.Zero || _token != _member._identity.Token || _epoch != _member._identity.Epoch)
                    throw new UnsupportedSignatureException("A foundation verification scope cannot leave its one actual group traversal.");
                _member.VerifyPrivateIssuer();
                group.VerifyFoundationVerificationIssuer(_member, _generation, _installation);
            }
            internal (IntPtr Address, int Length)[] ImageRanges(ExactHookBroker.PlaybackSourceInstallGroup group)
            {
                VerifyIssuer(group);
                return _member._ranges.Select(value => (value.Address, checked((int)value.Length))).ToArray();
            }
            internal void ComposeIntoPinnedFile(ExactHookBroker.PlaybackSourceInstallGroup group, uint rva, Span<byte> expected)
            {
                VerifyIssuer(group); _member.ComposeRetainedRanges(rva, expected);
            }
            public void Dispose()
            {
                if (_closed) return;
                try
                {
                    // Attempt the full fresh exit read even when the traversal
                    // failed. Unknown, issuer and generation changes reject it.
                    _member.VerifyCurrent(); VerifyIssuer(_group);
                }
                finally { _closed = true; }
            }
        }
        // Returned values reveal only immutable addresses/widths. Byte evidence
        // stays private, and can only be composed after revalidating this issuer.
        internal (IntPtr Address, int Length)[] ImageRanges()
        { VerifyCurrent(); return _ranges.Select(value => (value.Address, checked((int)value.Length))).ToArray(); }
        internal void ComposeIntoPinnedFile(uint rva, Span<byte> expected)
        {
            VerifyCurrent(); ComposeRetainedRanges(rva, expected);
        }
        internal void VerifyRendererUnityInvoke(IntPtr module, IntPtr address, byte[] mapped, string expectedHash)
        {
            VerifyCurrent();
            if (module != Module || address != _producer.UnityTarget || address != IntPtr.Add(module, 0x15642e0)
                || mapped.Length != 624)
                throw new UnsupportedSignatureException("Renderer UnityEvent verification requires the exact installed Invoke body.");

            // Reconstruct only writes owned by this producer. Every other byte,
            // including the padding beyond the unwind extent, stays in the hash.
            Span<byte> bytes = stackalloc byte[32];
            bool sawEntry = false;
            foreach (var row in _ranges)
            {
                long at = row.Address.ToInt64() - address.ToInt64();
                if (at >= mapped.Length || at + row.Length <= 0) continue;
                if (at < 0 || at + row.Length > mapped.Length)
                    throw new UnsupportedSignatureException("A renderer function boundary splits a registered write.");
                var slice = mapped.AsSpan((int)at, (int)row.Length);
                row.CopyAfter(bytes);
                if (!slice.SequenceEqual(bytes[..slice.Length]))
                    throw new UnsupportedSignatureException("Renderer Invoke differs from the installed native write.");
                row.CopyBefore(bytes);
                bytes[..slice.Length].CopyTo(slice);
                sawEntry |= at == 0;
            }
            if (!sawEntry || !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mapped))
                .Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("Renderer Invoke does not reconstruct to the complete stock body.");
            VerifyCurrent();
        }

        private void ComposeRetainedRanges(uint rva, Span<byte> expected)
        {
            Span<byte> bytes = stackalloc byte[32];
            var start = (long)rva; var end = start + expected.Length;
            foreach (var row in _ranges)
            {
                var source = row.Address.ToInt64() - Module.ToInt64(); var stop = source + row.Length;
                if (source >= end || stop <= start) continue;
                if (source < start || stop > end) throw new UnsupportedSignatureException("A pinned file interval splits a private FirstInstall source write.");
                var at = checked((int)(source - start)); var length = checked((int)row.Length);
                row.CopyBefore(bytes);
                if (!expected.Slice(at, length).SequenceEqual(bytes[..length]))
                    throw new UnsupportedSignatureException("The private FirstInstall pre-Prepare bytes differ from the pinned file and exact DIR64 relocations.");
                row.CopyAfter(bytes);
                bytes[..length].CopyTo(expected.Slice(at, length));
            }
        }
    }
}
