using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeOperationBootstrapInformation
{
    internal uint Version, Size, Kind, Slot, Attached, Unknown;
    internal IntPtr Target, Original, Entry;
    internal ulong Epoch, Entered, Exited, InFlight, EnterFaults, ExitFaults;
}

/// <summary>Adopts one already configured native operation channel. This class
/// never prepares/commits a detour or replaces its native original. Each native
/// entry snapshots its observer once, so old calls cannot inherit a later CLR
/// observer at Exit. Callback/DLL roots survive failure and logical detach.</summary>
internal sealed class NativeBootstrappedOperationBridge : INativeOperationContextBridge
{
    private static readonly object OwnerGate = new();
    private static readonly Dictionary<string, HashSet<(NativeOperationKind Kind, uint Slot)>> Claims = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<NativeBootstrappedOperationBridge> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr Enter, Exit, User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint kind, uint slot, ref NativeOperationBootstrapInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(uint kind, uint slot, ulong epoch, ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, nuint token, uint flags, IntPtr user);
    private sealed record Callbacks(Func<NativeOperationFrame, nuint> Enter,
        Action<NativeOperationFrame, nuint, NativeOperationExitFlags> Exit);
    private readonly object _gate = new();
    private readonly IntPtr _library;
    private readonly InfoNative _info;
    private readonly AttachNative _attach;
    private readonly EnterNative _enterNative;
    private readonly ExitNative _exitNative;
    private readonly ILogger _log;
    private Callbacks? _callbacks;
    private bool _attempted;
    private int _seenAttached, _unknown;
    internal NativeOperationChannelIdentity Identity { get; }
    public NativeOperationKind Kind => Identity.Kind;
    public IntPtr EntryAddress => Identity.Entry;

