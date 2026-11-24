using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackLowerPitchTarget
{
    internal LowerTimePreflight PrepareLowerTime() => LowerTimePreflight.Create(this);

    /// <summary>Closed stock/owned lower-time preflight for the third fixed
    /// native member. Installation and source authority require their actual
    /// retained owners; markers and caller data cannot select another profile.</summary>
    internal sealed class LowerTimePreflight
    {
        private const int WrapperPinLength = 80;
        private readonly uint _bodyRva, _wrapperRva;
        private readonly string _bodyHash;
        private readonly NativePlaybackLowerPitchTarget _lower;
        private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal _functions, _wrapperFunctions;
        private readonly NativePlaybackExecutableSeal.RangeSeal _cache, _wrapperTail;
        private readonly List<NativePlaybackExecutableSeal.RangeSeal> _metadata = new();
        private readonly byte[] _wrapperOriginal;
        internal NativePlaybackLowerPitchTarget Parent => _lower;
        internal IntPtr Module => _lower.Module;
        internal IntPtr Address { get; }
        internal IntPtr WrapperAddress { get; }
        internal IntPtr CacheAddress { get; }
        internal int BodyLength => 305;
        internal string BodySha256 => _bodyHash;
        internal VerifiedHookTarget DetourIdentity { get; }

        internal static LowerTimePreflight Create(NativePlaybackLowerPitchTarget lower)
        {
            ArgumentNullException.ThrowIfNull(lower);
            lower.VerifyCurrent();
            return new(lower);
        }

        private LowerTimePreflight(NativePlaybackLowerPitchTarget lower)
        {
            var owned = lower.OwnedFixture;
            if (owned && !lower.OwnedLowerTimeProfile)
                throw new UnsupportedSignatureException("The fixed legacy owned pitch profile has no lower-time member.");
            if (lower.ImageSha256 != (owned ? OwnedLowerImageSha256 : UnityPlayerSha256)
                || lower.WrapperImageSha256 != (owned ? OwnedWrapperImageSha256 : PlaybackSourceGroupTargets.StockImageSha256))
                throw new UnsupportedSignatureException("The fixed lower-time profile requires its exact private two-image receipt.");
            _lower = lower;
            _bodyRva = owned ? (uint)OwnedPlaybackFixtureProfile.LowerTimeRva : 0x1d9210u;
            _wrapperRva = owned ? (uint)OwnedPlaybackFixtureProfile.WrapperTimeRva : 0x1511c00u;
            _bodyHash = owned ? OwnedPlaybackFixtureProfile.LowerTimeBodySha256
                : "7e7788748f6dab5e9ea29cc2a8f457e7f4de3834082c21e9c8c41ba88fb3cb2a";
            var cacheRva = owned ? OwnedPlaybackFixtureProfile.TimeCacheRva : 0x1fb0688u;
            var wrapperLength = owned ? 80 : 67;
            Address = IntPtr.Add(Module, checked((int)_bodyRva));
            WrapperAddress = IntPtr.Add(lower.WrapperModule, checked((int)_wrapperRva));
            CacheAddress = IntPtr.Add(lower.WrapperModule, checked((int)cacheRva));
            HookTargetValidation.VerifyExecutable(Address, Module);
            HookTargetValidation.VerifyExecutable(WrapperAddress, lower.WrapperModule);
            DetourIdentity = new("private.native.lower-set-time", Address, IntPtr.Zero,
                IntPtr.Zero, ExactHookAbi.PlaybackIcallSingle1Void);
            var body = lower._lowerFile.CanonicalBytes(_bodyRva, BodyLength);
            if (Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant() != _bodyHash
                || !body.AsSpan(0, 5).SequenceEqual(new byte[] { 0x48, 0x89, 0x5c, 0x24, 0x10 }))
                throw new UnsupportedSignatureException("The complete fixed lower-time body or first MOV RBX prefix differs.");
            RequireFunction(lower._lowerFile, Module, _bodyRva, BodyLength, owned ? OwnedPlaybackFixtureProfile.LowerTimeUnwindRva : 0x1ac9dacu,
                Convert.FromHexString(owned ? OwnedPlaybackFixtureProfile.LowerTimeUnwindHex : "01150600156806000d3411000dd20650"));
            _functions = new(Address, BodyLength, 6, NativePlaybackSourceRole.LowerSetTime);

            var functionSlotRva = owned ? OwnedPlaybackFixtureProfile.TimeFunctionSlotRva : 0x1935ea0u;
            var nameSlotRva = owned ? OwnedPlaybackFixtureProfile.TimeNameSlotRva : 0x1936810u;
            var nameRva = owned ? OwnedPlaybackFixtureProfile.TimeRegisteredNameRva : 0x1932678u;
            if (!lower._lowerFile.HasDir64(functionSlotRva) || !lower._lowerFile.HasDir64(nameSlotRva))
                throw new UnsupportedSignatureException("The lower-time registration slots lack their original DIR64 records.");
            var functionSlot = CaptureData(lower._lowerFile, functionSlotRva, 8);
            var nameSlot = CaptureData(lower._lowerFile, nameSlotRva, 8);
            var registeredName = IntPtr.Add(Module, checked((int)nameRva));
            var registeredText = Encoding.ASCII.GetBytes("UnityEngine.AudioSource::set_time\0");
            if ((IntPtr)BitConverter.ToInt64(functionSlot.Bytes, 0) != Address
                || (IntPtr)BitConverter.ToInt64(nameSlot.Bytes, 0) != registeredName
                || !CaptureData(lower._lowerFile, nameRva, registeredText.Length).Bytes.AsSpan().SequenceEqual(registeredText))
                throw new UnsupportedSignatureException("The fixed registration index 50 does not bind the exact lower-time entry and name.");

            if (owned)
            {
                RequireExport(Module, "StockLowerSetTime", _bodyRva);
                RequireExport(Module, "StockLowerSetTimeEnd", _bodyRva + (uint)BodyLength);
                RequireExport(Module, "StockLowerTimeFunctions", OwnedPlaybackFixtureProfile.TimeFunctionTableRva);
                RequireExport(Module, "StockLowerTimeNames", OwnedPlaybackFixtureProfile.TimeNameTableRva);
                RequireExport(Module, "StockLowerTimeRegisteredName", nameRva);
                RequireExport(lower.WrapperModule, "StockSetTime", _wrapperRva);
                RequireExport(lower.WrapperModule, "StockSetTimeEnd", _wrapperRva + WrapperPinLength);
                RequireExport(lower.WrapperModule, "StockLowerTimeCache", cacheRva);
                Marker(lower._lowerFile, Module, "StockLowerTimeProfileMarker", "owned-native-lower-time-icall-v1");
                Marker(lower._wrapperFile, lower.WrapperModule, "StockLowerTimeWrapperMarker", "owned-native-lower-time-wrapper-v1");
                var countRva = checked((uint)(NativeLibrary.GetExport(Module, "StockLowerTimeRegistrationCount").ToInt64() - Module.ToInt64()));
                if (BitConverter.ToUInt32(CaptureData(lower._lowerFile, countRva, 4).Bytes, 0) != 51)
                    throw new UnsupportedSignatureException("The fixed owned lower-time registration count differs.");
                // This exact fixture reserves the first fifty entries as zero.
                // Capture both complete arrays, including the sole DIR64 slots.
                var functions = CaptureData(lower._lowerFile, OwnedPlaybackFixtureProfile.TimeFunctionTableRva, 51 * 8);
                var names = CaptureData(lower._lowerFile, OwnedPlaybackFixtureProfile.TimeNameTableRva, 51 * 8);
                if (functions.Bytes.Take(50 * 8).Any(value => value != 0)
                    || names.Bytes.Take(50 * 8).Any(value => value != 0))
                    throw new UnsupportedSignatureException("The complete owned lower-time reserved registration slots changed.");
            }

            _wrapperOriginal = lower._wrapperFile.CanonicalBytes(_wrapperRva, WrapperPinLength);
            VerifyWrapperPin(_wrapperOriginal, owned);
            if (owned && (!_wrapperOriginal.AsSpan(16, 3).SequenceEqual(new byte[] { 0x48, 0x8b, 0x05 })
                || checked((long)_wrapperRva + 23 + BitConverter.ToInt32(_wrapperOriginal, 19)) != cacheRva))
                throw new UnsupportedSignatureException("The fixed owned wrapper does not load its exact zero-fill lower-time cache.");
            NativePlaybackSourcePrologue.ForRole(NativePlaybackSourceRole.SetTime).RequirePrefix(_wrapperOriginal);
            // The stock SetTime source owner owns its fixed six-byte entry
            // projection. Supporting evidence retains its complete remaining
            // wrapper, including cache accesses, resolver string and tail JMP.
            _wrapperTail = lower._wrapperFile.CaptureCanonical(_wrapperRva + 6, WrapperPinLength - 6, executable: true);
            _wrapperFunctions = new(WrapperAddress, wrapperLength, 6, NativePlaybackSourceRole.SetTime);
            RequireFunction(lower._wrapperFile, lower.WrapperModule, _wrapperRva, wrapperLength, owned ? OwnedPlaybackFixtureProfile.WrapperTimeUnwindRva : 0x1ce57a4u,
                Convert.FromHexString(owned ? OwnedPlaybackFixtureProfile.WrapperTimeUnwindHex : "011504001568020006520230"));
            if (!owned)
            {
                var resolverText = Encoding.ASCII.GetBytes("UnityEngine.AudioSource::set_time(System.Single)\0");
                if (!CaptureData(lower._wrapperFile, 0x18551b8, resolverText.Length).Bytes.AsSpan().SequenceEqual(resolverText))
                    throw new UnsupportedSignatureException("The fixed wrapper resolver string changed.");
            }
            lower._wrapperFile.RequireZeroFill(cacheRva, IntPtr.Size);
            _cache = NativePlaybackExecutableSeal.RangeSeal.Capture(CacheAddress, IntPtr.Size, lower.WrapperModule, true);
            if ((_cache.StartProtection & 0xff) is not (4 or 8))
                throw new UnsupportedSignatureException("The fixed lower-time resolver cache is not writable image data.");
            VerifyUninstalled();
        }

        internal static void VerifyWrapperPin(ReadOnlySpan<byte> wrapper, bool owned)
        {
            var hash = owned ? OwnedPlaybackFixtureProfile.WrapperTimeBodySha256
                : "af9ed985ff99f5f8b1c58c0ca92ea565a2de6cac11381c9147dd8cccc4763b63";
            if (wrapper.Length != WrapperPinLength
                || !Convert.ToHexString(SHA256.HashData(wrapper)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The complete fixed lower-time wrapper pin differs.");
            // The retained stock evidence has REX.W JMP RAX at +0x40, ending
            // at RF +0x43. The owned wrapper has its two-byte JMP at +28.
            var tail = owned ? new byte[] { 0xff, 0xe0 } : new byte[] { 0x48, 0xff, 0xe0 };
            if (!wrapper.Slice(owned ? 28 : 0x40, tail.Length).SequenceEqual(tail))
                throw new UnsupportedSignatureException("The fixed lower-time wrapper tail JMP differs.");
        }

        internal void VerifyUninstalled()
        {
            _lower.VerifyCurrent();
            VerifySupportingEvidence();
            if (Convert.ToHexString(SHA256.HashData(NativePlaybackExecutableSeal.RangeSeal.Read(Address, BodyLength))).ToLowerInvariant() != _bodyHash
                || !NativePlaybackExecutableSeal.RangeSeal.Read(WrapperAddress, WrapperPinLength).AsSpan().SequenceEqual(_wrapperOriginal))
                throw new UnsupportedSignatureException("The lower-time preflight requires its complete still-uninstalled native and wrapper bodies.");
        }

        internal void VerifySupportingEvidence()
            => VerifySupportingEvidence(null);
        internal void VerifySupportingEvidence(ExactHookBroker.PlaybackSourceInstallGroup.VerificationTraversal? traversal,
            PlaybackSourceGroupTargets.TargetFamilyVerification? family = null)
        {
            if (family is not null)
            {
                if (traversal is not null)
                    throw new UnsupportedSignatureException("TargetFamily ancestor sharing cannot borrow a group traversal.");
                family.VerifyLowerTime(this);
                _lower.VerifySupportingEvidence(null, family);
            }
            else if (traversal is null) _lower.VerifySupportingEvidence();
            else
            {
                traversal.VerifySupportingIssuer(this);
                traversal.VerifySupportingIssuer(_lower);
            }
            _functions.Verify(); _wrapperFunctions.Verify(); _wrapperTail.Verify();
            foreach (var range in _metadata) range.Verify();
            _cache.VerifyMemoryIdentity();
            var before = Marshal.ReadIntPtr(CacheAddress); var after = Marshal.ReadIntPtr(CacheAddress);
            if (before != after || before != IntPtr.Zero && before != Address)
                throw new UnsupportedSignatureException("The lower-time wrapper cache no longer names its exact sealed native entry.");
        }

        internal void RequireResolvedWrapperCache()
        {
            VerifySupportingEvidence();
            if (Marshal.ReadIntPtr(CacheAddress) != Address)
                throw new UnsupportedSignatureException("The actual lower-time child requires its naturally resolved exact wrapper cache.");
        }
        internal void VerifyOperationSupportingEvidence()
        {
            _lower.VerifyOperationSupportingEvidence();
            _functions.Verify(); _wrapperFunctions.Verify(); _wrapperTail.Verify();
            foreach (var range in _metadata) range.Verify();
            _cache.VerifyMemoryIdentity();
            var before = Marshal.ReadIntPtr(CacheAddress); var after = Marshal.ReadIntPtr(CacheAddress);
            if (before != after || before != IntPtr.Zero && before != Address)
                throw new UnsupportedSignatureException("The running lower-time cache no longer names its exact native entry.");
        }
        internal void RequireOperationResolvedWrapperCache()
        {
            VerifyOperationSupportingEvidence();
            if (Marshal.ReadIntPtr(CacheAddress) != Address)
                throw new UnsupportedSignatureException("The running lower-time child requires its naturally resolved exact wrapper cache.");
        }

        private NativePlaybackExecutableSeal.RangeSeal CaptureData(PinnedImageFile file, uint rva, int length)
        {
            var seal = file.CaptureCanonical(rva, length);
            if (seal.StartProtection != 2)
                throw new UnsupportedSignatureException("The fixed lower-time metadata must retain its read-only image identity.");
            _metadata.Add(seal); return seal;
        }

        private static void RequireExport(IntPtr module, string name, uint rva)
        {
            if (NativeLibrary.GetExport(module, name) != IntPtr.Add(module, checked((int)rva)))
                throw new UnsupportedSignatureException("A fixed owned lower-time export changed its exact RVA.");
        }

        private void Marker(PinnedImageFile file, IntPtr module, string name, string text)
        {
            var rva = checked((uint)(NativeLibrary.GetExport(module, name).ToInt64() - module.ToInt64()));
            var expected = Encoding.ASCII.GetBytes(text + "\0");
            if (!CaptureData(file, rva, expected.Length).Bytes.AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("A fixed owned lower-time marker differs from its pinned two-image identity.");
        }

        private void RequireFunction(PinnedImageFile file, IntPtr module, uint begin, int length, uint unwind, byte[] program)
        {
            var start = IntPtr.Add(module, checked((int)begin));
            var function = RtlLookupFunctionEntry(unchecked((ulong)start.ToInt64()), out var image, IntPtr.Zero);
            if (function == IntPtr.Zero || image != unchecked((ulong)module.ToInt64()))
                throw new UnsupportedSignatureException("The fixed lower-time runtime function lost its pinned image owner.");
            var rf = file.CaptureCanonical(checked((uint)(function.ToInt64() - module.ToInt64())), 12);
            if (BitConverter.ToUInt32(rf.Bytes, 0) != begin || BitConverter.ToUInt32(rf.Bytes, 4) != begin + length
                || BitConverter.ToUInt32(rf.Bytes, 8) != unwind)
                throw new UnsupportedSignatureException("The fixed lower-time runtime-function extent or unwind RVA changed.");
            var uw = file.CaptureCanonical(unwind, program.Length);
            if (!uw.Bytes.AsSpan().SequenceEqual(program))
                throw new UnsupportedSignatureException("The complete fixed lower-time unwind program changed.");
            _metadata.Add(rf); _metadata.Add(uw);
        }
    }
}
