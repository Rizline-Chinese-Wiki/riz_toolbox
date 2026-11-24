using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Process-owned controlled prototype for instance Single(). It loads
/// an explicitly hashed owned helper; it neither installs a detour nor admits
/// production, modifies the SDK, or changes FirstInstall V7.</summary>
internal sealed partial class NativePlaybackSingleReturnBridge
{
    private static readonly object OwnersGate = new();
    private static readonly Dictionary<string, uint> NextSlots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<NativePlaybackSingleReturnBridge> ProcessOwners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Config
    { internal uint Version, Size; internal IntPtr Original, Enter, Exit, User; }
    [StructLayout(LayoutKind.Sequential)] private struct Stats
    {
        internal uint Version, Size;
        internal ulong Entered, Exited, InFlight, OriginalCalls, EnterFaults, ExitFaults, SourceFaults, Applied, Declined, Withdrawals;
        internal uint Unknown, Configured, ObserverEnabled, AuthorityActive;
        internal ulong PolicyGeneration;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint LimitNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative(uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(uint slot, ref Config config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint PublishNative(uint slot, ulong expected, ref NativePlaybackSingleReturnNativePolicy policy);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadPolicyNative(uint slot, ref NativePlaybackSingleReturnNativePolicy policy, out uint active);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WithdrawNative(uint slot, ulong expected);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint MarkFaultNative(uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint StatsNative(uint slot, ref Stats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint EnterNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExitNative(IntPtr frame, IntPtr completion, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ClockReadNative(IntPtr sample, IntPtr user);
    private readonly object _gate = new();
    private readonly FileStream _imageLock;
    private readonly IntPtr _library, _owner;
    private readonly uint _slot;
    private readonly PublishNative _publish;
    private readonly ReadPolicyNative _readPolicy;
    private readonly WithdrawNative _withdraw;
    private readonly MarkFaultNative _markFault;
    private readonly StatsNative _stats;
    private readonly RetryInitializationNative _retryAudioInterruptedInitialCapture, _retryAudioInterruptedInitializationHandoff;
    private readonly EnterNative _enterNative;
    private readonly ExitNative _exitNative;
    private readonly ClockReadNative _clockReadNative;
    private readonly IntPtr _readerPointer;
    private readonly Func<NativePlaybackSingleReturnSample> _readClock;
    private readonly Action<NativePlaybackSingleReturnFault> _faultOwner;
    private readonly Action<NativePlaybackSingleReturnFrame>? _before;
    private readonly Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? _after;
    private NativePlaybackSingleReturnAuthority? _published;
    private NativePlaybackSingleReturnFault? _fault;
    private readonly int _faultCleanupThread = Environment.CurrentManagedThreadId;
    private int _nativeFaultMarked;
    private bool _faultCleanupAttempted;
    private int _notified;
    private double _lastDsp = double.NegativeInfinity;
    internal IntPtr EntryAddress { get; }
    internal bool ProductionAdmission => false;

    internal NativePlaybackSingleReturnBridge(string absoluteHelperPath, string expectedSha256, IntPtr original,
        Func<NativePlaybackSingleReturnSample> readClock, Action<NativePlaybackSingleReturnFault> faultOwner,
        Action<NativePlaybackSingleReturnFrame>? before = null,
        Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? after = null)
        : this(absoluteHelperPath, expectedSha256, _ => original, readClock, faultOwner, before, after) { }

    // The broker obtains the actual entry before DobbyPrepare and configures
    // this immutable native slot only with the resulting original trampoline.
    // This factory is an installation dependency, never a caller verifier.
    internal NativePlaybackSingleReturnBridge(string absoluteHelperPath, string expectedSha256, Func<IntPtr, IntPtr> prepareOriginal,
        Func<NativePlaybackSingleReturnSample> readClock, Action<NativePlaybackSingleReturnFault> faultOwner,
        Action<NativePlaybackSingleReturnFrame>? before = null,
        Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? after = null)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("The controlled Single prototype requires Windows x64.");
        if (!Path.IsPathFullyQualified(absoluteHelperPath))
            throw new ArgumentException("A controlled helper absolute path and the exact owned native original are required.");
        ArgumentNullException.ThrowIfNull(prepareOriginal); ArgumentNullException.ThrowIfNull(readClock); ArgumentNullException.ThrowIfNull(faultOwner);
        var path = Path.GetFullPath(absoluteHelperPath);
        _imageLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = SHA256.Create();
        if (!CryptographicOperations.FixedTimeEquals(hash.ComputeHash(_imageLock), Convert.FromHexString(expectedSha256)))
            throw new InvalidOperationException("The controlled helper differs from the independently supplied image hash.");
        _library = NativeLibrary.Load(path); _readClock = readClock; _faultOwner = faultOwner; _before = before; _after = after;
        // Required before prepareOriginal can perform any physical Dobby work.
        // Only native registered receipts can be consumed by these exports.
        _retryAudioInterruptedInitialCapture = Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryAudioInterruptedInitialCapture");
        _retryAudioInterruptedInitializationHandoff = Export<RetryInitializationNative>("RizSingleReturnPrototype_RetryAudioInterruptedInitializationHandoff");
        if (Export<InitialCaptureVersionNative>("RizSingleReturnPrototype_InitialCaptureVersion")() != 1)
            throw new UnsupportedSignatureException("Audio-interruption retries require the actual private first-capture V1 coordinator.");
        _publish = Export<PublishNative>("RizSingleReturnPrototype_PublishMusicClock");
        _readPolicy = Export<ReadPolicyNative>("RizSingleReturnPrototype_ReadClockPolicy");
        _withdraw = Export<WithdrawNative>("RizSingleReturnPrototype_WithdrawMusicClock");
        _markFault = Export<MarkFaultNative>("RizSingleReturnPrototype_MarkFault");
        _stats = Export<StatsNative>("RizSingleReturnPrototype_GetStats");
        if (Export<LimitNative>("RizSingleReturnPrototype_GetLimit")() != 32 || Marshal.SizeOf<Config>() != 40
            || Marshal.SizeOf<Stats>() != 112 || Marshal.SizeOf<NativePlaybackSingleReturnFrame>() != 80
            || Marshal.SizeOf<NativePlaybackSingleReturnCompletion>() != 48 || Marshal.SizeOf<NativePlaybackSingleReturnNativePolicy>() != 128
            || Marshal.SizeOf<NativePlaybackSingleReturnNativeSample>() != 72)
            throw new InvalidOperationException("The controlled Single prototype layout differs.");
        _enterNative = Enter; _exitNative = Exit; _clockReadNative = ReadClock;
        _readerPointer = Marshal.GetFunctionPointerForDelegate(_clockReadNative);
        _owner = GCHandle.ToIntPtr(GCHandle.Alloc(this)); // native immutable process owner; never recycled
        lock (OwnersGate)
        {
            NextSlots.TryGetValue(path, out _slot);
            if (_slot >= 32) throw new InvalidOperationException("The controlled Single prototype target capacity is exhausted.");
            NextSlots[path] = _slot + 1;
            ProcessOwners.Add(this); // callbacks/image/metadata remain rooted even after an uncertain Configure
        }
        EntryAddress = Export<EntryNative>("RizSingleReturnPrototype_GetEntry")(_slot);
        if (EntryAddress == IntPtr.Zero) throw new InvalidOperationException("The owned exact Single entry is invalid.");
        var original = prepareOriginal(EntryAddress);
        if (original == IntPtr.Zero || EntryAddress == original) throw new InvalidOperationException("The owned exact Single original is invalid.");
        var config = new Config { Version = 1, Size = 40, Original = original, Enter = Marshal.GetFunctionPointerForDelegate(_enterNative),
            Exit = Marshal.GetFunctionPointerForDelegate(_exitNative), User = _owner };
        if (Export<ConfigureNative>("RizSingleReturnPrototype_Configure")(_slot, ref config) != 0)
            throw new InvalidOperationException("The native Single prototype target is already claimed or invalid.");
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    internal void MarkBrokerFault(string reason) => ReportFault(reason);

    // The Adapter invokes this while holding its immutable clock-state lock.
    // Native CAS requires zero inFlight before publishing; callbacks cannot
    // observe a model/native split after the Adapter assigns the new snapshot.
    internal void Publish(long expectedRevision, NativePlaybackSingleReturnAuthority next)
    {
        ArgumentNullException.ThrowIfNull(next);
        lock (_gate)
        {
            if (_freshSourceCapture is not null && _freshClock is not { HasTransferred: true }) throw new UnsupportedSignatureException("A Fresh source requires its registered typed Clock publisher.");
            if (_fault is not null) throw new InvalidOperationException("A faulted Single authority requires a new owner; publishing is permanently refused.");
            if (!next.IsValid || expectedRevision < 0 || (_published?.Revision ?? 0) != expectedRevision
                || next.Revision <= expectedRevision || _published is { } prior && (prior.Identity != next.Identity || prior.PermittedNullCaller != next.PermittedNullCaller))
                throw new InvalidOperationException("The exact clock binding or expected publication revision differs.");
            var value = next.ToNative(_readerPointer, _owner);
            var result = _publish(_slot, checked((ulong)expectedRevision), ref value);
            if (result != 0) throw new InvalidOperationException($"Controlled Single clock publication rejected: {result}.");
            _published = next;
            if (!ReadCurrentCore().Active) { ReportFault("The native clock publication did not read back as active."); throw new InvalidOperationException("Native Single publication readback failed."); }
        }
    }
    internal (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadCurrent()
    { lock (_gate) return ReadCurrentCore(); }
    // Private publication receipt: Publish stores this exact object only after
    // native CAS succeeds and before readback, so failed readback still leaves
    // a usable conditional cleanup ticket. Equal caller-made data is no ticket.
    internal bool OwnsPublication(NativePlaybackSingleReturnAuthority candidate)
    { lock (_gate) return ReferenceEquals(_published, candidate); }
    private (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadCurrentCore()
    {
        var value = new NativePlaybackSingleReturnNativePolicy { Version = 1, Size = 128 };
        var result = _readPolicy(_slot, ref value, out var active);
        if (result == 2 && _published is null && active == 0) return (null, false);
        if (result != 0 || active > 1 || _published is null || !_published.MatchesNative(value, _readerPointer, _owner))
        { ReportFault("The actual native Single policy differs from its immutable managed publication."); throw new InvalidOperationException("Native Single policy readback mismatch."); }
        return (_published, active == 1);
    }
    internal bool Withdraw(long expectedRevision)
    {
        lock (_gate)
        {
            if (expectedRevision <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            var result = _withdraw(_slot, checked((ulong)expectedRevision));
            if (result == 5) return false;
            if (result != 0) { ReportFault("Native conditional Single withdrawal failed."); throw new InvalidOperationException($"Native Single withdrawal rejected: {result}."); }
            return !ReadCurrentCore().Active;
        }
    }
    internal NativePlaybackSingleReturnHealth ReadHealth()
    {
        var stats = new Stats { Version = 1, Size = 112 };
        var result = _stats(_slot, ref stats);
        if (result != 0 || stats.Version != 1 || stats.Size != 112 || stats.Configured != 1 || stats.AuthorityActive > 1
            || stats.Unknown != 0 || stats.EnterFaults != 0 || stats.ExitFaults != 0 || stats.SourceFaults != 0)
            ReportFault("The native Single transport reported sticky Unknown or a native observer/source fault.");
        if (stats.PolicyGeneration > long.MaxValue) ReportFault("Native Single policy generation overflowed its managed identity domain.");
        CleanupFaultPublication();
        return new(_fault is null && result == 0, stats.AuthorityActive == 1, stats.PolicyGeneration <= long.MaxValue ? (long)stats.PolicyGeneration : 0,
            stats.InFlight, stats.OriginalCalls, stats.Applied, stats.Declined, stats.EnterFaults, stats.ExitFaults, stats.SourceFaults, _fault?.Reason);
    }
    private unsafe nuint Enter(IntPtr pointer, IntPtr user)
    {
        try
        {
            var frame = *(NativePlaybackSingleReturnFrame*)pointer;
            if (user != _owner || !frame.IsValid || frame.ThreadId != NativeTweenSourceReferences.CurrentThreadId())
                throw new InvalidOperationException("Native Single physical entry is invalid.");
            _before?.Invoke(frame); return checked((nuint)frame.Sequence);
        }
        catch (Exception error) { ReportFault("Managed Single entry observer failed; preserving the native original.", error); return 0; }
    }
    private unsafe void Exit(IntPtr framePointer, IntPtr completionPointer, IntPtr user)
    {
        try
        {
            var frame = *(NativePlaybackSingleReturnFrame*)framePointer;
            var completion = *(NativePlaybackSingleReturnCompletion*)completionPointer;
            if (user != _owner || !frame.IsValid || frame.ThreadId != NativeTweenSourceReferences.CurrentThreadId()
                || completion.Version != 1 || completion.Size != 48 || completion.Sequence != frame.Sequence
                || completion.PolicyGeneration != frame.PolicyGeneration || completion.Disposition > 11
                || ((uint)completion.Flags & ~31u) != 0
                || ((completion.Flags & NativePlaybackSingleReturnFlags.Returned) != 0)
                    == ((completion.Flags & NativePlaybackSingleReturnFlags.Abnormal) != 0)
                || (completion.Flags & NativePlaybackSingleReturnFlags.ObserverCaptured) == 0
                || (completion.Flags & NativePlaybackSingleReturnFlags.EnterFaulted) == 0
                    && completion.ObserverToken != checked((nuint)frame.Sequence))
                throw new InvalidOperationException("Native Single original completion is invalid.");
            if ((completion.Flags & NativePlaybackSingleReturnFlags.Abnormal) != 0)
                ReportFault("The exact native GetTime original exited abnormally; the audio/clock owner must withdraw.");
            else if (completion.Disposition is 2 or 3 or 4 or 5 or 7 or 9 or 10 or 11)
                ReportFault("The exact native clock declined an invalid or faulted return authority; stock fallback is not synchronized recovery.");
            _after?.Invoke(frame, completion);
        }
        catch (Exception error) { ReportFault("Managed Single completion observer failed; preserving the native original.", error); }
    }
    private unsafe uint ReadClock(IntPtr pointer, IntPtr user)
    {
        try
        {
            NativePlaybackSingleReturnAuthority current;
            lock (_gate)
            {
                if (user != _owner || _fault is not null || _published is not { } captured) return 1;
                current = captured;
            }
            // Never acquire the Adapter's clock-state lock while holding this
            // bridge lock: model publication takes those locks in reverse order.
            var sample = _readClock();
            lock (_gate)
            {
                if (_fault is not null || !ReferenceEquals(_published, current)) return 1;
                if (sample.Identity != current.Identity || sample.Revision != current.Revision || sample.Phase != current.Phase
                    || !double.IsFinite(sample.DspSeconds) || sample.DspSeconds < 0 || sample.DspSeconds < _lastDsp)
                    throw new InvalidOperationException("The actual DSP/run/source graph no longer belongs to the exact published clock snapshot.");
                _lastDsp = sample.DspSeconds; var run = current.RunFingerprint();
                *(NativePlaybackSingleReturnNativeSample*)pointer = new() { Version = 1, Size = 72, Self = sample.Identity.Player,
                    Method = sample.Identity.VerifiedGetTimeMethod, RunHigh = run.High, RunLow = run.Low,
                    ControllerGeneration = checked((ulong)sample.Identity.ControllerGeneration), Generation = checked((ulong)sample.Revision),
                    Phase = sample.Phase, DspNow = sample.DspSeconds };
                return 0;
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                // A captured read may be completing after this exact publisher
                // withdrew. No successor or foreign policy is inferred from flags.
                if (_fault is null && _published is not null && !HasNativeReaderFailure(error))
                {
                    try
                    {
                        // An actual getter may discover a normal backing change
                        // during this reader, after native's pre-candidate check.
                        // Revoke under the bound native joint gate before deciding
                        // whether the old read failed or merely lost authority.
                        _ = Export<MarkFaultNative>("RizSingleReturnPrototype_PollFreshSource")(_slot);
                        if (ReadHealth().Known && !ReadCurrentCore().Active) return 1;
                    }
                    catch { }
                }
            }
            ReportFault("The owned DSP clock source failed; stock fallback is not synchronized rate recovery.", error); return 1;
        }
    }
    private static bool HasNativeReaderFailure(Exception error)
    {
        // A native getter may reach this callback as a managed SEHException,
        // before the outer C++ reader can mark Unknown. Logical withdrawal
        // must not erase that independent native failure, even when wrapped.
        if (error is SEHException or AccessViolationException) return true;
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Any(HasNativeReaderFailure)) return true;
        return error.InnerException is { } inner && HasNativeReaderFailure(inner);
    }
    private void ReportFault(string reason, Exception? error = null)
    {
        var fault = new NativePlaybackSingleReturnFault(reason, error);
        Interlocked.CompareExchange(ref _fault, fault, null);
        // Native Unknown rejects every candidate before this callback returns.
        // Mark it without an owner-held managed lock; a source getter may be
        // waiting for this worker's original-only callback to finish.
        if (Volatile.Read(ref _nativeFaultMarked) == 0)
            try
            {
                // Publish this marker only after native completed, so a
                // concurrent fault cannot return while the first mark is pending.
                if (_markFault(_slot) == 0) Volatile.Write(ref _nativeFaultMarked, 1);
            }
            catch { /* Local fault remains sticky if the native query itself fails. */ }
        CleanupFaultPublication();
        if (Interlocked.Exchange(ref _notified, 1) == 0)
        {
            void Notify() { try { _faultOwner(_fault!); } catch { /* Owner failure cannot cross the native observer boundary. */ } }
            if (_faultCleanupThread != Environment.CurrentManagedThreadId || Monitor.IsEntered(_gate))
                ThreadPool.QueueUserWorkItem(_ => Notify());
            else Notify();
        }
    }

    private void CleanupFaultPublication()
    {
        // Fresh cleanup is owner-thread-only. Workers keep the exact ticket
        // and all physical roots; the owner's next health poll retires only
        // its retained publication after native Unknown has closed use.
        if (_faultCleanupThread != Environment.CurrentManagedThreadId || Volatile.Read(ref _fault) is null) return;
        lock (_gate)
        {
            if (!_faultCleanupAttempted)
            {
                _faultCleanupAttempted = true;
                // MarkFault preserves native in-flight responsibility but does not
                // clear authorityActive. Retire only this exact privately retained
                // publication, under the same lock as Publish. A changed/foreign
                // generation cannot be adopted or cleared by this cleanup ticket.
                if (_published is { Revision: > 0 } published)
                    try
                    {
                        if (_freshClock is { HasTransferred: false }) _freshClock.Withdraw(published.Revision);
                        else _withdraw(_slot, checked((ulong)published.Revision));
                    }
                    catch { /* Keep the exact publication and sticky fault if native cleanup is uncertain. */ }
            }
        }
    }
}
