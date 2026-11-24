namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal NativePlaybackSourceHealth ReadSourceOperationHealth()
        {
            VerifyOperationThread();
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyOperationGroupOwnership();
                var health = _hub!.ReadOperationHealth();
                VerifyOperationGroupOwnership();
                return health;
            }
        }

        internal void VerifyIssuedOperationBefore(NativePlaybackSourceObservation observation)
        {
            ArgumentNullException.ThrowIfNull(observation);
            var member = SourceOwner(observation.Role);
            VerifySourceOperationChannel(member);
            member.VerifyIssuedOperationBefore(observation);
            VerifySourceOperationChannel(member);
        }

        internal void VerifyIssuedOperationReturn(NativePlaybackSourceCompletion completion)
        {
            ArgumentNullException.ThrowIfNull(completion);
            var member = SourceOwner(completion.Observation.Role);
            VerifySourceOperationChannel(member);
            member.VerifyIssuedOperationReturn(completion);
            VerifySourceOperationChannel(member);
        }

        internal void VerifySourceOperationOwner()
        {
            VerifyOperationThread();
            lock (_verifyGate)
            lock (Gate) VerifyOperationGroupOwnership();
        }
        internal bool IsSourceOperationOwnerThread => _operationThread == Environment.CurrentManagedThreadId;

        internal void VerifyGetTimeTransportOwnership(PlaybackOwner member)
        {
            // A worker's original-only GetTime callback owns physical return
            // duties, but no source graph or publication admission. Registry
            // ownership is append-only and can be read without owner locks.
            if (!ReferenceEquals(_getTime, member) || !OwnsPlaybackGroup(this)
                || !OwnsPlaybackInstallation(member))
                throw new UnsupportedSignatureException("The GetTime transport lost its exact retained group/member ownership.");
            VerifySealIssuer(Module);
            _image!.VerifyOperationModule();
            _foundation?.VerifyRuntimeOwnership();
            var health = _hub!.ReadOperationTransportHealth();
            if (!health.Known || health.Unknown || health.InFlight > long.MaxValue)
                throw new UnsupportedSignatureException("The GetTime transport's retained native source owner is unknown.");
        }
        internal void VerifySourceOperationRole(NativePlaybackSourceRole role)
            => VerifySourceOperationChannel(SourceOwner(role));

        internal void RequireSourceOperationAdmission()
        {
            VerifyOperationThread();
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyOperationGroupOwnership();
                // This real native gate can decline a pending transition;
                // its Busy result is not a physical installation fault.
                _hub!.RequireOperationFreshAdmission();
                VerifyOperationGroupOwnership();
            }
        }

        internal void VerifyOperationReceivers(NativeRuntime api, IntPtr player, IntPtr source,
            IntPtr progress, IntPtr getTimeMethod)
        {
            VerifyOperationThread();
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyOperationGroupOwnership();
                // ObjectClass and the fixed metadata projection can reject
                // an expired graph without faulting a healthy physical group.
                VerifyReceiverProjection(api, player, source, progress, getTimeMethod);
                VerifyOperationGroupOwnership();
            }
        }

        internal IntPtr StockOperationLeafCaller(NativePlaybackSourceRole parent, NativePlaybackSourceRole leaf)
        {
            VerifySourceOperationOwner();
            VerifySourceOperationChannel(SourceOwner(parent));
            VerifySourceOperationChannel(SourceOwner(leaf));
            var caller = StockLeafCallerCore(parent, leaf);
            VerifySourceOperationOwner();
            return caller;
        }

        internal NativePlaybackStockSourceHub.WriterReservation ReserveOperationWriter(NativePlaybackSourceObservation root,
            IntPtr source, uint bits, ulong writer, ulong sourceFence, ulong scheduling,
            NativePlaybackStockSourceHub.WriterReservation? predecessor, NativePlaybackSourceRole role)
        {
            if (role is not (NativePlaybackSourceRole.SetPitch or NativePlaybackSourceRole.SetTime)
                || root.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay
                    or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume)
                || role == NativePlaybackSourceRole.SetTime && (!HasLowerTime
                    || root.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay)))
                throw new UnsupportedSignatureException("The running writer requires its actual supported stock root and exact writer role.");
            VerifySourceOperationChannel(SourceOwner(root.Role));
            VerifySourceOperationChannel(SourceOwner(role));
            _hub!.RequireOperationFreshAdmission();
            var target = _targets.SourceTarget(role);
            return _hub.ReserveOperationWriter(root, source, target.Address, target.MethodInfo, bits,
                writer, sourceFence, scheduling, predecessor, role);
        }

        internal string? PollOperationNativeFaults()
        {
            VerifyOperationThread();
            // Polling responsibility remains usable after withdrawal. It
            // conveys no authority and never replaces the actual finally row.
            // Return the synchronously observed physical fault: external
            // notifications are deliberately queued and may arrive later.
            var source = _hub!.ReadOperationHealth();
            var getTime = _getTime!.ReadTransportHealth();
            // Armed/AuthorityActive=false can be normal logical withdrawal or
            // a pending source transition; neither is a physical fault.
            if (Volatile.Read(ref _fault) is { } fault) return fault;
            if (!source.Known || source.Unknown || source.NativeFaults != 0)
                return source.FaultReason ?? "The retained native source transport is unknown.";
            if (!getTime.Known || getTime.NativeEnterFaults != 0 || getTime.NativeExitFaults != 0 || getTime.NativeSourceFaults != 0)
                return getTime.FaultReason ?? "The retained native GetTime transport is unknown.";
            return null;
        }
        internal void PumpOperationCompleted()
        {
            VerifyOperationThread();
            foreach (var owner in _sources.Values) owner.PumpOperationCompleted();
        }
    }

    internal sealed partial class PlaybackSourceOwner
    {
        internal void PumpOperationCompleted() => _bridge?.PumpOperationCompleted();
        internal void VerifyIssuedOperationBefore(NativePlaybackSourceObservation observation)
        {
            if (_bridge is null || !observation.BelongsTo(_bridge))
                throw new UnsupportedSignatureException("The running observation belongs to another physical source channel.");
            _bridge.VerifyIssuedOperationBefore(observation);
        }
        internal void VerifyIssuedOperationReturn(NativePlaybackSourceCompletion completion)
        {
            if (_bridge is null || !completion.Observation.BelongsTo(_bridge))
                throw new UnsupportedSignatureException("The running return belongs to another physical source channel.");
            _bridge.VerifyIssuedOperationReturn(completion);
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal NativePlaybackSourceHealth ReadSourceOperationHealth()
    { VerifyOperationLease(); return _owner.ReadSourceOperationHealth(); }
    internal void VerifySourceOperationOwner()
    { VerifyOperationLease(); _owner.VerifySourceOperationOwner(); }
    internal void VerifySourceOperationRole(NativePlaybackSourceRole role)
    { VerifyOperationLease(); _owner.VerifySourceOperationRole(role); }
    internal void RequireSourceOperationAdmission()
    { VerifyOperationLease(); _owner.RequireSourceOperationAdmission(); }
    internal void VerifyOperationReceivers(NativeRuntime api, IntPtr player, IntPtr source,
        IntPtr progress, IntPtr getTimeMethod)
    { VerifyOperationLease(); _owner.VerifyOperationReceivers(api, player, source, progress, getTimeMethod); }
    internal void VerifyIssuedOperationBefore(NativePlaybackSourceObservation observation)
    { VerifyOperationLease(); _owner.VerifyIssuedOperationBefore(observation); }
    internal void VerifyIssuedOperationReturn(NativePlaybackSourceCompletion completion)
    { VerifyOperationLease(); _owner.VerifyIssuedOperationReturn(completion); }
    internal IntPtr StockOperationLeafCaller(NativePlaybackSourceRole parent, NativePlaybackSourceRole leaf)
    { VerifyOperationLease(); return _owner.StockOperationLeafCaller(parent, leaf); }
    internal NativePlaybackStockSourceHub.WriterReservation ReserveOperationWriter(NativePlaybackSourceObservation root,
        IntPtr source, uint bits, ulong writer, ulong sourceFence, ulong scheduling,
        NativePlaybackStockSourceHub.WriterReservation? predecessor, NativePlaybackSourceRole role)
    { VerifyOperationLease(); return _owner.ReserveOperationWriter(root, source, bits, writer, sourceFence, scheduling, predecessor, role); }
    internal string? PollOperationNativeFaults()
    { VerifyOperationLease(); return _owner.PollOperationNativeFaults(); }
    internal void PumpOperationCompleted()
    { VerifyOperationLease(); _owner.PumpOperationCompleted(); }
}
