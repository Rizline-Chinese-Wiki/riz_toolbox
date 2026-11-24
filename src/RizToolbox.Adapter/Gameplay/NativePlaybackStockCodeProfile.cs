using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Fixed original bodies for the stock source lane. Metadata and code checks establish
/// offline evidence only. They cannot grant first-install admission or complete engine-writer coverage.</summary>
internal sealed class NativePlaybackStockCodeProfile
{
    internal const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    internal sealed record Body(string Name, int Rva, int Length, string Sha256);

    // One row per physical entry. Both pitch declarations use the SetPitch row.
    internal static IReadOnlyList<Body> HookBodies { get; } = Array.AsReadOnly(new[]
    {
        new Body("MUGMusicPlayer.GetTime", 0x3fbb10, 640, "32ee6e9d92f72b234c4003acda0a03336795a3719cd0b7f83debdd0ff58399bc"),
        new Body("MUGMusicPlayer.Pause", 0x3fbd90, 160, "6ed02e666fd0f1039a3ec777942542bab66f94ef9c5d4eb2c54bdbf510e14ce0"),
        new Body("MUGMusicPlayer.ReplayScheduled", 0x3fbe30, 560, "3eba14c6de352d349c62e582be4d7e0620c4d32eb62d0a762735075328aaee44"),
        new Body("MUGMusicPlayer.ResumeScheduled", 0x3fc060, 720, "34d0bff355b4b4efa623a8b81631cd736146af9dc07e9f1294b7fa4c9047bdca"),
        new Body("ProgressControl.Play", 0x400700, 272, "b3514fac283f1f83e31092b96e98c904fc1fe2821bbe37de731fbf530b65b3b3"),
        new Body("AudioSource.Pause", 0x1511640, 64, "6d0bfda80b85ad628d0bc2718bfd1b755e855f645641619805c14b39bff0d825"),
        new Body("AudioSource.PlayScheduled", 0x15116d0, 96, "a85d60f8b5c097c377d0b998c4ea6bd90ce2a51f0b7226b8612ffa1cc2582cef"),
        new Body("AudioSource.SetPitch", 0x15117c0, 80, "7f6d92b57a6420bd4fa11ecc508c0aaec5dca9f1dfd784cbd826e42ebbc2ea07"),
        new Body("AudioSource.set_clip", 0x1511a20, 80, "72f5e3938703c06d264f6e35bb93c85c0642f7157f1540bf41faf6a70b201e90"),
        new Body("AudioSource.set_time", 0x1511c00, 80, "af9ed985ff99f5f8b1c58c0ca92ea565a2de6cac11381c9147dd8cccc4763b63"),
        new Body("ProgressControl.<Start>d__25.MoveNext", 0x404990, 464, "3eb62e2d6e6e515b3f9d5abe5971eb942b46de3e87c51b74b23728c9a38563f5"),
        new Body("ProgressControl.set_MusicPlayer", 0x401700, 13, "0371e771ad93b0933dac17947821244eaa3537ea4353e77af9c85ef0cdac4dd8"),
        new Body("MUGMusicPlayer..ctor", 0x36c3e0, 64, "d9e5b877d7e952d3747cf8ee177244c56a9d8a6ada8c74d064ab85485a3a8450"),
        new Body("AudioSource.PlayHelper", 0x1511680, 80, "06d58d145acd4c5bd2f505dcf45ca8228913149f105a567b42c616d63191cbc4"),
        new Body("AudioSource.Play(Double)", 0x1511730, 80, "af45dbbe257899b158c056270345a029c7d7ff9f4fc6323d715e03d552082b3c"),
        new Body("AudioSource.Play()", 0x1511780, 64, "0b33e350a6fe9dc693e9d2f1799774e447e19b59cc5306fa09a16b90cca71a9b"),
        new Body("AudioSource.Stop()", 0x1511810, 64, "b18c02ed81b8e53b1aaa587a82aa10935e7305d53447cd4b67134df91cc97fed"),
        new Body("AudioSource.Stop(Boolean)", 0x1511850, 80, "3e4301aaba1399affedb0ac196872763b2b969ac7a378339fb8a597bedda19d3"),
        new Body("AudioSource.set_loop", 0x1511a70, 80, "bfd13a768216e2ccec1898e5c831cd4fd6ec1c5a83ff51fcabd808e80c743cf3"),
        new Body("ProgressControl.set_Clip", 0x4016f0, 13, "bb8036f89480f121c1bd96fd6bcd7c739d4d45f2bb58f562a0fb49b974aab390")
    });
    internal static IReadOnlyList<Body> ReadOnlyBodies { get; } = Array.AsReadOnly(new[]
    {
        new Body("ProgressControl.Update", 0x400fe0, 1728, "850dc15418ec4e14c8981d2b56569c6399915fdf0d74a3f557e16f7bd33866c9"),
        new Body("DspTimeService.get_DspTime", 0x3f7840, 832, "3f27ef0fe981fdef4f2d86c862f09587e441a5792ecb618288f672693f027bba"),
        new Body("AudioSource.GetPitch/get_pitch", 0x1511600, 64, "0c82b98521d9fad4823565b75f0100d41d9ffde7eca873ce12fad20e2f73a53e"),
        new Body("AudioSource.get_time", 0x15119a0, 64, "a4ba853a9fe464869e67896cfd49b8ccbf23a262a3fdec6c7810a516b822093a"),
        new Body("AudioSource.get_clip", 0x15118a0, 64, "5b215a8b1e39d153ee72cbe0e9972398f7f44972c7d0b141e231387f0cedff04")
    });

