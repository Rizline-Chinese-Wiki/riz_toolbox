using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    /// <summary>One of the closed eleven source roles. It owns its complete
    /// original body and metadata, actual Dobby relocation, private proof,
    /// installed patch and shared native-hub channel.</summary>
    internal sealed partial class PlaybackSourceOwner
    {
        private readonly PlaybackSourceInstallGroup _group;
        private readonly PlaybackSourceGroupTargets _targets;
        private readonly VerifiedHookTarget _target;
        private readonly DobbyDetourEngine _engine;
        private readonly NativePlaybackExecutableSeal _dobby;
        private readonly List<NativePlaybackExecutableSeal.RangeSeal> _metadata = new();
        private readonly NativePlaybackStockSourceHub _hub;
        private readonly Action<NativePlaybackSourceObservation>? _before;
        private readonly Action<NativePlaybackSourceCompletion>? _after;
        private NativePlaybackStockSourceBridge? _bridge;
        private NativePlaybackExecutableSeal.RangeSeal _source;
        private NativePlaybackExecutableSeal.RangeSeal? _relay;
        private NativePlaybackSourceTrampolineProof? _proof;
        private NativePlaybackEnginePitchPatchPair.MemberReceipt? _nativePairMember;
        private readonly byte[] _originalBody;
        private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal? _sourceFunctions;
        private readonly NativePlaybackProgressPlayerLeafProof.SourceLeafSeal? _sourceLeaf;
        private readonly NativePlaybackProgressClipLeafProof.SourceLeafSeal? _resourceLeaf;
        private int _committed;
        internal NativePlaybackSourceRole Role { get; }
        internal NativePlaybackSourceKind Kind { get; }
        internal IntPtr TargetAddress => _target.Address;
        internal IntPtr MemberModule => _targets.SourceModule(Role);
        internal NativePlaybackStockSourceBridge ConfiguredChannel => _bridge
            ?? throw new UnsupportedSignatureException("The physical member has not been prepared.");
        internal IntPtr EntryAddress => _bridge?.EntryAddress ?? IntPtr.Zero;
        internal IntPtr OriginalAddress => _proof?.OriginalAddress ?? IntPtr.Zero;
        internal IntPtr DobbyOriginalAddress => _proof?.DobbyOriginalAddress ?? IntPtr.Zero;
        internal IntPtr TrampolineUnwindAddress => _proof?.InformationAddress ?? IntPtr.Zero;
        internal string OriginalBodySha256 { get; }
        private int _patchedByteCount;
        internal int PatchedByteCount { get => _nativePairMember?.PatchedByteCount ?? _patchedByteCount; private set => _patchedByteCount = value; }
        internal int RelocatedByteCount => _proof?.RelocatedByteCount ?? 0;
        internal int OriginalBodyLength => _originalBody.Length;
        internal bool IsCommitted => _nativePairMember?.IsCommitted ?? Volatile.Read(ref _committed) == 1;
        internal PlaybackSourceInstallGroup Group => _group;
        internal bool HasEngine(DobbyDetourEngine engine) => ReferenceEquals(_engine, engine);
        internal ReadOnlySpan<byte> UninstalledOriginalBody => _originalBody;
        internal bool IsNativePitchPairMember => _targets.HasEnginePitch
            && Role is NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch
            || _targets.HasLowerTime && Role == NativePlaybackSourceRole.LowerSetTime;
        internal void VerifyIssuedBefore(NativePlaybackSourceObservation observation)
        {
            if (_bridge is null || !observation.BelongsTo(_bridge))
                throw new UnsupportedSignatureException("The observation belongs to another group's physical channel.");
            _bridge.VerifyIssuedBefore(observation);
        }
        internal void VerifyIssuedReturn(NativePlaybackSourceCompletion completion)
        {
            if (_bridge is null || !completion.Observation.BelongsTo(_bridge))
                throw new UnsupportedSignatureException("The return belongs to another group's physical channel.");
            _bridge.VerifyIssuedReturn(completion);
        }

        internal PlaybackSourceOwner(PlaybackSourceInstallGroup group, PlaybackSourceGroupTargets targets,
            NativePlaybackSourceRole role, DobbyDetourEngine engine, NativePlaybackStockSourceHub hub,
            Action<NativePlaybackSourceObservation>? before, Action<NativePlaybackSourceCompletion>? after)
        {
            (_group, _targets, Role, _engine, _hub, _before, _after) = (group, targets, role, engine, hub, before, after);
            _target = targets.SourceTarget(role); Kind = role switch
            {
                NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.PauseLeaf or NativePlaybackSourceRole.AudioPlay or NativePlaybackSourceRole.AudioStop => NativePlaybackSourceKind.Void0,
                NativePlaybackSourceRole.SetPitch or NativePlaybackSourceRole.SetTime or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume => NativePlaybackSourceKind.Single1,
                NativePlaybackSourceRole.PlayScheduled or NativePlaybackSourceRole.AudioPlayDelayed => NativePlaybackSourceKind.Double1,
                NativePlaybackSourceRole.SetClip or NativePlaybackSourceRole.ProgressMusicPlayer or NativePlaybackSourceRole.PlayerConstructor or NativePlaybackSourceRole.ProgressClip => NativePlaybackSourceKind.Reference1,
                NativePlaybackSourceRole.ProgressPlay or NativePlaybackSourceRole.AudioStopBoolean or NativePlaybackSourceRole.AudioSetLoop => NativePlaybackSourceKind.Boolean1,
                NativePlaybackSourceRole.ProgressStartMoveNext => NativePlaybackSourceKind.BooleanReturn0,
                NativePlaybackSourceRole.AudioPlayHelper => NativePlaybackSourceKind.UInt64_1,
                NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch or NativePlaybackSourceRole.LowerSetTime => NativePlaybackSourceKind.IcallSingle1,
                _ => throw new UnsupportedSignatureException("Unknown stock physical source role.")
            };
            targets.VerifyCurrent(); _dobby = engine.RetainPlaybackAuthority();
            _source = NativePlaybackExecutableSeal.RangeSeal.Capture(TargetAddress, targets.SourceBodyLength(role), MemberModule, true, true);
            _originalBody = (byte[])_source.Bytes.Clone();
            OriginalBodySha256 = Convert.ToHexString(SHA256.HashData(_originalBody)).ToLowerInvariant();
            if (!OriginalBodySha256.Equals(targets.SourceBodySha256(role), StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("A stock source body differs from its issuer's complete original fingerprint: " + role);
            // All members preflight before any Dobby Prepare. Unwind metadata is
            // pinned to the file here, rather than first trusted during relocation.
            if (role is NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch or NativePlaybackSourceRole.LowerSetTime)
            {
                var lower = targets.LowerPitch ?? throw new UnsupportedSignatureException("Missing native-code lower issuer.");
                lower.VerifyCurrent();
                var nativeIdentity = role switch
                {
                    NativePlaybackSourceRole.LowerSetPitch => lower.DetourIdentity,
                    NativePlaybackSourceRole.EngineSetPitch => targets.EnginePitch?.DetourIdentity
                        ?? throw new UnsupportedSignatureException("Missing fixed native-backing pitch issuer."),
                    NativePlaybackSourceRole.LowerSetTime => targets.LowerTime?.DetourIdentity
                        ?? throw new UnsupportedSignatureException("Missing fixed lower-time issuer."),
                    _ => throw new UnsupportedSignatureException("Unknown fixed native member.")
                };
                if (_target != nativeIdentity) throw new UnsupportedSignatureException("The native pitch projection lost its private issuer.");
                _sourceFunctions = new(TargetAddress, _originalBody.Length, role == NativePlaybackSourceRole.LowerSetTime ? 6 : 7, role);
            }
            else if (role == NativePlaybackSourceRole.ProgressMusicPlayer)
                _sourceLeaf = new(TargetAddress, _originalBody, group.Module, targets.ProgressMusicPlayerTail);
            else if (role == NativePlaybackSourceRole.ProgressClip)
                _resourceLeaf = new(TargetAddress, _originalBody, group.Module, targets.ProgressClipTail);
            else
                _sourceFunctions = new NativePlaybackSourceTrampolineProof.SourceFunctionSeal(TargetAddress,
                    _originalBody.Length, NativePlaybackSourcePrologue.ForRole(role).Length, role);
            if (role is not (NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch or NativePlaybackSourceRole.LowerSetTime))
                CaptureMethod(_target, role == NativePlaybackSourceRole.AudioPlayHelper);
            if (role == NativePlaybackSourceRole.SetPitch) CaptureMethod(targets.PitchStatic, true);
        }
        private void CaptureMethod(VerifiedHookTarget target, bool isStatic)
        {
            Capture(target.MethodInfo, Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var value = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(target.MethodInfo);
            var parameterCount = Kind is NativePlaybackSourceKind.Void0 or NativePlaybackSourceKind.BooleanReturn0 ? 0 : isStatic ? 2 : 1;
            if (value.MethodPointer != TargetAddress || value.Class != target.DeclaringClass || value.Class == IntPtr.Zero
                || (value.Flags & 0x10) != (isStatic ? 0x10 : 0) || value.ParameterCount != parameterCount
                || (value.Bitfield & 3) != 0 || value.Name == IntPtr.Zero || value.ReturnType == IntPtr.Zero
                || parameterCount != 0 && value.Parameters == IntPtr.Zero)
                throw new UnsupportedSignatureException("A stock source metadata declaration differs from its exact native ABI: " + Role);
            var name = Role switch
            {
                NativePlaybackSourceRole.SetPitch => isStatic ? "SetPitch" : "set_pitch",
                NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.PauseLeaf => "Pause",
                NativePlaybackSourceRole.Replay => "ReplayScheduled",
                NativePlaybackSourceRole.Resume => "ResumeScheduled",
                NativePlaybackSourceRole.PlayScheduled => "PlayScheduled",
                NativePlaybackSourceRole.AudioPlayHelper => "PlayHelper",
                NativePlaybackSourceRole.AudioPlayDelayed or NativePlaybackSourceRole.AudioPlay => "Play",
                NativePlaybackSourceRole.AudioStop or NativePlaybackSourceRole.AudioStopBoolean => "Stop",
                NativePlaybackSourceRole.AudioSetLoop => "set_loop",
                NativePlaybackSourceRole.SetClip => "set_clip",
                NativePlaybackSourceRole.PlayerConstructor => ".ctor",
                NativePlaybackSourceRole.SetTime => "set_time",
                NativePlaybackSourceRole.ProgressMusicPlayer => "set_MusicPlayer",
                NativePlaybackSourceRole.ProgressClip => "set_Clip",
                NativePlaybackSourceRole.ProgressPlay => "Play",
                NativePlaybackSourceRole.ProgressStartMoveNext => "MoveNext",
                _ => throw new UnsupportedSignatureException("Unknown stock source metadata name.")
            };
            var expectedName = Encoding.ASCII.GetBytes(name + "\0");
            if (!Capture(value.Name, expectedName.Length).Bytes.AsSpan().SequenceEqual(expectedName))
                throw new UnsupportedSignatureException("A stock source metadata name differs: " + Role);
            VerifyType(Capture(value.ReturnType, 16).Bytes, Kind == NativePlaybackSourceKind.BooleanReturn0 ? 2u : 1u);
            if (parameterCount != 0)
            {
                var parameters = Capture(value.Parameters, parameterCount * IntPtr.Size).Bytes;
                if (isStatic) VerifyType(Capture((IntPtr)BitConverter.ToInt64(parameters, 0), 16).Bytes, 0x12);
                var type = Kind switch
                {
                    NativePlaybackSourceKind.Single1 => 0x0cu, NativePlaybackSourceKind.Double1 => 0x0du,
                    NativePlaybackSourceKind.Reference1 => Role == NativePlaybackSourceRole.ProgressClip ? 0x15u : 0x12u, NativePlaybackSourceKind.Boolean1 => 2u,
                    NativePlaybackSourceKind.UInt64_1 => 0x0bu,
                    _ => throw new UnsupportedSignatureException("Unknown source parameter metadata type.")
                };
                VerifyType(Capture((IntPtr)BitConverter.ToInt64(parameters, isStatic ? 8 : 0), 16).Bytes, type);
            }
            NativePlaybackExecutableSeal.RangeSeal Capture(IntPtr address, int length)
            {
                HookTargetValidation.VerifyReadable(address, (nuint)length);
                var seal = NativePlaybackExecutableSeal.RangeSeal.Capture(address, length, IntPtr.Zero, false);
                _metadata.Add(seal); return seal;
            }
        }
        private static void VerifyType(byte[] bytes, uint expected)
        {
            var bits = BitConverter.ToUInt32(bytes, 8);
            if (((bits >> 16) & 0xff) != expected || (bits & 0x40000000) != 0)
                throw new UnsupportedSignatureException("A stock source metadata primitive/reference or byref shape differs.");
        }
        internal void PrepareInstall()
        {
            _group.RequireNoInstallationAudit();
            if (IsNativePitchPairMember)
                throw new UnsupportedSignatureException("A fixed native pitch member must adopt its actual pair's original, never prepare a second detour.");
            ReserveChannel();
            _group.VerifySourcePreparation(this);
            var actualOriginal = _engine.Prepare(_target, _bridge!.EntryAddress);
            _group.VerifySourcePreparation(this);
            if (actualOriginal == IntPtr.Zero || actualOriginal == TargetAddress || actualOriginal == _bridge.EntryAddress)
                throw new UnsupportedSignatureException("Dobby did not return a distinct original source relocation: " + Role);
            _proof = NativePlaybackSourceTrampolineProof.Create(TargetAddress, Role, _originalBody, actualOriginal, MemberModule,
                Role == NativePlaybackSourceRole.ProgressClip ? _targets.ProgressClipTail : _targets.ProgressMusicPlayerTail);
            _bridge.ConfigureOriginal(_proof.OriginalAddress);
            VerifyMemberEvidence();
        }
        internal void ReserveNativePairChannel()
        {
            _group.RequireNoInstallationAudit();
            if (!IsNativePitchPairMember) throw new UnsupportedSignatureException("A metadata source cannot reserve a fixed native pair channel.");
            ReserveChannel();
        }
        internal void AdoptNativePairMember(NativePlaybackEnginePitchPatchPair.MemberReceipt member)
        {
            _group.RequireNoInstallationAudit();
            if (!IsNativePitchPairMember || _bridge is null || _bridge.IsConfigured || _proof is not null || _nativePairMember is not null)
                throw new UnsupportedSignatureException("Only the sole reserved native source channel may adopt its pair member once.");
            _nativePairMember = member;
            _proof = NativePlaybackSourceTrampolineProof.AdoptNativePair(this, member);
            _bridge.ConfigureOriginal(_proof.OriginalAddress);
            VerifyMemberEvidence();
        }
        private void ReserveChannel()
        {
            if (_bridge is not null) throw new UnsupportedSignatureException("A retained physical source channel cannot be reserved twice.");
            _bridge = NativePlaybackStockSourceBridge.Reserve(_hub, Kind, Role, TargetAddress, observation =>
            {
                if (!_group.IsActive) { observation.DisposeOperation(); return; }
                try
                {
                    if (Role is not (NativePlaybackSourceRole.PlayerConstructor or NativePlaybackSourceRole.ProgressClip)
                        && observation.IncomingMethod != IntPtr.Zero && observation.IncomingMethod != _target.MethodInfo
                        && (Role != NativePlaybackSourceRole.SetPitch || observation.IncomingMethod != _targets.PitchStatic.MethodInfo))
                        throw new UnsupportedSignatureException("The actual source call used an unknown metadata declaration: " + Role);
                    if (Role is NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch or NativePlaybackSourceRole.LowerSetTime)
                    {
                        if (observation.IncomingMethod != IntPtr.Zero)
                            throw new UnsupportedSignatureException("A native lower entry has no MethodInfo parameter.");
                        if (observation.IsExactLowerPitchChild) _targets.LowerPitch!.RequireOperationResolvedChildCache();
                        if (observation.IsExactLowerTimeChild) _targets.LowerTime!.RequireOperationResolvedWrapperCache();
                        if (Role == NativePlaybackSourceRole.EngineSetPitch) observation.VerifyEngineAssociationCurrent();
                    }
                    _group.VerifySourceOperationChannel(this);
                    if (_before is null) observation.DisposeOperation(); else _before(observation);
                    if (_group.IsActive) _group.VerifySourceOperationChannel(this);
                }
                catch (Exception error) { _group.Fault("A stock source Before observer failed: " + Role + ": " + error.Message); throw; }
            }, completion =>
            {
                try
                {
                    if (!_group.IsActive) return;
                    _group.VerifySourceOperationChannel(this); _after?.Invoke(completion);
                    if (_group.IsActive) _group.VerifySourceOperationChannel(this);
                }
                catch (Exception error) { _group.Fault("A stock source return observer failed: " + Role + ": " + error.Message); throw; }
                finally
                {
                    if (completion.Abnormal || !completion.Returned)
                        _group.Fault("An actual stock source original did not return normally: " + Role);
                }
            });
            VerifyMemberEvidence();
        }
        internal void CommitInstall()
        {
            _group.RequireNoInstallationAudit();
            if (_nativePairMember is null && IsNativePitchPairMember)
                throw new UnsupportedSignatureException("A native pitch source lacks its actual prepared pair member.");
            _group.VerifySourceBeforeCommit(this);
            if (_nativePairMember is not null)
            {
                _nativePairMember.Commit(this);
                return;
            }
            var protection = _source.StartProtection;
            var span = _proof!.RelocatedByteCount;
            try { _engine.Commit(_target); }
            finally
            {
                if (!VirtualProtect(TargetAddress, (nuint)span, protection, out _)
                    || !FlushInstructionCache((IntPtr)(-1), TargetAddress, (nuint)span))
                    throw new UnsupportedSignatureException("A stock source commit did not restore and flush its exact source protection: " + Role);
            }
            var actual = NativePlaybackExecutableSeal.RangeSeal.Capture(TargetAddress, _originalBody.Length, MemberModule, true, true);
            if (actual.Bytes[0] == 0xe9)
            {
                PatchedByteCount = 5; var jump = (IntPtr)checked(TargetAddress.ToInt64() + 5 + BitConverter.ToInt32(actual.Bytes, 1));
                if (jump != EntryAddress)
                {
                    _relay = NativePlaybackExecutableSeal.RangeSeal.Capture(jump, 14, IntPtr.Zero, false, true);
                    if (!AbsoluteJump(_relay.Bytes, out var destination) || destination != EntryAddress)
                        throw new UnsupportedSignatureException("A stock source Dobby relay does not reach its exact native entry: " + Role);
                }
            }
            else if (actual.Bytes[0] == 0xff && actual.Bytes[1] == 0x25 && BitConverter.ToInt32(actual.Bytes, 2) != 0)
            {
                PatchedByteCount = 6; var slot = (IntPtr)checked(TargetAddress.ToInt64() + 6 + BitConverter.ToInt32(actual.Bytes, 2));
                _relay = NativePlaybackExecutableSeal.RangeSeal.Capture(slot, 8, IntPtr.Zero, false);
                if ((IntPtr)BitConverter.ToInt64(_relay.Bytes, 0) != EntryAddress)
                    throw new UnsupportedSignatureException("A stock source Dobby slot does not name its exact native entry: " + Role);
            }
            else throw new UnsupportedSignatureException("A stock source detour has an unsupported exact form: " + Role);
            if (PatchedByteCount > span || !actual.Bytes.AsSpan(PatchedByteCount).SequenceEqual(_originalBody.AsSpan(PatchedByteCount)))
                throw new UnsupportedSignatureException("Dobby modified a stock source outside its exact installed patch: " + Role);
            _source = actual; Volatile.Write(ref _committed, 1);
        }
        internal void VerifyMemberEvidence()
        {
            _targets.VerifyCurrent();
            VerifyLocalMemberEvidence(null);
        }
        internal void VerifyUnconfiguredPreparationEvidence(PlaybackSourceInstallGroup group,
            PlaybackSourceInstallGroup.VerificationTraversal traversal)
        {
            if (!traversal.IsInstallation || IsNativePitchPairMember || IsCommitted || _proof is not null
                || _bridge is not { IsConfigured: false } channel || channel.OriginalAddress != IntPtr.Zero)
                throw new UnsupportedSignatureException("Source preparation can share only the reserved entry's unconfigured member evidence.");
            VerifyMemberEvidenceForGroup(group, traversal);
        }
        internal void VerifyMemberEvidenceForGroup(PlaybackSourceInstallGroup group)
            => VerifyMemberEvidenceForGroup(group, null);
        internal void VerifyMemberEvidenceForGroup(PlaybackSourceInstallGroup group,
            PlaybackSourceInstallGroup.VerificationTraversal? traversal)
        {
            VerifyGroupMemberIssuer(group, traversal);
            VerifyLocalMemberEvidence(traversal);
        }
        private void VerifyGroupMemberIssuer(PlaybackSourceInstallGroup group,
            PlaybackSourceInstallGroup.VerificationTraversal? traversal)
        {
            // The aggregate verifies the common target receipt immediately
            // before traversing members. A member must not recursively verify
            // its sibling image again: that turns each full-image check into
            // another complete traversal for every source and section.
            if (!ReferenceEquals(group, _group))
                throw new UnsupportedSignatureException("A source member cannot borrow another group's shared target validation.");
            if (traversal is not null)
            {
                traversal.VerifyIssuer(group);
                traversal.VerifySupportingIssuer(_targets);
            }
            group.VerifySealIssuer(MemberModule);
        }
        // The batch is local to one locked VerifyCurrentBody. It never crosses
        // a source getter, original call, Read, or a completion pump.
        internal NativePlaybackStockSourceBridge? BeginMemberEvidenceForGroupBatch(
            PlaybackSourceInstallGroup group, PlaybackSourceInstallGroup.VerificationTraversal traversal)
        {
            ArgumentNullException.ThrowIfNull(traversal);
            VerifyGroupMemberIssuer(group, traversal);
            return VerifyLocalMemberBeforeReservation(traversal);
        }
        internal NativePlaybackStockSourceBridge? CompleteMemberEvidenceForGroupBatch(
            PlaybackSourceInstallGroup group, PlaybackSourceInstallGroup.VerificationTraversal traversal,
            NativePlaybackStockSourceBridge? reservation)
        {
            ArgumentNullException.ThrowIfNull(traversal);
            VerifyGroupMemberIssuer(group, traversal);
            return VerifyLocalMemberAfterReservation(traversal, reservation);
        }
        private void VerifyLocalMemberEvidence(PlaybackSourceInstallGroup.VerificationTraversal? traversal)
        {
            var reservation = VerifyLocalMemberBeforeReservation(traversal);
            if (reservation is null) return;
            if (traversal?.IsInstallation == true)
            {
                _hub.VerifyInstallationMemberReservation(traversal, reservation);
                var configured = VerifyLocalMemberAfterReservation(traversal, reservation);
                if (configured is not null) _hub.VerifyInstallationMemberEvidence(traversal, configured);
                return;
            }
            reservation.VerifyMemberReservation();
            VerifyLocalMemberAfterReservation(traversal, reservation)?.VerifyMemberEvidence();
        }
        private NativePlaybackStockSourceBridge? VerifyLocalMemberBeforeReservation(
            PlaybackSourceInstallGroup.VerificationTraversal? traversal)
        {
            if (!OwnsPlaybackSourceInstallation(this)) throw new UnsupportedSignatureException("A stock source lacks its actual registered private owner.");
            if (traversal is null) _dobby.Verify();
            else traversal.VerifySharedDobbySeal(_group, _engine, _dobby);
            VerifyLocalPhysicalEvidence(traversal);
            return _bridge;
        }
        internal void VerifyRetiredMemberEvidence(PlaybackSourceInstallGroup group)
        {
            VerifyGroupMemberIssuer(group, null);
            group.VerifyRetiredActivityContext();
            var channel = VerifyLocalMemberBeforeReservation(null);
            if (!IsCommitted || channel is not { IsConfigured: true } || _proof is not { } proof
                || !ReferenceEquals(channel.Hub, _hub) || channel.Role != Role || channel.Kind != Kind
                || channel.TargetAddress != TargetAddress || channel.OriginalAddress != proof.OriginalAddress)
                throw new UnsupportedSignatureException("The withdrawn source lost its retained committed original.");
            proof.Verify();
            _relay?.Verify();
            _hub.VerifyRetiredMemberEvidence(group, channel);
        }
        private void VerifyLocalPhysicalEvidence(PlaybackSourceInstallGroup.VerificationTraversal? traversal)
        {
            if (_nativePairMember is null) _source.Verify(); else _nativePairMember.Verify(traversal);
            _sourceFunctions?.Verify(); _sourceLeaf?.Verify(); _resourceLeaf?.Verify();
            foreach (var item in _metadata) item.Verify();
        }
        internal void VerifyRateWriterChannel(PlaybackSourceInstallGroup group, NativePlaybackStockSourceHub hub)
        {
            if (Role != NativePlaybackSourceRole.SetPitch)
                throw new UnsupportedSignatureException("The running pitch operation lost its exact committed writer channel.");
            VerifyOperationChannel(group, hub);
        }
        internal void VerifyOperationChannel(PlaybackSourceInstallGroup group, NativePlaybackStockSourceHub hub, bool requireArmed = true)
        {
            VerifyGroupMemberIssuer(group, null);
            if (!OwnsPlaybackSourceInstallation(this) || !IsCommitted || _proof is not { } proof
                || _bridge is not { } channel || !ReferenceEquals(_hub, hub) || !ReferenceEquals(channel.Hub, hub)
                || channel.Role != Role || channel.Kind != Kind || channel.TargetAddress != TargetAddress
                || channel.OriginalAddress != proof.OriginalAddress || !channel.IsConfigured || !group.OwnsOperationEngine(_engine))
                throw new UnsupportedSignatureException("The running source operation lost its exact committed member and native original.");
            if (_nativePairMember is null) _source.Verify();
            _sourceFunctions?.Verify(); _sourceLeaf?.Verify(); _resourceLeaf?.Verify();
            foreach (var item in _metadata) item.Verify();
            proof.VerifyOperation(this); _relay?.Verify();
            // A current channel read cannot pump old completion callbacks.
            hub.VerifyOperationMemberEvidence(channel, requireArmed);
        }
        private NativePlaybackStockSourceBridge? VerifyLocalMemberAfterReservation(
            PlaybackSourceInstallGroup.VerificationTraversal? traversal, NativePlaybackStockSourceBridge? reservation)
        {
            if (!ReferenceEquals(_bridge, reservation))
                throw new UnsupportedSignatureException("A source member cannot finish another channel's reservation.");
            if (reservation is null) return null;
            _proof?.Verify(traversal);
            if (!reservation.IsConfigured) return null;
            if (_proof is null) throw new UnsupportedSignatureException("A configured source channel has no actual original proof.");
            if (traversal?.IsInstallation != true)
            { _group.RequireNoInstallationAudit(); reservation.PumpCompleted(); }
            _relay?.Verify();
            return reservation;
        }
        internal void ComposeIntoPinnedFile(PlaybackSourceInstallGroup group, uint sectionRva, byte[] expected)
            => ComposeIntoPinnedFile(group, sectionRva, expected, null);
        internal void ComposeIntoPinnedFile(PlaybackSourceInstallGroup group, uint sectionRva, byte[] expected,
            PlaybackSourceInstallGroup.VerificationTraversal? traversal, bool retired = false)
        {
            if (retired) VerifyRetiredMemberEvidence(group);
            else VerifyMemberEvidenceForGroup(group, traversal);
            if (_nativePairMember is not null)
            { _nativePairMember.Compose(this, sectionRva, expected, traversal); return; }
            var relative = TargetAddress.ToInt64() - MemberModule.ToInt64() - sectionRva;
            if (relative < 0 || relative >= expected.Length) return;
            if (_originalBody.Length > expected.Length - relative
                || !expected.AsSpan(checked((int)relative), _originalBody.Length).SequenceEqual(_originalBody))
                throw new UnsupportedSignatureException("A stock source owner's complete original differs from the pinned file plus DIR64: " + Role);
            _source.Bytes.CopyTo(expected, checked((int)relative));
        }
        internal void PumpCompleted() { _group.RequireNoInstallationAudit(); _bridge?.PumpCompleted(); }
        private static bool AbsoluteJump(byte[] bytes, out IntPtr destination)
        {
            destination = IntPtr.Zero;
            if (bytes.Length < 14 || bytes[0] != 0xff || bytes[1] != 0x25 || BitConverter.ToInt32(bytes, 2) != 0) return false;
            destination = (IntPtr)BitConverter.ToInt64(bytes, 6); return destination != IntPtr.Zero;
        }
        [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint length, uint protection, out uint old);
        [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint length);
    }
}
