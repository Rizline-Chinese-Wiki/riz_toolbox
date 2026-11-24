using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class ClockCodeProfileChecks
{
    private sealed class Main : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Wrong test thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        { VerifyAccess(); cancellationToken.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
    }
    private sealed class TestLog : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
    private static void Reject(Action action)
    {
        try { action(); } catch (UnsupportedSignatureException) { return; }
        throw new InvalidOperationException("Expected the current code profile to refuse this evidence.");
    }
    // Explicit offline mode: no DllMain/import initialization and no game code calls.
    // Input is a D-drive evidence copy, never an installed game directory.
    internal static int RunMapped(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.StartsWith("D:\\riz-build-cache-next\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use an isolated D-drive evidence copy of the pinned image.");
        var module = LoadLibraryEx(path, IntPtr.Zero, 1); // DONT_RESOLVE_DLL_REFERENCES
        if (module == IntPtr.Zero) throw new InvalidOperationException("The offline image could not be mapped: " + Marshal.GetLastWin32Error());
        GameplayClockCodeProfile? profile = null;
        var checks = 0;
        try
        {
            profile = new GameplayClockCodeProfile(module);
            profile.VerifyCurrent(); checks++;
            foreach (var (rva, producer) in new[]
            {
                (0x400fe0, "InLevel.ProgressControl.Update"),
                (0x400c80, "InLevel.ProgressControl.RealTimeToChartTime"),
                (0x400400, "InLevel.ProgressControl.ChartTimeToRealTime"),
                (0x3e5670, "InLevel.Chart.get_ChartDelay"),
                (0x3fbb10, "Pigeon.MUGToolkit.Core.MUGMusicPlayer.GetTime")
            })
            {
                var address = IntPtr.Add(module, rva + 1); var original = Marshal.ReadByte(address);
                Write(address, (byte)(original ^ 0x01));
                try
                {
                    try { profile.VerifyCurrent(); throw new InvalidOperationException("A changed clock body was accepted: " + rva.ToString("x")); }
                    catch (UnsupportedSignatureException error) when (error.Message.Contains($"{producer} at RVA 0x{rva:X}", StringComparison.Ordinal))
                    {
                        Console.WriteLine($"Refused modified {producer} at RVA 0x{rva:X}.");
                        checks++;
                    }
                }
                finally { Write(address, original); }
                profile.VerifyCurrent();
            }
        }
        finally { if (!FreeLibrary(module)) throw new InvalidOperationException("The evidence image did not unload."); }
        try { profile!.VerifyCurrent(); throw new InvalidOperationException("An unloaded clock image was accepted."); }
        catch (UnsupportedSignatureException) { checks++; }
        Console.WriteLine($"{checks}/7 mapped clock code checks passed. Game code calls=0; DllMain calls=0.");
        return checks == 7 ? 0 : 1;
    }

    private static void Write(IntPtr address, byte value)
    {
        if (!VirtualProtect(address, 1, 0x40, out var old)) throw new InvalidOperationException("Cannot modify the isolated test mapping.");
        try { Marshal.WriteByte(address, value); }
        finally { VirtualProtect(address, 1, old, out _); FlushInstructionCache(GetCurrentProcess(), address, 1); }
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualProtect(IntPtr address, nuint size, uint flags, out uint old);
    [DllImport("kernel32")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint bytes);
}
