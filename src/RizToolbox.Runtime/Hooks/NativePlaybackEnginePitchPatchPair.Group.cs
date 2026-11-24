namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackEnginePitchPatchPair
{
    internal static NativePlaybackEnginePitchPatchPair CreateForGroup(
        NativePlaybackLowerPitchTarget.EnginePitchPreflight target, DobbyDetourEngine engine,
        ExactHookBroker.PlaybackSourceInstallGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.VerifyNativePitchPairIssuer(target, engine);
        return CreateCore(target, engine, group);
    }

    internal static NativePlaybackEnginePitchPatchPair CreateForGroup(
        NativePlaybackLowerPitchTarget.EnginePitchPreflight target,
        NativePlaybackLowerPitchTarget.LowerTimePreflight lowerTime, DobbyDetourEngine engine,
        ExactHookBroker.PlaybackSourceInstallGroup group)
    {
        ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(lowerTime);
        group.VerifyNativePitchPairIssuer(target, engine, lowerTime);
        return CreateCore(target, engine, group, lowerTime);
    }

    internal MemberReceipt IssueSourceMember(ExactHookBroker.PlaybackSourceOwner owner)
        => MemberReceipt.Issue(this, owner);

    /// <summary>One actual prepared native family member, permanently tied to one
    /// registered group/source/channel. Scalars, caller profiles and copied
    /// observations cannot construct or adopt a replacement receipt.</summary>
    internal sealed class MemberReceipt
    {
        private readonly NativePlaybackEnginePitchPatchPair _pair;
        private readonly Member _member;
        private readonly ExactHookBroker.PlaybackSourceOwner _owner;
        private readonly NativePlaybackStockSourceBridge _channel;
        private int _proofAdopted;
        private MemberReceipt(NativePlaybackEnginePitchPatchPair pair, Member member,
            ExactHookBroker.PlaybackSourceOwner owner)
            => (_pair, _member, _owner, _channel) = (pair, member, owner, owner.ConfiguredChannel);
        internal static MemberReceipt Issue(NativePlaybackEnginePitchPatchPair pair, ExactHookBroker.PlaybackSourceOwner owner)
        {
            pair.RequireOwnerThread(); ArgumentNullException.ThrowIfNull(owner);
            if (pair._group is null || !ReferenceEquals(owner.Group, pair._group) || !ExactHookBroker.OwnsPlaybackSourceInstallation(owner)
                || !owner.HasEngine(pair._engine) || !pair._prepared)
                throw new UnsupportedSignatureException("Only this actual registered group's prepared source owner may adopt a fixed native member.");
            var member = owner.Role switch
            {
                NativePlaybackSourceRole.LowerSetPitch => pair._lower,
                NativePlaybackSourceRole.EngineSetPitch => pair._native,
                NativePlaybackSourceRole.LowerSetTime => pair._lowerTime
                    ?? throw new UnsupportedSignatureException("This registered installation has no fixed lower-time member."),
                _ => throw new UnsupportedSignatureException("The native pair cannot replace a different source role.")
            };
            if (member.IssuedReceipt is not null || member.Committed || owner.Kind != NativePlaybackSourceKind.IcallSingle1
                || owner.MemberModule != pair.Module || owner.TargetAddress != member.Address
                || owner.EntryAddress != member.EntryAddress || owner.ConfiguredChannel.IsConfigured
                || !member.HasOriginal(owner.UninstalledOriginalBody))
                throw new UnsupportedSignatureException("A fixed native member requires its exact once-only owner, original body and reserved entry.");
            var receipt = new MemberReceipt(pair, member, owner);
            member.IssuedReceipt = receipt;
            return receipt;
        }
        internal IntPtr OriginalAddress => _member.OriginalAddress;
        internal IntPtr DobbyOriginalAddress => _member.DobbyOriginalAddress;
        internal IntPtr InformationAddress => _member.InformationAddress;
        internal int RelocatedByteCount => _member.RelocatedByteCount;
        internal int PatchedByteCount => _member.PatchedByteCount;
        internal bool IsCommitted => _member.Committed;

        internal void AdoptIntoProof(ExactHookBroker.PlaybackSourceOwner owner)
        {
            VerifyOwner(owner);
            if (Interlocked.Exchange(ref _proofAdopted, 1) != 0)
                throw new UnsupportedSignatureException("This actual native pair proof has already been adopted.");
            if (OriginalAddress == IntPtr.Zero || DobbyOriginalAddress == IntPtr.Zero || InformationAddress == IntPtr.Zero)
                throw new UnsupportedSignatureException("The fixed pair has no complete actual original/relocation/unwind proof.");
        }
        internal void Verify()
            => Verify(null);
        internal void VerifyOperation(ExactHookBroker.PlaybackSourceOwner owner)
        {
            if (GetCurrentThreadId() != _pair._thread)
                throw new UnsupportedSignatureException("The running native pair belongs to its actual original thread.");
            lock (Gate)
                if (_pair._failed || !Retained.TryGetValue(_pair.Module, out var actual) || !ReferenceEquals(actual, _pair))
                    throw new UnsupportedSignatureException("The running native pair lost its sole retained physical owner.");
            _pair._group?.VerifyNativePitchPairIssuer(_pair._target, _pair._engine, _pair._lowerTimeTarget);
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(_pair._group, owner.Group)
                || !ExactHookBroker.OwnsPlaybackSourceInstallation(owner) || !owner.HasEngine(_pair._engine)
                || !ReferenceEquals(_member.IssuedReceipt, this) || owner.TargetAddress != _member.Address
                || owner.EntryAddress != _member.EntryAddress || owner.MemberModule != _pair.Module
                || !ReferenceEquals(owner.ConfiguredChannel, _channel) || !IsCommitted || Volatile.Read(ref _proofAdopted) != 1)
                throw new UnsupportedSignatureException("The running native member lost its exact adopted proof, source or channel.");
            _member.VerifyOperation();
        }
        internal void Verify(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal)
        {
            VerifyOwner(_owner, traversal);
            if (Volatile.Read(ref _proofAdopted) != 1)
                throw new UnsupportedSignatureException("The source owner has not adopted its actual pair member proof.");
            _member.Verify(traversal);
        }
        internal void Commit(ExactHookBroker.PlaybackSourceOwner owner)
        {
            VerifyOwner(owner); Verify();
            if (!owner.ConfiguredChannel.IsConfigured)
                throw new UnsupportedSignatureException("An unconfigured actual native channel cannot publish its pair member.");
            switch (_member.Kind)
            {
                case MemberKind.LowerPitch: _pair.CommitLower(); break;
                case MemberKind.EnginePitch: _pair.CommitEngine(); break;
                case MemberKind.LowerTime: _pair.CommitLowerTime(); break;
                default: throw new UnsupportedSignatureException("Unknown fixed native member kind.");
            }
        }
        internal void Compose(ExactHookBroker.PlaybackSourceOwner owner, uint sectionRva, byte[] expected)
            => Compose(owner, sectionRva, expected, null);
        internal void Compose(ExactHookBroker.PlaybackSourceOwner owner, uint sectionRva, byte[] expected,
            ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal)
        { VerifyOwner(owner, traversal); Verify(traversal); _member.Compose(sectionRva, expected, traversal); }

        private void VerifyOwner(ExactHookBroker.PlaybackSourceOwner owner,
            ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal = null)
        {
            _pair.VerifySealIssuer(_pair.Module, traversal);
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(_pair._group, owner.Group)
                || !ExactHookBroker.OwnsPlaybackSourceInstallation(owner) || !owner.HasEngine(_pair._engine)
                || !ReferenceEquals(_member.IssuedReceipt, this) || owner.TargetAddress != _member.Address
                || owner.EntryAddress != _member.EntryAddress || owner.MemberModule != _pair.Module
                || !ReferenceEquals(owner.ConfiguredChannel, _channel))
                throw new UnsupportedSignatureException("The prepared native member lost its exact registered group, source owner or channel.");
        }
    }
}
