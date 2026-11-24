using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

/// <summary>One retained installation owner for the privately fixed native
/// pitch members and the candidate lower-time member. Every original body
/// preflights before preparation. The complete image composes only the actual
/// verified member patches into its pinned file plus DIR64 relocations.</summary>
internal sealed partial class NativePlaybackEnginePitchPatchPair
{
    private enum MemberKind { LowerPitch, EnginePitch, LowerTime }
    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, NativePlaybackEnginePitchPatchPair> Retained = new();
    private readonly NativePlaybackLowerPitchTarget.EnginePitchPreflight _target;
    private readonly NativePlaybackLowerPitchTarget.LowerTimePreflight? _lowerTimeTarget;
    private readonly DobbyDetourEngine _engine;
    private readonly NativePlaybackExecutableSeal _dobby;
    private readonly uint _thread;
    private readonly ExactHookBroker.PlaybackSourceInstallGroup? _group;
    private readonly Member _lower, _native;
    private readonly Member? _lowerTime;
    private readonly Member[] _members;
    private NativePlaybackExecutableSeal? _image;
    private bool _prepared, _failed;
    internal IntPtr Module => _target.Module;
    internal string ImageSha256 => _target.Parent.ImageSha256;
    internal IntPtr LowerOriginal => _lower.OriginalAddress;
    internal IntPtr EngineOriginal => _native.OriginalAddress;
    internal IntPtr LowerTimeOriginal => (_lowerTime ?? throw new UnsupportedSignatureException("This installation has no lower-time member.")).OriginalAddress;
    internal int MemberCount => _members.Length;
    internal int CommittedMembers => _members.Count(member => member.Committed);

    private NativePlaybackEnginePitchPatchPair(NativePlaybackLowerPitchTarget.EnginePitchPreflight target,
        DobbyDetourEngine engine, ExactHookBroker.PlaybackSourceInstallGroup? group,
        NativePlaybackLowerPitchTarget.LowerTimePreflight? lowerTime)
    {
        (_target, _engine, _thread, _lowerTimeTarget) = (target, engine, GetCurrentThreadId(), lowerTime);
        _group = group;
        _dobby = engine.RetainPlaybackAuthority();
        _lower = new(this, target.Parent.DetourIdentity, target.Parent.BodyLength, target.Parent.BodySha256, MemberKind.LowerPitch);
        _native = new(this, target.DetourIdentity, target.BodyLength, target.BodySha256, MemberKind.EnginePitch);
        if (lowerTime is null) _members = new[] { _lower, _native };
        else
        {
            _lowerTime = new(this, lowerTime.DetourIdentity, lowerTime.BodyLength, lowerTime.BodySha256, MemberKind.LowerTime);
            _members = new[] { _lower, _native, _lowerTime };
        }
    }

    internal static NativePlaybackEnginePitchPatchPair Create(
        NativePlaybackLowerPitchTarget.EnginePitchPreflight target, DobbyDetourEngine engine)
        => CreateCore(target, engine, null);

    internal static NativePlaybackEnginePitchPatchPair Create(
        NativePlaybackLowerPitchTarget.EnginePitchPreflight target,
        NativePlaybackLowerPitchTarget.LowerTimePreflight lowerTime, DobbyDetourEngine engine)
    {
        ArgumentNullException.ThrowIfNull(lowerTime);
        return CreateCore(target, engine, null, lowerTime);
    }

