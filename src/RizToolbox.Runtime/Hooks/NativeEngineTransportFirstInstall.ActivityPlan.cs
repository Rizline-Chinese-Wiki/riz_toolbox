using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeEngineTransportFirstInstall
{
    // This is a held-file parsing plan, not a cached observation or permission.
    // All source, protection, Windows RF and Kernel checks remain in the real
    // live verifier. Only repeated immutable export decoding and repeated reads
    // of one destination's complete export group share this exact boundary.
    private sealed partial class ModuleFacts
    {
        private readonly Dictionary<uint, string> _heldNames = new();
        private HeldExportFacts? _heldExports;
        private char[]? _activityPathBuffer;
        private NativeEngineTransportFirstInstall? _activityBoundaryOwner;
        private bool _activityExportsVerified;

        internal void BeginActivityBoundary(NativeEngineTransportFirstInstall owner)
        {
            owner.RequireActivityThread();
            if (_activityBoundaryOwner is not null)
                throw new UnsupportedSignatureException("A module export boundary cannot be nested or transferred.");
            _activityBoundaryOwner = owner; _activityExportsVerified = false;
        }
        internal void EndActivityBoundary()
        {
            _activityExportsVerified = false; _activityBoundaryOwner = null;
        }
        private bool CurrentModulePathMatches()
        {
            if (_activityBoundaryOwner is null)
                return string.Equals(ModulePath(Handle), Path, StringComparison.OrdinalIgnoreCase);
            _activityBoundaryOwner.RequireActivityThread();
            _activityPathBuffer ??= new char[32768];
            var length = GetModuleFileNameCharacters(Handle, _activityPathBuffer, _activityPathBuffer.Length);
            // The first raw path was independently Canonical-checked against
            // the held absolute path during acquisition. Matching that exact
            // UTF16 path retains its canonical semantics without allocating a
            // 65KB StringBuilder and a fresh full path on each activity read.
            return length is > 0 and < 32768
                && MemoryExtensions.Equals(_activityPathBuffer.AsSpan(0, checked((int)length)),
                    _rawModulePath.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }
        private void VerifyHeldExports(HeldExportFacts exports)
        {
            if (_activityBoundaryOwner is not null)
            {
                _activityBoundaryOwner.RequireActivityThread();
                if (_activityExportsVerified) return;
            }
            foreach (var block in exports.Blocks)
            {
                var address = Pointer(checked(Address(Handle) + block.Rva));
                Memory(address, block.Expected.Length, Handle, 0x02);
                if (!ReadProcessMemory(GetCurrentProcess(), address, block.Scratch, (nuint)block.Scratch.Length, out var read)
                    || read != (nuint)block.Scratch.Length || !block.Scratch.AsSpan().SequenceEqual(block.Expected))
                    throw new UnsupportedSignatureException("The complete actual export directory/EAT/name/ordinal/ASCII group changed.");
            }
            // This flag is set only after every real read and protection
            // succeeded, and finally cleared before this live boundary exits.
            if (_activityBoundaryOwner is not null) _activityExportsVerified = true;
        }
        private sealed class HeldExportBlock
        {
            internal readonly uint Rva;
            internal readonly byte[] Expected, Scratch;
            internal HeldExportBlock(uint rva, byte[] expected)
                => (Rva, Expected, Scratch) = (rva, expected, new byte[expected.Length]);
        }
        private sealed class HeldExportFacts
        {
            internal readonly Dictionary<string, uint> Named = new(StringComparer.Ordinal);
            internal readonly HeldExportBlock[] Blocks;
            internal HeldExportFacts(ModuleFacts module)
            {
                if (module._exportRva == 0 || module._exportBytes < 40
                    || (ulong)module._exportRva + module._exportBytes > module.ImageSize)
                    throw new UnsupportedSignatureException("The fixed graph destination has no complete export table.");
                var header = module.Disk(module._exportRva, 40);
                var functionCount = Word(header, 20); var nameCount = Word(header, 24);
                if (functionCount is 0 or > 65536 || nameCount is 0 or > 65536)
                    throw new UnsupportedSignatureException("Native export table counts exceed the bounded fixed graph.");
                var functions = Word(header, 28); var names = Word(header, 32); var ordinals = Word(header, 36);
                var heldFunctions = module.Disk(functions, checked((int)functionCount * 4));
                var heldNames = module.Disk(names, checked((int)nameCount * 4));
                var heldOrdinals = module.Disk(ordinals, checked((int)nameCount * 2));
                var ranges = new List<(uint Rva, int Bytes)> {
                    (module._exportRva, 40), (functions, heldFunctions.Length), (names, heldNames.Length), (ordinals, heldOrdinals.Length) };
                for (var index = 0; index < nameCount; ++index)
                {
                    var nameRva = Word(heldNames, checked(index * 4)); var name = module.DiskName(nameRva);
                    var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(heldOrdinals.AsSpan(checked(index * 2), 2));
                    if (ordinal >= functionCount || !Named.TryAdd(name, Word(heldFunctions, checked(ordinal * 4))))
                        throw new UnsupportedSignatureException("The held export set contains a duplicate name or an out-of-range ordinal.");
                    ranges.Add((nameRva, checked(name.Length + 1)));
                }
                // Only contiguous or overlapping independently held ranges are
                // merged. No unverified gap or mutable bytes join the group.
                ranges.Sort((left, right) => left.Rva.CompareTo(right.Rva));
                var blocks = new List<HeldExportBlock>();
                var begin = ranges[0].Rva; var end = checked((ulong)begin + (ulong)ranges[0].Bytes);
                for (var index = 1; index <= ranges.Count; ++index)
                {
                    if (index < ranges.Count && ranges[index].Rva <= end)
                    { end = Math.Max(end, checked((ulong)ranges[index].Rva + (ulong)ranges[index].Bytes)); continue; }
                    blocks.Add(new(begin, module.Disk(begin, checked((int)(end - begin)))));
                    if (index == ranges.Count) break;
                    begin = ranges[index].Rva; end = checked((ulong)begin + (ulong)ranges[index].Bytes);
                }
                Blocks = blocks.ToArray();
            }
        }
    }
    [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileNameCharacters(IntPtr module, [Out] char[] output, int capacity);
}
