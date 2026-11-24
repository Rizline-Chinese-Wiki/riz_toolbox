using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeVoidBootstrapInformation
{
    internal uint Version, Size, Slot, Attached;
    internal IntPtr Target, Original, Entry;
    internal ulong Epoch;
    internal uint Unknown, Reserved;
}

/// <summary>Adopts an already configured permanent native bootstrap slot. This
/// adapter never installs a detour, configures a native slot or proves the first
/// instruction publication safe. The native Enter boundary snapshots the single
/// immutable observer, so an invocation begun before Attach stays unobserved.</summary>
internal sealed class NativeBootstrappedVoidBridge : INativeVoidContextBridge, INativeVoidAdmissionBridge
{
    private static readonly object OwnerGate = new();
    private static readonly Dictionary<string, HashSet<uint>> Slots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<NativeBootstrappedVoidBridge> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr Enter, Exit, Mid, User; }
    [StructLayout(LayoutKind.Sequential)] private struct Statistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LimitNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint slot, ref NativeVoidBootstrapInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(uint slot, ulong epoch, ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint slot, ref Statistics stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, nuint token, uint flags, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MidNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AdmissionCloseNative(uint slot, uint timeout, ref NativeVoidAdmissionReceipt receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AdmissionReceiptNative(ref NativeVoidAdmissionReceipt receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AdmissionReadNative(uint slot, ref NativeVoidAdmissionGateStatistics stats);
    private sealed record Callbacks(Func<NativeVoidFrame, nuint> Enter,
        Action<NativeVoidFrame, nuint, NativeVoidExitFlags> Exit, Action<NativeEventMidFrame> Mid);

    private readonly object _gate = new();
    private readonly IntPtr _library, _target, _original;
    private readonly uint _slot;
    private readonly ulong _epoch;
    private readonly ILogger _log;
    private readonly InfoNative _info;
    private readonly AttachNative _attach;
    private readonly ReadNative _read;
    private readonly AdmissionCloseNative _close;
    private readonly AdmissionReceiptNative _verify, _release;
    private readonly AdmissionReadNative _gateStats;
    private readonly EnterNative _enterNative;
    private readonly ExitNative _exitNative;
    private readonly MidNative _midNative;
    private readonly AdmissionProtocol _admission;
    private Action<NativeEventMidFrame>? _mid;
    private Callbacks? _callbacks;
    private bool _configurationAttempted;
    private int _seenAttached, _unknown;
    public IntPtr EntryAddress { get; }
    internal uint Slot => _slot;
    internal IntPtr TargetAddress => _target;
    internal IntPtr OriginalAddress => _original;
    internal ulong Epoch => _epoch;

    internal static NativeBootstrappedVoidBridge AdoptBootstrap(string absolutePath, uint slot,
        IntPtr expectedTarget, IntPtr expectedOriginal, ulong expectedEpoch, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(absolutePath); ArgumentNullException.ThrowIfNull(log);
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("An absolute helper path is required.", nameof(absolutePath));
        if (slot >= 32 || expectedTarget == IntPtr.Zero || expectedOriginal == IntPtr.Zero || expectedEpoch == 0)
            throw new ArgumentException("Bootstrap adoption requires the exact ready slot, target, original and nonzero epoch.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Native bootstrap adoption requires Windows x64.");
        var path = Path.GetFullPath(absolutePath);
        lock (OwnerGate)
        {
            if (Slots.TryGetValue(path, out var slots) && slots.Contains(slot))
                throw new InvalidOperationException("The permanent bootstrap slot already has a managed owner.");
            var bridge = new NativeBootstrappedVoidBridge(path, slot, expectedTarget, expectedOriginal, expectedEpoch, log);
            // Preserve delegates and the DLL before any native Attach. Native
            // rejection or uncertainty must never discard published callbacks.
            Owners.Add(bridge);
            if (slots is null) Slots.Add(path, slots = new());
            slots.Add(slot);
            return bridge;
        }
    }
    private NativeBootstrappedVoidBridge(string path, uint slot, IntPtr target, IntPtr original, ulong epoch, ILogger log)
    {
        _slot = slot; _target = target; _original = original; _epoch = epoch; _log = log;
        _library = NativeLibrary.Load(path);
        _info = Export<InfoNative>("RizVoidBootstrap_GetInfo");
        _attach = Export<AttachNative>("RizVoidBootstrap_Attach");
        _read = Export<ReadNative>("RizVoidContext_GetStats");
        _close = Export<AdmissionCloseNative>("RizVoidContext_Close");
        _verify = Export<AdmissionReceiptNative>("RizVoidContext_Verify");
        _release = Export<AdmissionReceiptNative>("RizVoidContext_Release");
        _gateStats = Export<AdmissionReadNative>("RizVoidContext_GetGateStats");
        if (Marshal.SizeOf<NativeVoidBootstrapInformation>() != 56 || Marshal.SizeOf<Observer>() != 40
            || Marshal.SizeOf<NativeVoidFrame>() != 40 || Marshal.SizeOf<NativeEventMidFrame>() != 0x1b0
            || Marshal.SizeOf<Statistics>() != 48 || Marshal.SizeOf<NativeVoidAdmissionReceipt>() != 32
            || Marshal.SizeOf<NativeVoidAdmissionGateStatistics>() != 40
            || Export<LimitNative>("RizVoidContext_GetLimit")() != 32
            || Export<LimitNative>("RizVoidContext_GetFrameSize")() != 40
            || Export<LimitNative>("RizEventMidContext_GetFrameSize")() != 0x1b0)
            throw new UnsupportedSignatureException("Native bootstrap transport differs from version 1.");
        var info = NewInformation();
        if (_info(_slot, ref info) != 0 || !MatchesInformation(info, _slot, _target, _original, _epoch, IntPtr.Zero, false))
            throw new UnsupportedSignatureException("Native bootstrap slot does not match the expected unattached target, original and epoch.");
        EntryAddress = info.Entry;
        _enterNative = Enter; _exitNative = Exit; _midNative = Mid;
        _admission = new AdmissionProtocol(this);
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    internal static bool IsNativeSlotClaimed(IntPtr helper, uint slot)
    {
        if (!NativeLibrary.TryGetExport(helper, "RizVoidBootstrap_GetInfo", out var address)) return false;
        var read = Marshal.GetDelegateForFunctionPointer<InfoNative>(address);
        var info = NewInformation(); var status = read(slot, ref info);
        if (status is not (0 or 2)) throw new UnsupportedSignatureException("Native bootstrap slot claim information could not be read.");
        // Failed partial claims are permanent too. Empty tables return only the
        // requested slot and zero identities; no fixed slot is skipped blindly.
        return status == 0 || info.Target != IntPtr.Zero || info.Original != IntPtr.Zero || info.Entry != IntPtr.Zero || info.Epoch != 0 || info.Unknown != 0;
    }
    private static NativeVoidBootstrapInformation NewInformation() => new() { Version = 1, Size = 56 };
    internal static bool MatchesInformation(NativeVoidBootstrapInformation info, uint slot, IntPtr target,
        IntPtr original, ulong epoch, IntPtr entry, bool attached)
        => slot < 32 && target != IntPtr.Zero && original != IntPtr.Zero && epoch != 0
        && info.Version == 1 && info.Size == 56 && info.Slot == slot && info.Attached == (attached ? 1u : 0u)
        && info.Target == target && info.Original == original && info.Entry != IntPtr.Zero
        && (entry == IntPtr.Zero || info.Entry == entry) && info.Epoch == epoch && info.Unknown == 0 && info.Reserved == 0;

    internal void ConfigureMid(Action<NativeEventMidFrame> observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        lock (_gate)
        {
            if (_configurationAttempted || _mid is not null)
                throw new InvalidOperationException("Bootstrap mid observation must be configured once before Attach.");
            _mid = observe;
        }
    }
    public void Configure(IntPtr original, Func<NativeVoidFrame, nuint> enter,
        Action<NativeVoidFrame, nuint, NativeVoidExitFlags> exit)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        lock (_gate)
        {
            if (original != _original || _configurationAttempted || _mid is null)
                throw new InvalidOperationException("Bootstrap Attach requires its exact original and a previously configured immutable mid observer.");
            _configurationAttempted = true;
            Volatile.Write(ref _callbacks, new(enter, exit, _mid));
            var observer = new Observer { Version = 1, Size = 40,
                Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative),
                Mid = Marshal.GetFunctionPointerForDelegate(_midNative) };
            try
            {
                var before = NewInformation();
                if (_info(_slot, ref before) != 0 || !MatchesInformation(before, _slot, _target, _original, _epoch, EntryAddress, false))
                    throw new UnsupportedSignatureException("Native bootstrap identity changed before Attach.");
                if (_attach(_slot, _epoch, ref observer) != 0)
                    throw new InvalidOperationException("Native bootstrap immutable observer Attach was rejected.");
                var after = NewInformation();
                if (_info(_slot, ref after) != 0 || !MatchesInformation(after, _slot, _target, _original, _epoch, EntryAddress, true))
                    throw new UnsupportedSignatureException("Native bootstrap Attach could not be confirmed.");
                Interlocked.Exchange(ref _seenAttached, 1);
            }
            catch { Interlocked.Exchange(ref _unknown, 1); throw; }
        }
    }
    private unsafe nuint Enter(IntPtr frame, IntPtr user)
    {
        try
        {
            if (frame == IntPtr.Zero || user != IntPtr.Zero) throw new UnsupportedSignatureException("Native bootstrap enter frame or user is invalid.");
            var copy = *(NativeVoidFrame*)frame;
            if (!copy.IsValid) throw new UnsupportedSignatureException("Native bootstrap enter frame differs from version 1.");
            return (Volatile.Read(ref _callbacks) ?? throw new InvalidOperationException("Bootstrap callbacks are unavailable.")).Enter(copy);
        }
        catch (Exception error) { Fault("Native bootstrap enter callback failed.", error); return 0; }
    }
    private unsafe void Exit(IntPtr frame, nuint token, uint flags, IntPtr user)
    {
        try
        {
            if (frame == IntPtr.Zero || user != IntPtr.Zero) throw new UnsupportedSignatureException("Native bootstrap exit frame or user is invalid.");
            var copy = *(NativeVoidFrame*)frame;
            if (!copy.IsValid) throw new UnsupportedSignatureException("Native bootstrap exit frame differs from version 1.");
            (Volatile.Read(ref _callbacks) ?? throw new InvalidOperationException("Bootstrap callbacks are unavailable.")).Exit(copy, token, (NativeVoidExitFlags)flags);
        }
        catch (Exception error) { Fault("Native bootstrap exit callback failed.", error); }
    }
    private unsafe void Mid(IntPtr frame, IntPtr user)
    {
        try
        {
            if (frame == IntPtr.Zero || user != IntPtr.Zero) throw new UnsupportedSignatureException("Native bootstrap mid frame or user is invalid.");
            var copy = *(NativeEventMidFrame*)frame;
            if (!copy.IsValid) throw new UnsupportedSignatureException("Native bootstrap mid frame differs from version 1.");
            (Volatile.Read(ref _callbacks) ?? throw new InvalidOperationException("Bootstrap callbacks are unavailable.")).Mid(copy);
        }
        catch (Exception error) { Fault("Native bootstrap mid callback failed; continuation is preserved.", error); }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        // Native Attach can publish before its call returns to Configure. A
        // callback in that interval must use actual native attachment identity,
        // without waiting on Configure's lock or a later managed success flag.
        if (Volatile.Read(ref _callbacks) is null) return new(0, false);
        try
        {
            var info = NewInformation();
            var statistics = new Statistics { Version = 1, Size = 48 };
            var gate = new NativeVoidAdmissionGateStatistics { Version = 1, Size = 40 };
            // A concurrent reader may observe Attach after our native snapshot.
            // That newer observation cannot invalidate this earlier Info0.
            var seenAttachedBefore = Volatile.Read(ref _seenAttached);
            var readInfo = _info(_slot, ref info);
            var attached = readInfo == 0 && MatchesInformation(info, _slot, _target, _original, _epoch, EntryAddress, true);
            var validInfo = attached || (readInfo == 0 && seenAttachedBefore == 0
                && MatchesInformation(info, _slot, _target, _original, _epoch, EntryAddress, false));
            if (attached) Interlocked.Exchange(ref _seenAttached, 1);
            var validStats = _read(_slot, ref statistics) == 0 && statistics.Version == 1 && statistics.Size == 48
                && statistics.InFlight <= long.MaxValue && statistics.EnterFaults == 0 && statistics.ExitFaults == 0;
            var validGate = _gateStats(_slot, ref gate) == 0 && gate.IsValid && gate.Unknown == 0
                && gate.Active <= long.MaxValue && gate.Parked <= (ulong)long.MaxValue - gate.Active;
            if (!validInfo || !validStats || !validGate) Interlocked.Exchange(ref _unknown, 1);
            // The two native reads are separate observations. Retain the larger
            // physical count; neither a parked entry nor a callback tail is idle.
            var inFlight = statistics.InFlight <= long.MaxValue ? (long)statistics.InFlight : 0;
            if (validGate) inFlight = Math.Max(inFlight, (long)(gate.Active + gate.Parked));
            return new(inFlight, attached && validInfo && validStats && validGate && Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault("Native bootstrap activity read failed.", error); return new(0, false); }
    }
    public NativeVoidAdmissionAttempt TryAcquireAdmission(uint timeoutMilliseconds)
    {
        var result = NativeVoidAdmissionLease.TryAcquire(_admission, timeoutMilliseconds);
        if (result.Status == NativeVoidAdmissionAttemptStatus.Unknown) Interlocked.Exchange(ref _unknown, 1);
        return result;
    }
    private void Fault(string message, Exception error)
    { Interlocked.Exchange(ref _unknown, 1); try { _log.Log(LogLevel.Error, message, error); } catch { } }
    private sealed class AdmissionProtocol : INativeVoidAdmissionProtocol
    {
        private readonly NativeBootstrappedVoidBridge _owner;
        internal AdmissionProtocol(NativeBootstrappedVoidBridge owner) => _owner = owner;
        public uint Slot => _owner._slot;
        public uint CurrentOsThreadId => GetCurrentThreadId();
        public bool Ready => Volatile.Read(ref _owner._unknown) == 0 && _owner.ReadActivity().Known;
        public uint Close(uint timeoutMilliseconds, ref NativeVoidAdmissionReceipt receipt) => _owner._close(Slot, timeoutMilliseconds, ref receipt);
        public uint Verify(NativeVoidAdmissionReceipt receipt) => _owner._verify(ref receipt);
        public uint Release(NativeVoidAdmissionReceipt receipt) => _owner._release(ref receipt);
        public uint ReadStatistics(out NativeVoidAdmissionGateStatistics statistics)
        { statistics = new() { Version = 1, Size = 40 }; return _owner._gateStats(Slot, ref statistics); }
        public void ReportUnknown(string failure) => Interlocked.Exchange(ref _owner._unknown, 1);
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
