namespace RizToolbox.Runtime.Hooks;

/// <summary>One concrete typed channel in a native source hub. Only actual
/// native current scopes issue observations, and only native physical-exit
/// journal rows issue completion receipts. A copied frame never grants either.</summary>
internal sealed partial class NativePlaybackStockSourceBridge
{
    private readonly object _gate = new();
    private readonly object _issuer = new();
    private readonly NativePlaybackStockSourceHub _hub;
    private NativePlaybackExecutableSeal.RangeSeal? _originalHead;
    private int _configurationAttempted, _configured;
    private readonly Action<NativePlaybackSourceObservation>? _before;
    private readonly Action<NativePlaybackSourceCompletion>? _after;
    private readonly Dictionary<ulong, NativePlaybackSourceObservation> _observations = new();
    private readonly Dictionary<ulong, NativePlaybackSourceCompletion> _returns = new();
    internal NativePlaybackSourceKind Kind { get; }
    internal NativePlaybackSourceRole Role { get; }
    internal uint Slot { get; }
    internal IntPtr TargetAddress { get; }
    internal IntPtr EntryAddress { get; }
    internal IntPtr OriginalAddress { get; private set; }
    internal bool IsConfigured => Volatile.Read(ref _configured) == 1;
    internal ulong Epoch => _hub.Epoch;
    internal NativePlaybackStockSourceHub Hub => _hub;
    internal bool ProductionAdmission => false;

    internal NativePlaybackStockSourceBridge(string path, string sha256, NativePlaybackSourceKind kind,
        NativePlaybackSourceRole role, IntPtr target, Func<IntPtr, IntPtr> prepareOriginal,
        Action<NativePlaybackSourceObservation>? before, Action<NativePlaybackSourceCompletion>? after,
        Action<NativePlaybackSourceFault> faultOwner)
        : this(new NativePlaybackStockSourceHub(path, sha256, faultOwner), kind, role, target, prepareOriginal, before, after) { }

    internal NativePlaybackStockSourceBridge(NativePlaybackStockSourceHub hub, NativePlaybackSourceKind kind,
        NativePlaybackSourceRole role, IntPtr target, Func<IntPtr, IntPtr> prepareOriginal,
        Action<NativePlaybackSourceObservation>? before = null, Action<NativePlaybackSourceCompletion>? after = null)
        : this(hub, kind, role, target, before, after)
    {
        ArgumentNullException.ThrowIfNull(prepareOriginal);
        try { ConfigureOriginal(prepareOriginal(EntryAddress)); }
        catch (Exception error) { hub.MarkBrokerFault("The source channel preparation or native Configure failed.", error); throw; }
    }

    private NativePlaybackStockSourceBridge(NativePlaybackStockSourceHub hub, NativePlaybackSourceKind kind,
        NativePlaybackSourceRole role, IntPtr target,
        Action<NativePlaybackSourceObservation>? before, Action<NativePlaybackSourceCompletion>? after)
    {
        ArgumentNullException.ThrowIfNull(hub);
        if (target == IntPtr.Zero || !RoleMatches(kind, role))
            throw new UnsupportedSignatureException("The source role has no exact supported typed ABI or target.");
        (_hub, Kind, Role, TargetAddress, _before, _after) = (hub, kind, role, target, before, after);
        Slot = hub.ReserveChannel(this);
        try
        {
            EntryAddress = hub.GetEntry(kind, Slot);
        }
        catch (Exception error) { hub.MarkBrokerFault("The native source entry reservation failed and remains retained.", error); throw; }
    }
    internal static NativePlaybackStockSourceBridge Reserve(NativePlaybackStockSourceHub hub, NativePlaybackSourceKind kind,
        NativePlaybackSourceRole role, IntPtr target,
        Action<NativePlaybackSourceObservation>? before, Action<NativePlaybackSourceCompletion>? after)
        => new(hub, kind, role, target, before, after);

