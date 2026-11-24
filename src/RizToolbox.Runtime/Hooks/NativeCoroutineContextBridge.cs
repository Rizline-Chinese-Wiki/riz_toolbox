using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeCoroutineKind : uint { Start, MoveNext, Factory }
[Flags] internal enum NativeCoroutineExitFlags : uint { Returned = 1, Abnormal = 2, EnterFaulted = 4 }
[StructLayout(LayoutKind.Sequential)]
internal struct NativeCoroutineFrame
{
    internal uint Version, Size;
    internal NativeCoroutineKind Kind; internal uint Reserved;
    internal IntPtr Self, Enumerator, Method, Caller;
    internal bool IsValid => Version == 1 && Size == 0x30 && Reserved == 0
        && Kind is NativeCoroutineKind.Start or NativeCoroutineKind.MoveNext or NativeCoroutineKind.Factory && Self != IntPtr.Zero && Caller != IntPtr.Zero
        && (Kind == NativeCoroutineKind.Start || Enumerator == IntPtr.Zero);
}
internal interface INativeCoroutineContextBridge
{
    NativeCoroutineKind Kind { get; }
    IntPtr EntryAddress { get; }
    void Configure(IntPtr original, Func<NativeCoroutineFrame, nuint> enter,
        Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags> exit);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}

/// <summary>Immutable process owner; neither DLL nor reverse-P/Invoke callbacks
/// unload. Native helper calls original directly and never transforms its result.</summary>
internal sealed partial class NativeCoroutineContextBridge : INativeCoroutineContextBridge
{
    private static readonly object Gate = new(); private static readonly Dictionary<(string, NativeCoroutineKind), uint> Slots = new();
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr Original, Enter, Exit, User; }
    [StructLayout(LayoutKind.Sequential)] private struct Statistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LimitNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative(uint kind, uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint kind, uint slot, ref Configuration value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, uint slot, ref Statistics value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, nuint token, nuint result, uint flags, IntPtr user);
    private readonly IntPtr _library; private readonly uint _slot; private readonly ConfigureNative _configure; private readonly ReadNative _read;
    private readonly EnterNative _enterNative; private readonly ExitNative _exitNative; private readonly ILogger _log;
    private Func<NativeCoroutineFrame, nuint>? _enter; private Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags>? _exit;
    private volatile bool _configured; private int _unknown;
    public NativeCoroutineKind Kind { get; }
    public IntPtr EntryAddress { get; }
    internal NativeCoroutineContextBridge(string absolutePath, NativeCoroutineKind kind, ILogger log)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Coroutine transport requires Windows x64.");
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("An absolute helper path is required.");
        if (kind is not (NativeCoroutineKind.Start or NativeCoroutineKind.MoveNext or NativeCoroutineKind.Factory)) throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind; _log = log; _path = Path.GetFullPath(absolutePath); _library = NativeLibrary.Load(_path);
        _configure = Export<ConfigureNative>("RizCoroutineContext_Configure"); _read = Export<ReadNative>("RizCoroutineContext_GetStats");
        _readConfiguration = Export<ReadConfigurationNative>("RizCoroutineContext_ReadConfigurationV1");
        _getEntry = Export<EntryNative>("RizCoroutineContext_GetEntry");
        if (Export<LimitNative>("RizCoroutineContext_GetLimit")() != 32 || Export<LimitNative>("RizCoroutineContext_GetFrameSize")() != 0x30
            || Marshal.SizeOf<NativeCoroutineFrame>() != 0x30 || Marshal.SizeOf<Configuration>() != 0x28
            || Marshal.SizeOf<Statistics>() != 0x30 || Marshal.SizeOf<CurrentConfiguration>() != 0x48)
            throw new UnsupportedSignatureException("Native coroutine ABI differs from version 1.");
        lock (Gate)
        {
            var key = (_path.ToUpperInvariant(), kind); Slots.TryGetValue(key, out _slot);
            if (_slot >= 32) throw new InvalidOperationException("The immutable native coroutine target capacity is exhausted.");
            Slots[key] = _slot + 1; // Never recycle a slot after an uncertain publication.
        }
        EntryAddress = _getEntry((uint)kind, _slot);
        if (EntryAddress == IntPtr.Zero) throw new UnsupportedSignatureException("Native coroutine entry is null.");
        _enterNative = Enter; _exitNative = Exit;
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void Configure(IntPtr original, Func<NativeCoroutineFrame, nuint> enter,
        Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags> exit)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        if (_configureAttempted || original == IntPtr.Zero) throw new InvalidOperationException("Coroutine configuration is immutable and requires its original.");
        _configureAttempted = true;
        _enter = enter; _exit = exit;
        var value = new Configuration { Version = 1, Size = 0x28, Original = original,
            Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative) };
        _configuredIdentity = value; _configuredEnter = enter; _configuredExit = exit;
        _configuredKind = Kind; _configuredSlot = _slot;
        _enterMemory = CaptureCallback(value.Enter); _exitMemory = CaptureCallback(value.Exit);
        if (_configure((uint)Kind, _slot, ref value) != 0) { Interlocked.Exchange(ref _unknown, 1); throw new InvalidOperationException("Native coroutine configuration rejected."); }
        _configured = true;
        VerifyConfiguration(original);
    }
    private unsafe nuint Enter(IntPtr frame, IntPtr user)
    {
        try { return _enter!(*(NativeCoroutineFrame*)frame); }
        catch (Exception error) { Fault("Coroutine enter callback failed.", error); return 0; }
    }
    private unsafe void Exit(IntPtr frame, nuint token, nuint result, uint flags, IntPtr user)
    {
        try { _exit!(*(NativeCoroutineFrame*)frame, token, result, (NativeCoroutineExitFlags)flags); }
        catch (Exception error) { Fault("Coroutine exit callback failed.", error); }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        if (!_configured) return new(0, false);
        try
        {
            var value = new Statistics { Version = 1, Size = 0x30 };
            if (_read((uint)Kind, _slot, ref value) != 0 || value.Version != 1 || value.Size != 0x30
                || value.InFlight > long.MaxValue || value.EnterFaults != 0 || value.ExitFaults != 0)
            { Interlocked.Exchange(ref _unknown, 1); return new(0, false); }
            return new((long)value.InFlight, Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault("Coroutine statistics read failed.", error); return new(0, false); }
    }
    private void Fault(string message, Exception error)
    { Interlocked.Exchange(ref _unknown, 1); try { _log.Log(LogLevel.Error, message, error); } catch { } }
}
