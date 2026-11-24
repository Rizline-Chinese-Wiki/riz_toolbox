using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    // Running source consumers inspect the retained native owner and the
    // particular channel they use. Complete helper-image evidence remains in
    // installation/diagnostic/retirement APIs; no successful audit is cached.
    internal NativePlaybackSourceHealth ReadOperationHealth()
    {
        VerifyThread();
        if (_ownerOsThreadId != GetCurrentThreadId())
            throw new UnsupportedSignatureException("The running source operation belongs to its actual native owner thread.");
        return ReadOperationTransportHealth();
    }

    // Original-only GetTime callbacks may run on a native worker. This reads
    // the retained source transport without admitting that thread to a source
    // getter, writer, fresh gate or owner-thread publication operation.
    internal NativePlaybackSourceHealth ReadOperationTransportHealth()
    {
        try
        {
            _image.VerifyOperationModule();
            // Native serializes this owner/counter snapshot. Its retained
            // identity is immutable, so an original-only worker must not wait
            // for an owner-held managed lock around a source getter.
            var info = ReadOperationOwner();
            _image.VerifyOperationModule();
            var unknown = (info.Flags & NativePlaybackSourceFlags.Unknown) != 0 || info.Faults != 0;
            if (unknown) MarkBrokerFault("The running native source owner reported sticky Unknown or a source callback fault.");
            var fault = Volatile.Read(ref _fault);
            return new(fault is null && !unknown,
                (info.Flags & NativePlaybackSourceFlags.Armed) != 0
                    && (info.Flags & (NativePlaybackSourceFlags.Dormant | NativePlaybackSourceFlags.Withdrawn)) == 0
                    && fault is null && !Volatile.Read(ref _withdrawn),
                info.Entered, info.Exited, info.InFlight, info.WriterSequence, info.SourceSequence,
                info.SchedulingSequence, info.Faults, unknown || fault is not null, fault?.Reason);
        }
        catch (Exception error)
        {
            MarkBrokerFault("The running source owner identity or native counters changed.", error);
            return new(false, false, 0, 0, ulong.MaxValue, 0, 0, 0, ulong.MaxValue, true, _fault?.Reason);
        }
    }

    private NativePlaybackSourceInformation ReadOperationOwnerLocked()
    {
        if (!System.Threading.Monitor.IsEntered(_gate))
            throw new UnsupportedSignatureException("A running native owner query requires its retained hub lock.");
        return ReadOperationOwner();
    }

    private NativePlaybackSourceInformation ReadOperationOwner()
    {
        var info = NewInformation();
        if (_readOwner(_owner, ref info) != 1)
            throw new UnsupportedSignatureException("The actual running native source owner cannot be read.");
        ValidateOwner(info);
        if (info.Kind != 0 || info.Role != 0 || info.Slot != uint.MaxValue
            || info.Target != IntPtr.Zero || info.Original != IntPtr.Zero || info.Entry != IntPtr.Zero
            || info.InFlight > long.MaxValue)
            throw new UnsupportedSignatureException("The running source owner has invalid owner-only identity or counters.");
        return info;
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    internal void VerifyOperationMemberEvidence(NativePlaybackStockSourceBridge channel, bool requireArmed = true)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(channel);
        try
        {
            bool unknown;
            lock (_gate)
            {
                if (Volatile.Read(ref _fault) is not null || !ReferenceEquals(channel.Hub, this)
                    || !_channels.TryGetValue(channel.Slot, out var actual) || !ReferenceEquals(actual, channel)
                    || !channel.IsConfigured || channel.Epoch != Epoch)
                    throw new UnsupportedSignatureException("The running source channel lost its exact retained reservation and original.");
                var entry = _entry((uint)channel.Kind, channel.Slot);
                HookTargetValidation.VerifyExecutable(entry, _library);
                channel.VerifyOriginalHead();
                var info = NewInformation();
                if (entry != channel.EntryAddress || _info((uint)channel.Kind, channel.Slot, ref info) != 1)
                    throw new UnsupportedSignatureException("The running typed source entry or channel cannot be read.");
                ValidateOwner(info);
                var owner = ReadOperationOwnerLocked();
                if (info.Kind != channel.Kind || info.Role != channel.Role || info.Slot != channel.Slot
                    || info.Target != channel.TargetAddress || info.Original != channel.OriginalAddress
                    || info.Entry != channel.EntryAddress || info.OwnerThreadId != owner.OwnerThreadId
                    || info.InFlight > long.MaxValue)
                    throw new UnsupportedSignatureException("The running source channel differs from its exact ABI, target, original or native owner.");
                unknown = (info.Flags & NativePlaybackSourceFlags.Unknown) != 0 || info.Faults != 0
                    || (owner.Flags & NativePlaybackSourceFlags.Unknown) != 0 || owner.Faults != 0;
                if (requireArmed && (_withdrawn || (info.Flags & NativePlaybackSourceFlags.Armed) == 0
                    || (owner.Flags & NativePlaybackSourceFlags.Armed) == 0
                    || ((info.Flags | owner.Flags) & (NativePlaybackSourceFlags.Withdrawn | NativePlaybackSourceFlags.Dormant)) != 0))
                    throw new UnsupportedSignatureException("The running source channel is withdrawn or no longer armed.");
            }
            // Fault callbacks may withdraw the group synchronously. They run
            // outside the read interval and can never turn a failed read into admission.
            if (unknown) MarkBrokerFault("The running source channel or owner reported sticky Unknown.");
            if (unknown || Volatile.Read(ref _fault) is not null)
                throw new UnsupportedSignatureException("The running source channel is unknown.");
        }
        catch (Exception error)
        {
            MarkBrokerFault("The running source channel lost its retained native identity.", error);
            throw;
        }
    }

    internal void RequireOperationFreshAdmission()
    {
        VerifyThread();
        if (ReadOperationHealth() is not { Known: true, Armed: true })
            throw new UnsupportedSignatureException("The running source owner cannot admit a fresh operation.");
        // This is the actual native source initialization/backing gate. Busy declines
        // this command without erasing pending roots or faulting the group.
        if (Export<EnterFreshNative>("RtPlaybackSourceEnterFresh")(_owner) != 1)
            throw new UnsupportedSignatureException("Initialization is pending; the running writer or Clock operation is unavailable.");
        Export<LeaveFreshNative>("RtPlaybackSourceLeaveFresh")(_owner);
    }

    internal NativePlaybackSourceFrame ReadOperationCurrentActual(NativePlaybackStockSourceBridge channel,
        NativePlaybackSourceFrame expected, bool requireBefore, bool allowWithdrawnCompletion)
    {
        VerifyOperationMemberEvidence(channel, requireArmed: !allowWithdrawnCompletion);
        var actual = NewFrame();
        if (_current((uint)channel.Kind, channel.Slot, ref actual) != 1
            || !NativePlaybackStockSourceBridge.SameCall(expected, actual)
            || requireBefore && actual.Stage != NativePlaybackSourceStage.Before
            || allowWithdrawnCompletion && actual.Stage is not (NativePlaybackSourceStage.OriginalReturned or NativePlaybackSourceStage.Abnormal))
            throw new UnsupportedSignatureException("No exact running native source scope exists for this observation.");
        ValidateFrame(actual, physical: false);
        if (_verify(_owner, ref actual) != 1)
            throw new UnsupportedSignatureException("The running native scope verifier rejected its actual owner and token.");
        VerifyOperationMemberEvidence(channel, requireArmed: !allowWithdrawnCompletion);
        return actual;
    }

    internal bool TryReadOperationReceipt(NativePlaybackStockSourceBridge channel,
        NativePlaybackSourceFrame origin, out NativePlaybackSourceFrame actual)
    {
        VerifyOperationMemberEvidence(channel, requireArmed: false);
        actual = NewFrame();
        if (_receipt(_owner, origin.Sequence, origin.Token, ref actual) != 1) return false;
        if (!NativePlaybackStockSourceBridge.SameCall(origin, actual))
            throw new UnsupportedSignatureException("The running source journal receipt belongs to another actual call.");
        ValidateFrame(actual, physical: true);
        VerifyOperationMemberEvidence(channel, requireArmed: false);
        return true;
    }
}