    private static NativePlaybackEnginePitchPatchPair CreateCore(
        NativePlaybackLowerPitchTarget.EnginePitchPreflight target, DobbyDetourEngine engine,
        ExactHookBroker.PlaybackSourceInstallGroup? group,
        NativePlaybackLowerPitchTarget.LowerTimePreflight? lowerTime = null)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(engine);
        if (lowerTime is not null && (!ReferenceEquals(lowerTime.Parent, target.Parent) || lowerTime.Module != target.Module))
            throw new UnsupportedSignatureException("Every fixed native member must derive from the same private two-image receipt.");
        target.VerifyUninstalled(); lowerTime?.VerifyUninstalled();
        lock (Gate)
        {
            if (Retained.ContainsKey(target.Module))
                throw new UnsupportedSignatureException("The fixed native family already has its retained physical owner.");
            var value = new NativePlaybackEnginePitchPatchPair(target, engine, group, lowerTime);
            Retained.Add(target.Module, value);
            try
            {
                value._image = NativePlaybackExecutableSeal.CaptureEnginePitchPair(value);
                target.Parent.RetainEnginePitchPair(value);
                return value;
            }
            catch { value._failed = true; throw; }
        }
    }
    internal bool IdentifiesParent(NativePlaybackLowerPitchTarget parent) => ReferenceEquals(_target.Parent, parent);

    internal void Prepare(IntPtr lowerEntry, IntPtr engineEntry)
    {
        if (_lowerTime is not null)
            throw new UnsupportedSignatureException("The three-member family requires all three reserved entries together.");
        PrepareCore(new[] { lowerEntry, engineEntry });
    }
    internal void Prepare(IntPtr lowerEntry, IntPtr engineEntry, IntPtr lowerTimeEntry)
    {
        if (_lowerTime is null)
            throw new UnsupportedSignatureException("Only the closed lower-time preflight can add the third native member.");
        PrepareCore(new[] { lowerEntry, engineEntry, lowerTimeEntry });
    }
    private void PrepareCore(IntPtr[] entries)
    {
        _group?.RequireNoInstallationAudit();
        RequireOwnerThread();
        if (_prepared || CommittedMembers != 0 || entries.Length != _members.Length
            || entries.Any(entry => entry == IntPtr.Zero || _members.Any(member => member.Address == entry))
            || entries.Distinct().Count() != entries.Length)
            throw new UnsupportedSignatureException("Only distinct reserved entries may prepare the complete fixed native family once.");
        VerifyInstallationEvidence();
        try
        {
            for (var index = 0; index < _members.Length; ++index) _members[index].Prepare(entries[index]);
            _prepared = true; VerifyInstallationEvidence();
        }
        catch { _failed = true; throw; }
    }

    internal void CommitLower()
    {
        RequireOwnerThread();
        if (!_prepared || CommittedMembers != 0)
            throw new UnsupportedSignatureException("The fixed native family must publish its prepared lower-pitch member first.");
        Commit(_lower);
    }
    internal void CommitEngine()
    {
        RequireOwnerThread();
        if (!_prepared || !_lower.Committed || _native.Committed)
            throw new UnsupportedSignatureException("The fixed engine-pitch member must follow its prepared lower-pitch member.");
        Commit(_native);
    }
    internal void CommitLowerTime()
    {
        RequireOwnerThread();
        var member = _lowerTime ?? throw new UnsupportedSignatureException("This installation has no lower-time member.");
        if (!_prepared || !_lower.Committed || !_native.Committed || member.Committed)
            throw new UnsupportedSignatureException("The fixed lower-time member must publish last, exactly once.");
        Commit(member);
    }
    private void Commit(Member member)
    {
        _group?.RequireNoInstallationAudit();
        VerifyInstallationEvidence();
        try { member.Commit(); VerifyInstallationEvidence(); }
        catch { _failed = true; throw; }
    }

    internal void VerifyInstallationEvidence()
        => VerifyInstallationEvidence(null);
    internal void VerifyInstallationEvidence(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
        PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
    {
        VerifySealIssuer(Module, traversal, family); VerifyDobby(traversal);
        foreach (var member in _members) member.Verify(traversal, family);
        var image = _image ?? throw new UnsupportedSignatureException("The native family has no complete retained executable seal.");
        if (family is not null) image.VerifyForTargetFamily(family);
        else if (traversal is null) image.Verify(); else image.VerifyForGroupTraversal(traversal);
    }
    internal void VerifySealIssuer(IntPtr module)
        => VerifySealIssuer(module, null);
    internal void VerifySealIssuer(IntPtr module, ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
        PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
    {
        // Standalone pairs and actual native writes retain the unscoped path.
        // A locked active traversal or one dormant installation read can share
        // its supporting parents; neither scope spans an actual native write.
        if (family is not null && traversal is not null)
            throw new UnsupportedSignatureException("TargetFamily ancestor sharing cannot borrow a group traversal.");
        if (traversal is not null)
            traversal.VerifyIssuer(_group ?? throw new UnsupportedSignatureException("A standalone native pair cannot borrow a group traversal."));
        lock (Gate)
            if (module != Module || _failed || !Retained.TryGetValue(Module, out var actual) || !ReferenceEquals(actual, this))
                throw new UnsupportedSignatureException("The exact native image lacks its sole retained fixed-member owner.");
        _target.VerifySupportingEvidence(traversal, family); _lowerTimeTarget?.VerifySupportingEvidence(traversal, family);
        _group?.VerifyNativePitchPairIssuer(_target, _engine, _lowerTimeTarget);
    }
    internal (IntPtr Address, int Length)[] ImageBodies()
    {
        VerifySealIssuer(Module);
        return _members.Select(member => (member.Address, member.BodyLength)).ToArray();
    }
    internal void ComposeVerifiedMembers(uint sectionRva, byte[] expected)
        => ComposeVerifiedMembers(sectionRva, expected, null);
    internal void ComposeVerifiedMembers(uint sectionRva, byte[] expected,
        ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
        PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
    {
        VerifySealIssuer(Module, traversal, family); VerifyDobby(traversal);
        foreach (var member in _members) member.Compose(sectionRva, expected, traversal, family);
    }
    private void VerifyDobby(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal)
    {
        if (traversal is null) _dobby.Verify();
        else traversal.VerifySharedDobbySeal(_group
            ?? throw new UnsupportedSignatureException("A standalone native pair cannot borrow the group Dobby seal."), _engine, _dobby);
    }
    private void RequireOwnerThread()
    {
        if (GetCurrentThreadId() != _thread)
            throw new UnsupportedSignatureException("Only the original owner thread may prepare or publish the fixed native family.");
        VerifySealIssuer(Module);
    }

    private sealed class Member
    {
        private readonly NativePlaybackEnginePitchPatchPair _pair;
        private readonly VerifiedHookTarget _identity;
        private readonly byte[] _original;
        internal MemberKind Kind { get; }
        private NativePlaybackExecutableSeal.RangeSeal _source;
        private NativePlaybackExecutableSeal.RangeSeal? _relay;
        private NativePlaybackLowerPitchTrampolineProof? _lowerProof;
        private NativePlaybackEnginePitchTrampolineProof? _engineProof;
        private NativePlaybackLowerTimeTrampolineProof? _lowerTimeProof;
        private IntPtr _entry, _dobbyOriginal;
        internal MemberReceipt? IssuedReceipt { get; set; }
        internal bool Committed { get; private set; }
        internal int PatchedByteCount { get; private set; }
        internal int RelocatedByteCount => Kind == MemberKind.LowerTime ? 6 : 7;
        internal IntPtr Address => _identity.Address;
        internal int BodyLength => _original.Length;
        internal IntPtr EntryAddress => _entry;
        internal IntPtr DobbyOriginalAddress => _dobbyOriginal;
        internal IntPtr InformationAddress => Kind switch
        {
            MemberKind.LowerPitch => _lowerProof?.InformationAddress ?? IntPtr.Zero,
            MemberKind.EnginePitch => _engineProof?.InformationAddress ?? IntPtr.Zero,
            MemberKind.LowerTime => _lowerTimeProof?.InformationAddress ?? IntPtr.Zero,
            _ => throw new UnsupportedSignatureException("Unknown fixed native member kind.")
        };
        internal IntPtr OriginalAddress => Kind switch
        {
            MemberKind.LowerPitch => _lowerProof?.OriginalAddress ?? IntPtr.Zero,
            MemberKind.EnginePitch => _engineProof?.OriginalAddress ?? IntPtr.Zero,
            MemberKind.LowerTime => _lowerTimeProof?.OriginalAddress ?? IntPtr.Zero,
            _ => throw new UnsupportedSignatureException("Unknown fixed native member kind.")
        };
        internal Member(NativePlaybackEnginePitchPatchPair pair, VerifiedHookTarget identity, int length, string hash, MemberKind kind)
        {
            (_pair, _identity, Kind) = (pair, identity, kind);
            _source = NativePlaybackExecutableSeal.RangeSeal.Capture(identity.Address, length, pair.Module, true, true);
            _original = (byte[])_source.Bytes.Clone();
            if (Convert.ToHexString(SHA256.HashData(_original)).ToLowerInvariant() != hash)
                throw new UnsupportedSignatureException("A fixed native member's complete original differs from its private issuer.");
        }
        internal void Prepare(IntPtr entry)
        {
            if (_entry != IntPtr.Zero) throw new UnsupportedSignatureException("A fixed native member was prepared twice.");
            _ = NativePlaybackExecutableSeal.RangeSeal.Capture(entry, 1, IntPtr.Zero, false, true);
            _entry = entry;
            _dobbyOriginal = _pair._engine.Prepare(_identity, entry);
            switch (Kind)
            {
                case MemberKind.LowerPitch:
                    _lowerProof = new(_pair._target.Parent, _original, _dobbyOriginal, _pair._target); break;
                case MemberKind.EnginePitch:
                    _engineProof = new(_pair._target, _original, _dobbyOriginal); break;
                case MemberKind.LowerTime:
                    _lowerTimeProof = new(_pair._lowerTimeTarget!, _original, _dobbyOriginal); break;
                default: throw new UnsupportedSignatureException("Unknown fixed native member kind.");
            }
        }
        internal void Commit()
        {
            if (Committed || OriginalAddress == IntPtr.Zero)
                throw new UnsupportedSignatureException("A native member lacks its once-only prepared original proof.");
            var protection = _source.StartProtection;
            try { _pair._engine.Commit(_identity); }
            finally
            {
                if (!VirtualProtect(Address, (nuint)RelocatedByteCount, protection, out _)
                    || !FlushInstructionCache((IntPtr)(-1), Address, (nuint)RelocatedByteCount))
                    throw new UnsupportedSignatureException("The native commit did not restore its image protection and instruction cache.");
            }
            var actual = NativePlaybackExecutableSeal.RangeSeal.Capture(Address, BodyLength, _pair.Module, true, true);
            int patched;
            if (Kind != MemberKind.LowerTime && actual.Bytes[0] == 0xe9)
            {
                patched = 5;
                var jump = (IntPtr)checked(Address.ToInt64() + 5 + BitConverter.ToInt32(actual.Bytes, 1));
                if (jump != _entry)
                {
                    _relay = NativePlaybackExecutableSeal.RangeSeal.Capture(jump, 14, IntPtr.Zero, false, true);
                    if (!_relay.Bytes.AsSpan(0, 6).SequenceEqual(new byte[] { 0xff, 0x25, 0, 0, 0, 0 })
                        || (IntPtr)BitConverter.ToInt64(_relay.Bytes, 6) != _entry)
                        throw new UnsupportedSignatureException("The actual native relay reaches another entry.");
                }
            }
            else if (actual.Bytes[0] == 0xff && actual.Bytes[1] == 0x25 && BitConverter.ToInt32(actual.Bytes, 2) != 0)
            {
                patched = 6;
                var slot = (IntPtr)checked(Address.ToInt64() + 6 + BitConverter.ToInt32(actual.Bytes, 2));
                _relay = NativePlaybackExecutableSeal.RangeSeal.Capture(slot, 8, IntPtr.Zero, false);
                if ((IntPtr)BitConverter.ToInt64(_relay.Bytes, 0) != _entry)
                    throw new UnsupportedSignatureException("The actual native slot names another entry.");
            }
            else throw new UnsupportedSignatureException("The native member has an unsupported installed patch form.");
            if (!actual.Bytes.AsSpan(patched).SequenceEqual(_original.AsSpan(patched)))
                throw new UnsupportedSignatureException("The actual native patch changed bytes beyond its exact entry transfer.");
            _source = actual; PatchedByteCount = patched; Committed = true;
        }
        internal void Verify()
            => Verify(null);
        internal void VerifyOperation()
        {
            _source.Verify(); _relay?.Verify();
            _lowerProof?.VerifyOperation(); _engineProof?.VerifyOperation(); _lowerTimeProof?.VerifyOperation();
        }
        internal void Verify(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
            PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
        {
            _source.Verify(); _relay?.Verify();
            _lowerProof?.Verify(traversal, family); _engineProof?.VerifyRelocationEvidence(traversal, family);
            _lowerTimeProof?.Verify(traversal, family);
        }
        internal void Compose(uint sectionRva, byte[] expected)
            => Compose(sectionRva, expected, null);
        internal void Compose(uint sectionRva, byte[] expected,
            ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
            PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
        {
            Verify(traversal, family); var relative = Address.ToInt64() - _pair.Module.ToInt64() - sectionRva;
            if (relative < 0 || relative >= expected.Length) return;
            if (BodyLength > expected.Length - relative
                || !expected.AsSpan(checked((int)relative), BodyLength).SequenceEqual(_original))
                throw new UnsupportedSignatureException("A fixed native original differs from the complete pinned file plus DIR64.");
            _source.Bytes.CopyTo(expected, checked((int)relative));
        }
        internal bool HasOriginal(ReadOnlySpan<byte> expected) => expected.SequenceEqual(_original);
    }
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint length);
}
