using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

[Flags] internal enum NativeVoidExitFlags : uint { Returned = 1, Abnormal = 2, EnterFaulted = 4 }
[StructLayout(LayoutKind.Sequential)]
internal struct NativeVoidFrame
{
    internal uint Version, Size, Reserved0, Reserved1;
    internal IntPtr Self, Method, Caller;
    internal bool IsValid => Version == 1 && Size == 0x28 && Reserved0 == 0 && Reserved1 == 0
        && Self != IntPtr.Zero && Caller != IntPtr.Zero;
}
internal interface INativeVoidContextBridge
{
    IntPtr EntryAddress { get; }
    void Configure(IntPtr original, Func<NativeVoidFrame, nuint> enter,
        Action<NativeVoidFrame, nuint, NativeVoidExitFlags> exit);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}

/// <summary>Process-owned native void transport. The helper calls its original;
/// managed observers only retain and release observations of physical frames.</summary>
internal sealed partial class NativeVoidContextBridge : INativeVoidContextBridge
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, uint> Slots = new(StringComparer.OrdinalIgnoreCase);
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr Original, Enter, Exit, User; }
    [StructLayout(LayoutKind.Sequential)] private struct Statistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LimitNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative(uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint slot, ref Configuration value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint slot, ref Statistics value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, nuint token, uint flags, IntPtr user);
    private readonly IntPtr _library; private readonly uint _slot;
    private readonly ConfigureNative _configure; private readonly ReadNative _read;
    private readonly EnterNative _enterNative; private readonly ExitNative _exitNative; private readonly ILogger _log;
    private Func<NativeVoidFrame, nuint>? _enter;
    private Action<NativeVoidFrame, nuint, NativeVoidExitFlags>? _exit;
    private volatile bool _configured; private int _configurationAttempted, _unknown;
    public IntPtr EntryAddress { get; }
    internal NativeVoidContextBridge(string absolutePath, ILogger log)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Native void0 transport requires Windows x64.");
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("An absolute helper path is required.");
        ArgumentNullException.ThrowIfNull(log); _log = log;
        var path = Path.GetFullPath(absolutePath); _library = NativeLibrary.Load(path);
        _configure = Export<ConfigureNative>("RizVoidContext_Configure"); _read = Export<ReadNative>("RizVoidContext_GetStats");
        InitializeAdmission();
        if (Export<LimitNative>("RizVoidContext_GetLimit")() != 32 || Export<LimitNative>("RizVoidContext_GetFrameSize")() != 0x28
            || Marshal.SizeOf<NativeVoidFrame>() != 0x28 || Marshal.SizeOf<Configuration>() != 0x28 || Marshal.SizeOf<Statistics>() != 0x30)
            throw new UnsupportedSignatureException("Native void0 transport differs from ABI version 1.");
        lock (Gate)
        {
            Slots.TryGetValue(path, out _slot);
            while (_slot < 32 && NativeBootstrappedVoidBridge.IsNativeSlotClaimed(_library, _slot)) ++_slot;
            if (_slot >= 32) throw new InvalidOperationException("The immutable native void0 target capacity is exhausted.");
            Slots[path] = _slot + 1;
        }
        EntryAddress = Export<EntryNative>("RizVoidContext_GetEntry")(_slot);
        if (EntryAddress == IntPtr.Zero) throw new UnsupportedSignatureException("Native void0 entry is null.");
        _enterNative = Enter; _exitNative = Exit;
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void Configure(IntPtr original, Func<NativeVoidFrame, nuint> enter,
        Action<NativeVoidFrame, nuint, NativeVoidExitFlags> exit)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        if (original == IntPtr.Zero || Interlocked.CompareExchange(ref _configurationAttempted, 1, 0) != 0)
            throw new InvalidOperationException("Native void0 configuration is immutable and requires its original.");
        _enter = enter; _exit = exit;
        var value = new Configuration { Version = 1, Size = 0x28, Original = original,
            Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative) };
        try
        {
            if (_configure(_slot, ref value) != 0) throw new InvalidOperationException("Native void0 configuration rejected.");
            _configured = true;
        }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }
    private unsafe nuint Enter(IntPtr frame, IntPtr user)
    {
        try { if (frame == IntPtr.Zero) throw new InvalidOperationException("Native void0 enter frame is null."); return _enter!(*(NativeVoidFrame*)frame); }
        catch (Exception error) { Fault("Native void0 enter callback failed.", error); return 0; }
    }
    private unsafe void Exit(IntPtr frame, nuint token, uint flags, IntPtr user)
    {
        try { if (frame == IntPtr.Zero) throw new InvalidOperationException("Native void0 exit frame is null."); _exit!(*(NativeVoidFrame*)frame, token, (NativeVoidExitFlags)flags); }
        catch (Exception error) { Fault("Native void0 exit callback failed.", error); }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        if (!_configured) return new(0, false);
        try
        {
            var value = new Statistics { Version = 1, Size = 0x30 };
            if (_read(_slot, ref value) != 0 || value.Version != 1 || value.Size != 0x30 || value.InFlight > long.MaxValue)
            { Interlocked.Exchange(ref _unknown, 1); return new(0, false); }
            if (value.EnterFaults != 0 || value.ExitFaults != 0) Interlocked.Exchange(ref _unknown, 1);
            var admission = ReadAdmissionActivity();
            return new(Math.Max((long)value.InFlight, admission.InFlight), admission.Known && Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault("Native void0 statistics read failed.", error); return new(0, false); }
    }
    private void Fault(string message, Exception error)
    { Interlocked.Exchange(ref _unknown, 1); try { _log.Log(LogLevel.Error, message, error); } catch { } }
}
