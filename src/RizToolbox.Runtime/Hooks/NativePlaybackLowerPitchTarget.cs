using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

/// <summary>The one fixed two-argument native SetPitch icall. This private
/// issuer owns its two image identities, registration pair, complete native
/// body/RF/UW and mutable wrapper cache. It never creates a MethodInfo.</summary>
internal sealed partial class NativePlaybackLowerPitchTarget
{
    internal const string UnityPlayerSha256 = "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1";
    private const string LowerUnwindHex = "011e09001e680d001774210017341f0017011c000c500000";
    private readonly NativeRuntime.NativePlaybackStockTargets? _stock;
    internal NativeRuntime.NativePlaybackStockTargets? StockIssuer => _stock;
    private readonly PinnedImageFile _wrapperFile, _lowerFile;
    private readonly NativePlaybackExecutableSeal _image;
    private NativePlaybackEnginePitchPatchPair? _enginePitchPair;
    private readonly NativePlaybackExecutableSeal.RangeSeal _cache;
    private readonly List<NativePlaybackExecutableSeal.RangeSeal> _registration = new();
    private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal _functions;
    private readonly NativePlaybackSourceTrampolineProof.SourceFunctionSeal? _registrationFunction;
    internal IntPtr WrapperModule { get; }
    internal IntPtr Module { get; }
    internal IntPtr Address { get; }
    internal IntPtr CacheAddress { get; }
    internal bool OwnedFixture { get; }
    internal bool OwnedLowerTimeProfile { get; }
    internal string WrapperImageSha256 { get; }
    internal string ImageSha256 { get; }
    internal int BodyLength => 495;
    internal string BodySha256 { get; }
    // An address/ABI reservation projection only. The dedicated owner below
    // consumes this native receipt, never this projection as metadata evidence.
    internal VerifiedHookTarget DetourIdentity { get; }