    private readonly IntPtr _module;
    private readonly string _path;
    private PlaybackSourceGroupLease? _group;
    private NativeRuntime.NativePlaybackStockTargets? _targets;
    private int _faulted;

    internal NativePlaybackStockCodeProfile(IntPtr module, PlaybackSourceGroupLease? group = null,
        NativeRuntime.NativePlaybackStockTargets? targets = null, NativePlaybackFilePreparation.PreparedFiles? preparedFiles = null)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || module == IntPtr.Zero)
            throw new UnsupportedSignatureException("Stock playback code requires a mapped Windows x64 image.");
        _module = module;
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped stock playback image path is unavailable.");
        _path = path.ToString();
        if (preparedFiles is not null) preparedFiles.VerifyStockImage(_path, Fingerprint);
        else
        {
            using var file = File.OpenRead(_path);
            using var hash = SHA256.Create();
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The stock playback image differs from the pinned fixed evidence.");
        }
        if ((group is null) != (targets is null) || targets is not null && targets.Module != module)
            throw new UnsupportedSignatureException("A source group profile requires its exact resolved stock target receipt.");
        _group = group; _targets = targets;
        VerifyCurrent();
    }

    /// <summary>Only the actual joint installation lease can account for all twenty stock physical members.
    /// Caller spans, copied member records and alias arrays are not accepted.</summary>
    internal void AttachSourceGroup(PlaybackSourceGroupLease group, NativeRuntime.NativePlaybackStockTargets targets)
    {
        ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(targets);
        if (_group is not null || targets.Module != _module || Volatile.Read(ref _faulted) != 0)
            throw new InvalidOperationException("A stock code profile accepts one matching current source group.");
        try
        {
            VerifyGroup(group, targets);
            VerifyBodies(group);
            VerifyGroup(group, targets);
            _targets = targets; _group = group;
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
    }

    internal void VerifyCurrent()
    {
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The stock playback code profile has been withdrawn.");
        try
        {
            if (GetModuleHandle(_path) != _module)
                throw new UnsupportedSignatureException("The mapped stock playback module changed or was unloaded.");
            if (_group is { } group) VerifyGroup(group, _targets!);
            VerifyBodies(_group);
            if (_group is { } after) VerifyGroup(after, _targets!);
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
    }

    internal void VerifyReadOnlyBodies(PlaybackSourceGroupLease group, NativeRuntime.NativePlaybackStockTargets targets)
    {
        if (Volatile.Read(ref _faulted) != 0 || !ReferenceEquals(_group, group) || !ReferenceEquals(_targets, targets))
            throw new UnsupportedSignatureException("The retained stock reader profile has been withdrawn or identifies another group.");
        try
        {
            if (GetModuleHandle(_path) != _module)
                throw new UnsupportedSignatureException("The mapped stock playback module changed or was unloaded.");
            VerifyPinnedBodies(ReadOnlyBodies);
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
    }

    private void VerifyGroup(PlaybackSourceGroupLease group, NativeRuntime.NativePlaybackStockTargets targets)
    {
        group.VerifyCurrent(targets);
        targets.VerifyCurrent();
    }

    private void VerifyBodies(PlaybackSourceGroupLease? group)
    {
        if (group is not null)
        {
            var bodies = HookBodies.Select(body => (Address: IntPtr.Add(_module, body.Rva), Hash: body.Sha256, body.Length)).ToArray();
            if (!group.MatchesOriginalBodies(bodies))
                throw new UnsupportedSignatureException("A source group original body differs from the fixed stock profile.");
        }
        VerifyPinnedBodies(group is null ? HookBodies.Concat(ReadOnlyBodies) : ReadOnlyBodies);
    }

    private void VerifyPinnedBodies(IEnumerable<Body> bodies)
    {
        foreach (var body in bodies)
        {
            var address = IntPtr.Add(_module, body.Rva);
            if (ExactHookBroker.TryVerifyLocalProgressUpdateBody(_module, address, body.Length, body.Sha256)
                || ExactHookBroker.TryVerifyDormantProgressPlayBody(_module, address, body.Length, body.Sha256)) continue;
            HookTargetValidation.VerifyExecutable(address, _module);
            HookTargetValidation.VerifyReadable(address, (nuint)body.Length);
            var bytes = new byte[body.Length]; Marshal.Copy(address, bytes, 0, bytes.Length);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("A stock playback source/reader body changed: " + body.Name);
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
