using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return BrokerChecks.Run(args);

internal static partial class BrokerChecks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action, string reason)
    { ++_checks; try { action(); } catch (UnsupportedSignatureException) { return; } throw new InvalidOperationException(reason); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static byte[] Bytes(IntPtr pointer, int length) { var bytes = new byte[length]; Marshal.Copy(pointer, bytes, 0, length); return bytes; }
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Pointer();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Set(uint branch, uint bits, uint block);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Read(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Last(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WaitEntered();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeCaught(IntPtr entry, IntPtr self, IntPtr method, out uint bits, out uint code);
    private sealed class Main : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Wrong test main thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { VerifyAccess(); action(); return Task.CompletedTask; }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private sealed class FakeEngine : INativeDetourEngine { public Delegate Install(VerifiedHookTarget target, Delegate replacement) => replacement; }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint length);
    private static void Write(IntPtr address, byte[] bytes)
    {
        Check(VirtualProtect(address, (nuint)bytes.Length, 0x40, out var old), "Owned tamper page could not be opened.");
        Marshal.Copy(bytes, 0, address, bytes.Length);
        Check(VirtualProtect(address, (nuint)bytes.Length, old, out _), "Owned tamper page protection was not restored.");
        Check(FlushInstructionCache((IntPtr)(-1), address, (nuint)bytes.Length), "Owned tamper instruction cache was not flushed.");
    }
}
