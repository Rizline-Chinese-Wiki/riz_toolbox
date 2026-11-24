using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackLowerPitchTarget
{
    /// <summary>Resolve only the closed native target below this actual lower
    /// receipt. This preflight does not install a hook, issue a source identity
    /// or grant mutation. Its uninstalled-body verifier deliberately refuses
    /// patched code; installation requires its separately retained fixed joint
    /// image owner.</summary>
    internal EnginePitchPreflight PrepareEnginePitch() => EnginePitchPreflight.Create(this);

    internal sealed class EnginePitchPreflight
    {
        private readonly NativePlaybackLowerPitchTarget _lower;
        private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal _functions;
        private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal _getterFunctions;
        private readonly List<NativePlaybackExecutableSeal.RangeSeal> _metadata = new();
        private readonly NativePlaybackExecutableSeal.RangeSeal _lowerCall, _getter;
        private readonly uint _bodyRva;
        private readonly string _bodyHash;
        internal IntPtr Module => _lower.Module;
        internal IntPtr Address { get; }
        internal IntPtr OriginalLowerReturn { get; }
        internal IntPtr GetterAddress { get; }
        internal ScheduledPitchPreflight? ScheduledPitch { get; }
        internal VerifiedHookTarget DetourIdentity { get; }
        internal int BodyLength => 640;
        internal string BodySha256 => _bodyHash;
        internal bool OwnedFixture => _lower.OwnedFixture;
        internal NativePlaybackLowerPitchTarget Parent => _lower;

        internal NativePlaybackEnginePitchPatchPair PreparePatchPair(DobbyDetourEngine engine)
            => NativePlaybackEnginePitchPatchPair.Create(this, engine);

        internal static EnginePitchPreflight Create(NativePlaybackLowerPitchTarget lower)
        {
            ArgumentNullException.ThrowIfNull(lower);
            lower.VerifyCurrent();
            return new(lower);
        }

        private EnginePitchPreflight(NativePlaybackLowerPitchTarget lower)
        {
            _lower = lower;
            // No caller supplies an RVA, function list, hash, native self or
            // completeness flag. The already fixed two-image receipt selects
            // exactly one independently reviewed stock/owned profile.
            _bodyRva = OwnedFixture ? lower.OwnedLowerTimeProfile ? (uint)OwnedPlaybackFixtureProfile.EnginePitchRva : 0x1380u : 0xbbad20u;
            Address = IntPtr.Add(Module, checked((int)_bodyRva));
            HookTargetValidation.VerifyExecutable(Address, Module);
            DetourIdentity = new("private.native.engine-set-pitch", Address, IntPtr.Zero,
                IntPtr.Zero, ExactHookAbi.PlaybackIcallSingle1Void);
            _bodyHash = OwnedFixture
                ? lower.OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.EnginePitchBodySha256 : "67a9452cd1c94cc993c544fb8c92e71d27c33ba1950be52d7c5fb2d809ff1a87"
                : "19f35cda95d1a1eb06e828a83d2ffbd04429441cd40730ca3367667c7eb10c37";
            var body = lower._lowerFile.CanonicalBytes(_bodyRva, BodyLength);
            if (Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant() != _bodyHash
                || !body.AsSpan(0, 7).SequenceEqual(new byte[] { 0x48, 0x8b, 0xc4, 0x48, 0x89, 0x58, 0x18 }))
                throw new UnsupportedSignatureException("The exact native-engine complete 640-byte body or +18 prologue differs.");
            if (OwnedFixture && (NativeLibrary.GetExport(Module, "StockEngineSetPitch") != Address
                || NativeLibrary.GetExport(Module, "StockEnginePitchPart2") != IntPtr.Add(Address, 341)
                || NativeLibrary.GetExport(Module, "StockEnginePitchPart3") != IntPtr.Add(Address, 614)
                || NativeLibrary.GetExport(Module, "StockEngineSetPitchEnd") != IntPtr.Add(Address, BodyLength)))
                throw new UnsupportedSignatureException("The fixed owned native-engine exports do not retain the complete three-range body.");

            var rootUw = OwnedFixture ? lower.OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.EngineRootUnwindRva : 0x3260u : 0x1b66e6cu;
            var part2Uw = OwnedFixture ? lower.OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.EnginePart2UnwindRva : 0x3278u : 0x1b66e84u;
            var part3Uw = OwnedFixture ? lower.OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.EnginePart3UnwindRva : 0x328cu : 0x1b66e98u;
            RequireFunction(_bodyRva, _bodyRva + 341, rootUw,
                Convert.FromHexString("011e09001e6809001774190017341800170114000c500000"));
            RequireFunction(_bodyRva + 341, _bodyRva + 614, part2Uw,
                Chain("2108020008641600", _bodyRva, _bodyRva + 341, rootUw));
            RequireFunction(_bodyRva + 614, _bodyRva + 640, part3Uw,
                Chain("21000000", _bodyRva, _bodyRva + 341, rootUw));
            _functions = new(Address, BodyLength, 7);

            var callRva = OwnedFixture ? lower.OwnedLowerTimeProfile ? (uint)OwnedPlaybackFixtureProfile.LowerEngineCallRva : 0x1671u : 0x1d78f9u;
            var expectedLower = OwnedFixture ? lower.OwnedLowerTimeProfile ? (uint)OwnedPlaybackFixtureProfile.LowerPitchRva : 0x1640u : 0x1d7740u;
            if (lower.Address != IntPtr.Add(Module, checked((int)expectedLower))
                || callRva < expectedLower || callRva + 5 > expectedLower + lower.BodyLength)
                throw new UnsupportedSignatureException("The native-engine parent is not the fixed complete lower function.");
            var call = lower._lowerFile.CanonicalBytes(callRva, 5);
            if (call[0] != 0xe8 || checked((long)callRva + 5 + BitConverter.ToInt32(call, 1)) != _bodyRva)
                throw new UnsupportedSignatureException("The exact lower CALL does not reach this privately derived native target.");
            _lowerCall = lower._lowerFile.CaptureCanonical(callRva, 5, executable: true);
            OriginalLowerReturn = IntPtr.Add(Module, checked((int)callRva + 5));
            if (OwnedFixture && NativeLibrary.GetExport(Module, "StockLowerEngineReturned") != OriginalLowerReturn)
                throw new UnsupportedSignatureException("The owned lower CALL return label changed.");

            // The owned get_pitch is in the wrapper image and dereferences its
            // actual cached native pointer. Stock's registered getter is in
            // UnityPlayer. Both fixed bodies read native +1F4, never a mirror.
            var getterModule = OwnedFixture ? lower.WrapperModule : Module;
            var getterFile = OwnedFixture ? lower._wrapperFile : lower._lowerFile;
            var getterRva = OwnedFixture ? lower.OwnedLowerTimeProfile ? (uint)OwnedPlaybackFixtureProfile.PitchGetterRva : 0x23c0u : 0x1d7620u;
            var getterLength = OwnedFixture ? 60 : 284;
            var getterHash = OwnedFixture
                ? lower.OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.PitchGetterBodySha256 : "da434e251bc28b762c32248d35b7acd6f9387eb1cd78b3a0e3545739f06be478"
                : "231fd81438f67d8ed32483d55a8d137a01fa9303a5f8b9a9a42362af63c2cfb6";
            GetterAddress = IntPtr.Add(getterModule, checked((int)getterRva));
            HookTargetValidation.VerifyExecutable(GetterAddress, getterModule);
            var getterBody = getterFile.CanonicalBytes(getterRva, getterLength);
            if (Convert.ToHexString(SHA256.HashData(getterBody)).ToLowerInvariant() != getterHash
                || OwnedFixture && NativeLibrary.GetExport(getterModule, "StockFixtureNativeGetPitch") != GetterAddress)
                throw new UnsupportedSignatureException("The exact native-field getter differs from its independent fixed fingerprint.");
            _getter = getterFile.CaptureCanonical(getterRva, getterLength, executable: true);
            _getterFunctions = new(GetterAddress, getterLength, 1);
            if (!OwnedFixture) ScheduledPitch = ScheduledPitchPreflight.CreateStock(this);
            VerifyUninstalled();
        }

        /// <summary>Only pre-installation evidence. There is deliberately no
        /// descriptor/publication/lease API on this intermediate issuer.</summary>
        internal void VerifyUninstalled()
        {
            _lower.VerifyCurrent();
            VerifySupportingEvidence();
            var actual = NativePlaybackExecutableSeal.RangeSeal.Read(Address, BodyLength);
            if (Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant() != _bodyHash)
                throw new UnsupportedSignatureException("The native-engine preflight requires its complete still-uninstalled original body.");
        }

        /// <summary>Fixed file, identity, CALL, getter and unwind evidence only.
        /// Installed entry bytes and the complete executable image remain the
        /// responsibility of the joint installation/broker owner. This deliberately
        /// does not issue a Current descriptor, source ticket or SDK grant.</summary>
        internal void VerifySupportingEvidence()
            => VerifySupportingEvidence(null);
        internal void VerifySupportingEvidence(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
            PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
        {
            if (family is not null)
            {
                if (traversal is not null)
                    throw new UnsupportedSignatureException("TargetFamily ancestor sharing cannot borrow a group traversal.");
                family.VerifyEngine(this);
                _lower.VerifySupportingEvidence(null, family);
            }
            else if (traversal is null) _lower.VerifySupportingEvidence();
            else
            {
                traversal.VerifySupportingIssuer(this);
                traversal.VerifySupportingIssuer(_lower);
            }
            _functions.Verify(); _getterFunctions.Verify(); _lowerCall.Verify(); _getter.Verify();
            foreach (var range in _metadata) range.Verify();
            ScheduledPitch?.VerifySupportingEvidence();
        }

        internal void VerifyOperationSupportingEvidence()
        {
            _lower.VerifyOperationSupportingEvidence();
            _functions.Verify(); _getterFunctions.Verify(); _lowerCall.Verify(); _getter.Verify();
            foreach (var range in _metadata) range.Verify();
            ScheduledPitch?.VerifyOperationSupportingEvidence();
        }

        private void RequireFunction(uint begin, uint end, uint unwind, byte[] program)
        {
            var start = IntPtr.Add(Module, checked((int)begin));
            var function = RtlLookupFunctionEntry(unchecked((ulong)start.ToInt64()), out var image, IntPtr.Zero);
            if (function == IntPtr.Zero || image != unchecked((ulong)Module.ToInt64()))
                throw new UnsupportedSignatureException("The exact native-engine RF is not owned by its pinned image.");
            var rva = checked((uint)(function.ToInt64() - Module.ToInt64()));
            var rf = _lower._lowerFile.CaptureCanonical(rva, 12);
            if (BitConverter.ToUInt32(rf.Bytes, 0) != begin || BitConverter.ToUInt32(rf.Bytes, 4) != end
                || BitConverter.ToUInt32(rf.Bytes, 8) != unwind)
                throw new UnsupportedSignatureException("The exact native-engine three-RF tuple changed.");
            var uw = _lower._lowerFile.CaptureCanonical(unwind, program.Length);
            if (!uw.Bytes.AsSpan().SequenceEqual(program))
                throw new UnsupportedSignatureException("The complete native-engine UW or explicit chain tuple changed.");
            _metadata.Add(rf); _metadata.Add(uw);
        }

        private static byte[] Chain(string local, uint begin, uint end, uint unwind)
        {
            var prefix = Convert.FromHexString(local);
            var bytes = new byte[prefix.Length + 12];
            prefix.CopyTo(bytes, 0);
            BitConverter.GetBytes(begin).CopyTo(bytes, prefix.Length);
            BitConverter.GetBytes(end).CopyTo(bytes, prefix.Length + 4);
            BitConverter.GetBytes(unwind).CopyTo(bytes, prefix.Length + 8);
            return bytes;
        }
    }
}
