using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Process-retained owner of the independent typed source helper.
/// Channels share its native TLS, counters and bounded completion journal.
/// This is an execution bridge, not production first-install admission.</summary>
internal sealed partial class NativePlaybackStockSourceHub
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VersionNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObserverNative(IntPtr context, IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateNative(ObserverNative observer, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConfigureNative(IntPtr owner, uint kind, uint slot, uint role, IntPtr target, IntPtr original);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative(uint kind, uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ArmedNative(IntPtr owner, ulong epoch, int armed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FaultNative(IntPtr owner, ulong epoch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InfoNative(uint kind, uint slot, ref NativePlaybackSourceInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OwnerNative(IntPtr owner, ref NativePlaybackSourceInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CurrentNative(uint kind, uint slot, ref NativePlaybackSourceFrame frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int VerifyNative(IntPtr owner, ref NativePlaybackSourceFrame frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReceiptNative(IntPtr owner, ulong sequence, ulong token, ref NativePlaybackSourceFrame frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReleaseNative(IntPtr owner, ulong sequence, ulong token);
    private static readonly object ProcessGate = new();
    private static readonly Dictionary<string, uint> NextSlots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<NativePlaybackStockSourceHub> ProcessOwners = new();
    private readonly object _gate = new();
    private readonly uint _ownerOsThreadId = GetCurrentThreadId();
    private readonly string _path;
    private readonly NativePlaybackExecutableSeal _image;
    private readonly IntPtr _library, _owner, _context;
    private readonly ConfigureNative _configure;
    private readonly EntryNative _entry;
    private readonly ArmedNative _armed;
    private readonly FaultNative _markFault;
    private readonly InfoNative _info;
    private readonly OwnerNative _readOwner;
    private readonly CurrentNative _current;
    private readonly VerifyNative _verify;
    private readonly ReceiptNative _receipt;
    private readonly ReleaseNative _release;
    private readonly ObserverNative _observer;
    private readonly Action<NativePlaybackSourceFault> _faultOwner;
    private readonly Action<NativePlaybackSourceFault>? _synchronousFault;
    private readonly Dictionary<uint, NativePlaybackStockSourceBridge> _channels = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private int _notified;
    private bool _armAttempted, _withdrawn;
    private NativePlaybackSourceFault? _fault;
    internal ulong Epoch { get; }
    internal IntPtr NativeOwner => _owner;
    internal IntPtr NativeLibraryHandle { get { VerifyThread(); _image.Verify(); return _library; } }
    internal bool ProductionAdmission => false;

    internal NativePlaybackStockSourceHub(string path, string sha256, Action<NativePlaybackSourceFault> faultOwner,
        Action<NativePlaybackSourceFault>? synchronousFault = null)
    {
        ArgumentNullException.ThrowIfNull(faultOwner);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || !Path.IsPathFullyQualified(path))
            throw new UnsupportedSignatureException("The typed source bridge requires a hashed Windows x64 helper at an absolute path.");
        _path = Path.GetFullPath(path); _faultOwner = faultOwner; _synchronousFault = synchronousFault;
        _library = NativeLibrary.Load(_path);
        _image = NativePlaybackExecutableSeal.Capture(_library, sha256);
        if (Marshal.SizeOf<NativePlaybackSourceFrame>() != 168 || Marshal.SizeOf<NativePlaybackSourceInformation>() != 136
            || Export<VersionNative>("RtPlaybackSourceVersion")() != 4)
            throw new UnsupportedSignatureException("The independent typed source helper wire differs from v4.");
        // Bind the additive ABI before issuing an owner or reserving a channel.
        // This does not admit a lower-time SourceOwner or change frame rejection.
        _lowerTime = new LowerTimeNativeApi(this);
        _configure = Export<ConfigureNative>("RtPlaybackSourceConfigure"); _entry = Export<EntryNative>("RtPlaybackSourceGetEntry");
        _armed = Export<ArmedNative>("RtPlaybackSourceSetArmed"); _markFault = Export<FaultNative>("RtPlaybackSourceMarkFault");
        _info = Export<InfoNative>("RtPlaybackSourceGetInfo"); _readOwner = Export<OwnerNative>("RtPlaybackSourceReadOwner");
        _current = Export<CurrentNative>("RtPlaybackSourceReadCurrent"); _verify = Export<VerifyNative>("RtPlaybackSourceVerifyCurrent");
        _receipt = Export<ReceiptNative>("RtPlaybackSourceReadReceipt"); _release = Export<ReleaseNative>("RtPlaybackSourceReleaseReceipt");
        _observer = Observe;
        _context = GCHandle.ToIntPtr(GCHandle.Alloc(this));
        lock (ProcessGate) ProcessOwners.Add(this); // Native callbacks survive failed Configure and logical withdrawal.
        _owner = Export<CreateNative>("RtPlaybackSourceCreateOwner")(_observer, _context);
        if (_owner == IntPtr.Zero) throw new UnsupportedSignatureException("The native source owner capacity is exhausted.");
        var owner = NewInformation();
        if (_readOwner(_owner, ref owner) != 1 || owner.Owner != _owner || owner.OwnerEpoch == 0
            || owner.Kind != 0 || owner.Role != 0 || owner.Slot != uint.MaxValue
            || owner.ConfiguredSlots != 0 || owner.InFlight != 0 || owner.Entered != 0 || owner.Exited != 0
            || owner.WriterSequence != 1 || owner.SourceSequence != 1 || owner.SchedulingSequence != 1
            || owner.Flags != NativePlaybackSourceFlags.Dormant || owner.Faults != 0)
            throw new UnsupportedSignatureException("The newly issued native source owner has an unexpected identity.");
        Epoch = owner.OwnerEpoch; ValidateOwner(owner);
    }
    private T Export<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    internal uint ReserveChannel(NativePlaybackStockSourceBridge channel)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(channel);
        lock (_gate)
        {
            if (_armAttempted || _withdrawn || _fault is not null)
                throw new InvalidOperationException("Source channels must be created before the native owner is armed.");
            lock (ProcessGate)
            {
                NextSlots.TryGetValue(_path, out var slot);
                if (slot >= 32) throw new UnsupportedSignatureException("The source helper's physical slot capacity is exhausted.");
                NextSlots[_path] = slot + 1; _channels.Add(slot, channel); return slot;
            }
        }
    }
    internal IntPtr GetEntry(NativePlaybackSourceKind kind, uint slot)
    {
        _image.Verify(); var entry = _entry((uint)kind, slot);
        if (entry == IntPtr.Zero) throw new UnsupportedSignatureException("The exact typed source native entry is unavailable.");
        HookTargetValidation.VerifyExecutable(entry, _library); return entry;
    }
    internal void VerifyReservation(NativePlaybackStockSourceBridge channel)
    {
        // This checks retained immutable entry identity, including from an
        // original-only GetTime observer on another thread. It performs no
        // reservation, configuration, source capture or authority issuance.
        // The actual installation and private observation paths retain their
        // own owner-thread checks; a worker source entry still becomes Unknown.
        _image.Verify();
        lock (_gate)
        {
            if (_withdrawn || _fault is not null || !_channels.TryGetValue(channel.Slot, out var actual)
                || !ReferenceEquals(actual, channel) || channel.EntryAddress != GetEntry(channel.Kind, channel.Slot))
                throw new UnsupportedSignatureException("The native channel lost its exact retained helper entry reservation.");
        }
    }
    internal void Configure(NativePlaybackStockSourceBridge channel)
    {
        VerifyThread(); _image.Verify();
        lock (_gate)
        {
            if (_armAttempted || _withdrawn || _fault is not null || !_channels.TryGetValue(channel.Slot, out var actual)
                || !ReferenceEquals(actual, channel)) throw new InvalidOperationException("The source channel has no current dormant owner reservation.");
            if (_configure(_owner, (uint)channel.Kind, channel.Slot, (uint)channel.Role, channel.TargetAddress, channel.OriginalAddress) != 1)
                throw new UnsupportedSignatureException("The native source Configure rejected its exact channel once-only reservation.");
            VerifyChannel(channel);
        }
    }
    internal void Arm()
    {
        VerifyThread();
        lock (_gate)
        {
            if (_fault is not null || _withdrawn) throw new UnsupportedSignatureException("A source owner cannot be revived after withdrawal or fault.");
            if (_armAttempted)
            {
                if (!ReadHealth().Armed) throw new UnsupportedSignatureException("The native source arm attempt did not retain authority.");
                return;
            }
            foreach (var channel in _channels.Values) VerifyChannel(channel);
            if (_channels.Values.Any(value => value.Role == NativePlaybackSourceRole.LowerSetTime) && !_lowerTimeConfigured)
                throw new UnsupportedSignatureException("The exact lower-time wrapper and native member must bind before the first Arm.");
            if (_channels.Values.Any(value => value.Role == NativePlaybackSourceRole.LowerSetPitch) && !_lowerPitchConfigured)
                throw new UnsupportedSignatureException("The lower icall and wrapper must bind their exact configured channels before the first Arm.");
            if (_channels.Values.Any(value => !value.IsConfigured)
                || _channels.Values.Any(value => value.Role == NativePlaybackSourceRole.EngineSetPitch) && !_enginePitchConfigured)
                throw new UnsupportedSignatureException("Every actual source original and the fixed engine pair must be configured before Arm.");
            _armAttempted = true;
            if (_armed(_owner, Epoch, 1) != 1 || !ReadHealth().Armed)
            { MarkBrokerFault("The source owner did not arm at its actual dormant, zero-in-flight boundary."); throw new UnsupportedSignatureException("Native source arm/readback failed."); }
        }
    }
    internal void Withdraw()
    {
        lock (_gate)
        {
            if (_withdrawn) return;
            _withdrawn = true;
            if (_armed(_owner, Epoch, 0) != 1) MarkBrokerFault("Native source withdrawal was rejected.");
        }
    }
    internal NativePlaybackSourceHealth ReadHealth()
    {
        try
        {
            _image.Verify(); var info = NewInformation();
            if (_readOwner(_owner, ref info) != 1) throw new UnsupportedSignatureException("The actual native source owner cannot be read.");
            ValidateOwner(info);
            var unknown = (info.Flags & NativePlaybackSourceFlags.Unknown) != 0 || info.Faults != 0;
            if (unknown) MarkBrokerFault("The native source owner reported sticky Unknown or a source callback fault.");
            return new(_fault is null && !unknown, (info.Flags & NativePlaybackSourceFlags.Armed) != 0 && _fault is null && !_withdrawn,
                info.Entered, info.Exited, info.InFlight, info.WriterSequence, info.SourceSequence, info.SchedulingSequence,
                info.Faults, unknown || _fault is not null, _fault?.Reason);
        }
        catch (Exception error)
        {
            MarkBrokerFault("The source helper or actual owner identity changed.", error);
            return new(false, false, 0, 0, ulong.MaxValue, 0, 0, 0, ulong.MaxValue, true, _fault?.Reason);
        }
    }
    internal void VerifyChannel(NativePlaybackStockSourceBridge channel)
    {
        _image.Verify(); channel.VerifyOriginalHead(); var info = NewInformation();
        if (_info((uint)channel.Kind, channel.Slot, ref info) != 1) throw new UnsupportedSignatureException("The native source channel cannot be read.");
        ValidateOwner(info);
        if (info.Kind != channel.Kind || info.Role != channel.Role || info.Slot != channel.Slot
            || info.Target != channel.TargetAddress || info.Original != channel.OriginalAddress || info.Entry != channel.EntryAddress)
            throw new UnsupportedSignatureException("The actual source channel differs from its immutable target/original/entry identity.");
    }
    internal NativePlaybackSourceFrame ReadCurrentActual(NativePlaybackStockSourceBridge channel,
        NativePlaybackSourceFrame expected, bool requireBefore, bool allowWithdrawnCompletion)
    {
        if (allowWithdrawnCompletion) VerifyKnown(channel); else VerifyAuthority(channel);
        var actual = NewFrame();
        if (_current((uint)channel.Kind, channel.Slot, ref actual) != 1 || !NativePlaybackStockSourceBridge.SameCall(expected, actual)
            || requireBefore && actual.Stage != NativePlaybackSourceStage.Before
            || allowWithdrawnCompletion && actual.Stage is not (NativePlaybackSourceStage.OriginalReturned or NativePlaybackSourceStage.Abnormal))
            throw new UnsupportedSignatureException("No exact native current source scope exists for this observation.");
        ValidateFrame(actual, physical: false);
        if (_verify(_owner, ref actual) != 1) throw new UnsupportedSignatureException("The native source current-scope verifier rejected its private owner/token.");
        if (allowWithdrawnCompletion) VerifyKnown(channel); else VerifyAuthority(channel);
        return actual;
    }
    internal void VerifyKnown(NativePlaybackStockSourceBridge channel)
    {
        VerifyChannel(channel);
        if (!ReadHealth().Known)
            throw new UnsupportedSignatureException("This source channel lost its known native owner identity.");
    }
    internal void VerifyAuthority(NativePlaybackStockSourceBridge channel)
    {
        VerifyChannel(channel);
        var health = ReadHealth();
        if (!health.Known || !health.Armed)
            throw new UnsupportedSignatureException("This source channel has no current armed native owner authority.");
    }
    internal bool TryReadReceipt(NativePlaybackSourceFrame origin, out NativePlaybackSourceFrame actual)
    {
        _image.Verify(); actual = NewFrame();
        if (_receipt(_owner, origin.Sequence, origin.Token, ref actual) != 1) return false;
        if (!NativePlaybackStockSourceBridge.SameCall(origin, actual))
            throw new UnsupportedSignatureException("The native source receipt belongs to another actual call.");
        ValidateFrame(actual, physical: true); return true;
    }
    internal bool Release(NativePlaybackSourceFrame origin)
        => _release(_owner, origin.Sequence, origin.Token) == 1;
    internal void MarkBrokerFault(string reason, Exception? error = null)
    {
        var fault = new NativePlaybackSourceFault(reason, error);
        var first = Interlocked.CompareExchange(ref _fault, fault, null) is null;
        if (first && _owner != IntPtr.Zero) _markFault(_owner, Epoch);
        // Broker policy withdrawal must finish before a native failure returns.
        if (first) { try { _synchronousFault?.Invoke(_fault!); } catch { } }
        if (Interlocked.Exchange(ref _notified, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ => { try { _faultOwner(_fault!); } catch { } });
    }
    private unsafe void Observe(IntPtr context, IntPtr pointer)
    {
        try
        {
            if (context != _context || pointer == IntPtr.Zero) throw new UnsupportedSignatureException("The actual source callback context is invalid.");
            var frame = *(NativePlaybackSourceFrame*)pointer;
            NativePlaybackStockSourceBridge channel;
            lock (_gate)
            {
                if (!_channels.TryGetValue(frame.Slot, out channel!)) throw new UnsupportedSignatureException("The native callback has no retained source channel.");
            }
            channel.ObserveActual(frame);
        }
        catch (Exception error) { MarkBrokerFault("The actual source observer failed; the original remains owned by native code.", error); }
    }
    private void ValidateOwner(NativePlaybackSourceInformation info)
    {
        if (info.Version != 4 || info.Size != 136 || info.Owner != _owner || info.OwnerEpoch != Epoch
            || info.OwnerEpoch == 0 || info.OwnerThreadId == 0 || info.OwnerThreadId != _ownerOsThreadId
            || info.Reserved != 0 || info.JournalCapacity != 256 || info.ConfiguredSlots > 32
            || info.Entered < info.Exited || info.Entered - info.Exited != info.InFlight
            || ((uint)info.Flags & ~((uint)NativePlaybackSourceFlags.Armed | (uint)NativePlaybackSourceFlags.Dormant
                | (uint)NativePlaybackSourceFlags.Unknown | (uint)NativePlaybackSourceFlags.Withdrawn)) != 0)
            throw new UnsupportedSignatureException("The source owner wire, counters or immutable identity changed.");
    }
    // This native journal fact carries physical completion ownership only.
    // Dormant engine and configured time attempts retain their real rows while
    // the logical source gate is closed. Before eligibility comes from the
    // authenticated native frame and remains attached to its issued observation.
    internal bool IsResourceInitializationResponsibility(NativePlaybackSourceFrame frame)
        => (frame.Flags & NativePlaybackSourceFlags.Dormant) != 0
            && (frame.Flags & NativePlaybackSourceFlags.Armed) == 0
            && (frame.Flags & NativePlaybackSourceFlags.InitializationResponsibility) != 0
            && (_progressResourceEnabled && frame.Role is (NativePlaybackSourceRole.ProgressStartMoveNext
                    or NativePlaybackSourceRole.SetClip or NativePlaybackSourceRole.ProgressClip)
                || _enginePitchConfigured && frame.Role == NativePlaybackSourceRole.EngineSetPitch
                || frame.Role is (NativePlaybackSourceRole.SetTime or NativePlaybackSourceRole.LowerSetTime)
                    && IsConfiguredLowerTimeFrame(frame));
    internal bool CanObserveResourceInitialization(NativePlaybackStockSourceBridge channel, NativePlaybackSourceFrame frame)
    {
        if (frame.Stage != NativePlaybackSourceStage.Before || !IsResourceInitializationResponsibility(frame)) return false;
        // Native sets this private frame fact only after it actually allocates
        // an initialization journal row. A merely closed ordinary handoff has
        // no such receipt, even if Start subsequently changes FreshBlocked.
        var actual = ReadOperationCurrentActual(channel, frame, requireBefore: true, allowWithdrawnCompletion: false);
        return IsResourceInitializationResponsibility(actual);
    }
    private void ValidateFrame(NativePlaybackSourceFrame frame, bool physical)
    {
        // Later physical receipts use their exact issued native origin; the
        // current gate may already have opened or been normally withdrawn.
        var initializing = IsResourceInitializationResponsibility(frame);
        var lowerChild = (frame.Flags & NativePlaybackSourceFlags.LowerPitchChild) != 0;
        var engineChild = (frame.Flags & NativePlaybackSourceFlags.EnginePitchChild) != 0;
        var lowerTimeChild = (frame.Flags & NativePlaybackSourceFlags.LowerTimeChild) != 0;
        var lowerTimeFrame = frame.Role == NativePlaybackSourceRole.LowerSetTime && IsConfiguredLowerTimeFrame(frame);
        if (frame.Version != 4 || frame.Size != 168 || frame.Owner != _owner || frame.OwnerEpoch != Epoch || frame.Sequence == 0
            || frame.Token == 0 || frame.Self == IntPtr.Zero || frame.Caller == IntPtr.Zero || frame.Depth is 0 or > 64
            || frame.ThreadId == 0 || frame.Reserved != 0
            || frame.Kind == NativePlaybackSourceKind.IcallSingle1
                && (frame.Role is not (NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch
                    or NativePlaybackSourceRole.LowerSetTime) || frame.IncomingMethod != IntPtr.Zero)
            || (frame.Role is NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch
                    or NativePlaybackSourceRole.LowerSetTime) && frame.Kind != NativePlaybackSourceKind.IcallSingle1
            || frame.Role == NativePlaybackSourceRole.LowerSetTime && !lowerTimeFrame
            || lowerChild && (!_lowerPitchConfigured || frame.Kind != NativePlaybackSourceKind.IcallSingle1
                || frame.Role != NativePlaybackSourceRole.LowerSetPitch || frame.ParentSequence == 0)
            || engineChild && (!_enginePitchConfigured || frame.Kind != NativePlaybackSourceKind.IcallSingle1
                || frame.Role != NativePlaybackSourceRole.EngineSetPitch || frame.ParentSequence == 0 || lowerChild)
            || lowerTimeChild && (!lowerTimeFrame || frame.ParentSequence == 0 || frame.Depth <= 1 || lowerChild || engineChild)
            || frame.ReturnPresent > 1 || frame.RawReturnByte > byte.MaxValue
            || frame.ReturnPresent == 0 && frame.RawReturnByte != 0
            || (frame.ReturnPresent == 1) != (frame.Kind == NativePlaybackSourceKind.BooleanReturn0
                && (frame.Flags & NativePlaybackSourceFlags.OriginalReturned) != 0
                && (frame.Flags & NativePlaybackSourceFlags.Abnormal) == 0) || frame.Entered < frame.Exited || frame.Entered - frame.Exited != frame.InFlight
            || frame.Stage is < NativePlaybackSourceStage.Before or > NativePlaybackSourceStage.PhysicalExit
            || physical != (frame.Stage == NativePlaybackSourceStage.PhysicalExit)
            || physical != ((frame.Flags & NativePlaybackSourceFlags.PhysicalExited) != 0)
            || !initializing && ((frame.Flags & NativePlaybackSourceFlags.Armed) == 0 || (frame.Flags & NativePlaybackSourceFlags.Dormant) != 0)
            || !physical && (frame.Flags & NativePlaybackSourceFlags.Unknown) != 0
            || (physical || frame.Stage != NativePlaybackSourceStage.Before)
                && ((frame.Flags & NativePlaybackSourceFlags.OriginalReturned) != 0) == ((frame.Flags & NativePlaybackSourceFlags.Abnormal) != 0))
            throw new UnsupportedSignatureException("The actual source frame has no complete armed scope/return identity.");
    }
    private void VerifyThread()
    { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("The source installation owner is main-thread bound."); }
    private static NativePlaybackSourceFrame NewFrame() => new() { Version = 4, Size = 168 };
    private static NativePlaybackSourceInformation NewInformation() => new() { Version = 4, Size = 136 };
}
