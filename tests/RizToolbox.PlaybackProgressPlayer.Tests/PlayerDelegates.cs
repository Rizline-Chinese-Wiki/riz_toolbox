using System.Runtime.InteropServices;
internal static partial class StockProviderChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong PlayerRead(uint field);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PlayerMode(uint mode);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreatePlayer(IntPtr source);
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address,nuint length,uint protection,out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process,IntPtr address,nuint length);
    private static void Mutate(IntPtr address,byte value) {
        if(!VirtualProtect(address,1,0x40,out var old))throw new InvalidOperationException("Fixture protection unavailable.");
        Marshal.WriteByte(address,value);VirtualProtect(address,1,old,out _);FlushInstructionCache((IntPtr)(-1),address,1);
    }
}
