using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

// Independently held PE/image facts; never consumes a caller publication as proof.
internal sealed partial class NativeEngineTransportFirstInstall
{
    private static string Canonical(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute native module path is required.", nameof(path));
        return System.IO.Path.GetFullPath(path);
    }
    private static string Digest(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("An independent SHA256 build fingerprint is required.", nameof(value));
        return value.ToLowerInvariant();
    }
    private static ulong Address(IntPtr value) => unchecked((ulong)value.ToInt64());
    private static string ModulePath(IntPtr module)
        => Canonical(RawModulePath(module));
    private static string RawModulePath(IntPtr module)
    {
        var text = new StringBuilder(32768); var count = GetModuleFileName(module, text, text.Capacity);
        if (count is 0 or >= 32768) throw new UnsupportedSignatureException("Cannot identify the loaded native module path.");
        return text.ToString();
    }
    private static void Memory(IntPtr address, int bytes, IntPtr module, uint protection)
    {
        if (bytes <= 0 || Address(address) > ulong.MaxValue - (ulong)bytes) throw new UnsupportedSignatureException("Invalid native memory range.");
        var current = Address(address); var end = current + (ulong)bytes;
        while (current < end)
        {
            if (VirtualQuery(Pointer(current), out var memory, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0
                || memory.AllocationBase != module || memory.State != 0x1000 || memory.Type != 0x1000000
                || (memory.Protect & 0x101) != 0 || (protection != 0 && memory.Protect != protection)
                || current < Address(memory.BaseAddress) || current - Address(memory.BaseAddress) >= (ulong)memory.RegionSize)
                throw new UnsupportedSignatureException("Actual native range is not committed exact-module image memory with the required protection.");
            var next = Address(memory.BaseAddress) + (ulong)memory.RegionSize;
            if (next <= current) throw new UnsupportedSignatureException("Invalid native memory region bounds.");
            current = Math.Min(next, end);
        }
    }
    private static byte[] ReadMemory(IntPtr address, int bytes, IntPtr module, uint protection)
    {
        Memory(address, bytes, module, protection); var data = new byte[bytes];
        if (!ReadProcessMemory(GetCurrentProcess(), address, data, (nuint)bytes, out var read) || read != (nuint)bytes)
            throw new UnsupportedSignatureException("Cannot copy the exact native publication memory range.");
        return data;
    }

    private sealed partial class ModuleFacts
    {
        internal readonly string Path, Hash;
        internal readonly IntPtr Handle;
        internal readonly byte[] Data;
        internal readonly uint Machine, ImageSize, Timestamp, EntryRva, ExceptionRva, VolumeSerial;
        internal readonly ulong FileIndex;
        private readonly FileStream _held;
        private readonly bool _executable;
        private readonly string _rawModulePath;
        private readonly uint _headerSize;
        private readonly uint _importRva, _importBytes, _exportRva, _exportBytes, _delayRva, _delayBytes;
        private readonly List<(uint Rva, uint Raw, uint Bytes)> _sections = new();
        private readonly List<Function> _functions = new();
        // Immutable held-file facts only. Live metadata/Windows registration
        // remain checked on every VerifyReady; an ordinal is not an observation.
        private readonly Dictionary<Function, uint> _functionOrdinals = new();
        internal ModuleFacts(string path, string hash, bool executable)
        {
            Path = path; Hash = hash; _executable = executable;
            Handle = executable ? GetModuleHandle(null) : GetModuleHandle(path);
            _rawModulePath = RawModulePath(Handle);
            if (Handle == IntPtr.Zero || !string.Equals(Canonical(_rawModulePath), path, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The exact native publication dependency is not already loaded from its canonical path.");
            _held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            lock (OwnersGate) PermanentRoots.Add(this); // Preserve actual handles even if later identity verification refuses adoption.
            if (_held.Length is <= 0 or > 268435456) throw new UnsupportedSignatureException("Native image file length is out of bounds.");
            Data = new byte[checked((int)_held.Length)]; var used = 0;
            while (used < Data.Length) { var count = _held.Read(Data, used, Data.Length - used); if (count == 0) throw new EndOfStreamException(); used += count; }
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(Data)), hash, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("An already-loaded native module differs from its independent exact build hash.");
            if (!GetFileInformationByHandle(_held.SafeFileHandle.DangerousGetHandle(), out var id))
                throw new UnsupportedSignatureException("Cannot identify the independently held native file.");
            VolumeSerial = id.Volume; FileIndex = ((ulong)id.IndexHigh << 32) | id.IndexLow;
            if (U16(0) != 0x5a4d) throw new UnsupportedSignatureException("Native image DOS header is invalid.");
            var nt = checked((int)U32(0x3c)); var optional = checked(nt + 24);
            if (U32(nt) != 0x4550 || U16(nt + 4) != 0x8664 || U16(optional) != 0x20b || U32(optional + 108) < 4)
                throw new UnsupportedSignatureException("Native image is not a complete AMD64 PE32+ image.");
            Machine = U16(nt + 4); Timestamp = U32(nt + 8); EntryRva = U32(optional + 16);
            ImageSize = U32(optional + 56); _headerSize = U32(optional + 60); ExceptionRva = U32(optional + 112 + 3 * 8);
            _exportRva = U32(optional + 112); _exportBytes = U32(optional + 116);
            _importRva = U32(optional + 120); _importBytes = U32(optional + 124);
            _delayRva = U32(optional + 216); _delayBytes = U32(optional + 220);
            var exceptionBytes = U32(optional + 116 + 3 * 8); var sectionAt = checked(optional + U16(nt + 20));
            for (var index = 0; index < U16(nt + 6); ++index)
            {
                var at = checked(sectionAt + index * 40); var rva = U32(at + 12); var raw = U32(at + 20); var bytes = U32(at + 16);
                if ((ulong)raw + bytes > (ulong)Data.Length) throw new UnsupportedSignatureException("Native image section is truncated.");
                _sections.Add((rva, raw, bytes));
            }
            if (exceptionBytes == 0 || exceptionBytes % 12 != 0 || exceptionBytes > 16 * 1024 * 1024)
                throw new UnsupportedSignatureException("Native image lacks a bounded complete static unwind table.");
            var pdata = Disk(ExceptionRva, checked((int)exceptionBytes));
            for (var at = 0; at < pdata.Length; at += 12)
            {
                var function = new Function(BinaryPrimitives.ReadUInt32LittleEndian(pdata.AsSpan(at)),
                    BinaryPrimitives.ReadUInt32LittleEndian(pdata.AsSpan(at + 4)), BinaryPrimitives.ReadUInt32LittleEndian(pdata.AsSpan(at + 8)));
                if (function.Begin >= function.End || function.End > ImageSize || function.Unwind >= ImageSize)
                    throw new UnsupportedSignatureException("Native runtime function range is invalid.");
                _functionOrdinals.TryAdd(function, checked((uint)_functions.Count));
                _functions.Add(function);
            }
            VerifyCurrent();
        }
        private ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(at, 2));
        private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(at, 4));
        internal byte[] Disk(uint rva, int bytes)
        {
            if (bytes <= 0) throw new UnsupportedSignatureException("Invalid bounded disk RVA request.");
            if ((ulong)rva + (ulong)bytes <= _headerSize) return Data.AsSpan(checked((int)rva), bytes).ToArray();
            foreach (var section in _sections)
                if (rva >= section.Rva && (ulong)rva - section.Rva + (ulong)bytes <= section.Bytes)
                    return Data.AsSpan(checked((int)(section.Raw + rva - section.Rva)), bytes).ToArray();
            throw new UnsupportedSignatureException("Native RVA does not have a complete held file-backed range.");
        }
        internal void VerifyCurrent()
        {
            if ((_executable ? GetModuleHandle(null) : GetModuleHandle(Path)) != Handle
                || !CurrentModulePathMatches()
                || !GetFileInformationByHandle(_held.SafeFileHandle.DangerousGetHandle(), out var identity)
                || identity.Volume != VolumeSerial || (((ulong)identity.IndexHigh << 32) | identity.IndexLow) != FileIndex
                || (((ulong)identity.SizeHigh << 32) | identity.SizeLow) != (ulong)Data.Length)
                throw new UnsupportedSignatureException("An adopted loaded native dependency or held file changed.");
            var diskHeader = Data.AsSpan(0, checked((int)Math.Min(_headerSize, 4096)));
            Memory(Handle, diskHeader.Length, Handle, 0x02);
            Span<byte> mappedHeader = stackalloc byte[diskHeader.Length];
            unsafe
            {
                fixed (byte* buffer = mappedHeader)
                    if (!ReadProcessMemory(GetCurrentProcess(), Handle, buffer, (nuint)mappedHeader.Length, out var read)
                        || read != (nuint)mappedHeader.Length)
                        throw new UnsupportedSignatureException("Cannot copy the exact native publication memory range.");
            }
            var imageBaseOffset = checked((int)U32(0x3c) + 48); // NT signature + file header + PE32+ ImageBase offset.
            if (imageBaseOffset < 0 || imageBaseOffset > diskHeader.Length - sizeof(ulong))
                throw new UnsupportedSignatureException("The complete PE32+ ImageBase field is outside the held header range.");
            var preferredBase = BinaryPrimitives.ReadUInt64LittleEndian(diskHeader.Slice(imageBaseOffset, sizeof(ulong)));
            var mappedBase = BinaryPrimitives.ReadUInt64LittleEndian(mappedHeader.Slice(imageBaseOffset, sizeof(ulong)));
            if (mappedBase != preferredBase && mappedBase != Address(Handle))
                throw new UnsupportedSignatureException("Mapped PE ImageBase is neither its held preferred base nor its actual loaded module base.");
            // The observed loader may update exactly this validated eight-byte
            // field for ASLR. Normalize only that known value, then retain the
            // full byte comparison of every other mapped DOS/NT/header byte.
            BinaryPrimitives.WriteUInt64LittleEndian(mappedHeader.Slice(imageBaseOffset, sizeof(ulong)), preferredBase);
            if (!mappedHeader.SequenceEqual(diskHeader))
                throw new UnsupportedSignatureException("Mapped PE headers differ from the independently held native image.");
        }
        private static uint Word(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        private static ulong Quad(byte[] data, int at) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at, 8));
        private string DiskName(uint rva)
        {
            if (_heldNames.TryGetValue(rva, out var cached)) return cached;
            var name = new StringBuilder();
            for (uint index = 0; index < 512; ++index)
            {
                var value = Disk(checked(rva + index), 1)[0];
                if (value == 0) { var result = name.ToString(); _heldNames.Add(rva, result); return result; }
                if (value < 0x20 || value > 0x7e) throw new UnsupportedSignatureException("Native import/export name is not bounded ASCII.");
                name.Append((char)value);
            }
            throw new UnsupportedSignatureException("Native import/export name exceeds its fixed bound.");
        }
        internal uint ExportRva(string name, uint expectedProtection = 0x20)
        {
            var exports = _heldExports ??= new HeldExportFacts(this);
            VerifyHeldExports(exports);
            if (!exports.Named.TryGetValue(name, out var found))
                throw new UnsupportedSignatureException("The fixed graph export is missing or duplicated: " + name + ".");
            if (found == 0 || found >= ImageSize || found >= _exportRva && (ulong)found < (ulong)_exportRva + _exportBytes)
                throw new UnsupportedSignatureException("The actual fixed export is forwarded, altered or outside its destination image.");
            Memory(Pointer(Address(Handle) + found), 1, Handle, expectedProtection);
            return found;
        }
        internal void RequireExportSet(IReadOnlyList<string> expectedNames)
        {
            var header = Disk(_exportRva, 40);
            var functions = Word(header, 20); var names = Word(header, 24);
            if (functions != expectedNames.Count || names != expectedNames.Count)
                throw new UnsupportedSignatureException("The V5 dependency has extra named, ordinal-only or missing exports.");
            var table = Word(header, 32); var actual = new HashSet<string>(StringComparer.Ordinal);
            for (uint i = 0; i < names; ++i)
            {
                var name = DiskName(Word(Disk(checked(table + i * 4), 4), 0));
                if (!actual.Add(name)) throw new UnsupportedSignatureException("The V5 export table repeats a name.");
                _ = ExportRva(name);
            }
            if (!actual.SetEquals(expectedNames)) throw new UnsupportedSignatureException("The V5 complete export name set differs.");
        }
        internal IReadOnlyDictionary<string, (uint Lookup, uint Iat, uint Export)> VerifyImportsTo(ModuleFacts destination, IReadOnlyList<string> expectedNames)
        {
            if (_delayRva != 0 || _delayBytes != 0) throw new UnsupportedSignatureException("V5 ordinary dependencies cannot be supplemented by delayed imports.");
            if (_importRva == 0 || _importBytes < 20 || _importBytes > 1024 * 1024 || (ulong)_importRva + _importBytes > ImageSize)
                throw new UnsupportedSignatureException("The fixed graph source has no bounded ordinary import table.");
            var edges = new Dictionary<string, (uint Lookup, uint Iat, uint Export)>(StringComparer.Ordinal);
            var remaining = new HashSet<string>(expectedNames, StringComparer.Ordinal);
            if (remaining.Count != expectedNames.Count || remaining.Count == 0) throw new UnsupportedSignatureException("Invalid fixed ordinary import names.");
            var destinationName = System.IO.Path.GetFileName(destination.Path); var descriptors = 0; var ended = false;
            for (uint offset = 0; offset + 20 <= _importBytes; offset += 20)
            {
                var descriptorRva = checked(_importRva + offset); var descriptor = Disk(descriptorRva, 20);
                if (IsZero(descriptor)) { ended = true; break; }
                if (!string.Equals(DiskName(Word(descriptor, 12)), destinationName, StringComparison.OrdinalIgnoreCase)) continue;
                ++descriptors;
                if (!ReadMemory(Pointer(Address(Handle) + descriptorRva), 20, Handle, 0x02).AsSpan().SequenceEqual(descriptor))
                    throw new UnsupportedSignatureException("An actual fixed ordinary import descriptor changed.");
                var dllName = Word(descriptor, 12); var dllNameBytes = Encoding.ASCII.GetBytes(DiskName(dllName) + '\0');
                if (!ReadMemory(Pointer(Address(Handle) + dllName), dllNameBytes.Length, Handle, 0x02).AsSpan().SequenceEqual(dllNameBytes))
                    throw new UnsupportedSignatureException("An actual fixed ordinary import DLL name changed.");
                var original = Word(descriptor, 0); var iat = Word(descriptor, 16);
                if (original == 0 || iat == 0) throw new UnsupportedSignatureException("The fixed ordinary import has no independent ILT/IAT.");
                var thunkEnded = false;
                for (uint index = 0; index <= 65536; ++index)
                {
                    var iltRva = checked(original + index * 8); var iatRva = checked(iat + index * 8);
                    var held = Disk(iltRva, 8); var nameRva = Quad(held, 0);
                    if (!ReadMemory(Pointer(Address(Handle) + iltRva), 8, Handle, 0x02).AsSpan().SequenceEqual(held))
                        throw new UnsupportedSignatureException("An actual fixed ordinary import name thunk changed.");
                    var slot = ReadMemory(Pointer(Address(Handle) + iatRva), 8, Handle, 0x02);
                    if (nameRva == 0)
                    {
                        if (Quad(slot, 0) != 0) throw new UnsupportedSignatureException("The actual fixed IAT terminator changed.");
                        thunkEnded = true; break;
                    }
                    if (nameRva > uint.MaxValue || (nameRva & 0x8000000000000000) != 0)
                        throw new UnsupportedSignatureException("An ordinal or unbounded import cannot prove the fixed named graph.");
                    var name = DiskName(checked((uint)nameRva + 2));
                    var importNameBytes = Disk((uint)nameRva, checked(name.Length + 3));
                    if (!ReadMemory(Pointer(Address(Handle) + nameRva), importNameBytes.Length, Handle, 0x02).AsSpan().SequenceEqual(importNameBytes))
                        throw new UnsupportedSignatureException("An actual fixed ordinary import name/hint changed.");
                    if (!remaining.Remove(name)) throw new UnsupportedSignatureException("The fixed graph import is unexpected or repeated: " + name + ".");
                    var expected = checked(Address(destination.Handle) + destination.ExportRva(name));
                    if (Quad(slot, 0) != expected) throw new UnsupportedSignatureException("An actual fixed IAT slot resolves to a different module/export: " + name + ".");
                    edges.Add(name, (iltRva, iatRva, checked((uint)(expected - Address(destination.Handle)))));
                }
                if (!thunkEnded) throw new UnsupportedSignatureException("The fixed ordinary import thunks are unterminated.");
            }
            if (!ended || descriptors != 1 || remaining.Count != 0)
                throw new UnsupportedSignatureException("The complete fixed ordinary import edge set is missing, duplicated or unterminated.");
            return edges;
        }
        internal byte[] Unwind(Function function)
        {
            var header = Disk(function.Unwind, 4); var flags = header[0] >> 3;
            if ((header[0] & 7) != 1 || (flags & ~7) != 0 || ((flags & 4) != 0 && (flags & 3) != 0))
                throw new UnsupportedSignatureException("Unsupported or contradictory native unwind metadata.");
            var bytes = (4 + header[2] * 2 + 3) & ~3;
            return Disk(function.Unwind, checked(bytes + ((flags & 4) != 0 ? 12 : (flags & 3) != 0 ? 4 : 0)));
        }
        private Function Root(Function function)
        {
            var seen = new HashSet<Function>();
            for (var depth = 0; depth < 32; ++depth)
            {
                if (!seen.Add(function)) throw new UnsupportedSignatureException("Cyclic native unwind chain.");
                var bytes = Unwind(function); if ((bytes[0] >> 3 & 4) == 0) return function;
                var at = bytes.Length - 12;
                function = new(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 8)));
                if (!_functions.Contains(function)) throw new UnsupportedSignatureException("Native unwind chain leaves its held static function table.");
            }
            throw new UnsupportedSignatureException("Native unwind chain exceeds the fixed bound.");
        }
        internal List<Function> Family(uint begin)
        {
            var first = _functions.Where(x => x.Begin == begin).ToArray();
            if (first.Length != 1) throw new UnsupportedSignatureException("Fixed source has no unique actual static runtime function entry.");
            var root = Root(first[0]);
            var family = _functions.Where(x => Root(x) == root).OrderBy(x => x.Begin).ToList();
            if (family.Count is 0 or > 64 || family[0].Begin != begin)
                throw new UnsupportedSignatureException("Fixed source has an unexpected complete unwind family.");
            for (var index = 1; index < family.Count; ++index)
                if (family[index - 1].End != family[index].Begin) throw new UnsupportedSignatureException("Fixed source body contains a gap or overlapping unwind ranges.");
            return family;
        }
        internal uint FunctionOrdinal(Function value)
        {
            if (!_functionOrdinals.TryGetValue(value, out var ordinal))
                throw new UnsupportedSignatureException("The exact static RF is absent from the held .pdata table.");
            return ordinal;
        }
        internal List<Function> RangeFamily(uint begin, uint end)
        {
            if (begin >= end || end > ImageSize) throw new UnsupportedSignatureException("Invalid fixed full function bounds.");
            var selected = _functions.Where(x => x.Begin >= begin && x.End <= end).OrderBy(x => x.Begin).ToList();
            if (selected.Count is 0 or > 64 || selected[0].Begin != begin || selected[^1].End != end)
                throw new UnsupportedSignatureException("The complete fixed function RF range is missing or truncated.");
            var root = Root(selected[0]);
            for (var i = 0; i < selected.Count; ++i)
                if (Root(selected[i]) != root || i != 0 && selected[i - 1].End != selected[i].Begin)
                    throw new UnsupportedSignatureException("The fixed RF range contains a gap, overlap or foreign unwind root.");
            return selected;
        }
        internal void VerifyUnwind(Function function)
        {
            var address = Pointer(Address(Handle) + function.Begin);
            var entry = RtlLookupFunctionEntry(Address(address), out var imageBase, IntPtr.Zero);
            if (entry == IntPtr.Zero || imageBase != Address(Handle))
                throw new UnsupportedSignatureException("Actual source/original has no matching OS static unwind registration.");
            var bytes = ReadMemory(entry, 12, Handle, 0x02);
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != function.Begin
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != function.End
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != function.Unwind)
                throw new UnsupportedSignatureException("Actual OS unwind tuple differs from independently held static metadata.");
            var unwind = Unwind(function);
            if (!ReadMemory(Pointer(Address(Handle) + function.Unwind), unwind.Length, Handle, 0x02).AsSpan().SequenceEqual(unwind))
                throw new UnsupportedSignatureException("Actual source/original unwind metadata differs from its full held chain bytes.");
        }
    }
    private readonly record struct Function(uint Begin, uint End, uint Unwind);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal nuint RegionSize; internal uint State, Protect, Type; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    { internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder output, int capacity);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation output, nuint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern unsafe bool ReadProcessMemory(IntPtr process, IntPtr address, byte* output, nuint size, out nuint read);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] output, nuint size, out nuint read);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(IntPtr file, out FileInformation info);
    [DllImport("ntdll.dll")] private static extern IntPtr RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr history);
}
