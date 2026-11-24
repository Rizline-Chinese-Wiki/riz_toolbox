using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Immutable copy of the actual inline patch return frame. RSI and RBP
/// are observations only; the callback cannot modify the native continuation.</summary>
[StructLayout(LayoutKind.Explicit, Size = 0x1b0)]
internal struct NativeEventMidFrame
{
    [FieldOffset(0)] internal uint Version;
    [FieldOffset(4)] internal uint Size;
    [FieldOffset(8)] internal IntPtr ReturnPc;
    [FieldOffset(0x10)] internal ulong EntryRsp;
    [FieldOffset(0x18)] internal ulong Rflags;
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
    [FieldOffset(0x1a0)] internal uint Mxcsr;
    [FieldOffset(0x1a4)] internal ushort Fpcsr;
    [FieldOffset(0x1a6)] internal ushort Reserved0;
    [FieldOffset(0x1a8)] internal ulong Reserved1;
    internal IntPtr Event => new(unchecked((long)Rsi));
    internal IntPtr Executing => new(unchecked((long)Rbp));
    internal bool IsValid => Version == 1 && Size == 0x1b0 && ReturnPc != IntPtr.Zero && EntryRsp != 0
        && EntryRsp <= ulong.MaxValue - 8 && Rsp == EntryRsp + 8 && (EntryRsp & 7) == 0
        && Reserved0 == 0 && Reserved1 == 0;
}

internal interface INativeEventMidContextBridge
{
    IntPtr EntryAddress { get; }
    void Configure(IntPtr expectedReturnPc, Action<NativeEventMidFrame> observe);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}

/// <summary>Owns one immutable native mid-observation slot and roots its library
/// and callback for process lifetime, including configuration uncertainty.</summary>
internal sealed class NativeEventMidContextBridge : INativeEventMidContextBridge
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, uint> Slots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<NativeEventMidContextBridge> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr ExpectedReturnPc, Callback, User; internal ulong Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Statistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, Faults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LimitNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint slot, ref Configuration value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint slot, ref Statistics value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObserveNative(IntPtr frame, IntPtr user);
    private readonly IntPtr _library;
    private readonly uint _slot;
    private readonly ConfigureNative _configure;
    private readonly ReadNative _read;
    private readonly ObserveNative _observeNative;
    private readonly ILogger _log;
    private Action<NativeEventMidFrame>? _observe;
    private IntPtr _expectedReturnPc;
    private volatile bool _configured;
    private int _configurationAttempted, _unknown;
    public IntPtr EntryAddress { get; }
    internal NativeEventMidContextBridge(string absolutePath, ILogger log)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Native event mid transport requires Windows x64.");
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("An absolute helper path is required.");
        ArgumentNullException.ThrowIfNull(log); _log = log;
        var path = Path.GetFullPath(absolutePath); _library = NativeLibrary.Load(path);
        _configure = Export<ConfigureNative>("RizEventMidContext_Configure"); _read = Export<ReadNative>("RizEventMidContext_GetStats");
        if (Export<LimitNative>("RizEventMidContext_GetLimit")() != 32 || Export<LimitNative>("RizEventMidContext_GetFrameSize")() != 0x1b0
            || Marshal.SizeOf<NativeEventMidFrame>() != 0x1b0 || Marshal.SizeOf<Configuration>() != 40 || Marshal.SizeOf<Statistics>() != 40)
            throw new UnsupportedSignatureException("Native event mid transport differs from ABI version 1.");
        EntryAddress = Export<EntryNative>("RizEventMidContext_GetEntry")();
        if (EntryAddress == IntPtr.Zero) throw new UnsupportedSignatureException("Native event mid entry is null.");
        _observeNative = Observe;
        lock (Gate)
        {
            Slots.TryGetValue(path, out _slot);
            while (_slot < 32 && NativeBootstrappedVoidBridge.IsNativeSlotClaimed(_library, _slot)) ++_slot;
            if (_slot >= 32) throw new InvalidOperationException("The immutable native event mid capacity is exhausted.");
            Slots[path] = _slot + 1; Owners.Add(this);
        }
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void Configure(IntPtr expectedReturnPc, Action<NativeEventMidFrame> observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        if (expectedReturnPc == IntPtr.Zero || Interlocked.CompareExchange(ref _configurationAttempted, 1, 0) != 0)
            throw new InvalidOperationException("Native event mid configuration is immutable and requires its actual return PC.");
        _observe = observe; _expectedReturnPc = expectedReturnPc;
        var value = new Configuration { Version = 1, Size = 40, ExpectedReturnPc = expectedReturnPc,
            Callback = Marshal.GetFunctionPointerForDelegate(_observeNative) };
        try
        {
            if (_configure(_slot, ref value) != 0) throw new InvalidOperationException("Native event mid configuration rejected.");
            _configured = true;
        }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }
    private unsafe void Observe(IntPtr frame, IntPtr user)
    {
        try
        {
            if (frame == IntPtr.Zero) throw new InvalidOperationException("Native event mid frame is null.");
            var copy = *(NativeEventMidFrame*)frame;
            if (!copy.IsValid || copy.ReturnPc != _expectedReturnPc)
                throw new UnsupportedSignatureException("Native event mid frame differs from the configured actual return PC.");
            _observe!(copy);
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref _unknown, 1);
            try { _log.Log(LogLevel.Error, "Native event mid observation failed; continuation is preserved.", error); } catch { }
        }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        if (!_configured) return new(0, false);
        try
        {
            var value = new Statistics { Version = 1, Size = 40 };
            if (_read(_slot, ref value) != 0 || value.Version != 1 || value.Size != 40 || value.InFlight > long.MaxValue)
            { Interlocked.Exchange(ref _unknown, 1); return new(0, false); }
            if (value.Faults != 0) Interlocked.Exchange(ref _unknown, 1);
            return new((long)value.InFlight, Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref _unknown, 1);
            try { _log.Log(LogLevel.Error, "Native event mid statistics read failed.", error); } catch { }
            return new(0, false);
        }
    }
}
