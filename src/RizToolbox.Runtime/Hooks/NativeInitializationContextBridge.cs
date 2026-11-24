using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeInitializationKind : uint { Start = 0, Getter = 1 }
[Flags] internal enum NativeInitializationExitFlags : uint { Returned = 1, Abnormal = 2, EnterFaulted = 4, Skipped = 8 }
internal delegate uint NativeInitializationEnter(IntPtr self, IntPtr information, IntPtr method, out nuint token);
internal delegate IntPtr NativeInitializationExit(IntPtr self, IntPtr information, IntPtr method, IntPtr returned, nuint token, NativeInitializationExitFlags flags);
internal sealed record NativeInitializationActivity(long InFlight, bool Known);
internal interface INativeInitializationContextBridge
{
    NativeInitializationKind Kind { get; }
    IntPtr EntryAddress { get; }
    void Configure(IntPtr original, NativeInitializationEnter enter, NativeInitializationExit exit, Action<string> fault);
    NativeInitializationActivity ReadActivity();
}

// Process lifetime owner: the DLL, immutable configuration and reverse P/Invoke
// delegates stay rooted by the broker entry, including uncertain publication.
internal sealed partial class NativeInitializationContextBridge : INativeInitializationContextBridge
{
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr Original, Enter, Exit, User; }
    [StructLayout(LayoutKind.Sequential)] private struct Statistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults, Skipped; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetEntryNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetConfigSizeNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint kind, ref Configuration configuration);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, ref Statistics statistics);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint EnterNative(IntPtr self, IntPtr information, IntPtr method, out nuint token, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ExitNative(IntPtr self, IntPtr information, IntPtr method, IntPtr returned, nuint token, uint flags, IntPtr user);
    private readonly IntPtr _library;
    private readonly ConfigureNative _configure;
    private readonly ReadNative _read;
    private readonly EnterNative _enterNative;
    private readonly ExitNative _exitNative;
    private readonly ILogger _log;
    private NativeInitializationEnter? _enter;
    private NativeInitializationExit? _exit;
    private Action<string>? _fault;
    private volatile bool _configured;
    private int _unknown;
    public NativeInitializationKind Kind { get; }
    public IntPtr EntryAddress { get; }
    internal NativeInitializationContextBridge(string absoluteLibraryPath, NativeInitializationKind kind, ILogger log)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Initialization transport requires Windows x64.");
        if (!Path.IsPathFullyQualified(absoluteLibraryPath)) throw new ArgumentException("An absolute native helper path is required.");
        if (kind is not (NativeInitializationKind.Start or NativeInitializationKind.Getter)) throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind; _log = log; _path = Path.GetFullPath(absoluteLibraryPath); _library = NativeLibrary.Load(_path);
        _configure = Export<ConfigureNative>("RizInitializationContext_Configure");
        _read = Export<ReadNative>("RizInitializationContext_GetStats");
        _readPublication = Export<ReadPublicationNative>("RizInitializationContext_GetStats");
        _getEntry = Export<GetEntryNative>("RizInitializationContext_GetEntry");
        if (Export<GetConfigSizeNative>("RizInitializationContext_GetConfigSize")() != 0x28
            || Marshal.SizeOf<Configuration>() != 0x28 || Marshal.SizeOf<Statistics>() != 0x38)
            throw new UnsupportedSignatureException("The typed initialization configuration/statistics ABI differs from version 1.");
        EntryAddress = _getEntry((uint)kind);
        if (EntryAddress == IntPtr.Zero) throw new UnsupportedSignatureException("The typed initialization native entry is null.");
        _enterNative = Enter; _exitNative = Exit;
        var probe = NewPublication();
        var probeResult = _readPublication((uint)kind, ref probe);
        // The original helper rejects V2. Its typed LOCAL installation can
        // still run, without issuing a readback-backed image member.
        SupportsPublicationReadback = probeResult is 0 or 2;
        if (probeResult > 2 || probeResult == 0 && !NamesPublication(probe))
            throw new UnsupportedSignatureException("The native initialization configuration capability is inconsistent.");
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void Configure(IntPtr original, NativeInitializationEnter enter, NativeInitializationExit exit, Action<string> fault)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit); ArgumentNullException.ThrowIfNull(fault);
        if (original == IntPtr.Zero || Interlocked.CompareExchange(ref _configureAttempted, 1, 0) != 0)
            throw new InvalidOperationException("Initialization requires one immutable non-null native original.");
        _enter = enter; _exit = exit; _fault = fault;
        _configuredIdentity = new Configuration { Version = 1, Size = 0x28, Original = original,
            Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative),
            User = GCHandle.ToIntPtr(GCHandle.Alloc(this)) };
        if (SupportsPublicationReadback)
        {
            _enterCode = CaptureCallback(_configuredIdentity.Enter);
            _exitCode = CaptureCallback(_configuredIdentity.Exit);
        }
        var configuration = _configuredIdentity;
        if (_configure((uint)Kind, ref configuration) != 0)
        { Unknown("Native initialization configuration was rejected."); throw new InvalidOperationException("Native initialization configuration failed."); }
        _configured = true;
        if (SupportsPublicationReadback)
        {
            try
            {
                var actual = ReadPublication();
                _slot = NativePlaybackExecutableSeal.RangeSeal.Capture(actual.Slot, 0x28, _library, true);
                _slotIdentity = actual.Slot;
                VerifyPublishedConfiguration(original);
            }
            catch { Unknown("The actual native initialization configuration could not be read after publication."); throw; }
        }
    }
    private uint Enter(IntPtr self, IntPtr information, IntPtr method, out nuint token, IntPtr user)
    {
        token = 0;
        try
        {
            if (user != _configuredIdentity.User)
            {
                Unknown("Native initialization entered with a different callback owner.");
                return 0;
            }
            var allow = _enter!(self, information, method, out token);
            if (allow <= 1) return allow;
            Unknown("Initialization enter returned an invalid original decision."); return 0;
        }
        catch (Exception error) { Unknown("Initialization enter callback failed.", error); return 0; }
    }
    private IntPtr Exit(IntPtr self, IntPtr information, IntPtr method, IntPtr returned, nuint token, uint flags, IntPtr user)
    {
        try
        {
            if (user != _configuredIdentity.User)
            {
                Unknown("Native initialization completed with a different callback owner.");
                return IntPtr.Zero;
            }
            return _exit!(self, information, method, returned, token, (NativeInitializationExitFlags)flags);
        }
        catch (Exception error) { Unknown("Initialization exit callback failed.", error); return IntPtr.Zero; }
    }
    public NativeInitializationActivity ReadActivity()
    {
        try
        {
            var statistics = new Statistics { Version = 1, Size = 0x38 };
            if (!_configured || _read((uint)Kind, ref statistics) != 0 || statistics.Version != 1 || statistics.Size != 0x38
                // The native fields are independent atomic reads, not one
                // transaction: Exited may include a call absent from Entered's
                // earlier snapshot while workers run. Never compare those totals.
                || statistics.InFlight > long.MaxValue
                || statistics.EnterFaults != 0 || statistics.ExitFaults != 0)
            { Unknown("Native initialization activity cannot prove a known boundary."); return new(0, false); }
            return new((long)statistics.InFlight, Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Unknown("Native initialization activity read failed.", error); return new(0, false); }
    }
    private void Unknown(string message, Exception? error = null)
    {
        if (Interlocked.Exchange(ref _unknown, 1) == 0)
        {
            try { _fault?.Invoke(message); } catch { }
            try { _log.Log(LogLevel.Error, message, error); } catch { }
        }
    }
}