    internal void ConfigureOriginal(IntPtr original)
    {
        if (Interlocked.Exchange(ref _configurationAttempted, 1) != 0)
            throw new UnsupportedSignatureException("A native channel's retained original can be configured only once.");
        try
        {
            OriginalAddress = original;
            if (OriginalAddress == IntPtr.Zero || OriginalAddress == EntryAddress || OriginalAddress == TargetAddress)
                throw new UnsupportedSignatureException("The source bridge needs the actual distinct retained original trampoline.");
            // Dobby and the independently proved unwind thunk live in private
            // executable allocations. The concrete broker verifies their full
            // semantics; this bridge also retains the actual executable head.
            _originalHead = NativePlaybackExecutableSeal.RangeSeal.Capture(OriginalAddress, 1, IntPtr.Zero, false, true);
            _hub.Configure(this);
            Volatile.Write(ref _configured, 1);
        }
        catch (Exception error) { _hub.MarkBrokerFault("The actual source original configuration failed and remains retained.", error); throw; }
    }
    internal void Arm() => _hub.Arm();
    internal void Withdraw() => _hub.Withdraw();
    internal void MarkBrokerFault(string reason) => _hub.MarkBrokerFault(reason);
    internal NativePlaybackSourceHealth ReadHealth() => _hub.ReadHealth();
    internal void VerifyCurrent() => _hub.VerifyChannel(this);
    internal void VerifyAuthority() => _hub.VerifyAuthority(this);
    internal void VerifyKnown() => _hub.VerifyKnown(this);
    internal void VerifyOriginalHead() => (_originalHead
        ?? throw new UnsupportedSignatureException("This retained channel has not configured its actual original.")).Verify();
    internal void VerifyReservation() => _hub.VerifyReservation(this);
    internal void VerifyMemberReservation() => _hub.VerifyMemberReservation(this);
    internal void VerifyMemberEvidence() => _hub.VerifyMemberEvidence(this);
    internal void VerifyIssuer(object issuer)
    {
        if (!ReferenceEquals(_issuer, issuer)) throw new UnsupportedSignatureException("Only the retained source channel can issue a private observation or completion.");
    }
    internal NativePlaybackSourceFrame RetainedWriterOrigin(NativePlaybackSourceObservation observation)
    {
        var frame = observation.ActualOrigin(this);
        lock (_gate)
            if (!_observations.TryGetValue(frame.Sequence, out var retained) || !ReferenceEquals(retained, observation))
                throw new UnsupportedSignatureException("A writer requires the channel's exact retained root or attempt.");
        return frame;
    }
    internal void VerifyIssuedBefore(NativePlaybackSourceObservation observation)
    {
        var origin = observation.ActualOrigin(this);
        lock (_gate)
            if (!_observations.TryGetValue(origin.Sequence, out var actual) || !ReferenceEquals(actual, observation))
                throw new UnsupportedSignatureException("The group requires the channel's exact issued observation object.");
        ReadCurrentActual(origin);
        observation.VerifyEngineAssociationCurrent();
    }
    internal void VerifyIssuedReturn(NativePlaybackSourceCompletion completion)
    {
        var origin = completion.Observation.ActualOrigin(this);
        lock (_gate)
            if (!_observations.TryGetValue(origin.Sequence, out var actual) || !ReferenceEquals(actual, completion.Observation)
                || !_returns.TryGetValue(origin.Sequence, out var returned) || !ReferenceEquals(returned, completion))
                throw new UnsupportedSignatureException("The group requires the channel's exact issued return object.");
        if (completion.PhysicalExited) throw new UnsupportedSignatureException("A journal projection cannot issue a stock return.");
        completion.VerifyAuthority();
    }

