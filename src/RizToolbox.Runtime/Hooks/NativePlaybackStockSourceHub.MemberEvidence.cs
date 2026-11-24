namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    internal NativePlaybackExecutableSeal RetainMemberImageEvidenceForGroup(
        ExactHookBroker.PlaybackSourceInstallGroup group,
        ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal traversal)
    {
        group.VerifyNativeHelperBatchIssuer(this, traversal);
        _image.Verify();
        return _image;
    }

    internal void VerifyMemberReservation(NativePlaybackStockSourceBridge channel)
    {
        // The owner keeps this check before proof verification and completion
        // pumping. GetEntry is a pure native getter, so calling its outer
        // image-verifying wrapper here would only repeat the same precheck.
        ArgumentNullException.ThrowIfNull(channel);
        _image.Verify();
        lock (_gate) VerifyMemberReservationLocked(channel);
    }
    internal void VerifyInstallationMemberReservation(
        ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal traversal, NativePlaybackStockSourceBridge channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        traversal.VerifyInstallationSourceHelper(this, _image);
        lock (_gate) VerifyMemberReservationLocked(channel);
        traversal.VerifyInstallationSourceHelper(this, _image);
    }
    internal void VerifyInstallationMemberEvidence(
        ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal traversal, NativePlaybackStockSourceBridge channel)
    {
        try
        {
            traversal.VerifyInstallationSourceHelper(this, _image);
            lock (_gate)
            {
                // The same exact entry/info/owner getters remain live. The
                // traversal's independently retained full helper seal brackets
                // this pure read with no configuration or journal release.
                if (VerifyMemberEvidenceLocked(channel))
                    throw new UnsupportedSignatureException("The installation source owner reported sticky Unknown or a source callback fault.");
                if (_withdrawn || Volatile.Read(ref _fault) is not null)
                    throw new UnsupportedSignatureException("The native channel lost its current retained owner.");
            }
            traversal.VerifyInstallationSourceHelper(this, _image);
        }
        catch (Exception error)
        {
            MarkBrokerFault("The installation source helper or actual owner identity changed.", error);
            throw;
        }
    }

    // Only the group's one locked verification traversal calls this. No
    // completion pump or gameplay getter occurs while this helper image is
    // bracketed; every actual native entry reservation is still queried.
    internal void VerifyMemberReservationsForGroup(ExactHookBroker.PlaybackSourceInstallGroup group,
        ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal traversal,
        IReadOnlyList<NativePlaybackStockSourceBridge> channels)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(channels);
        group.VerifyNativeHelperBatchIssuer(this, traversal);
        try
        {
            lock (_gate)
            {
                _image.Verify();
                try
                {
                    foreach (var channel in channels)
                        VerifyMemberReservationLocked(channel);
                }
                finally { _image.Verify(); }
            }
            group.VerifyNativeHelperBatchIssuer(this, traversal);
        }
        catch (Exception error)
        {
            MarkBrokerFault("The batched source helper entry reservation changed.", error);
            throw;
        }
    }

    internal void VerifyMemberEvidence(NativePlaybackStockSourceBridge channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        VerifyMemberEvidenceCore(new[] { channel });
    }

    internal void VerifyRetiredMemberEvidence(ExactHookBroker.PlaybackSourceInstallGroup group,
        NativePlaybackStockSourceBridge channel)
    {
        group.VerifyRetiredSourceHelper(this);
        lock (_gate)
        {
            _image.Verify();
            try
            {
                if (VerifyMemberEvidenceLocked(channel, retired: true))
                    throw new UnsupportedSignatureException("The withdrawn native source owner reported sticky Unknown or a callback fault.");
            }
            finally { _image.Verify(); }
        }
        group.VerifyRetiredSourceHelper(this);
    }

    // Read-only native entry/info/owner getters are the only calls between
    // these full helper-image checks. A batch is one synchronous locked read
    // interval, never a retained successful image ticket.
    internal void VerifyMemberEvidenceForGroup(ExactHookBroker.PlaybackSourceInstallGroup group,
        ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal traversal,
        IReadOnlyList<NativePlaybackStockSourceBridge> channels)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(channels);
        group.VerifyNativeHelperBatchIssuer(this, traversal);
        VerifyMemberEvidenceCore(channels);
        group.VerifyNativeHelperBatchIssuer(this, traversal);
    }

    private void VerifyMemberEvidenceCore(IReadOnlyList<NativePlaybackStockSourceBridge> channels)
    {
        try
        {
            var unknown = false;
            lock (_gate)
            {
                _image.Verify();
                try
                {
                    foreach (var channel in channels)
                    {
                        // These native getters only read their actual entry,
                        // channel and owner. They cannot invoke an observer.
                        if (VerifyMemberEvidenceLocked(channel)) { unknown = true; break; }
                    }
                }
                finally { _image.Verify(); }
                if (_withdrawn || Volatile.Read(ref _fault) is not null)
                    throw new UnsupportedSignatureException("The native channel lost its current retained owner.");
            }
            // Fault publication may synchronously invoke the group's owner.
            // It stays outside both the read-only interval and the hub lock;
            // a callback can never turn this failed query into acceptance.
            if (unknown) MarkBrokerFault("The native source owner reported sticky Unknown or a source callback fault.");
            if (unknown || Volatile.Read(ref _fault) is not null)
                throw new UnsupportedSignatureException("The shared stock source transport is unknown.");
        }
        catch (Exception error)
        {
            MarkBrokerFault("The source helper or actual owner identity changed.", error);
            throw;
        }
    }

    private bool VerifyMemberEvidenceLocked(NativePlaybackStockSourceBridge channel, bool retired = false)
    {
        VerifyMemberReservationLocked(channel, retired);
        if (!channel.IsConfigured)
            throw new UnsupportedSignatureException("This retained channel has not configured its actual original.");
        channel.VerifyOriginalHead();
        var info = NewInformation();
        if (_info((uint)channel.Kind, channel.Slot, ref info) != 1)
            throw new UnsupportedSignatureException("The native source channel cannot be read.");
        ValidateOwner(info);
        if (info.Kind != channel.Kind || info.Role != channel.Role || info.Slot != channel.Slot
            || info.Target != channel.TargetAddress || info.Original != channel.OriginalAddress || info.Entry != channel.EntryAddress)
            throw new UnsupportedSignatureException("The actual source channel differs from its immutable target/original/entry identity.");

        var owner = NewInformation();
        if (_readOwner(_owner, ref owner) != 1)
            throw new UnsupportedSignatureException("The actual native source owner cannot be read.");
        ValidateOwner(owner);
        if (retired && (!IsExplicitlyWithdrawn(info.Flags) || !IsExplicitlyWithdrawn(owner.Flags)))
            throw new UnsupportedSignatureException("The source helper did not read back the explicit native withdrawal.");
        return (info.Flags & NativePlaybackSourceFlags.Unknown) != 0 || info.Faults != 0
            || (owner.Flags & NativePlaybackSourceFlags.Unknown) != 0 || owner.Faults != 0;
    }

    private static bool IsExplicitlyWithdrawn(NativePlaybackSourceFlags flags)
        => (flags & (NativePlaybackSourceFlags.Armed | NativePlaybackSourceFlags.Withdrawn)) == NativePlaybackSourceFlags.Withdrawn;

    private void VerifyMemberReservationLocked(NativePlaybackStockSourceBridge channel, bool retired = false)
    {
        if (_withdrawn != retired || Volatile.Read(ref _fault) is not null || !ReferenceEquals(channel.Hub, this)
            || !_channels.TryGetValue(channel.Slot, out var actual) || !ReferenceEquals(actual, channel))
            throw new UnsupportedSignatureException("The native channel lost its exact retained helper entry reservation.");
        var entry = _entry((uint)channel.Kind, channel.Slot);
        if (entry == IntPtr.Zero)
            throw new UnsupportedSignatureException("The exact typed source native entry is unavailable.");
        HookTargetValidation.VerifyExecutable(entry, _library);
        if (entry != channel.EntryAddress || channel.Epoch != Epoch)
            throw new UnsupportedSignatureException("The native channel lost its exact retained helper entry reservation.");
    }
}
