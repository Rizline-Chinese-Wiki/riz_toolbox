using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

/// <summary>A closed receipt for the fixed source entries plus GetTime. The issuing
/// paths own every body pin and both pitch aliases; callers cannot supply a
/// member list, replacement body pin, address mask or metadata DTO.</summary>
internal sealed class PlaybackSourceGroupTargets
{
    internal const string StockImageSha256 = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime.NativePlaybackStockTargets? _stock;
    // Identity projection only; the locked traversal authenticates this actual
    // receipt after a complete VerifyCurrent, never this projection alone.
    internal NativeRuntime.NativePlaybackStockTargets? StockIssuer => _stock;
    private readonly IntPtr _module;
    internal NativePlaybackLowerPitchTarget? LowerPitch { get; }
    internal NativePlaybackLowerPitchTarget.EnginePitchPreflight? EnginePitch { get; }
    internal NativePlaybackLowerPitchTarget.LowerTimePreflight? LowerTime { get; }
    internal bool HasLowerTime => LowerTime is not null;
    internal bool HasLowerPitch => LowerPitch is not null;
    internal bool HasEnginePitch => EnginePitch is not null;
    internal IntPtr SourceModule(NativePlaybackSourceRole role) => role is NativePlaybackSourceRole.LowerSetPitch or NativePlaybackSourceRole.EngineSetPitch or NativePlaybackSourceRole.LowerSetTime
        ? LowerPitch?.Module ?? throw new UnsupportedSignatureException("No exact lower member issuer.") : _module;
    private readonly bool _fixture;
    private readonly NativePlaybackExecutableSeal.RangeSeal? _resourceMarker;
    private readonly NativePlaybackExecutableSeal.RangeSeal? _unityBackingMarker;
    private readonly Dictionary<NativePlaybackSourceRole, VerifiedHookTarget> _sources;
    private readonly Dictionary<NativePlaybackSourceRole, (int Length, string Sha256)> _pins;
    internal VerifiedHookTarget GetTime { get; }
    internal VerifiedHookTarget PitchInstance => _sources[NativePlaybackSourceRole.SetPitch];
    internal VerifiedHookTarget PitchStatic { get; }
    internal VerifiedHookTarget Pause => _sources[NativePlaybackSourceRole.Pause];
    internal VerifiedHookTarget ReplayScheduled => _sources[NativePlaybackSourceRole.Replay];
    internal VerifiedHookTarget ResumeScheduled => _sources[NativePlaybackSourceRole.Resume];
    internal VerifiedHookTarget ProgressPlay => _sources[NativePlaybackSourceRole.ProgressPlay];
    internal VerifiedHookTarget AudioPause => _sources[NativePlaybackSourceRole.PauseLeaf];
    internal VerifiedHookTarget PlayScheduled => _sources[NativePlaybackSourceRole.PlayScheduled];
    internal VerifiedHookTarget ProgressStartMoveNext => _sources[NativePlaybackSourceRole.ProgressStartMoveNext];
    internal VerifiedHookTarget SetTime => _sources[NativePlaybackSourceRole.SetTime];
    internal VerifiedHookTarget SetClip => _sources[NativePlaybackSourceRole.SetClip];
    internal VerifiedHookTarget ProgressMusicPlayer => _sources[NativePlaybackSourceRole.ProgressMusicPlayer];
    internal IntPtr ProgressMusicPlayerTail => _fixture ? NativeLibrary.GetExport(_module, "StockProgressPlayerTail") : IntPtr.Add(_module, 0x263f60);
    internal VerifiedHookTarget ProgressClip => _sources[NativePlaybackSourceRole.ProgressClip];
    internal IntPtr ProgressClipTail => _fixture ? NativeLibrary.GetExport(_module, "StockProgressClipTail") : IntPtr.Add(_module, 0x263f60);
    internal IntPtr Module => _module;
    internal bool OwnedFixture => _fixture;
    internal bool HasPlayerConstructor { get; }
    internal bool HasDirectAudioControls { get; }
    internal bool HasProgressResource { get; }
    internal bool HasUnityBacking { get; }
    internal IEnumerable<NativePlaybackSourceRole> Roles => SourceRoles(HasPlayerConstructor, HasDirectAudioControls, HasProgressResource)
        .Concat(HasLowerPitch ? new[] { NativePlaybackSourceRole.LowerSetPitch, NativePlaybackSourceRole.EngineSetPitch } : Array.Empty<NativePlaybackSourceRole>())
        .Concat(HasLowerTime ? new[] { NativePlaybackSourceRole.LowerSetTime } : Array.Empty<NativePlaybackSourceRole>());
    internal int SourceRoleCount => (HasProgressResource ? 19 : HasDirectAudioControls ? 18 : HasPlayerConstructor ? 12 : 11) + (HasLowerPitch ? 2 : 0) + (HasLowerTime ? 1 : 0);
    internal string ImageSha256 { get; }
    internal int GetTimeBodyLength => 640;
    internal string GetTimeBodySha256 { get; }

