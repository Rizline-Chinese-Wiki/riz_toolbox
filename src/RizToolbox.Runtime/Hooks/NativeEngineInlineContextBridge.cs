using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Adopts a configured observation-only side family from the same DLL
/// as its exact native Start channel. It neither configures code nor grants a
/// publication, startup admission, cancellation or game Ready credential.</summary>
internal sealed class NativeEngineInlineContextBridge : INativeEngineInlineContextBridge
{
    private static readonly object OwnersGate = new();
    private static readonly Dictionary<(IntPtr Module, uint Slot), NativeEngineInlineContextBridge> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr Callback, User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ScalarNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint slot, ref NativeEngineInlineInformation information);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NodeInfoNative(uint kind, uint slot, ref NativeEngineNodeInformation information);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(uint slot, ulong epoch, ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObserveNative(IntPtr frame, IntPtr user);
    private readonly object _gate = new();
    private readonly IntPtr _module;
    private readonly InfoNative _info;
    private readonly NodeInfoNative _nodeInfo;
    private readonly AttachNative _attach;
    private readonly ObserveNative _observeNative;
    private readonly ILogger _log;
    private readonly INativeEnginePublicationLifetime? _publicationOwner;
    private Action<NativeEngineInlineFrame>? _observe;
    private bool _attempted;
    private int _unknown;
    public NativeEngineInlineChannelIdentity Identity { get; }

    internal static NativeEngineInlineContextBridge AdoptConfigured(string absoluteHelperPath,
        NativeEngineInlineChannelIdentity identity, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(absoluteHelperPath); ArgumentNullException.ThrowIfNull(log);
        if (!Path.IsPathFullyQualified(absoluteHelperPath)) throw new ArgumentException("An absolute configured helper path is required.", nameof(absoluteHelperPath));
        if (!identity.IsComplete) throw new ArgumentException("The complete actual inline and Start channel identities are required.", nameof(identity));
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Native inline observation requires Windows x64.");
        var module = NativeLibrary.Load(Path.GetFullPath(absoluteHelperPath));
        lock (OwnersGate)
        {
            var key = (module, identity.Slot);
            if (Owners.ContainsKey(key)) throw new InvalidOperationException("This permanent native inline family already has a managed owner.");
            var owner = new NativeEngineInlineContextBridge(module, identity, log);
            Owners.Add(key, owner); // Callback delegates and module remain rooted through every later partial failure.
            return owner;
        }
    }

    internal static NativeEngineInlineContextBridge AdoptPublished(NativeEngineTransportFirstInstall owner, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(log);
        try
        {
            owner.VerifyReady(); var identity = owner.InlineIdentity(); var module = owner.KernelModule;
            lock (OwnersGate)
            {
                var key = (module, identity.Slot);
                if (Owners.ContainsKey(key)) throw new InvalidOperationException("The V5 Inline family already has a permanent managed owner.");
                var bridge = new NativeEngineInlineContextBridge(module, identity, log, owner); Owners.Add(key, bridge); return bridge;
            }
        }
        catch { owner.MarkUnknown(); throw; }
    }

    private NativeEngineInlineContextBridge(IntPtr module, NativeEngineInlineChannelIdentity identity, ILogger log, INativeEnginePublicationLifetime? publicationOwner = null)
    {
        (_module, Identity, _log) = (module, identity, log); _publicationOwner = publicationOwner; _publicationOwner?.VerifyReady();
        _info = Export<InfoNative>("RizEngineNodeContext_GetInlineInfo");
        _nodeInfo = Export<NodeInfoNative>("RizEngineNodeContext_GetInfo");
        _attach = Export<AttachNative>("RizEngineNodeContext_AttachInline"); _observeNative = Observe;
        if (Export<ScalarNative>("RizEngineNodeContext_GetAbiVersion")() != 2
            || Export<ScalarNative>("RizEngineNodeContext_GetFrameSize")() != 112
            || Export<ScalarNative>("RizEngineNodeContext_GetCompletionSize")() != 32
            || Export<ScalarNative>("RizEngineNodeContext_GetInlineAbiVersion")() != 1
            || Export<ScalarNative>("RizEngineNodeContext_GetInlineLimit")() != 32
            || Export<ScalarNative>("RizEngineNodeContext_GetInlineFrameSize")() != 144
            || Export<EntryNative>("RizEngineNodeContext_GetInlineEntry")() != identity.Entry
            || Marshal.SizeOf<NativeEngineInlineFrame>() != 144 || Marshal.SizeOf<NativeEngineInlineInformation>() != 160
            || Marshal.SizeOf<NativeEngineInlineConfiguration>() != 88 || Marshal.SizeOf<Observer>() != 24)
            throw new UnsupportedSignatureException("Native inline side ABI1 or unchanged node ABI2 differs from its exact layout.");
        RequireStart(); var information = NewInformation();
        if (_info(identity.Slot, ref information) != 0 || !MatchesInformation(information, identity, false))
            throw new UnsupportedSignatureException("Native inline family differs from the actual unattached configuration.");
        _publicationOwner?.VerifyReady();
    }

    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, name));
    private static NativeEngineInlineInformation NewInformation() => new() { Version = 1, Size = 160 };
    private void RequireStart()
    {
        var information = new NativeEngineNodeInformation { Version = 1, Size = 112 };
        if (_nodeInfo((uint)NativeEngineNodeKind.Start, Identity.Start.Slot, ref information) != 0
            || !NativeEngineNodeContextBridge.MatchesInformation(information, Identity.Start, null))
            throw new UnsupportedSignatureException("Inline family does not share this helper's exact configured Start channel.");
    }
    internal static bool MatchesInformation(NativeEngineInlineInformation value, NativeEngineInlineChannelIdentity identity, bool attached)
        => identity.IsComplete && value.Version == 1 && value.Size == 160 && value.Slot == identity.Slot
            && value.Configured == 1 && value.Attached == (attached ? 1u : 0u) && value.Unknown == 0 && value.Reserved == 0
            && value.MainThreadId == identity.MainThreadId && value.Epoch == identity.Epoch
            && value.StartEpoch == identity.Start.Epoch && value.StartTarget == identity.Start.Target && value.Entry == identity.Entry
            && value.StartsInFlight <= long.MaxValue && value.ProbesInFlight <= long.MaxValue
            && value.Faults == 0 && value.SnapshotFaults == 0 && value.OrphanProbes == 0 && value.UnmatchedStarts == 0;

    public void Attach(Action<NativeEngineInlineFrame> observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        lock (_gate)
        {
            if (_attempted) throw new InvalidOperationException("Native inline Attach is a single permanent attempt.");
            _attempted = true; Volatile.Write(ref _observe, observe);
            try
            {
                _publicationOwner?.VerifyReady(); RequireStart(); var before = NewInformation();
                if (_info(Identity.Slot, ref before) != 0 || !MatchesInformation(before, Identity, false))
                    throw new UnsupportedSignatureException("Native inline identity changed before Attach.");
                var observer = new Observer { Version = 1, Size = 24, Callback = Marshal.GetFunctionPointerForDelegate(_observeNative) };
                if (_attach(Identity.Slot, Identity.Epoch, ref observer) != 0)
                    throw new InvalidOperationException("Native inline Attach was rejected.");
                var after = NewInformation();
                if (_info(Identity.Slot, ref after) != 0 || !MatchesInformation(after, Identity, true) || Volatile.Read(ref _unknown) != 0)
                    throw new UnsupportedSignatureException("Native inline Attach could not be confirmed.");
                RequireStart(); _publicationOwner?.VerifyReady();
            }
            catch { Interlocked.Exchange(ref _unknown, 1); _publicationOwner?.MarkUnknown(); throw; }
        }
    }

    private unsafe void Observe(IntPtr pointer, IntPtr user)
    {
        try
        {
            if (pointer == IntPtr.Zero || user != IntPtr.Zero) throw new UnsupportedSignatureException("Invalid native inline callback pointer or user.");
            var frame = *(NativeEngineInlineFrame*)pointer;
            // Dispatch even malformed/faulted scalar records so an existing
            // frozen Start context receives its failure and retains its duty.
            (Volatile.Read(ref _observe) ?? throw new InvalidOperationException("Native inline callback is unavailable."))(frame);
        }
        catch (Exception error) { Fault("Native inline callback failed.", error); }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        try
        {
            _publicationOwner?.VerifyReady(); RequireStart(); var value = NewInformation();
            var valid = _info(Identity.Slot, ref value) == 0 && MatchesInformation(value, Identity, true)
                && value.StartsInFlight <= (ulong)long.MaxValue - value.ProbesInFlight;
            if (!valid) { Interlocked.Exchange(ref _unknown, 1); _publicationOwner?.MarkUnknown(); }
            else _publicationOwner?.VerifyReady();
            return new(valid ? checked((long)(value.StartsInFlight + value.ProbesInFlight)) : 0,
                valid && Volatile.Read(ref _observe) is not null && Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault("Native inline activity failed.", error); return new(0, false); }
    }
    private void Fault(string message, Exception error)
    { Interlocked.Exchange(ref _unknown, 1); _publicationOwner?.MarkUnknown(); try { _log.Log(LogLevel.Error, message, error); } catch { } }
}
