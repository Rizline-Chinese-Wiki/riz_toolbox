using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.LineRingSource;

// Copied synthetic SEC_IMAGE helper from Runtime.Tests/ListenerMetadataRegression.
// It validates module/RVA membership and executes no mapped machine code.
internal static partial class NativeSeekLineRingSourceChecks
{
    private sealed class RendererSyntheticImage : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "RizToolbox.RendererConsumer." + Guid.NewGuid().ToString("N") + ".dll");
        private readonly FileStream _file; private readonly IntPtr _mapping;
        internal readonly IntPtr Address;
        internal RendererSyntheticImage()
        {
            var bytes = new byte[0x400]; using (var writer = new BinaryWriter(new MemoryStream(bytes)))
            {
                writer.Write((ushort)0x5A4D); writer.BaseStream.Position = 0x3C; writer.Write(0x80); writer.BaseStream.Position = 0x80;
                writer.Write(0x4550); writer.Write((ushort)0x8664); writer.Write((ushort)1); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write((ushort)0xF0); writer.Write((ushort)0x2022);
                var optional = writer.BaseStream.Position; writer.Write((ushort)0x20B); writer.BaseStream.Position = optional + 24; writer.Write(0x180000000UL);
                writer.Write(0x1000); writer.Write(0x200); writer.BaseStream.Position = optional + 40; writer.Write((ushort)6); writer.Write((ushort)0);
                writer.BaseStream.Position = optional + 48; writer.Write((ushort)6); writer.Write((ushort)0);
                writer.BaseStream.Position = optional + 56; writer.Write(0x1570000); writer.Write(0x200); writer.BaseStream.Position = optional + 68; writer.Write((ushort)3);
                writer.BaseStream.Position = optional + 108; writer.Write(16); writer.BaseStream.Position = optional + 0xF0;
                // The first image section starts at SectionAlignment, as required
                // by the Windows image mapper. Its zero-filled virtual tail covers
                // listener and Tween pinned RVAs without code execution or DllMain.
                writer.Write(new byte[] { 46, 116, 101, 120, 116, 0, 0, 0 }); writer.Write(0x156E000); writer.Write(0x1000); writer.Write(0x200); writer.Write(0x200);
                writer.Write(0); writer.Write(0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0x60000020);
            }
            File.WriteAllBytes(_path, bytes); _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _mapping = CreateFileMapping(_file.SafeFileHandle.DangerousGetHandle(), IntPtr.Zero, 0x1000002, 0, 0, null);
            if (_mapping == IntPtr.Zero) throw new InvalidOperationException("Synthetic metadata SEC_IMAGE mapping failed: " + Marshal.GetLastWin32Error());
            Address = MapViewOfFile(_mapping, 4, 0, 0, 0);
            if (Address == IntPtr.Zero) throw new InvalidOperationException("Synthetic metadata image view failed: " + Marshal.GetLastWin32Error());
        }
        public void Dispose() { UnmapViewOfFile(Address); CloseHandle(_mapping); _file.Dispose(); File.Delete(_path); }
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attributes, uint protect, uint high, uint low, string? name);
        [DllImport("kernel32", SetLastError = true)] private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint high, uint low, nuint size);
        [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnmapViewOfFile(IntPtr view);
        [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
}