    private PlaybackSourceGroupTargets(IntPtr module, NativeRuntime.NativePlaybackStockTargets? stock, bool fixture, bool constructorFixture = false, bool directAudioFixture = false, bool progressResourceFixture = false, bool unityBackingFixture = false, NativePlaybackLowerPitchTarget? lowerPitch = null, bool lowerTimeFixture = false)
    {
        (_module, _stock, _fixture) = (module, stock, fixture);
        LowerPitch = lowerPitch ?? (stock is null ? null : NativePlaybackLowerPitchTarget.ResolveStock(stock));
        if (fixture && LowerPitch is { } fixtureLower && fixtureLower.OwnedLowerTimeProfile != lowerTimeFixture)
            throw new UnsupportedSignatureException("Each owned two-image profile requires its exact fixed source group issuer.");
        if (LowerPitch is { } issued && (issued.WrapperModule != module || issued.OwnedFixture != fixture))
            throw new UnsupportedSignatureException("The lower receipt belongs to a different fixed two-image profile.");
        HasPlayerConstructor = stock is not null || constructorFixture || directAudioFixture || progressResourceFixture;
        HasDirectAudioControls = stock is not null || directAudioFixture || progressResourceFixture;
        HasProgressResource = stock is not null || progressResourceFixture;
        HasUnityBacking = stock is not null || unityBackingFixture;
        if (fixture && !unityBackingFixture && NativeLibrary.TryGetExport(module, "StockUnityBackingProfileMarker", out _))
            throw new UnsupportedSignatureException("The Unity-backed fixture requires its own complete source and backing issuer.");
        if (unityBackingFixture)
        {
            var expected = Encoding.ASCII.GetBytes("owned-unity-backing-lifetime-v1\0");
            _unityBackingMarker = NativePlaybackExecutableSeal.RangeSeal.Capture(
                NativeLibrary.GetExport(module, "StockUnityBackingProfileMarker"), expected.Length, module, true);
            if (_unityBackingMarker.StartProtection != 2 || !_unityBackingMarker.Bytes.AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("The exact Unity-backed fixture marker is absent or not read-only.");
        }
        if (fixture && !progressResourceFixture && NativeLibrary.TryGetExport(module, "StockProgressResourceProfileMarker", out _))
            throw new UnsupportedSignatureException("The Progress resource fixture requires its own complete nineteen-source issuer.");
        if (progressResourceFixture)
        {
            var expected = Encoding.ASCII.GetBytes("owned-progress-resource-continuity-v1\0");
            _resourceMarker = NativePlaybackExecutableSeal.RangeSeal.Capture(
                NativeLibrary.GetExport(module, "StockProgressResourceProfileMarker"), expected.Length, module, true);
            if (_resourceMarker.StartProtection != 2 || !_resourceMarker.Bytes.AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("The exact Progress resource fixture marker is absent or not read-only.");
        }
        if (stock is not null)
        {
            stock.VerifyCurrent();
            GetTime = stock.GetTime; PitchStatic = stock.PitchStatic;
            _sources = new()
            {
                [NativePlaybackSourceRole.SetPitch] = stock.PitchInstance,
                [NativePlaybackSourceRole.Pause] = stock.Pause,
                [NativePlaybackSourceRole.Replay] = stock.ReplayScheduled,
                [NativePlaybackSourceRole.Resume] = stock.ResumeScheduled,
                [NativePlaybackSourceRole.PlayScheduled] = stock.PlayScheduled,
                [NativePlaybackSourceRole.SetClip] = stock.SetClip,
                [NativePlaybackSourceRole.SetTime] = stock.SetTime,
                [NativePlaybackSourceRole.ProgressPlay] = stock.ProgressPlay,
                [NativePlaybackSourceRole.PauseLeaf] = stock.AudioPause,
                [NativePlaybackSourceRole.ProgressStartMoveNext] = stock.ProgressStartMoveNext,
                [NativePlaybackSourceRole.ProgressMusicPlayer] = stock.ProgressMusicPlayer,
                [NativePlaybackSourceRole.PlayerConstructor] = stock.PlayerConstructor,
                [NativePlaybackSourceRole.AudioPlayHelper] = stock.AudioPlayHelper,
                [NativePlaybackSourceRole.AudioPlayDelayed] = stock.AudioPlayDelayed,
                [NativePlaybackSourceRole.AudioPlay] = stock.AudioPlay,
                [NativePlaybackSourceRole.AudioStop] = stock.AudioStop,
                [NativePlaybackSourceRole.AudioStopBoolean] = stock.AudioStopBoolean,
                [NativePlaybackSourceRole.AudioSetLoop] = stock.AudioSetLoop,
                [NativePlaybackSourceRole.ProgressClip] = stock.ProgressClip
            };
            ImageSha256 = StockImageSha256;
            GetTimeBodySha256 = "32ee6e9d92f72b234c4003acda0a03336795a3719cd0b7f83debdd0ff58399bc";
            _pins = new()
            {
                [NativePlaybackSourceRole.SetPitch] = (80, "7f6d92b57a6420bd4fa11ecc508c0aaec5dca9f1dfd784cbd826e42ebbc2ea07"),
                [NativePlaybackSourceRole.Pause] = (160, "6ed02e666fd0f1039a3ec777942542bab66f94ef9c5d4eb2c54bdbf510e14ce0"),
                [NativePlaybackSourceRole.Replay] = (560, "3eba14c6de352d349c62e582be4d7e0620c4d32eb62d0a762735075328aaee44"),
                [NativePlaybackSourceRole.Resume] = (720, "34d0bff355b4b4efa623a8b81631cd736146af9dc07e9f1294b7fa4c9047bdca"),
                [NativePlaybackSourceRole.PlayScheduled] = (96, "a85d60f8b5c097c377d0b998c4ea6bd90ce2a51f0b7226b8612ffa1cc2582cef"),
                [NativePlaybackSourceRole.ProgressStartMoveNext] = (464, "3eb62e2d6e6e515b3f9d5abe5971eb942b46de3e87c51b74b23728c9a38563f5"),
                [NativePlaybackSourceRole.SetTime] = (80, "af9ed985ff99f5f8b1c58c0ca92ea565a2de6cac11381c9147dd8cccc4763b63"),
                [NativePlaybackSourceRole.SetClip] = (80, "72f5e3938703c06d264f6e35bb93c85c0642f7157f1540bf41faf6a70b201e90"),
                [NativePlaybackSourceRole.ProgressPlay] = (272, "b3514fac283f1f83e31092b96e98c904fc1fe2821bbe37de731fbf530b65b3b3"),
                [NativePlaybackSourceRole.PauseLeaf] = (64, "6d0bfda80b85ad628d0bc2718bfd1b755e855f645641619805c14b39bff0d825"),
                [NativePlaybackSourceRole.ProgressMusicPlayer] = (13, "0371e771ad93b0933dac17947821244eaa3537ea4353e77af9c85ef0cdac4dd8"),
                [NativePlaybackSourceRole.PlayerConstructor] = (64, "d9e5b877d7e952d3747cf8ee177244c56a9d8a6ada8c74d064ab85485a3a8450"),
                [NativePlaybackSourceRole.AudioPlayHelper] = (80, "06d58d145acd4c5bd2f505dcf45ca8228913149f105a567b42c616d63191cbc4"),
                [NativePlaybackSourceRole.AudioPlayDelayed] = (80, "af45dbbe257899b158c056270345a029c7d7ff9f4fc6323d715e03d552082b3c"),
                [NativePlaybackSourceRole.AudioPlay] = (64, "0b33e350a6fe9dc693e9d2f1799774e447e19b59cc5306fa09a16b90cca71a9b"),
                [NativePlaybackSourceRole.AudioStop] = (64, "b18c02ed81b8e53b1aaa587a82aa10935e7305d53447cd4b67134df91cc97fed"),
                [NativePlaybackSourceRole.AudioStopBoolean] = (80, "3e4301aaba1399affedb0ac196872763b2b969ac7a378339fb8a597bedda19d3"),
                [NativePlaybackSourceRole.AudioSetLoop] = (80, "bfd13a768216e2ccec1898e5c831cd4fd6ec1c5a83ff51fcabd808e80c743cf3"),
                [NativePlaybackSourceRole.ProgressClip] = (13, "bb8036f89480f121c1bd96fd6bcd7c739d4d45f2bb58f562a0fb49b974aab390")
            };
        }
        else
        {
            // Fixed owned-fixture exports and spans are part of this issuer.
            // Their original bytes still have to match the complete pinned
            // file plus every actual DIR64 relocation at group construction.
            GetTime = ReadFixtureMethod("StockMethodGetTime", "StockGetTime", ExactHookAbi.InstanceSingleReturn0);
            PitchStatic = ReadFixtureMethod("StockMethodSetPitch", "StockSetPitch", ExactHookAbi.PlaybackPitchSingle1Void);
            _sources = new(); _pins = new();
            foreach (var role in SourceRoles(HasPlayerConstructor, HasDirectAudioControls, HasProgressResource))
            {
                var (methodExport, entryExport, abi, length) = FixtureDeclaration(role);
                var target = ReadFixtureMethod(methodExport, entryExport, abi);
                var bodyHash = HashBody(target.Address, length);
                if (LowerPitch is { OwnedLowerTimeProfile: true } && bodyHash != OwnedPlaybackFixtureProfile.BodySha256(entryExport))
                    throw new UnsupportedSignatureException("Owned wrapper source body differs from its generated fixed profile.");
                _sources.Add(role, target); _pins.Add(role, (length, bodyHash));
            }
            GetTimeBodySha256 = HashBody(GetTime.Address, GetTimeBodyLength);
            if (LowerPitch is { OwnedLowerTimeProfile: true } && GetTimeBodySha256 != OwnedPlaybackFixtureProfile.BodySha256("StockGetTime"))
                throw new UnsupportedSignatureException("Owned GetTime body differs from its generated fixed profile.");
            using var file = File.OpenRead(NativePlaybackExecutableSeal.ModulePath(module));
            using var sha = SHA256.Create();
            ImageSha256 = Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant();
        }
        if (LowerPitch is { } lower)
        {
            EnginePitch = lower.PrepareEnginePitch();
            _sources.Add(NativePlaybackSourceRole.LowerSetPitch, lower.DetourIdentity);
            _pins.Add(NativePlaybackSourceRole.LowerSetPitch, (lower.BodyLength, lower.BodySha256));
            _sources.Add(NativePlaybackSourceRole.EngineSetPitch, EnginePitch.DetourIdentity);
            _pins.Add(NativePlaybackSourceRole.EngineSetPitch, (EnginePitch.BodyLength, EnginePitch.BodySha256));
            // The runtime-issued stock receipt includes the fixed lower-time
            // profile. Only the separate exact owned issuer may add it to a fixture.
            if (stock is not null || lowerTimeFixture)
            {
                LowerTime = lower.PrepareLowerTime();
                if (fixture && !lower.OwnedLowerTimeProfile || LowerTime.WrapperAddress != SetTime.Address
                    || !ReferenceEquals(LowerTime.Parent, lower) || LowerTime.Module != lower.Module
                    || SourceBodyLength(NativePlaybackSourceRole.SetTime) != 80)
                    throw new UnsupportedSignatureException("The lower-time group requires its exact metadata wrapper and native third-member issuer.");
                _sources.Add(NativePlaybackSourceRole.LowerSetTime, LowerTime.DetourIdentity);
                _pins.Add(NativePlaybackSourceRole.LowerSetTime, (LowerTime.BodyLength, LowerTime.BodySha256));
            }
        }
        VerifyCurrent();
    }

    internal static PlaybackSourceGroupTargets FromStock(NativeRuntime.NativePlaybackStockTargets stock)
    { ArgumentNullException.ThrowIfNull(stock); return new(stock.Module, stock, false); }
    internal static PlaybackSourceGroupTargets ResolveOwnedFixture(IntPtr module) => new(module, null, true);
    internal static PlaybackSourceGroupTargets ResolveOwnedLowerPitchFixture(IntPtr module, IntPtr lower)
        => new(module, null, true, true, true, true, true, NativePlaybackLowerPitchTarget.ResolveOwnedFixture(module, lower));
    internal static PlaybackSourceGroupTargets ResolveOwnedLowerTimeFixture(IntPtr module, IntPtr lower)
        => new(module, null, true, true, true, true, true,
            NativePlaybackLowerPitchTarget.ResolveOwnedFixture(module, lower), true);
    // Historical owned fixtures explicitly do not claim the new constructor role.
    // Frozen from the separately built constructor fixture. Historical owned
    // images keep their eleven source roles and cannot enter this issuer.
    internal const string ConstructorFixtureSha256 = "3b57292bec39c3e1a2b208fa5c63dd7382e55fb4aa1f8921c743d79dab85d693";
    internal static PlaybackSourceGroupTargets ResolveOwnedConstructorFixture(IntPtr module)
    {
        using var file = File.OpenRead(NativePlaybackExecutableSeal.ModulePath(module));
        using var sha = SHA256.Create();
        if (!Convert.ToHexString(sha.ComputeHash(file)).Equals(ConstructorFixtureSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("No exact frozen constructor fixture profile admits this image.");
        return new(module, null, true, true);
    }
    // Independently frozen image with all eighteen source entries. A fresh
    // recipe build must obtain a new reviewed profile; names alone cannot admit it.
    internal const string DirectAudioFixtureSha256 = "604ebbddb5c7fb19974b1b329a1a5bc6f43d10e6fe6ebfd0e77b6e9e59e186a3";
    // The same reviewed fixture source rebuilt from this tree. Its export table
    // and every pinned parent/leaf CALL RVA are identical to the frozen image
    // above; only the embedded PDB path differs, so its file bytes form a
    // distinct reviewed identity. No name-based rebuild is admitted.
    internal const string DirectAudioFixtureSha256Current = "91fd56654c9e185576e5a6ee4c4c6b9796da9ec541f919771911cd37ea98f122";
    internal const int DirectAudioFixtureResumeCountdownCallerRva = 0x1763;
    internal static bool IsDirectAudioFixtureImage(string imageSha256)
        => imageSha256 is DirectAudioFixtureSha256 or DirectAudioFixtureSha256Current;
    internal static PlaybackSourceGroupTargets ResolveOwnedDirectAudioFixture(IntPtr module)
    {
        using var file = File.OpenRead(NativePlaybackExecutableSeal.ModulePath(module));
        using var sha = SHA256.Create();
        if (!Convert.ToHexString(sha.ComputeHash(file)).Equals(DirectAudioFixtureSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("No fixed direct-audio fixture profile admits this image.");
        return new(module, null, true, true, true);
    }
    // New owned image, independently built and reviewed. Historical fixtures
    // retain their exact role counts and cannot opt into the resource setter.
    internal const string ProgressResourceFixtureSha256 = "4a76ea8f8a4bda5f8fde7b411ddb602085a2b99f95fd1883d0cce30febb32d1d";
    internal static PlaybackSourceGroupTargets ResolveOwnedProgressResourceFixture(IntPtr module)
    {
        using var file = File.OpenRead(NativePlaybackExecutableSeal.ModulePath(module));
        using var sha = SHA256.Create();
        if (!Convert.ToHexString(sha.ComputeHash(file)).Equals(ProgressResourceFixtureSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("No exact frozen Progress resource fixture profile admits this image.");
        return new(module, null, true, true, true, true);
    }
    internal const string UnityBackingFixtureSha256 = "22c3527d3a0be10ec1b5681f70855e0690c6396b7738533bbd62e841bae35c87";
    internal static PlaybackSourceGroupTargets ResolveOwnedUnityBackingFixture(IntPtr module)
    {
        using var file = File.OpenRead(NativePlaybackExecutableSeal.ModulePath(module));
        using var sha = SHA256.Create();
        if (!Convert.ToHexString(sha.ComputeHash(file)).Equals(UnityBackingFixtureSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("No exact frozen Unity-backed nineteen-source fixture profile admits this image.");
        return new(module, null, true, true, true, true, true);
    }
    internal static IEnumerable<NativePlaybackSourceRole> SourceRoles(bool constructor = false, bool directAudio = false, bool progressResource = false)
    {
        yield return NativePlaybackSourceRole.SetPitch; yield return NativePlaybackSourceRole.Pause;
        yield return NativePlaybackSourceRole.Replay; yield return NativePlaybackSourceRole.Resume;
        yield return NativePlaybackSourceRole.PlayScheduled; yield return NativePlaybackSourceRole.SetClip;
        yield return NativePlaybackSourceRole.ProgressPlay; yield return NativePlaybackSourceRole.PauseLeaf;
        yield return NativePlaybackSourceRole.ProgressMusicPlayer; yield return NativePlaybackSourceRole.SetTime;
        yield return NativePlaybackSourceRole.ProgressStartMoveNext;
        if (constructor) yield return NativePlaybackSourceRole.PlayerConstructor;
        if (directAudio)
        {
            yield return NativePlaybackSourceRole.AudioPlayHelper;
            yield return NativePlaybackSourceRole.AudioPlayDelayed;
            yield return NativePlaybackSourceRole.AudioPlay;
            yield return NativePlaybackSourceRole.AudioStop;
            yield return NativePlaybackSourceRole.AudioStopBoolean;
            yield return NativePlaybackSourceRole.AudioSetLoop;
        }
        if (progressResource) yield return NativePlaybackSourceRole.ProgressClip;
    }
    internal VerifiedHookTarget SourceTarget(NativePlaybackSourceRole role) => _sources.TryGetValue(role, out var target)
        ? target : throw new UnsupportedSignatureException("Unknown stock physical source role.");
    internal int SourceBodyLength(NativePlaybackSourceRole role) => _pins[role].Length;
    internal string SourceBodySha256(NativePlaybackSourceRole role) => _pins[role].Sha256;
    internal bool IdentifiesStock(NativeRuntime.NativePlaybackStockTargets stock)
        => ReferenceEquals(_stock, stock) && stock.Module == Module;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ReadPointer();
    private VerifiedHookTarget ReadFixtureMethod(string methodExport, string entryExport, ExactHookAbi abi)
    {
        var reader = NativeLibrary.GetExport(_module, methodExport); HookTargetValidation.VerifyExecutable(reader, _module);
        var method = Marshal.GetDelegateForFunctionPointer<ReadPointer>(reader)();
        HookTargetValidation.VerifyReadable(method, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        if (LowerPitch is { OwnedLowerTimeProfile: true })
            OwnedPlaybackFixtureProfile.RequireMethodIdentity(_module, methodExport, entryExport, method);
        var value = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method);
        if (value.MethodPointer != NativeLibrary.GetExport(_module, entryExport))
            throw new UnsupportedSignatureException("Owned source metadata does not identify its actual native entry.");
        return new("owned.stock." + methodExport, value.MethodPointer, method, value.Class, abi);
    }
    private static (string Method, string Entry, ExactHookAbi Abi, int Length) FixtureDeclaration(NativePlaybackSourceRole role) => role switch
    {
        NativePlaybackSourceRole.SetPitch => ("StockMethodSetPitchAlias", "StockSetPitch", ExactHookAbi.PlaybackPitchSingle1Void, 320),
        NativePlaybackSourceRole.Pause => ("StockMethodPause", "StockPause", ExactHookAbi.PlaybackVoid0, 160),
        NativePlaybackSourceRole.Replay => ("StockMethodReplay", "StockReplay", ExactHookAbi.PlaybackSingle1Void, 560),
        NativePlaybackSourceRole.Resume => ("StockMethodResume", "StockResume", ExactHookAbi.PlaybackSingle1Void, 720),
        NativePlaybackSourceRole.PlayScheduled => ("StockMethodPlayScheduled", "StockPlayScheduled", ExactHookAbi.PlaybackDouble1Void, 96),
        NativePlaybackSourceRole.SetTime => ("StockMethodSetTime", "StockSetTime", ExactHookAbi.PlaybackSingle1Void, 80),
        NativePlaybackSourceRole.PlayerConstructor => ("StockMethodPlayerConstructor", "StockPlayerConstructor", ExactHookAbi.PlaybackReference1Void, 64),
        NativePlaybackSourceRole.AudioPlayHelper => ("StockMethodAudioPlayHelper", "StockAudioPlayHelper", ExactHookAbi.PlaybackUInt64_1Void, 80),
        NativePlaybackSourceRole.AudioPlayDelayed => ("StockMethodAudioPlayDelayed", "StockAudioPlayDelayed", ExactHookAbi.PlaybackDouble1Void, 80),
        NativePlaybackSourceRole.AudioPlay => ("StockMethodAudioPlay", "StockAudioPlay", ExactHookAbi.PlaybackVoid0, 64),
        NativePlaybackSourceRole.AudioStop => ("StockMethodAudioStop", "StockAudioStop", ExactHookAbi.PlaybackVoid0, 64),
        NativePlaybackSourceRole.AudioStopBoolean => ("StockMethodAudioStopBoolean", "StockAudioStopBoolean", ExactHookAbi.PlaybackBoolean1Void, 80),
        NativePlaybackSourceRole.AudioSetLoop => ("StockMethodAudioSetLoop", "StockAudioSetLoop", ExactHookAbi.PlaybackBoolean1Void, 80),
        NativePlaybackSourceRole.SetClip => ("StockMethodSetClip", "StockSetClip", ExactHookAbi.PlaybackReference1Void, 80),
        NativePlaybackSourceRole.ProgressMusicPlayer => ("StockMethodProgressMusicPlayer", "StockProgressMusicPlayer", ExactHookAbi.PlaybackReference1Void, 13),
        NativePlaybackSourceRole.ProgressClip => ("StockMethodProgressClip", "StockProgressClip", ExactHookAbi.PlaybackReference1Void, 13),
        NativePlaybackSourceRole.ProgressPlay => ("StockMethodProgressPlay", "StockProgressPlay", ExactHookAbi.PlaybackBoolean1Void, 272),
        NativePlaybackSourceRole.ProgressStartMoveNext => ("StockMethodStartMoveNext", "StockStartMoveNext", ExactHookAbi.PlaybackBooleanReturn0, 464),
        NativePlaybackSourceRole.PauseLeaf => ("StockMethodPauseLeaf", "StockPauseLeaf", ExactHookAbi.PlaybackVoid0, 64),
        _ => throw new UnsupportedSignatureException("Unknown stock physical source role.")
    };
    private static string HashBody(IntPtr target, int length)
    {
        HookTargetValidation.VerifyReadable(target, (nuint)length);
        var bytes = new byte[length]; Marshal.Copy(target, bytes, 0, length);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    internal void VerifyCurrent()
    {
        using var family = new TargetFamilyVerification(this);
        LowerPitch?.VerifyCurrent(family);
        EnginePitch?.VerifySupportingEvidence(null, family);
        LowerTime?.VerifySupportingEvidence(null, family);
        _unityBackingMarker?.Verify();
        _resourceMarker?.Verify();
        if (_stock is not null) family.VerifyStock(_stock);
        if (GetTime.Abi != ExactHookAbi.InstanceSingleReturn0 || PitchInstance.Abi != ExactHookAbi.PlaybackPitchSingle1Void
            || PitchStatic.Abi != ExactHookAbi.PlaybackPitchSingle1Void || PitchInstance.Address != PitchStatic.Address
            || PitchInstance.MethodInfo == PitchStatic.MethodInfo || PitchInstance.DeclaringClass != PitchStatic.DeclaringClass
            || _sources.Count != SourceRoleCount || _sources.Values.Select(value => value.Address).Append(GetTime.Address).Distinct().Count() != SourceRoleCount + 1)
            throw new UnsupportedSignatureException("Playback group requires its exact distinct physical entries and one pitch alias pair.");
        HookTargetValidation.VerifyExecutable(GetTime.Address, _module);
        foreach (var role in Roles) HookTargetValidation.VerifyExecutable(SourceTarget(role).Address, SourceModule(role));
        if (_fixture)
        {
            if (ReadFixtureMethod("StockMethodGetTime", "StockGetTime", GetTime.Abi) != GetTime
                || ReadFixtureMethod("StockMethodSetPitch", "StockSetPitch", PitchStatic.Abi) != PitchStatic)
                throw new UnsupportedSignatureException("Owned fixture metadata exports changed their actual method identity.");
            foreach (var role in SourceRoles(HasPlayerConstructor, HasDirectAudioControls, HasProgressResource))
            {
                var (method, entry, abi, _) = FixtureDeclaration(role);
                if (ReadFixtureMethod(method, entry, abi) != SourceTarget(role))
                    throw new UnsupportedSignatureException("Owned fixture metadata exports changed their actual source identity.");
            }
        }
    }

    /// <summary>Only the common Stock/Lower ancestors of one synchronous
    /// TargetFamily verification. The body still verifies every child, member
    /// and image; this scope cannot issue a source or writer receipt.</summary>
    internal sealed class TargetFamilyVerification : IDisposable
    {
        private readonly PlaybackSourceGroupTargets _targets;
        private readonly NativeRuntime.NativePlaybackStockTargets? _stock;
        private readonly NativePlaybackLowerPitchTarget? _lower;
        private readonly int _thread;
        private bool _closed;

        internal TargetFamilyVerification(PlaybackSourceGroupTargets targets)
        {
            _targets = targets;
            _stock = targets._stock;
            _lower = targets.LowerPitch;
            _thread = Environment.CurrentManagedThreadId;
            VerifyAncestors();
        }

        private void RequireActive()
        {
            if (_closed || _thread != Environment.CurrentManagedThreadId
                || !ReferenceEquals(_targets._stock, _stock)
                || !ReferenceEquals(_targets.LowerPitch, _lower)
                || _lower is not null && (!ReferenceEquals(_lower.StockIssuer, _stock)
                    || _lower.WrapperModule != _targets.Module
                    || _lower.OwnedFixture != _targets.OwnedFixture))
                throw new UnsupportedSignatureException("TargetFamily ancestor sharing left its exact synchronous issuer.");
        }

        internal void VerifyStock(NativeRuntime.NativePlaybackStockTargets stock)
        {
            RequireActive();
            if (_stock is null || !ReferenceEquals(stock, _stock) || !_targets.IdentifiesStock(stock))
                throw new UnsupportedSignatureException("TargetFamily Stock ancestor belongs to another issuer.");
        }

        internal void VerifyLower(NativePlaybackLowerPitchTarget lower)
        {
            RequireActive();
            if (_lower is null || !ReferenceEquals(lower, _lower))
                throw new UnsupportedSignatureException("TargetFamily Lower ancestor belongs to another issuer.");
        }

        internal void VerifyEngine(NativePlaybackLowerPitchTarget.EnginePitchPreflight engine)
        {
            RequireActive();
            if (!ReferenceEquals(engine, _targets.EnginePitch) || !ReferenceEquals(engine.Parent, _lower))
                throw new UnsupportedSignatureException("TargetFamily engine member belongs to another issuer.");
        }

        internal void VerifyLowerTime(NativePlaybackLowerPitchTarget.LowerTimePreflight lowerTime)
        {
            RequireActive();
            if (!ReferenceEquals(lowerTime, _targets.LowerTime) || !ReferenceEquals(lowerTime.Parent, _lower))
                throw new UnsupportedSignatureException("TargetFamily lower-time member belongs to another issuer.");
        }

        private void VerifyAncestors()
        {
            RequireActive();
            if (_lower is not null) _lower.VerifySupportingEvidence();
            else _stock?.VerifyCurrent();
        }

        public void Dispose()
        {
            if (_closed) return;
            try { VerifyAncestors(); }
            finally { _closed = true; }
        }
    }
}