    internal NativePlaybackSourceFrame ReadCurrentActual(NativePlaybackSourceFrame expected, bool requireBefore = true,
        bool allowWithdrawnCompletion = false)
        => _hub.ReadCurrentActual(this, expected, requireBefore, allowWithdrawnCompletion);
    internal NativePlaybackSourceFrame ReadReceiptActual(NativePlaybackSourceFrame origin)
    {
        if (!_hub.TryReadReceipt(origin, out var actual))
            throw new UnsupportedSignatureException("The actual source call has no retained physical-exit receipt yet.");
        return actual;
    }
    internal bool TryReadCompletionActual(NativePlaybackSourceObservation observation, NativePlaybackSourceFrame origin,
        out NativePlaybackSourceCompletion? completion)
    {
        if (!observation.BelongsTo(this)) throw new UnsupportedSignatureException("The private source observation has another channel owner.");
        lock (_gate)
            if (!_observations.TryGetValue(origin.Sequence, out var issued) || !ReferenceEquals(issued, observation))
                throw new UnsupportedSignatureException("A copied source observation is not the channel's actual issued Before.");
        if (!_hub.TryReadReceipt(origin, out _)) { completion = null; return false; }
        completion = NativePlaybackSourceCompletion.CapturePhysicalActual(this, observation, origin, _issuer); return true;
    }
    internal void ReleaseWhenExited(NativePlaybackSourceObservation observation)
    {
        lock (_gate)
        {
            if (observation.IsReleased) return;
            var origin = observation.ActualOrigin(this);
            if (!_observations.TryGetValue(origin.Sequence, out var actual) || !ReferenceEquals(actual, observation))
                throw new UnsupportedSignatureException("No exact retained Before owns this source journal row.");
            if (!_hub.TryReadReceipt(origin, out _)) return;
            if (!_hub.Release(origin)) throw new UnsupportedSignatureException("The exact physical-exit source row could not be released.");
            observation.MarkReleased(this); _observations.Remove(origin.Sequence);
            _returns.Remove(origin.Sequence);
        }
    }
    /// <summary>Must run outside any native callback if a callback disposed its
    /// observation. Until actual exit, the journal and managed owner retain it.</summary>
    internal void PumpCompleted()
    {
        NativePlaybackSourceObservation[] pending;
        lock (_gate) pending = _observations.Values.Where(value => value.ReleaseRequested).ToArray();
        foreach (var observation in pending) ReleaseWhenExited(observation);
    }
    internal void ObserveActual(NativePlaybackSourceFrame frame)
    {
        if (frame.Kind != Kind || frame.Role != Role || frame.Slot != Slot)
            throw new UnsupportedSignatureException("The native callback differs from this retained typed source channel.");
        if (Kind == NativePlaybackSourceKind.IcallSingle1 && frame.IncomingMethod != IntPtr.Zero)
            throw new UnsupportedSignatureException("The two-argument lower icall has no incoming MethodInfo parameter.");
        // A closed initialization gate has no current-audio authority, but its
        // exact resource/Start calls and private engine attempts still own
        // physical completion duties, independently of logical source authority.
        // CaptureActual below still verifies the live native callback/token;
        // ordinary Dormant and physically withdrawn owners remain ignored.
        if ((frame.Flags & NativePlaybackSourceFlags.Armed) == 0)
        {
            bool initializing;
            if (frame.Stage == NativePlaybackSourceStage.Before)
                initializing = _hub.CanObserveResourceInitialization(this, frame);
            else
            {
                // Acceptance was fixed by the actual Before. Current hold
                // state cannot reconstruct whether this return owns a journal.
                lock (_gate) initializing = _observations.ContainsKey(frame.Sequence)
                    && _hub.IsResourceInitializationResponsibility(frame);
            }
            if (!initializing)
            {
                if ((frame.Flags & NativePlaybackSourceFlags.Unknown) != 0) MarkBrokerFault("A dormant source entry reported native Unknown.");
                return;
            }
        }
        if (frame.Stage == NativePlaybackSourceStage.Before)
        {
            var observation = NativePlaybackSourceObservation.CaptureActual(this, frame, _issuer);
            lock (_gate)
            {
                if (_observations.ContainsKey(frame.Sequence)) throw new UnsupportedSignatureException("The native source issued a duplicate Before sequence.");
                _observations.Add(frame.Sequence, observation);
            }
            _before?.Invoke(observation); return;
        }
        NativePlaybackSourceObservation original;
        lock (_gate)
        {
            if (!_observations.TryGetValue(frame.Sequence, out original!))
                throw new UnsupportedSignatureException("A source return has no exact actual Before observation.");
        }
        var completion = NativePlaybackSourceCompletion.CaptureReturnedActual(this, original, frame, _issuer);
        lock (_gate) _returns.Add(frame.Sequence, completion);
        _after?.Invoke(completion);
    }
    internal static bool SameCall(NativePlaybackSourceFrame expected, NativePlaybackSourceFrame actual)
        => expected.Version == actual.Version && expected.Size == actual.Size && expected.Kind == actual.Kind
            && expected.Role == actual.Role && expected.Slot == actual.Slot && expected.Owner == actual.Owner
            && expected.OwnerEpoch == actual.OwnerEpoch && expected.Sequence == actual.Sequence && expected.Token == actual.Token
            && expected.ParentSequence == actual.ParentSequence && expected.Self == actual.Self
            && expected.IncomingMethod == actual.IncomingMethod && expected.Caller == actual.Caller && expected.ArgBits == actual.ArgBits
            && expected.ThreadId == actual.ThreadId && expected.Depth == actual.Depth
            && (expected.Flags & (NativePlaybackSourceFlags.LowerPitchChild | NativePlaybackSourceFlags.EnginePitchChild | NativePlaybackSourceFlags.LowerTimeChild))
                == (actual.Flags & (NativePlaybackSourceFlags.LowerPitchChild | NativePlaybackSourceFlags.EnginePitchChild | NativePlaybackSourceFlags.LowerTimeChild));
    private static bool RoleMatches(NativePlaybackSourceKind kind, NativePlaybackSourceRole role) => kind switch
    {
        NativePlaybackSourceKind.Void0 => role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.PauseLeaf or NativePlaybackSourceRole.AudioPlay or NativePlaybackSourceRole.AudioStop,
        NativePlaybackSourceKind.Single1 => role is NativePlaybackSourceRole.SetPitch or NativePlaybackSourceRole.SetTime or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume,
        NativePlaybackSourceKind.Double1 => role is NativePlaybackSourceRole.PlayScheduled or NativePlaybackSourceRole.AudioPlayDelayed,
        NativePlaybackSourceKind.Reference1 => role is NativePlaybackSourceRole.SetClip or NativePlaybackSourceRole.ProgressMusicPlayer or NativePlaybackSourceRole.PlayerConstructor or NativePlaybackSourceRole.ProgressClip,
        NativePlaybackSourceKind.Boolean1 => role is NativePlaybackSourceRole.ProgressPlay or NativePlaybackSourceRole.AudioStopBoolean or NativePlaybackSourceRole.AudioSetLoop,
        NativePlaybackSourceKind.BooleanReturn0 => role == NativePlaybackSourceRole.ProgressStartMoveNext,
        NativePlaybackSourceKind.UInt64_1 => role == NativePlaybackSourceRole.AudioPlayHelper,
        NativePlaybackSourceKind.IcallSingle1 => role is NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch or NativePlaybackSourceRole.LowerSetTime,
        _ => false
    };
}
