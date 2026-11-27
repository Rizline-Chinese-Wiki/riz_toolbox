using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackLowerPitchTarget
{
    private sealed class PinnedImageFile
    {
        private readonly IntPtr _module;
        private readonly string _path;
        private readonly FileStream _file;
        private readonly PEReader _reader;
        private readonly PEHeader _header;
        private readonly NativePlaybackExecutableSeal.RangeSeal _headers;
        private readonly uint[] _dir64;
        internal PinnedImageFile(IntPtr module, string pin)
        {
            if (pin.Length != 64) throw new UnsupportedSignatureException("No reviewed fixed two-image lower-pitch profile has been issued.");
            _module = module; _path = NativePlaybackExecutableSeal.ModulePath(module);
            _file = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using (var sha = SHA256.Create())
                if (Convert.ToHexString(sha.ComputeHash(_file)).ToLowerInvariant() != pin)
                    throw new UnsupportedSignatureException("The lower target issuer rejected an unpinned image; markers cannot grant admission.");
            _file.Position = 0; _reader = new(_file, PEStreamOptions.LeaveOpen);
            _header = _reader.PEHeaders.PEHeader ?? throw new UnsupportedSignatureException("Missing lower-profile PE header.");
            if (_header.Magic != PEMagic.PE32Plus || _reader.PEHeaders.CoffHeader.Machine != Machine.Amd64
                || _header.SizeOfImage <= 0 || _header.SizeOfHeaders <= 0)
                throw new UnsupportedSignatureException("Lower target issuer requires exact AMD64 SEC_IMAGE files.");
            _headers = NativePlaybackExecutableSeal.RangeSeal.Capture(module, _header.SizeOfHeaders, module, true);
            var diskHeaders = new byte[_header.SizeOfHeaders];
            _file.Position = 0;
            for (var read = 0; read < diskHeaders.Length;)
            {
                var count = _file.Read(diskHeaders, read, diskHeaders.Length - read);
                if (count == 0) throw new UnsupportedSignatureException("Truncated lower-profile PE headers.");
                read += count;
            }
            var imageBaseAt = checked(BitConverter.ToInt32(diskHeaders, 0x3c) + 48);
            var mappedBase = BitConverter.ToInt64(_headers.Bytes, imageBaseAt);
            if (unchecked((ulong)mappedBase) != _header.ImageBase && mappedBase != module.ToInt64())
                throw new UnsupportedSignatureException("Lower-profile mapped headers have an unknown loader ImageBase edit.");
            BitConverter.GetBytes(mappedBase).CopyTo(diskHeaders, imageBaseAt);
            if (!diskHeaders.AsSpan().SequenceEqual(_headers.Bytes))
                throw new UnsupportedSignatureException("Lower-profile mapped headers differ from their exact pinned file.");
            var relocations = new HashSet<uint>();
            var directory = _header.BaseRelocationTableDirectory;
            if (directory.Size > 0)
            {
                var reloc = FileBytes(checked((uint)directory.RelativeVirtualAddress), directory.Size);
                for (var at = 0; at < reloc.Length;)
                {
                    if (reloc.Length - at < 8) throw new UnsupportedSignatureException("Truncated lower-profile DIR64 block.");
                    var page = BitConverter.ToUInt32(reloc, at); var size = BitConverter.ToUInt32(reloc, at + 4);
                    if (size < 8 || (size & 1) != 0 || size > reloc.Length - at)
                        throw new UnsupportedSignatureException("Unbounded lower-profile DIR64 block.");
                    for (var itemAt = at + 8; itemAt < at + size; itemAt += 2)
                    {
                        var item = BitConverter.ToUInt16(reloc, itemAt); var kind = item >> 12;
                        if (kind == 0) continue;
                        var rva = checked(page + (uint)(item & 0xfff));
                        if (kind != 10 || rva > _header.SizeOfImage - 8 || !relocations.Add(rva))
                            throw new UnsupportedSignatureException("Lower-profile relocation is not one unique bounded AMD64 DIR64 slot.");
                    }
                    at += checked((int)size);
                }
            }
            _dir64 = relocations.ToArray();
            Array.Sort(_dir64);
            if (!GetModuleHandleEx(4, module, out var held) || held != module)
                throw new UnsupportedSignatureException("The lower issuer could not retain its actual already loaded image.");
            Verify();
        }
        internal bool HasDir64(uint rva) => Array.BinarySearch(_dir64, rva) >= 0;
        internal byte[] FileBytes(uint rva, int length)
        {
            if (length <= 0 || rva > _header.SizeOfImage || length > _header.SizeOfImage - rva
                || !_reader.PEHeaders.SectionHeaders.Any(section => rva >= section.VirtualAddress
                    && rva - section.VirtualAddress <= section.SizeOfRawData
                    && length <= section.SizeOfRawData - (rva - section.VirtualAddress)))
                throw new UnsupportedSignatureException("Lower-profile evidence is outside a bounded file-backed section.");
            return _reader.GetSectionData(checked((int)rva)).GetContent(0, length).ToArray();
        }
        internal byte[] CanonicalBytes(uint rva, int length)
        {
            var bytes = FileBytes(rva, length);
            var delta = unchecked((ulong)_module.ToInt64() - _header.ImageBase);
            var index = Array.BinarySearch(_dir64, rva);
            if (index < 0) index = ~index;
            var end = (ulong)rva + (uint)length;
            for (; index < _dir64.Length && _dir64[index] < end; ++index)
            {
                var at = checked((int)(_dir64[index] - rva));
                if (bytes.Length - at < 8) throw new UnsupportedSignatureException("A lower-profile DIR64 slot crosses its exact evidence range.");
                BitConverter.GetBytes(unchecked(BitConverter.ToUInt64(bytes, at) + delta)).CopyTo(bytes, at);
            }
            return bytes;
        }
        internal NativePlaybackExecutableSeal.RangeSeal CaptureCanonical(uint rva, int length, bool executable = false)
        {
            var expected = CanonicalBytes(rva, length);
            var seal = NativePlaybackExecutableSeal.RangeSeal.Capture(IntPtr.Add(_module, checked((int)rva)), length, _module, true, executable);
            if (!seal.Bytes.AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("Mapped lower-profile bytes differ from the fixed file plus original DIR64 records.");
            return seal;
        }
        internal void RequireZeroFill(uint rva, int length)
        {
            if (!_reader.PEHeaders.SectionHeaders.Any(section => rva >= section.VirtualAddress
                    && rva - section.VirtualAddress >= section.SizeOfRawData
                    && length <= section.VirtualSize - (rva - section.VirtualAddress)))
                throw new UnsupportedSignatureException("The fixed stock resolver cache is not its original zero-fill image data.");
        }
        internal void Verify()
        {
            if (!NativePlaybackExecutableSeal.ModulePath(_module).Equals(_path, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("A lower-profile loaded image identity changed.");
            _headers.Verify();
        }
        [DllImport("kernel32", EntryPoint = "GetModuleHandleExW")]
        private static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);
    }
}
