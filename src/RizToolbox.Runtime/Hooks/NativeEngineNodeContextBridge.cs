using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineNodeInformation
{
    internal uint Version, Size, Kind, Slot, Attached, Unknown;
    internal IntPtr Target, Original, Entry;
    internal ulong Epoch, Entered, Exited, InFlight, EnterFaults, ExitFaults, SnapshotFaults, OutputFaults;
    internal NativeEngineNodeChannelIdentity Identity => new((NativeEngineNodeKind)Kind, Slot, Target, Original, Entry, Epoch);
}

/// <summary>Adopts an already configured native engine channel. This class never
/// configures or publishes a detour and cannot grant first-install/game Ready
/// authority. A future production adoption requires its separate V4 publication
/// owner; the configured identity alone is only a transport/fixture contract.</summary>
internal sealed class NativeEngineNodeContextBridge : INativeEngineNodeContextBridge
{
    private static readonly object OwnerGate = new();
    private static readonly Dictionary<string, HashSet<(NativeEngineNodeKind Kind, uint Slot)>> Claims = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<NativeEngineNodeContextBridge> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr Enter, Exit, User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LimitNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint kind, uint slot, ref NativeEngineNodeInformation information);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(uint kind, uint slot, ulong epoch, ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, nuint token, IntPtr completion, IntPtr user);
    private sealed record Callbacks(Func<NativeEngineNodeFrame, nuint> Enter,
        Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion> Exit);
    private readonly object _gate = new();
    private readonly IntPtr _library;
    private readonly InfoNative _info;
    private readonly AttachNative _attach;
    private readonly EnterNative _enterNative;
    private readonly ExitNative _exitNative;
    private readonly ILogger _log;
    private readonly INativeEnginePublicationLifetime? _publicationOwner;
    private Callbacks? _callbacks;
    private bool _attempted;
    private int _seenAttached, _unknown;
    public NativeEngineNodeChannelIdentity Identity { get; }

    internal static NativeEngineNodeContextBridge AdoptConfigured(string absolutePath, NativeEngineNodeChannelIdentity identity, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(absolutePath); ArgumentNullException.ThrowIfNull(log);
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("An absolute helper path is required.", nameof(absolutePath));
        if (!identity.IsComplete) throw new ArgumentException("Engine node adoption requires the actual complete configured identity.", nameof(identity));
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Engine node transport requires Windows x64.");
        var path = Path.GetFullPath(absolutePath); var key = (identity.Kind, identity.Slot);
        lock (OwnerGate)
        {
            if (Claims.TryGetValue(path, out var slots) && slots.Contains(key))
                throw new InvalidOperationException("The permanent engine node channel already has a managed owner.");
            var bridge = new NativeEngineNodeContextBridge(path, identity, log);
            Owners.Add(bridge); // DLL, delegates and eventual dispatcher stay rooted even after a failed Attach.
            if (slots is null) Claims.Add(path, slots = new());
            slots.Add(key); return bridge;
        }
    }

    internal static NativeEngineNodeContextBridge AdoptPublished(NativeEngineFirstInstall owner, NativeEngineNodeKind kind, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(log);
        owner.VerifyReady(); var identity = owner.Identity(kind); var path = owner.KernelPath; var key = (identity.Kind, identity.Slot);
        lock (OwnerGate)
        {
            if (Claims.TryGetValue(path, out var slots) && slots.Contains(key))
                throw new InvalidOperationException("The permanent published engine node channel already has a managed owner.");
            var bridge = new NativeEngineNodeContextBridge(owner.KernelModule, identity, log, new NativeEngineV4PublicationLifetime(owner));
            Owners.Add(bridge);
            if (slots is null) Claims.Add(path, slots = new());
            slots.Add(key); return bridge;
        }
    }

    internal static NativeEngineNodeContextBridge AdoptPublished(NativeEngineTransportFirstInstall owner, NativeEngineNodeKind kind, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(log);
        try
        {
            owner.VerifyReady(); var identity = owner.NodeIdentity(kind); var path = owner.KernelPath; var key = (kind, identity.Slot);
            lock (OwnerGate)
            {
                if (Claims.TryGetValue(path, out var slots) && slots.Contains(key))
                    throw new InvalidOperationException("This V5 published Node channel already has a permanent managed owner.");
                var bridge = new NativeEngineNodeContextBridge(owner.KernelModule, identity, log, owner); Owners.Add(bridge);
                if (slots is null) Claims.Add(path, slots = new()); slots.Add(key); return bridge;
            }
        }
        catch { owner.MarkUnknown(); throw; }
    }

    private NativeEngineNodeContextBridge(string path, NativeEngineNodeChannelIdentity identity, ILogger log)
        : this(NativeLibrary.Load(path), identity, log, null) { }

    private NativeEngineNodeContextBridge(IntPtr loadedModule, NativeEngineNodeChannelIdentity identity, ILogger log,
        INativeEnginePublicationLifetime? publicationOwner)
    {
        Identity = identity; _log = log; _library = loadedModule; _publicationOwner = publicationOwner;
        _publicationOwner?.VerifyReady();
        _info = Export<InfoNative>("RizEngineNodeContext_GetInfo");
        _attach = Export<AttachNative>("RizEngineNodeContext_Attach");
        _enterNative = Enter; _exitNative = Exit;
        if (Export<LimitNative>("RizEngineNodeContext_GetAbiVersion")() != 2
            || Export<LimitNative>("RizEngineNodeContext_GetLimit")() != 32
            || Export<LimitNative>("RizEngineNodeContext_GetFrameSize")() != 112
            || Export<LimitNative>("RizEngineNodeContext_GetCompletionSize")() != 32
            || Marshal.SizeOf<NativeEngineNodeFrame>() != 112 || Marshal.SizeOf<NativeEngineNodeCompletion>() != 32
            || Marshal.SizeOf<NativeEngineNodeInformation>() != 112 || Marshal.SizeOf<Observer>() != 32)
            throw new UnsupportedSignatureException("Native engine node transport ABI2/scalar1 differs from its exact layout.");
        var information = NewInformation();
        if (_info((uint)identity.Kind, identity.Slot, ref information) != 0 || !MatchesInformation(information, identity, false))
            throw new UnsupportedSignatureException("Engine node channel differs from its exact unattached configured identity.");
        _publicationOwner?.VerifyReady();
    }

    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    private static NativeEngineNodeInformation NewInformation() => new() { Version = 1, Size = 112 };
    internal static bool MatchesInformation(NativeEngineNodeInformation information, NativeEngineNodeChannelIdentity identity, bool? attached)
        => identity.IsComplete && information.Version == 1 && information.Size == 112
            && information.Kind == (uint)identity.Kind && information.Slot == identity.Slot
            && information.Attached <= 1 && (attached is null || information.Attached == (attached.Value ? 1u : 0u))
            && information.Unknown == 0 && information.Target == identity.Target && information.Original == identity.Original
            && information.Entry == identity.Entry && information.Epoch == identity.Epoch
            && information.InFlight <= long.MaxValue && information.EnterFaults == 0 && information.ExitFaults == 0
            && information.SnapshotFaults == 0 && information.OutputFaults == 0;

    public void Attach(Func<NativeEngineNodeFrame, nuint> enter,
        Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion> exit)
    {
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        lock (_gate)
        {
            if (_attempted) throw new InvalidOperationException("Engine node observer Attach can be attempted only once.");
            _attempted = true; Volatile.Write(ref _callbacks, new(enter, exit));
            var observer = new Observer { Version = 1, Size = 32,
                Enter = Marshal.GetFunctionPointerForDelegate(_enterNative), Exit = Marshal.GetFunctionPointerForDelegate(_exitNative) };
            try
            {
                _publicationOwner?.VerifyReady();
                var before = NewInformation();
                if (_info((uint)Identity.Kind, Identity.Slot, ref before) != 0 || !MatchesInformation(before, Identity, false))
                    throw new UnsupportedSignatureException("Engine node identity changed before Attach.");
                if (_attach((uint)Identity.Kind, Identity.Slot, Identity.Epoch, ref observer) != 0)
                    throw new InvalidOperationException("Native engine node observer Attach was rejected.");
                var after = NewInformation();
                if (_info((uint)Identity.Kind, Identity.Slot, ref after) != 0 || !MatchesInformation(after, Identity, true)
                    || Volatile.Read(ref _unknown) != 0)
                    throw new UnsupportedSignatureException("Native engine node Attach could not be confirmed.");
                _publicationOwner?.VerifyReady();
                Interlocked.Exchange(ref _seenAttached, 1);
            }
            catch { Interlocked.Exchange(ref _unknown, 1); _publicationOwner?.MarkUnknown(); throw; }
        }
    }

    private unsafe nuint Enter(IntPtr pointer, IntPtr user)
    {
        try
        {
            if (pointer == IntPtr.Zero || user != IntPtr.Zero) throw new UnsupportedSignatureException("Invalid engine node Enter record/user.");
            var frame = *(NativeEngineNodeFrame*)pointer;
            // Faulted native snapshots are valid records: the sink must receive
            // the actual call identity and retain its corresponding responsibility.
            if (!frame.IsValid || frame.Kind != Identity.Kind) throw new UnsupportedSignatureException("Engine node Enter differs from its exact ABI.");
            return (Volatile.Read(ref _callbacks) ?? throw new InvalidOperationException("Engine node callbacks are unavailable.")).Enter(frame);
        }
        catch (Exception error) { Fault("Native engine node Enter failed.", error); return 0; }
    }

    private unsafe void Exit(IntPtr pointer, nuint token, IntPtr completionPointer, IntPtr user)
    {
        try
        {
            if (pointer == IntPtr.Zero || completionPointer == IntPtr.Zero || user != IntPtr.Zero)
                throw new UnsupportedSignatureException("Invalid engine node Exit record/user.");
            var frame = *(NativeEngineNodeFrame*)pointer;
            var completion = *(NativeEngineNodeCompletion*)completionPointer;
            if (!frame.IsValid || frame.Kind != Identity.Kind)
                throw new UnsupportedSignatureException("Engine node Exit differs from its exact ABI.");
            // The dispatcher validates completion shape after exact frame
            // pairing, so malformed results still retain captured cleanup duty.
            (Volatile.Read(ref _callbacks) ?? throw new InvalidOperationException("Engine node callbacks are unavailable.")).Exit(frame, token, completion);
        }
        catch (Exception error) { Fault("Native engine node Exit failed.", error); }
    }

    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        if (Volatile.Read(ref _callbacks) is null) return new(0, false);
        try
        {
            _publicationOwner?.VerifyReady();
            var information = NewInformation(); var seenBefore = Volatile.Read(ref _seenAttached);
            var status = _info((uint)Identity.Kind, Identity.Slot, ref information);
            var attached = status == 0 && MatchesInformation(information, Identity, true);
            var valid = attached || status == 0 && seenBefore == 0 && MatchesInformation(information, Identity, false);
            if (attached) Interlocked.Exchange(ref _seenAttached, 1);
            if (!valid) { Interlocked.Exchange(ref _unknown, 1); _publicationOwner?.MarkUnknown(); }
            else _publicationOwner?.VerifyReady();
            return new(information.InFlight <= long.MaxValue ? (long)information.InFlight : 0,
                attached && valid && Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault("Native engine node activity failed.", error); return new(0, false); }
    }

    private void Fault(string message, Exception error)
    { Interlocked.Exchange(ref _unknown, 1); _publicationOwner?.MarkUnknown(); try { _log.Log(LogLevel.Error, message, error); } catch { } }
}