    private NativePlaybackLowerPitchTarget(IntPtr wrapper, IntPtr lower, bool fixture,
        NativeRuntime.NativePlaybackStockTargets? stock, bool lowerTimeProfile = false)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || wrapper == IntPtr.Zero
            || lower == IntPtr.Zero || wrapper == lower || fixture == (stock is not null))
            throw new UnsupportedSignatureException("The fixed lower target requires its exact distinct, already loaded two-image issuer.");
        (WrapperModule, Module, OwnedFixture, _stock) = (wrapper, lower, fixture, stock);
        OwnedLowerTimeProfile = fixture && lowerTimeProfile;
        WrapperImageSha256 = fixture ? lowerTimeProfile ? OwnedWrapperImageSha256 : LegacyOwnedWrapperImageSha256 : PlaybackSourceGroupTargets.StockImageSha256;
        ImageSha256 = fixture ? lowerTimeProfile ? OwnedLowerImageSha256 : LegacyOwnedLowerImageSha256 : UnityPlayerSha256;
        _wrapperFile = new(wrapper, WrapperImageSha256); _lowerFile = new(lower, ImageSha256);
        if (OwnedLowerTimeProfile) OwnedPlaybackFixtureProfile.RequireExports(wrapper, lower);
        Address = fixture ? NativeLibrary.GetExport(lower, "StockLowerSetPitch") : IntPtr.Add(lower, 0x1d7740);
        CacheAddress = fixture ? NativeLibrary.GetExport(wrapper, "StockLowerPitchCache") : IntPtr.Add(wrapper, 0x1fb0650);
        HookTargetValidation.VerifyExecutable(Address, lower);
        if (fixture && (Address != IntPtr.Add(lower, OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.LowerPitchRva : 0x1640)
            || NativeLibrary.GetExport(lower, "StockLowerSetPitchEnd") != IntPtr.Add(Address, BodyLength)))
            throw new UnsupportedSignatureException("The exact owned lower body does not retain its complete 495-byte extent.");
        var rva = checked((uint)(Address.ToInt64() - lower.ToInt64()));
        var body = _lowerFile.CanonicalBytes(rva, BodyLength);
        if (!NativePlaybackExecutableSeal.RangeSeal.Read(Address, BodyLength).AsSpan().SequenceEqual(body)
            || !body.AsSpan(0, 7).SequenceEqual(new byte[] { 0x48, 0x8b, 0xc4, 0x48, 0x89, 0x58, 0x10 }))
            throw new UnsupportedSignatureException("The fixed lower native body differs from its exact file/DIR64/prologue profile.");
        BodySha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        if (OwnedLowerTimeProfile && BodySha256 != OwnedLowerBodySha256)
            throw new UnsupportedSignatureException("The owned lower native body differs from its independently fixed complete fingerprint.");
        if (!fixture && BodySha256 != "8925285b79a441c16d49ed0559a0c88ce1e6ecc5ab34a13c00b9933cfa43c17a")
            throw new UnsupportedSignatureException("The stock lower native body differs from its complete fixed 495-byte fingerprint.");
        _image = NativePlaybackExecutableSeal.Capture(lower, ImageSha256, Address, BodyLength);
        _cache = NativePlaybackExecutableSeal.RangeSeal.Capture(CacheAddress, IntPtr.Size, wrapper, true);
        if ((_cache.StartProtection & 0xff) is not (4 or 8))
            throw new UnsupportedSignatureException("The fixed resolver cache is not writable image data.");
        _wrapperFile.RequireZeroFill(fixture ? OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.PitchCacheRva : 0x9f18u : 0x1fb0650u, IntPtr.Size);
        if (fixture)
        {
            Marker(wrapper, "StockLowerPitchWrapperMarker", "owned-native-engine-pitch-wrapper-v2");
            Marker(lower, "StockLowerPitchProfileMarker", "owned-native-engine-pitch-icall-v1");
            var count = NativeLibrary.GetExport(lower, "StockLowerPitchRegistrationCount");
            var countSeal = CaptureData(count, 4, lower);
            if (BitConverter.ToUInt32(countSeal.Bytes, 0) != 1)
                throw new UnsupportedSignatureException("The fixed owned lower registration has exactly one entry.");
            RegistrationTables(NativeLibrary.GetExport(lower, "StockLowerPitchFunctions"),
                NativeLibrary.GetExport(lower, "StockLowerPitchNames"), 1, 0,
                NativeLibrary.GetExport(lower, "StockLowerPitchRegisteredName"));
        }
        else
        {
            RegistrationTables(IntPtr.Add(lower, 0x1935d10), IntPtr.Add(lower, 0x1936680), 302, 38,
                IntPtr.Add(lower, 0x1932470));
            var registrationBody = _lowerFile.FileBytes(0xfd9440, 173);
            if (Convert.ToHexString(SHA256.HashData(registrationBody)).ToLowerInvariant()
                    != "a26555ffa23993d583d1181aec388b1f8b76a225acaf92eb52b3a15a4c1188f1")
                throw new UnsupportedSignatureException("The fixed Audio registration implementation changed.");
            _registration.Add(_lowerFile.CaptureCanonical(0xfd9440, 173, executable: true));
            RequireFunction(IntPtr.Add(lower, 0xfd9440), 173, 0x1ae8e10,
                Convert.FromHexString("011e0c001e740b001e640a001e5409001e3408001e321af018e016c0"));
            _registrationFunction = new(IntPtr.Add(lower, 0xfd9440), 173, 1);
        }
        var expectedUnwind = fixture ? Convert.FromHexString(OwnedLowerUnwindHex) : Convert.FromHexString(LowerUnwindHex);
        RequireFunction(Address, BodyLength, OwnedLowerTimeProfile ? OwnedPlaybackFixtureProfile.LowerPitchUnwindRva : fixture ? null : 0x1ad6e84u, expectedUnwind);
        _functions = new(Address, BodyLength, 7, NativePlaybackSourceRole.LowerSetPitch);
        DetourIdentity = new("private.native.lower-set-pitch", Address, IntPtr.Zero, IntPtr.Zero, ExactHookAbi.PlaybackIcallSingle1Void);
        VerifyCurrent();

        void Marker(IntPtr module, string export, string text)
        {
            var expected = Encoding.ASCII.GetBytes(text + "\0");
            var seal = CaptureData(NativeLibrary.GetExport(module, export), expected.Length, module);
            if (!seal.Bytes.AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("The independently pinned lower fixture marker differs.");
        }
    }

    internal static NativePlaybackLowerPitchTarget ResolveStock(NativeRuntime.NativePlaybackStockTargets stock)
    {
        ArgumentNullException.ThrowIfNull(stock); stock.VerifyCurrent();
        var lower = GetModuleHandle("UnityPlayer.dll");
        if (lower == IntPtr.Zero || GetModuleHandle("GameAssembly.dll") != stock.Module)
            throw new UnsupportedSignatureException("Only naturally loaded exact GameAssembly and UnityPlayer images can issue the fixed lower target.");
        return new(stock.Module, lower, false, stock);
    }
    internal static NativePlaybackLowerPitchTarget ResolveOwnedFixture(IntPtr wrapper, IntPtr lower)
    {
        var wrapperHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(NativePlaybackExecutableSeal.ModulePath(wrapper)))).ToLowerInvariant();
        var lowerHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(NativePlaybackExecutableSeal.ModulePath(lower)))).ToLowerInvariant();
        if (wrapperHash == OwnedWrapperImageSha256 && lowerHash == OwnedLowerImageSha256)
            return new(wrapper, lower, true, null, true);
        if (wrapperHash == LegacyOwnedWrapperImageSha256 && lowerHash == LegacyOwnedLowerImageSha256)
            return new(wrapper, lower, true, null);
        throw new UnsupportedSignatureException("No closed owned two-image profile identifies this exact pair.");
    }

    internal void VerifyCurrent()
        => VerifyCurrent(null);
    internal void VerifyCurrent(PlaybackSourceGroupTargets.TargetFamilyVerification? family)
    {
        VerifySupportingEvidence(null, family);
        if (_enginePitchPair is null) _image.Verify(); else _enginePitchPair.VerifyInstallationEvidence(null, family);
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
            family.VerifyLower(this);
            return;
        }
        if (traversal is null) _stock?.VerifyCurrent();
        else
        {
            traversal.VerifySupportingIssuer(this);
            if (_stock is not null) traversal.VerifySupportingIssuer(_stock);
        }
        // Only this invocation's fixed parent receipt is shared. The lower
        // files, cache, registration and function evidence are always fresh.
        _wrapperFile.Verify(); _lowerFile.Verify();
        _cache.VerifyMemoryIdentity();
        _functions.Verify(); _registrationFunction?.Verify();
        foreach (var seal in _registration) seal.Verify();
        var before = Marshal.ReadIntPtr(CacheAddress); var after = Marshal.ReadIntPtr(CacheAddress);
        if (before != after || before != IntPtr.Zero && before != Address)
            throw new UnsupportedSignatureException("The naturally resolved wrapper cache is not this exact sealed lower target.");
    }
    internal void RetainEnginePitchPair(NativePlaybackEnginePitchPatchPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        if (_enginePitchPair is not null || !pair.IdentifiesParent(this))
            throw new UnsupportedSignatureException("The native pitch image handoff requires this exact issuer's sole retained fixed pair.");
        // The complete fixed-member image has already been compared with the
        // pinned file. This sole retained owner replaces the initial image view
        // before any additional native member can publish its actual patch.
        pair.VerifyInstallationEvidence(); _image.Verify();
        _enginePitchPair = pair;
        VerifyCurrent();
    }
    internal void RequireResolvedChildCache()
    {
        VerifyCurrent();
        // A zero cache may preflight/install. We never call its resolver. The
        // actual lower Before occurs only after the wrapper's real tail jump.
        if (Marshal.ReadIntPtr(CacheAddress) != Address)
            throw new UnsupportedSignatureException("The exact lower physical child has no naturally resolved current wrapper cache.");
        _cache.VerifyMemoryIdentity();
    }
    internal void VerifyOperationSupportingEvidence()
    {
        // The installed owner authenticates this exact receipt. Inspect only
        // current cache, registration and function evidence required by its
        // native child; complete two-image audits stay in VerifyCurrent.
        _cache.VerifyMemoryIdentity();
        _functions.Verify(); _registrationFunction?.Verify();
        foreach (var seal in _registration) seal.Verify();
        var before = Marshal.ReadIntPtr(CacheAddress); var after = Marshal.ReadIntPtr(CacheAddress);
        if (before != after || before != IntPtr.Zero && before != Address)
            throw new UnsupportedSignatureException("The running wrapper cache no longer names its exact lower target.");
    }
    internal void RequireOperationResolvedChildCache()
    {
        VerifyOperationSupportingEvidence();
        if (Marshal.ReadIntPtr(CacheAddress) != Address)
            throw new UnsupportedSignatureException("The actual running lower child requires its naturally resolved exact wrapper cache.");
    }

    private NativePlaybackExecutableSeal.RangeSeal CaptureData(IntPtr address, int length, IntPtr module)
    {
        var file = module == Module ? _lowerFile : module == WrapperModule ? _wrapperFile
            : throw new UnsupportedSignatureException("Unknown lower-profile member image.");
        var seal = file.CaptureCanonical(checked((uint)(address.ToInt64() - module.ToInt64())), length);
        if (seal.StartProtection != 2)
            throw new UnsupportedSignatureException("The exact lower profile requires immutable read-only image data.");
        _registration.Add(seal); return seal;
    }
    private void RegistrationTables(IntPtr functions, IntPtr names, int count, int index, IntPtr registeredName)
    {
        var functionRva = checked((uint)(functions.ToInt64() - Module.ToInt64()));
        var nameRva = checked((uint)(names.ToInt64() - Module.ToInt64()));
        if (!OwnedFixture && (Convert.ToHexString(SHA256.HashData(_lowerFile.FileBytes(functionRva, count * 8))).ToLowerInvariant()
                != "daffeb8d079feeab96d55c1f7fb8b822ad0ae62a58e040f6da5a62da875435f5"
            || Convert.ToHexString(SHA256.HashData(_lowerFile.FileBytes(nameRva, count * 8))).ToLowerInvariant()
                != "81479ff0f7b13ca395d63fac94457a9ae4077e8f092d9b387c25ea890a2971a0"))
            throw new UnsupportedSignatureException("The complete fixed Audio function/name registration arrays changed.");
        for (var at = 0; at < count; ++at)
            if (!_lowerFile.HasDir64(checked(functionRva + (uint)at * 8))
                || !_lowerFile.HasDir64(checked(nameRva + (uint)at * 8)))
                throw new UnsupportedSignatureException("Every fixed registration slot requires its own original DIR64 record.");
        var fn = CaptureData(functions, checked(count * 8), Module);
        var nm = CaptureData(names, checked(count * 8), Module);
        var text = Encoding.ASCII.GetBytes("UnityEngine.AudioSource::SetPitch\0");
        var registered = CaptureData(registeredName, text.Length, Module);
        if ((IntPtr)BitConverter.ToInt64(fn.Bytes, index * 8) != Address
            || (IntPtr)BitConverter.ToInt64(nm.Bytes, index * 8) != registeredName
            || !registered.Bytes.AsSpan().SequenceEqual(text))
            throw new UnsupportedSignatureException("The exact lower registration index does not bind this name and native-code entry.");
    }
    private void RequireFunction(IntPtr entry, int length, uint? unwindRva, byte[] unwind)
    {
        var table = RtlLookupFunctionEntry(unchecked((ulong)entry.ToInt64()), out var imageBase, IntPtr.Zero);
        if (table == IntPtr.Zero || imageBase != unchecked((ulong)Module.ToInt64()))
            throw new UnsupportedSignatureException("The fixed lower profile has no actual image-owned runtime function.");
        var rfRva = checked((uint)(table.ToInt64() - Module.ToInt64()));
        var rf = _lowerFile.CaptureCanonical(rfRva, 12);
        var begin = checked((uint)(entry.ToInt64() - Module.ToInt64()));
        var actualUnwind = BitConverter.ToUInt32(rf.Bytes, 8);
        if (BitConverter.ToUInt32(rf.Bytes, 0) != begin || BitConverter.ToUInt32(rf.Bytes, 4) != begin + length
            || unwindRva is { } expected && actualUnwind != expected)
            throw new UnsupportedSignatureException("The fixed lower complete runtime-function extent or unwind RVA differs.");
        var uw = _lowerFile.CaptureCanonical(actualUnwind, unwind.Length);
        if (!uw.Bytes.AsSpan().SequenceEqual(unwind))
            throw new UnsupportedSignatureException("The complete fixed lower unwind program differs.");
        _registration.Add(rf); _registration.Add(uw);
    }

    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);
    [DllImport("ntdll")] private static extern IntPtr RtlLookupFunctionEntry(ulong pc, out ulong imageBase, IntPtr history);
}
