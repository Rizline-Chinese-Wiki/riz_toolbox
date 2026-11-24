using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackExecutableSeal
{
    private sealed record FileSection(uint Size, uint Rva, uint RawSize, uint RawOffset, uint Flags);
    private void VerifyExecutableFile(BinaryReader reader, int pe, ushort optionalSize, ushort count, uint imageSize)
    {
        var sections = new List<FileSection>();
        for (var index = 0; index < count; ++index)
        {
            _file.Position = pe + 24 + optionalSize + index * 40 + 8;
            var size = reader.ReadUInt32(); var rva = reader.ReadUInt32(); var rawSize = reader.ReadUInt32(); var raw = reader.ReadUInt32();
            _file.Position += 12; var flags = reader.ReadUInt32();
            if (raw > _file.Length || rawSize > _file.Length - raw) throw new UnsupportedSignatureException("Playback raw section exceeds its pinned file.");
            sections.Add(new(size, rva, rawSize, raw, flags));
        }
        _file.Position = pe + 24 + 24; var preferred = reader.ReadUInt64();
        var delta = unchecked((ulong)_module.ToInt64() - preferred);
        var relocations = new HashSet<uint>();
        _file.Position = pe + 24 + 108; var directories = reader.ReadUInt32();
        if (directories > 5 && optionalSize >= 160)
        {
            _file.Position = pe + 24 + 112 + 5 * 8; var relocRva = reader.ReadUInt32(); var relocSize = reader.ReadUInt32();
            if (relocSize != 0)
            {
                var relocation = FileBytes(relocRva, checked((int)relocSize));
                for (var offset = 0; offset < relocation.Length;)
                {
                    if (relocation.Length - offset < 8) throw new UnsupportedSignatureException("Playback base-relocation block is truncated.");
                    var page = BitConverter.ToUInt32(relocation, offset); var size = BitConverter.ToUInt32(relocation, offset + 4);
                    if (size < 8 || (size & 1) != 0 || size > relocation.Length - offset)
                        throw new UnsupportedSignatureException("Playback base-relocation block size differs.");
                    for (var at = offset + 8; at < offset + size; at += 2)
                    {
                        var item = BitConverter.ToUInt16(relocation, at); var kind = item >> 12;
                        if (kind == 0) continue;
                        var target = checked(page + (uint)(item & 0xfff));
                        if (kind != 10 || target > imageSize - 8 || !relocations.Add(target))
                            throw new UnsupportedSignatureException("Playback relocation is not a unique bounded AMD64 DIR64 slot.");
                    }
                    offset += checked((int)size);
                }
            }
        }
        ulong transportWindows = 0;
        foreach (var section in sections.Where(value => (value.Flags & 0x20000000) != 0 && value.Size != 0))
        {
            var expected = new byte[checked((int)section.Size)];
            var rawCount = checked((int)Math.Min(section.RawSize, section.Size));
            _file.Position = section.RawOffset;
            if (_file.Read(expected, 0, rawCount) != rawCount) throw new UnsupportedSignatureException("Playback executable raw section is truncated.");
            foreach (var rva in relocations.Where(value => value >= section.Rva && value < (ulong)section.Rva + section.Size))
            {
                var relative = checked((int)(rva - section.Rva));
                if (expected.Length - relative < 8) throw new UnsupportedSignatureException("Playback DIR64 slot crosses its executable section.");
                var rebased = unchecked(BitConverter.ToUInt64(expected, relative) + delta);
                BitConverter.GetBytes(rebased).CopyTo(expected, relative);
            }
            if (_group is not null || _pitchPair is not null || _transport is not null)
                _executableFileRanges.Add((section.Rva, (byte[])expected.Clone()));
            if (_transport is not null)
                transportWindows |= _transport.ComposeVerifiedPlaybackWindows(_module, FileSha256, section.Rva, expected);
            if (_group is not null)
            {
                _group.ComposeVerifiedMembers(_module, section.Rva, expected);
            }
            if (_pitchPair is not null)
            {
                _pitchPair.ComposeVerifiedMembers(section.Rva, expected);
            }
            var mapped = RangeSeal.Read(IntPtr.Add(_module, checked((int)section.Rva)), expected.Length);
            if (!mapped.AsSpan().SequenceEqual(expected))
            {
                var first = 0;
                while (mapped[first] == expected[first]) ++first;
                var windowLength = Math.Min(16, expected.Length - first);
                throw new UnsupportedSignatureException("Playback executable section differs from the pinned file plus its exact AMD64 base relocations. "
                    + $"module={Path.GetFileName(_path)}; sectionRVA=0x{section.Rva:x}; firstDiffRVA=0x{checked(section.Rva + (uint)first):x}; "
                    + $"expected={Convert.ToHexString(expected.AsSpan(first, windowLength))}; actual={Convert.ToHexString(mapped.AsSpan(first, windowLength))}.");
            }
        }
        if (_transport is not null && transportWindows != NativeEngineTransportPublicationLayout.AllWindows)
            throw new UnsupportedSignatureException("Playback executable sections do not cover all 36 retained V5 transport windows.");
        byte[] FileBytes(uint rva, int size)
        {
            var section = sections.SingleOrDefault(value => rva >= value.Rva && rva - value.Rva <= value.RawSize
                && size <= value.RawSize - (rva - value.Rva));
            if (section is null || size < 0) throw new UnsupportedSignatureException("Playback relocation directory is outside pinned raw sections.");
            _file.Position = checked(section.RawOffset + rva - section.Rva); var bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw new UnsupportedSignatureException("Playback relocation directory is truncated.");
            return bytes;
        }
    }
}
