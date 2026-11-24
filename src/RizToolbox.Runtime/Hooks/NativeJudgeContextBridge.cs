using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeJudgeXmm { internal ulong Low, High; }

/// <summary>Internal immutable copy of the native entry snapshot. Never part of a plugin contract.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x1a0)]
internal struct NativeJudgeFrame
{
    [FieldOffset(0)] internal uint AbiVersion;
    [FieldOffset(4)] internal uint ByteSize;
    [FieldOffset(8)] internal ulong ReturnAddress;
    [FieldOffset(0x10)] internal ulong EntryStackPointer;
    [FieldOffset(0x18)] internal ulong Reserved;
    [FieldOffset(0x20)] internal ulong Rax;
    [FieldOffset(0x28)] internal ulong Rcx;
    [FieldOffset(0x30)] internal ulong Rdx;
    [FieldOffset(0x38)] internal ulong Rbx;
    [FieldOffset(0x40)] internal ulong Rsp;
    [FieldOffset(0x48)] internal ulong Rbp;
    [FieldOffset(0x50)] internal ulong Rsi;
    [FieldOffset(0x58)] internal ulong Rdi;
    [FieldOffset(0x60)] internal ulong R8;
    [FieldOffset(0x68)] internal ulong R9;
    [FieldOffset(0x70)] internal ulong R10;
    [FieldOffset(0x78)] internal ulong R11;
    [FieldOffset(0x80)] internal ulong R12;
    [FieldOffset(0x88)] internal ulong R13;
    [FieldOffset(0x90)] internal ulong R14;
    [FieldOffset(0x98)] internal ulong R15;
    [FieldOffset(0xa0)] internal NativeJudgeXmm Xmm0;
    [FieldOffset(0xb0)] internal NativeJudgeXmm Xmm1;
    [FieldOffset(0xc0)] internal NativeJudgeXmm Xmm2;
    [FieldOffset(0xd0)] internal NativeJudgeXmm Xmm3;
    [FieldOffset(0xe0)] internal NativeJudgeXmm Xmm4;
    [FieldOffset(0xf0)] internal NativeJudgeXmm Xmm5;
    [FieldOffset(0x100)] internal NativeJudgeXmm Xmm6;
    [FieldOffset(0x110)] internal NativeJudgeXmm Xmm7;
    [FieldOffset(0x120)] internal NativeJudgeXmm Xmm8;
    [FieldOffset(0x130)] internal NativeJudgeXmm Xmm9;
    [FieldOffset(0x140)] internal NativeJudgeXmm Xmm10;
    [FieldOffset(0x150)] internal NativeJudgeXmm Xmm11;
    [FieldOffset(0x160)] internal NativeJudgeXmm Xmm12;
    [FieldOffset(0x170)] internal NativeJudgeXmm Xmm13;
    [FieldOffset(0x180)] internal NativeJudgeXmm Xmm14;
    [FieldOffset(0x190)] internal NativeJudgeXmm Xmm15;
    internal IntPtr Receiver => new(unchecked((long)Rcx));
    internal int Judge => unchecked((int)Rdx);
    internal float Delta => BitConverter.Int32BitsToSingle(unchecked((int)Xmm2.Low));
    internal bool IsValid => AbiVersion == 1 && ByteSize == 0x1a0 && Reserved == 0
        && ReturnAddress != 0 && Rsp == EntryStackPointer && (Rsp & 15) == 8;
}

[Flags]
internal enum NativeJudgeExitFlags : uint { Returned = 1, Abnormal = 2, EnterFaulted = 4 }
internal sealed record NativeJudgeActivity(long InFlight, bool Known);

internal interface INativeJudgeContextBridge
{
    IntPtr EntryAddress { get; }
    void Configure(IntPtr original, Func<NativeJudgeFrame, nuint> enter,
        Action<NativeJudgeFrame, nuint, NativeJudgeExitFlags> exit);
    NativeJudgeActivity ReadActivity() => new(0, false);
}

