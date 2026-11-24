using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeEngineTransportFirstInstall
{
    private HeldImportGraph? _activityImportGraph;

    // Called after the existing per-module/producer-handle/owned-marker checks.
    // Cold/full verification continues to use its original independent path.
    // This plan contains only held-file decoding and derived expected bytes;
    // every successful activity boundary still performs all actual graph reads.
    private bool TryVerifyActivityImportGraph()
    {
        if (_activityOwnerThread == 0) return false;
        RequireActivityThread();
        var graph = _activityImportGraph ??= new HeldImportGraph(this);
        graph.Verify(this);
        return true;
    }

    private sealed class HeldImportGraph
    {
        private readonly NativeEngineTransportFirstInstall _owner;
        private readonly ModuleFacts.HeldImportFacts[] _groups;
        private readonly byte[] _digest;
        internal HeldImportGraph(NativeEngineTransportFirstInstall owner)
        {
            owner.RequireActivityThread(); _owner = owner;
            _groups = new[] {
                new ModuleFacts.HeldImportFacts(owner._modules[3], owner._modules[2], 3, 2, new[] { "UnityMain2" }),
                new ModuleFacts.HeldImportFacts(owner._modules[2], owner._modules[4], 2, 4,
                    new[] { "WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle" }),
                new ModuleFacts.HeldImportFacts(owner._modules[4], owner._modules[0], 4, 0, PublicationExports),
                new ModuleFacts.HeldImportFacts(owner._modules[0], owner._modules[1], 0, 1, KernelExports)
            };
            using var canonical = new MemoryStream(); var count = 0;
            foreach (var group in _groups) count += group.AppendCanonical(canonical);
            _digest = SHA256.HashData(canonical.ToArray());
            Require(count == 44 && owner._wire.DependencyGraphSha256.AsSpan().SequenceEqual(_digest),
                "The complete held-file import plan differs from the independently resolved forty-four-edge V5 graph.");
        }
        internal void Verify(NativeEngineTransportFirstInstall owner)
        {
            owner.RequireActivityThread();
            Require(ReferenceEquals(owner, _owner) && owner._wire.DependencyGraphSha256.AsSpan().SequenceEqual(_digest),
                "The immutable held import plan belongs to a different exact publication owner or graph.");
            foreach (var group in _groups) group.Verify(owner);
        }
    }

    private sealed partial class ModuleFacts
    {
        private void RequireExactImportBoundary(NativeEngineTransportFirstInstall owner)
        {
            owner.RequireActivityThread();
            if (!ReferenceEquals(_activityBoundaryOwner, owner))
                throw new UnsupportedSignatureException("A held import plan escaped its exact module activity boundary.");
        }
        private uint HeldNamedImportDestination(string name)
        {
            var exports = _heldExports ??= new HeldExportFacts(this);
            if (!exports.Named.TryGetValue(name, out var rva) || rva == 0 || rva >= ImageSize
                || rva >= _exportRva && (ulong)rva < (ulong)_exportRva + _exportBytes)
                throw new UnsupportedSignatureException("The held graph destination is missing, duplicated, forwarded or outside its exact image.");
            return rva;
        }

        // The four groups are constructed from independently hash-checked held
        // PE bytes. IAT expectations come from the held unique destination EAT
        // plus its independently identified module base, never from live IAT.
        internal sealed class HeldImportFacts
        {
            private readonly ModuleFacts _source, _destination;
            private readonly uint _sourceIndex, _destinationIndex;
            private readonly ImportEdge[] _edges;
            private readonly ImportReadBlock[] _blocks;
            private readonly record struct ImportEdge(string Name, uint Lookup, uint Iat, uint Export);
            private readonly record struct ImportRange(uint Rva, byte[] Expected);
            private sealed class ImportReadBlock
            {
                internal readonly uint Rva;
                internal readonly byte[] Expected, Scratch;
                internal ImportReadBlock(uint rva, byte[] expected)
                    => (Rva, Expected, Scratch) = (rva, expected, new byte[expected.Length]);
            }

            internal HeldImportFacts(ModuleFacts source, ModuleFacts destination, uint sourceIndex, uint destinationIndex,
                IReadOnlyList<string> expectedNames)
            {
                _source = source; _destination = destination; _sourceIndex = sourceIndex; _destinationIndex = destinationIndex;
                if (source._delayRva != 0 || source._delayBytes != 0)
                    throw new UnsupportedSignatureException("The fixed import plan cannot be supplemented by delayed imports.");
                if (source._importRva == 0 || source._importBytes < 20 || source._importBytes > 1024 * 1024
                    || (ulong)source._importRva + source._importBytes > source.ImageSize)
                    throw new UnsupportedSignatureException("The fixed import plan has no bounded ordinary descriptor table.");
                var remaining = new HashSet<string>(expectedNames, StringComparer.Ordinal);
                if (remaining.Count == 0 || remaining.Count != expectedNames.Count)
                    throw new UnsupportedSignatureException("The fixed import plan requires one complete unique expected name set.");
                var ranges = new List<ImportRange>(); var edges = new Dictionary<string, ImportEdge>(StringComparer.Ordinal);
                var destinationName = System.IO.Path.GetFileName(destination.Path); var descriptors = 0; var ended = false;
                for (uint offset = 0; offset + 20 <= source._importBytes; offset += 20)
                {
                    var descriptorRva = checked(source._importRva + offset); var descriptor = source.Disk(descriptorRva, 20);
                    // Retain the complete descriptor prefix through its zero
                    // terminator. This also observes unrelated descriptor bytes
                    // whose held identity determined the selected DLL group.
                    ranges.Add(new(descriptorRva, descriptor));
                    if (IsZero(descriptor)) { ended = true; break; }
                    var dllNameRva = Word(descriptor, 12); var dllName = source.DiskName(dllNameRva);
                    ranges.Add(new(dllNameRva, source.Disk(dllNameRva, checked(dllName.Length + 1))));
                    if (!string.Equals(dllName, destinationName, StringComparison.OrdinalIgnoreCase)) continue;
                    ++descriptors;
                    var original = Word(descriptor, 0); var iat = Word(descriptor, 16);
                    if (original == 0 || iat == 0)
                        throw new UnsupportedSignatureException("The fixed import plan requires independent complete ILT and IAT tables.");
                    var thunkEnded = false;
                    for (uint index = 0; index <= 65536; ++index)
                    {
                        var lookupRva = checked(original + index * 8); var iatRva = checked(iat + index * 8);
                        var heldLookup = source.Disk(lookupRva, 8); var nameRva = Quad(heldLookup, 0);
                        ranges.Add(new(lookupRva, heldLookup));
                        _ = source.Disk(iatRva, 8); // The derived IAT slot must still have complete independent file backing.
                        var expectedIat = new byte[8];
                        if (nameRva == 0)
                        {
                            ranges.Add(new(iatRva, expectedIat)); thunkEnded = true; break;
                        }
                        if (nameRva > uint.MaxValue || (nameRva & 0x8000000000000000) != 0)
                            throw new UnsupportedSignatureException("The fixed import plan cannot use ordinal or unbounded name imports.");
                        var name = source.DiskName(checked((uint)nameRva + 2));
                        ranges.Add(new((uint)nameRva, source.Disk((uint)nameRva, checked(name.Length + 3))));
                        if (!remaining.Remove(name))
                            throw new UnsupportedSignatureException("The fixed import plan has an unexpected or repeated named edge.");
                        var exportRva = destination.HeldNamedImportDestination(name);
                        BinaryPrimitives.WriteUInt64LittleEndian(expectedIat, checked(Address(destination.Handle) + exportRva));
                        ranges.Add(new(iatRva, expectedIat)); edges.Add(name, new(name, lookupRva, iatRva, exportRva));
                    }
                    if (!thunkEnded) throw new UnsupportedSignatureException("The held import plan has an unterminated ILT/IAT.");
                }
                if (!ended || descriptors != 1 || remaining.Count != 0)
                    throw new UnsupportedSignatureException("The held import plan is missing, duplicated, unterminated or incomplete.");
                _edges = new ImportEdge[expectedNames.Count];
                for (var index = 0; index < _edges.Length; ++index) _edges[index] = edges[expectedNames[index]];
                _blocks = MergeRanges(ranges, source.ImageSize);
            }

            internal int AppendCanonical(Stream output)
            {
                foreach (var edge in _edges)
                {
                    var row = new byte[20];
                    BinaryPrimitives.WriteUInt32LittleEndian(row, _sourceIndex);
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), _destinationIndex);
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8), edge.Lookup);
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(12), edge.Iat);
                    BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(16), edge.Export);
                    output.Write(row); output.Write(Encoding.ASCII.GetBytes(edge.Name + '\0'));
                }
                return _edges.Length;
            }

            internal void Verify(NativeEngineTransportFirstInstall owner)
            {
                _source.RequireExactImportBoundary(owner); _destination.RequireExactImportBoundary(owner);
                // The full destination directory/EAT/name/ordinal/ASCII group
                // is read by the same current boundary; only its held parsing
                // is retained. Every imported target also retains a live RX VQ.
                _destination.VerifyHeldExports(_destination._heldExports
                    ?? throw new UnsupportedSignatureException("The held destination export parsing disappeared."));
                foreach (var edge in _edges)
                    Memory(Pointer(checked(Address(_destination.Handle) + edge.Export)), 1, _destination.Handle, 0x20);
                foreach (var block in _blocks)
                {
                    var address = Pointer(checked(Address(_source.Handle) + block.Rva));
                    Memory(address, block.Expected.Length, _source.Handle, 0x02);
                    if (!ReadProcessMemory(GetCurrentProcess(), address, block.Scratch, (nuint)block.Scratch.Length, out var read)
                        || read != (nuint)block.Scratch.Length || !block.Scratch.AsSpan().SequenceEqual(block.Expected))
                        throw new UnsupportedSignatureException("The actual complete descriptor/DLL/ILT/hint-name/IAT import group changed.");
                }
            }

            private static ImportReadBlock[] MergeRanges(List<ImportRange> ranges, uint imageSize)
            {
                ranges.Sort((left, right) => left.Rva.CompareTo(right.Rva));
                var blocks = new List<ImportReadBlock>();
                uint begin = 0; byte[]? expected = null;
                foreach (var range in ranges)
                {
                    if (range.Expected.Length == 0 || (ulong)range.Rva + (ulong)range.Expected.Length > imageSize)
                        throw new UnsupportedSignatureException("The held import range leaves its independently bounded source image.");
                    if (expected is null) { begin = range.Rva; expected = range.Expected; continue; }
                    var end = checked((ulong)begin + (ulong)expected.Length);
                    if (range.Rva > end)
                    {
                        blocks.Add(new(begin, expected)); begin = range.Rva; expected = range.Expected; continue;
                    }
                    var offset = checked((int)(range.Rva - begin));
                    var overlap = Math.Min(expected.Length - offset, range.Expected.Length);
                    if (overlap > 0 && !expected.AsSpan(offset, overlap).SequenceEqual(range.Expected.AsSpan(0, overlap)))
                        throw new UnsupportedSignatureException("Overlapping held import obligations disagree about the exact expected bytes.");
                    var length = checked(offset + range.Expected.Length);
                    if (length <= expected.Length) continue;
                    var merged = new byte[length]; expected.CopyTo(merged, 0); range.Expected.CopyTo(merged, offset); expected = merged;
                }
                if (expected is not null) blocks.Add(new(begin, expected));
                if (blocks.Count == 0) throw new UnsupportedSignatureException("The held import plan cannot omit all actual read ranges.");
                return blocks.ToArray();
            }
        }
    }
}
