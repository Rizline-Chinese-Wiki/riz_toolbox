namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal void VerifyCurrent(IntPtr module, IntPtr address, IntPtr methodInfo)
        {
            lock (_gate)
            {
                if (module != _module || address != TargetAddress || methodInfo != _target.MethodInfo)
                    throw new UnsupportedSignatureException("The GetTime receipt belongs to a different module, target or metadata method.");
                if (!IsActive || Volatile.Read(ref _committed) != 1)
                    throw new UnsupportedSignatureException("The exact broker GetTime installation is withdrawn, faulted or incomplete.");
                try { if (_group is not null) _group.VerifyCurrent(); else VerifyPhysical(); if (!_bridge!.ReadHealth().Known) throw new UnsupportedSignatureException("The native Single transport is not known."); }
                catch (Exception error) { Fault("GetTime installation lost its current authority: " + error.Message); throw; }
            }
        }
        private void VerifyPhysical()
        {
            if (!OwnsPlaybackInstallation(this)) throw new UnsupportedSignatureException("GetTime owner is absent from the actual broker address registry.");
            CurrentImage.Verify(); VerifyMemberEvidence();
        }
        internal void VerifyMemberEvidence(PlaybackSourceInstallGroup.VerificationTraversal? traversal = null)
        {
            if (!OwnsPlaybackInstallation(this)) throw new UnsupportedSignatureException("GetTime owner is absent from the actual broker address registry.");
            if (traversal is null) _dobby.Verify();
            else traversal.VerifySharedDobbySeal(_group
                ?? throw new UnsupportedSignatureException("An ungrouped GetTime cannot borrow the group Dobby seal."), _engine, _dobby);
            VerifyLocalPhysicalEvidence();
            if (_bridge is not null && !_bridge.ReadHealth().Known)
                throw new UnsupportedSignatureException("The native Single transport is not known.");
        }
        private void VerifyLocalPhysicalEvidence()
        {
            _sourceFunctions.Verify(); _method.Verify(); _methodName.Verify(); _returnType.Verify();
            _source!.Verify();
            if (_bridge is null) return;
            _helper!.Verify();
            _relocation!.Verify(); _originalThunk!.Verify(); _relay?.Verify(); _unwind!.Verify();
        }
        internal (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadOperationPolicy(PlaybackSourceInstallGroup group)
            => ReadOperationPolicy(group, out _);

        internal ulong ReadRateOperationClockInFlight(PlaybackSourceInstallGroup group)
        {
            _ = ReadOperationPolicy(group, out var health);
            return health.InFlight;
        }

        private (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadOperationPolicy(
            PlaybackSourceInstallGroup group, out NativePlaybackSingleReturnHealth health)
        {
            if (!ReferenceEquals(_group, group) || !OwnsPlaybackInstallation(this) || !IsActive
                || Volatile.Read(ref _committed) != 1 || _bridge is not { } bridge)
                throw new UnsupportedSignatureException("The running Clock lost its exact committed GetTime owner.");
            VerifyOperationPhysicalEvidence();
            var policy = bridge.ReadCurrent();
            health = bridge.ReadHealth();
            if (!health.Known || health.InFlight > long.MaxValue || policy.Active != health.AuthorityActive
                || health.Revision != (policy.Authority?.Revision ?? 0))
                throw new UnsupportedSignatureException("GetTime policy and current transport health disagree.");
            return policy;
        }
        private void VerifyOperationPhysicalEvidence()
        {
            _sourceFunctions.Verify(); _method.Verify(); _methodName.Verify(); _returnType.Verify();
            _source!.Verify();
            _helper!.VerifyOperationModule();
            HookTargetValidation.VerifyExecutable(_bridge!.EntryAddress, _helper.Module);
            _relocation!.Verify(); _originalThunk!.Verify(); _relay?.Verify(); _unwind!.Verify();
        }
        internal void PublishOperationPolicy(PlaybackSourceInstallGroup group, long expectedRevision,
            NativePlaybackSingleReturnAuthority next)
        {
            _ = ReadOperationPolicy(group);
            if (next.Identity.VerifiedGetTimeMethod != _target.MethodInfo)
                throw new UnsupportedSignatureException("The running Clock publication identifies another GetTime method.");
            _bridge!.Publish(expectedRevision, next);
            var actual = ReadOperationPolicy(group);
            if (!actual.Active || !ReferenceEquals(actual.Authority, next))
                throw new UnsupportedSignatureException("The running Clock publication failed its exact native readback.");
        }
        private void VerifyRunningChannel()
        {
            if (_group is { } group) _ = group.ReadOperationClockPolicy(group.GetTimeLease);
            else VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
        }
        private void VerifyCallbackChannel(NativePlaybackSingleReturnFrame frame)
        {
            if (!frame.IsValid || frame.ThreadId != NativeTweenSourceReferences.CurrentThreadId())
                throw new UnsupportedSignatureException("The GetTime callback differs from its actual native thread and physical frame.");
            if (_group is not { } group || group.IsSourceOperationOwnerThread)
            {
                if (IsActive) VerifyRunningChannel();
                else VerifyRetainedTransport();
                return;
            }
            // A worker may call the retained original with another receiver or
            // metadata argument. Authenticate that physical channel and its
            // actual in-flight/native faults without owner-thread admission.
            VerifyRetainedTransport();
        }
        private void VerifyRetainedTransport()
        {
            if (!OwnsPlaybackInstallation(this) || Volatile.Read(ref _committed) != 1 || _bridge is null)
                throw new UnsupportedSignatureException("The GetTime physical callback lost its actual committed installation.");
            if (_group is { } group) group.VerifyGetTimeTransportOwnership(this);
            else CurrentImage.VerifyOperationModule();
            VerifyOperationPhysicalEvidence();
            var health = _bridge.ReadHealth();
            if (!health.Known || health.InFlight is 0 or > long.MaxValue)
                throw new UnsupportedSignatureException("The captured GetTime callback lost its actual in-flight native return responsibility.");
            VerifyOperationPhysicalEvidence();
        }
        internal bool MatchesOriginalBody(string hash, int length)
        {
            VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
            return length == OriginalBodyLength && OriginalBodySha256.Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
        internal NativePlaybackSingleReturnHealth ReadHealth()
        {
            VerifyCurrent(_module, TargetAddress, _target.MethodInfo); return _bridge!.ReadHealth();
        }
        internal ManagedBoundaryActivity ReadActivity()
        {
            // Group withdrawal ends authority, not physical responsibility.
            // The group's retained verifier does not require an active lease
            // and reads both actual native members until they really drain.
            if (_group is not null) return _group.ReadActivity();
            lock (_gate)
            {
                var health = _bridge?.ReadHealth();
                try { VerifyPhysical(); }
                catch (Exception error) { Fault("GetTime activity authority failed: " + error.Message); }
                return new(health is not null && health.InFlight <= long.MaxValue ? (long)health.InFlight : long.MaxValue,
                    health is { Known: true } && health.InFlight <= long.MaxValue && _fault is null && _committed == 1);
            }
        }
        internal void Publish(long expectedRevision, NativePlaybackSingleReturnAuthority next)
        {
            lock (_gate)
            {
                VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
                if (next.Identity.VerifiedGetTimeMethod != _target.MethodInfo)
                    throw new UnsupportedSignatureException("Clock publication metadata differs from the broker's exact GetTime.");
                _bridge!.Publish(expectedRevision, next); VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
            }
        }
        internal (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadCurrent()
        { VerifyCurrent(_module, TargetAddress, _target.MethodInfo); return _bridge!.ReadCurrent(); }
        internal bool OwnsPublication(NativePlaybackSingleReturnAuthority authority) => _bridge?.OwnsPublication(authority) == true;
        internal bool WithdrawPolicy(long expectedRevision) => _bridge?.Withdraw(expectedRevision) == true;
        internal void Withdraw()
        {
            if (_group is not null) { _group.Withdraw(); return; }
            lock (_gate)
            {
                if (Interlocked.Exchange(ref _active, 0) == 0) return;
                var policy = _bridge!.ReadCurrent();
                if (policy.Authority is { Revision: > 0 } authority) _bridge.Withdraw(authority.Revision);
            }
        }
        private NativePlaybackSingleReturnSample ReadClock()
        {
            VerifyRunningChannel();
            var sample = _readClock(); VerifyRunningChannel(); return sample;
        }
        private void Before(NativePlaybackSingleReturnFrame frame)
        {
            VerifyCallbackChannel(frame);
            if (!IsActive) return;
            // With no external observer there is no intervening operation to
            // bracket. The game original still has its separate After check.
            if (_before is null) return;
            _before(frame);
            VerifyCallbackChannel(frame);
        }
        private void After(NativePlaybackSingleReturnFrame frame, NativePlaybackSingleReturnCompletion completion)
        {
            VerifyCallbackChannel(frame);
            if (!IsActive) return;
            if (_after is null) return;
            _after(frame, completion);
            VerifyCallbackChannel(frame);
        }
        private void Fault(string reason)
        {
            if (_group is not null) { _group.Fault(reason); return; }
            RevokeFromGroup(reason);
        }
        internal void RevokeFromGroup(string reason)
        {
            Interlocked.CompareExchange(ref _fault, reason, null); Volatile.Write(ref _active, 0);
            _bridge?.MarkBrokerFault(_fault!); NotifyFaultCore(new(_fault!));
        }
        private void NotifyFault(NativePlaybackSingleReturnFault fault)
        {
            if (_group is not null) { _group.Fault(fault.Reason); return; }
            NotifyFaultCore(fault);
        }
        private void NotifyFaultCore(NativePlaybackSingleReturnFault fault)
        {
            Interlocked.CompareExchange(ref _fault, fault.Reason, null); Volatile.Write(ref _active, 0);
            if (Interlocked.Exchange(ref _notified, 1) != 0) return;
            // External clock-state locks are never acquired under this owner.
            ThreadPool.QueueUserWorkItem(_ => { try { _faultOwner(fault); } catch { } });
        }
        internal NativePlaybackSingleReturnHealth ReadTransportHealth() => _bridge!.ReadHealth();
        internal void WithdrawFromGroup()
        {
            Volatile.Write(ref _active, 0);
            var policy = _bridge?.ReadCurrent();
            if (policy?.Authority is { Revision: > 0 } authority) _bridge!.Withdraw(authority.Revision);
        }
    }
}