/// <summary>Process lifetime owner. This library and reverse P/Invoke delegates must never be unloaded.</summary>
internal sealed partial class NativeJudgeContextBridge : INativeJudgeContextBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(ref Configuration config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetEntryNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetFrameSizeNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(ref Statistics statistics);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, nuint token, uint flags, IntPtr user);
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    {
        internal uint Version, Size;
        internal IntPtr Original, Enter, Exit, User;
        internal uint Enabled, Reserved;
    }
    private readonly IntPtr _library;
    private readonly ConfigureNative _configure;
    private readonly ReadNative _read;
    [StructLayout(LayoutKind.Sequential)] private struct Statistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    private readonly EnterNative _enterNative;
    private readonly ExitNative _exitNative;
    private readonly ILogger _log;
    private Func<NativeJudgeFrame, nuint>? _enter;
    private Action<NativeJudgeFrame, nuint, NativeJudgeExitFlags>? _exit;
    private volatile bool _configured;
    private int _unknown;
    public IntPtr EntryAddress { get; }

    internal NativeJudgeContextBridge(string absoluteLibraryPath, ILogger log)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("The judge context bridge requires Windows x64.");
        if (!Path.IsPathFullyQualified(absoluteLibraryPath)) throw new ArgumentException("An absolute native helper path is required.");
        _log = log;
        _path = Path.GetFullPath(absoluteLibraryPath);
        _library = NativeLibrary.Load(_path);
        _configure = Export<ConfigureNative>("RizJudgeContext_Configure");
        _read = Export<ReadNative>("RizJudgeContext_GetStats");
        _readConfiguration = Export<ReadConfigurationNative>("RizJudgeContext_ReadConfigurationV1");
        _getEntry = Export<GetEntryNative>("RizJudgeContext_GetEntry");
        var size = Export<GetFrameSizeNative>("RizJudgeContext_GetFrameSize")();
        if (size != 0x1a0 || Marshal.SizeOf<NativeJudgeFrame>() != size || Marshal.SizeOf<Configuration>() != 0x30
            || Marshal.SizeOf<Statistics>() != 0x30 || Marshal.SizeOf<CurrentConfiguration>() != 0x48)
            throw new UnsupportedSignatureException("The native judge frame/configuration ABI differs from version 1.");
        EntryAddress = _getEntry();
        if (EntryAddress == IntPtr.Zero) throw new UnsupportedSignatureException("The native judge entry is null.");
        _enterNative = Enter; _exitNative = Exit;
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void Configure(IntPtr original, Func<NativeJudgeFrame, nuint> enter,
        Action<NativeJudgeFrame, nuint, NativeJudgeExitFlags> exit)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        if (_configureAttempted || original == IntPtr.Zero) throw new InvalidOperationException("A native judge bridge requires one non-null original and one immutable configuration.");
        _configureAttempted = true;
        _enter = enter; _exit = exit; // root all managed callbacks before native configuration can publish
        var config = new Configuration { Version = 1, Size = 0x30, Original = original, Enabled = 1,
            Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative) };
        _configuredIdentity = config; _configuredEnter = enter; _configuredExit = exit;
        _enterMemory = CaptureCallback(config.Enter); _exitMemory = CaptureCallback(config.Exit);
        var result = _configure(ref config);
        if (result != 0) { Interlocked.Exchange(ref _unknown, 1); throw new InvalidOperationException($"NativeJudgeConfigurationFailed: {result}."); }
        _configured = true;
        VerifyConfiguration(original);
    }
    private unsafe nuint Enter(IntPtr frame, IntPtr user)
    {
        try
        {
            var copy = *(NativeJudgeFrame*)frame;
            return _enter!(copy);
        }
        catch (Exception error) { Interlocked.Exchange(ref _unknown, 1); Log("Native judge enter observer failed; forwarding original.", error); return 0; }
    }
    private unsafe void Exit(IntPtr frame, nuint token, uint flags, IntPtr user)
    {
        // Managed exceptions must be contained here; native SEH containment is not
        // a substitute for a safe CLR reverse P/Invoke boundary.
        try { _exit!(*(NativeJudgeFrame*)frame, token, (NativeJudgeExitFlags)flags); }
        catch (Exception error) { Interlocked.Exchange(ref _unknown, 1); Log("Native judge exit observer failed.", error); }
    }
    public NativeJudgeActivity ReadActivity()
    {
        if (!_configured) return new(0, false);
        try
        {
            var statistics = new Statistics { Version = 1, Size = 0x30 };
            if (_read(ref statistics) != 0 || statistics.Version != 1 || statistics.Size != 0x30
                || statistics.InFlight > long.MaxValue || statistics.EnterFaults != 0 || statistics.ExitFaults != 0)
            { Interlocked.Exchange(ref _unknown, 1); return new(0, false); }
            // Independent atomic fields may come from different instants while a
            // worker runs. Entered/Exited totals are not a coherent drain snapshot.
            return new((long)statistics.InFlight, Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error)
        { Interlocked.Exchange(ref _unknown, 1); Log("Native judge activity read failed.", error); return new(0, false); }
    }
    private void Log(string message, Exception error) { try { _log.Log(LogLevel.Error, message, error); } catch { } }
}
