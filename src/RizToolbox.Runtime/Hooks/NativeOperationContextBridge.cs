using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeOperationKind : uint { Void0, Reference1, Reference2, Single1 }
[Flags] internal enum NativeOperationExitFlags : uint { Returned = 1, Abnormal = 2, EnterFaulted = 4, Skipped = 8 }
[StructLayout(LayoutKind.Sequential)]
internal struct NativeOperationFrame
{
    internal uint Version, Size;
    internal NativeOperationKind Kind;
    internal uint Reserved;
    internal IntPtr Self, Arg0, Arg1, Method, Caller;
    internal ulong Sequence;
    internal bool IsValid => Version == 1 && Size == 64 && Reserved == 0 && Self != IntPtr.Zero
        && Caller != IntPtr.Zero && Sequence is > 0 and <= long.MaxValue
        && Kind is NativeOperationKind.Void0 or NativeOperationKind.Reference1 or NativeOperationKind.Reference2 or NativeOperationKind.Single1
        && (Kind != NativeOperationKind.Void0 || Arg0 == IntPtr.Zero)
        && (Kind == NativeOperationKind.Reference2 || Arg1 == IntPtr.Zero);
    internal uint ScalarBits => Kind == NativeOperationKind.Single1
        ? unchecked((uint)Arg0.ToInt64()) : throw new InvalidOperationException("This operation frame has no Single argument.");
}
internal interface INativeOperationContextBridge
{
    NativeOperationKind Kind { get; }
    IntPtr EntryAddress { get; }
    void Configure(IntPtr original, Func<NativeOperationFrame, nuint> enter,
        Action<NativeOperationFrame, nuint, NativeOperationExitFlags> exit);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}

/// <summary>Typed, observation-only native void operations. Original exceptions
/// remain on the native stack; logical detach does not unload this channel.</summary>
internal sealed class NativeOperationContextBridge : INativeOperationContextBridge
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string, NativeOperationKind), uint> Slots = new();
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr Original, Enter, Exit, User; }
    [StructLayout(LayoutKind.Sequential)] private struct Statistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LimitNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative(uint kind, uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint kind, uint slot, ref Configuration value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, uint slot, ref Statistics value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, nuint token, uint flags, IntPtr user);
    private readonly IntPtr _library;
    private readonly uint _slot;
    private readonly ConfigureNative _configure;
    private readonly ReadNative _read;
    private readonly EnterNative _enterNative;
    private readonly ExitNative _exitNative;
    private readonly ILogger _log;
    private Func<NativeOperationFrame, nuint>? _enter;
    private Action<NativeOperationFrame, nuint, NativeOperationExitFlags>? _exit;
    private int _attempted, _unknown;
    private volatile bool _configured;
    public NativeOperationKind Kind { get; }
    public IntPtr EntryAddress { get; }
    internal NativeOperationContextBridge(string absolutePath, NativeOperationKind kind, ILogger log)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Operation transport requires Windows x64.");
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("An absolute helper path is required.");
        if (kind is not (NativeOperationKind.Void0 or NativeOperationKind.Reference1 or NativeOperationKind.Reference2 or NativeOperationKind.Single1))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind; _log = log; var path = Path.GetFullPath(absolutePath); _library = NativeLibrary.Load(path);
        _configure = Export<ConfigureNative>("RizOperationContext_Configure"); _read = Export<ReadNative>("RizOperationContext_GetStats");
        if (Export<LimitNative>("RizOperationContext_GetLimit")() != 32 || Export<LimitNative>("RizOperationContext_GetFrameSize")() != 64
            || Marshal.SizeOf<NativeOperationFrame>() != 64 || Marshal.SizeOf<Configuration>() != 40 || Marshal.SizeOf<Statistics>() != 48)
            throw new UnsupportedSignatureException("Native operation ABI differs from version 1.");
        lock (Gate)
        {
            var key = (path.ToUpperInvariant(), kind); Slots.TryGetValue(key, out _slot);
            while (_slot < 32 && NativeBootstrappedOperationBridge.IsNativeSlotClaimed(_library, kind, _slot)) ++_slot;
            if (_slot >= 32) throw new InvalidOperationException("Permanent native operation target capacity exhausted.");
            Slots[key] = _slot + 1;
        }
        EntryAddress = Export<EntryNative>("RizOperationContext_GetEntry")((uint)kind, _slot);
        if (EntryAddress == IntPtr.Zero) throw new UnsupportedSignatureException("Native operation entry is null.");
        _enterNative = Enter; _exitNative = Exit;
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void Configure(IntPtr original, Func<NativeOperationFrame, nuint> enter,
        Action<NativeOperationFrame, nuint, NativeOperationExitFlags> exit)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        if (original == IntPtr.Zero || Interlocked.Exchange(ref _attempted, 1) != 0)
            throw new InvalidOperationException("Native operation configuration is immutable.");
        _enter = enter; _exit = exit;
        var value = new Configuration { Version = 1, Size = 40, Original = original,
            Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative) };
        if (_configure((uint)Kind, _slot, ref value) != 0)
        { Interlocked.Exchange(ref _unknown, 1); throw new InvalidOperationException("Native operation configuration rejected."); }
        _configured = true;
    }
    private unsafe nuint Enter(IntPtr pointer, IntPtr user)
    {
        try { if (pointer == IntPtr.Zero) throw new InvalidOperationException("Missing native operation frame."); return _enter!(*(NativeOperationFrame*)pointer); }
        catch (Exception error) { Fault(error); return 0; }
    }
    private unsafe void Exit(IntPtr pointer, nuint token, uint flags, IntPtr user)
    {
        try { if (pointer == IntPtr.Zero) throw new InvalidOperationException("Missing native operation completion."); _exit!(*(NativeOperationFrame*)pointer, token, (NativeOperationExitFlags)flags); }
        catch (Exception error) { Fault(error); }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        if (!_configured) return new(0, false);
        try
        {
            var value = new Statistics { Version = 1, Size = 48 };
            if (_read((uint)Kind, _slot, ref value) != 0 || value.Version != 1 || value.Size != 48 || value.InFlight > long.MaxValue
                || value.EnterFaults != 0 || value.ExitFaults != 0)
            { Interlocked.Exchange(ref _unknown, 1); return new(value.InFlight <= long.MaxValue ? (long)value.InFlight : 0, false); }
            return new((long)value.InFlight, Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault(error); return new(0, false); }
    }
    private void Fault(Exception error)
    { Interlocked.Exchange(ref _unknown, 1); try { _log.Log(LogLevel.Error, "Native operation callback or statistics failed.", error); } catch { } }
}
