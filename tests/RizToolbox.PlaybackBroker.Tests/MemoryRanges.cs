using System.Runtime.InteropServices;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class BrokerChecks
{
    private static void CheckMemoryRanges()
    {
        var allocation = VirtualAlloc(IntPtr.Zero, 16384, 0x3000, 4);
        Check(allocation != IntPtr.Zero, "Dedicated range test allocation failed.");
        try
        {
            var seal = NativePlaybackExecutableSeal.RangeSeal.Capture(IntPtr.Add(allocation, 32), 88, allocation, false);
            Check(VirtualProtect(IntPtr.Add(allocation, 8192), 4096, 2, out _), "Neighbor region split failed.");
            seal.Verify(); Check(true, "Unchanged covered bytes were rejected after adjacent split.");
            Check(VirtualProtect(IntPtr.Add(allocation, 8192), 4096, 4, out _), "Neighbor region merge failed.");
            seal.Verify(); Check(true, "Unchanged covered bytes were rejected after adjacent merge.");
            var crossing = NativePlaybackExecutableSeal.RangeSeal.Capture(IntPtr.Add(allocation, 4096 - 32), 64, allocation, false);
            Check(VirtualProtect(IntPtr.Add(allocation, 4096), 4096, 2, out _), "Covered range protection test failed.");
            Reject(crossing.Verify, "Actual covered second-page protection change was accepted.");
            Check(VirtualProtect(IntPtr.Add(allocation, 4096), 4096, 4, out _), "Covered range protection restore failed.");
            crossing.Verify(); Check(true, "Restored covered page failed scalar range verification.");
        }
        finally { Check(VirtualFree(allocation, 0, 0x8000), "Dedicated range allocation was not released."); }
        var owner = Marshal.AllocHGlobal(88); var neighbors = new List<IntPtr>();
        try
        {
            Marshal.Copy(new byte[88], 0, owner, 88);
            var heap = NativePlaybackExecutableSeal.RangeSeal.Capture(owner, 88, IntPtr.Zero, false);
            for (var index = 0; index < 64; ++index)
            {
                neighbors.Add(Marshal.AllocHGlobal(32768 + index * 128));
                _ = new StringBuilder(32768).Append('x', 32700).ToString();
                heap.Verify();
            }
            foreach (var pointer in neighbors.AsEnumerable().Reverse()) Marshal.FreeHGlobal(pointer);
            neighbors.Clear(); heap.Verify();
            Check(true, "Actual heap growth and release changed an unrelated retained 88-byte range.");
        }
        finally { foreach (var pointer in neighbors) Marshal.FreeHGlobal(pointer); Marshal.FreeHGlobal(owner); }
    }
    [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint bytes, uint allocationType, uint protection);
    [DllImport("kernel32")] private static extern bool VirtualFree(IntPtr address, nuint bytes, uint freeType);
}