    internal static NativeBootstrappedOperationBridge AdoptBootstrap(string absolutePath, NativeOperationChannelIdentity identity, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(absolutePath); ArgumentNullException.ThrowIfNull(log);
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("An absolute helper path is required.", nameof(absolutePath));
        if (!identity.IsComplete) throw new ArgumentException("Operation adoption requires the actual complete native channel identity.", nameof(identity));
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Operation adoption requires Windows x64.");
        var path = Path.GetFullPath(absolutePath); var key = (identity.Kind, identity.Slot);
        lock (OwnerGate)
        {
            if (Claims.TryGetValue(path, out var slots) && slots.Contains(key))
                throw new InvalidOperationException("The permanent native operation channel already has a managed owner.");
            var bridge = new NativeBootstrappedOperationBridge(path, identity, log);
            Owners.Add(bridge); // Root both callbacks and DLL before any Attach.
            if (slots is null) Claims.Add(path, slots = new());
            slots.Add(key); return bridge;
        }
    }
    private NativeBootstrappedOperationBridge(string path, NativeOperationChannelIdentity identity, ILogger log)
    {
        Identity = identity; _log = log; _library = NativeLibrary.Load(path);
        _info = Export<InfoNative>("RizOperationBootstrap_GetInfo");
        _attach = Export<AttachNative>("RizOperationBootstrap_Attach");
        _enterNative = Enter; _exitNative = Exit;
        if (Marshal.SizeOf<NativeOperationFrame>() != 64 || Marshal.SizeOf<NativeOperationBootstrapInformation>() != 96 || Marshal.SizeOf<Observer>() != 32)
            throw new UnsupportedSignatureException("Native operation bootstrap ABI differs from version 1.");
        var info = NewInformation();
        if (_info((uint)Kind, Identity.Slot, ref info) != 0 || !MatchesInformation(info, Identity, false))
            throw new UnsupportedSignatureException("Native operation channel differs from its exact unattached publication identity.");
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    private static NativeOperationBootstrapInformation NewInformation() => new() { Version = 1, Size = 96 };
    internal static bool MatchesInformation(NativeOperationBootstrapInformation info, NativeOperationChannelIdentity identity, bool? attached)
        => identity.IsComplete && info.Version == 1 && info.Size == 96 && info.Kind == (uint)identity.Kind && info.Slot == identity.Slot
        && info.Attached <= 1 && (attached is null || info.Attached == (attached.Value ? 1u : 0u)) && info.Unknown == 0
        && info.Target == identity.Target && info.Original == identity.Original && info.Entry == identity.Entry && info.Epoch == identity.Epoch
        && info.InFlight <= long.MaxValue && info.EnterFaults == 0 && info.ExitFaults == 0;

    internal static bool IsNativeSlotClaimed(IntPtr helper, NativeOperationKind kind, uint slot)
    {
        if (!NativeLibrary.TryGetExport(helper, "RizOperationBootstrap_GetInfo", out var address)) return false;
        var info = NewInformation(); var read = Marshal.GetDelegateForFunctionPointer<InfoNative>(address);
        var result = read((uint)kind, slot, ref info);
        if (info.Version != 1 || info.Size != 96 || info.Kind != (uint)kind || info.Slot != slot || result is not (0 or 2 or 3))
            throw new UnsupportedSignatureException("Native operation slot claim could not be read.");
        // A failed or legacy claim is permanent too. GetInfo returns its real
        // helper Entry even for an empty slot, so Entry alone is not a claim.
        return result != 2 || info.Unknown != 0 || info.Target != IntPtr.Zero || info.Original != IntPtr.Zero || info.Epoch != 0 || info.Attached != 0;
    }
    public void Configure(IntPtr original, Func<NativeOperationFrame, nuint> enter,
        Action<NativeOperationFrame, nuint, NativeOperationExitFlags> exit)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        lock (_gate)
        {
            if (_attempted || original != Identity.Original) throw new InvalidOperationException("Operation Attach requires its immutable original and can be attempted only once.");
            _attempted = true;
            Volatile.Write(ref _callbacks, new(enter, exit));
            var observer = new Observer { Version = 1, Size = 32,
                Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative) };
            try
            {
                var before = NewInformation();
                if (_info((uint)Kind, Identity.Slot, ref before) != 0 || !MatchesInformation(before, Identity, false))
                    throw new UnsupportedSignatureException("Native operation identity changed before Attach.");
                if (_attach((uint)Kind, Identity.Slot, Identity.Epoch, ref observer) != 0)
                    throw new InvalidOperationException("Native operation observer Attach was rejected.");
                var after = NewInformation();
                if (_info((uint)Kind, Identity.Slot, ref after) != 0 || !MatchesInformation(after, Identity, true))
                    throw new UnsupportedSignatureException("Native operation Attach could not be confirmed.");
                if (Volatile.Read(ref _unknown) != 0)
                    throw new UnsupportedSignatureException("Native operation observer faulted during Attach publication.");
                Interlocked.Exchange(ref _seenAttached, 1);
            }
            catch { Interlocked.Exchange(ref _unknown, 1); throw; }
        }
    }
    private unsafe nuint Enter(IntPtr pointer, IntPtr user)
    {
        try
        {
            if (pointer == IntPtr.Zero || user != IntPtr.Zero) throw new UnsupportedSignatureException("Invalid operation Enter frame/user.");
            var frame = *(NativeOperationFrame*)pointer;
            if (!frame.IsValid || frame.Kind != Kind) throw new UnsupportedSignatureException("Operation Enter differs from its exact ABI.");
            return (Volatile.Read(ref _callbacks) ?? throw new InvalidOperationException("Operation callbacks are unavailable.")).Enter(frame);
        }
        catch (Exception error) { Fault("Native bootstrapped operation Enter failed.", error); return 0; }
    }
    private unsafe void Exit(IntPtr pointer, nuint token, uint flags, IntPtr user)
    {
        try
        {
            if (pointer == IntPtr.Zero || user != IntPtr.Zero) throw new UnsupportedSignatureException("Invalid operation Exit frame/user.");
            var frame = *(NativeOperationFrame*)pointer;
            if (!frame.IsValid || frame.Kind != Kind) throw new UnsupportedSignatureException("Operation Exit differs from its exact ABI.");
            (Volatile.Read(ref _callbacks) ?? throw new InvalidOperationException("Operation callbacks are unavailable.")).Exit(frame, token, (NativeOperationExitFlags)flags);
        }
        catch (Exception error) { Fault("Native bootstrapped operation Exit failed.", error); }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        if (Volatile.Read(ref _callbacks) is null) return new(0, false);
        try
        {
            var info = NewInformation(); var seenBefore = Volatile.Read(ref _seenAttached);
            var status = _info((uint)Kind, Identity.Slot, ref info);
            var attached = status == 0 && MatchesInformation(info, Identity, true);
            var valid = attached || (status == 0 && seenBefore == 0 && MatchesInformation(info, Identity, false));
            if (attached) Interlocked.Exchange(ref _seenAttached, 1);
            if (!valid) Interlocked.Exchange(ref _unknown, 1);
            return new(info.InFlight <= long.MaxValue ? (long)info.InFlight : 0, attached && valid && Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault("Native bootstrapped operation activity failed.", error); return new(0, false); }
    }
    private void Fault(string message, Exception error)
    { Interlocked.Exchange(ref _unknown, 1); try { _log.Log(LogLevel.Error, message, error); } catch { } }
}
