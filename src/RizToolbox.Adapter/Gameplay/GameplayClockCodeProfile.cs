using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Optional provider path for a concrete broker-owned hook lease. A provider's generic
/// capability probe or source description cannot replace this private current code receipt.</summary>
internal interface INativePlaybackGetTimeHookProvider
{
    NativePlaybackGetTimeHookLease? CaptureGetTimeHook();
}

/// <summary>Code evidence for the pinned image, independent of metadata/root admission.
/// A mapped image can be checked without invoking any of its methods. Only a broker lease
/// can account for a changed GetTime head. A registered LOCAL Update owner
/// accounts for its exact branch, and each complete original remains pinned.</summary>
internal sealed class GameplayClockCodeProfile
{
    internal const int GetTimeRva = 0x3fbb10;
    private readonly IntPtr _module;
    private readonly string _modulePath;
    private readonly GameplayClockCodeProfile _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private NativePlaybackGetTimeHookLease? _getTimeHook;
    private IntPtr _methodInfo;
    private static readonly (string Producer, int Rva, int Length, string Sha256)[] Bodies =
    {
        ("InLevel.ProgressControl.Update", 0x400fe0, 1728, "850dc15418ec4e14c8981d2b56569c6399915fdf0d74a3f557e16f7bd33866c9"),
        ("InLevel.ProgressControl.RealTimeToChartTime", 0x400c80, 624, "198a612876421ca443fd64d8248f196f731b81764dcbbcd3dbc48021331b8d2c"),
        ("InLevel.ProgressControl.ChartTimeToRealTime", 0x400400, 624, "0a9e0327261de3d598c27d21a77f72adfbc5e9d587644b506a198008124efef2"),
        ("InLevel.Chart.get_ChartDelay", 0x3e5670, 32, "917d0db5fe3e213e43ba17fda6356f3da6d6db1ab08f42bd6d8bee9ae6126934"),
        ("Pigeon.MUGToolkit.Core.MUGMusicPlayer.GetTime", GetTimeRva, 640, "32ee6e9d92f72b234c4003acda0a03336795a3719cd0b7f83debdd0ff58399bc")
    };

    internal GameplayClockCodeProfile(IntPtr module, NativePlaybackGetTimeHookLease? lease = null, IntPtr methodInfo = default,
        NativePlaybackFilePreparation.PreparedFiles? preparedFiles = null)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || module == IntPtr.Zero)
            throw new UnsupportedSignatureException("Clock code evidence requires a mapped Windows x64 image.");
        _module = module;
        _registered = this;
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity) throw new UnsupportedSignatureException("The mapped clock image path is unavailable.");
        _modulePath = path.ToString();
        if (preparedFiles is not null) preparedFiles.VerifyStockImage(_modulePath, GameplayClockNativeProfile.Fingerprint);
        else
        {
            using var file = File.OpenRead(_modulePath);
            using var hash = SHA256.Create();
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(GameplayClockNativeProfile.Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The mapped clock image fingerprint differs from the pinned consumer evidence.");
        }
        if (lease is null) VerifyCurrent();
        else AttachGetTimeHook(lease, methodInfo);
    }

    internal void AttachGetTimeHook(NativePlaybackGetTimeHookLease lease, IntPtr methodInfo)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (methodInfo == IntPtr.Zero || _getTimeHook is not null)
            throw new InvalidOperationException("The code profile needs one exact metadata identity and one once-only GetTime hook lease.");
        VerifyModule();
        // Let the broker observe image changes before a local body check can throw.
        // Restoring changed bytes must not revive an already faulted hook owner.
        VerifyHookBody(lease, methodInfo, 640, Bodies[^1].Sha256);
        VerifyReadOnlyBodies();
        VerifyHookBody(lease, methodInfo, 640, Bodies[^1].Sha256);
        _methodInfo = methodInfo; _getTimeHook = lease;
    }

    internal void VerifyCurrent()
    {
        VerifyModule();
        if (_getTimeHook is { } current)
            VerifyHookBody(current, _methodInfo, 640, Bodies[^1].Sha256);
        foreach (var body in Bodies)
        {
            if (body.Rva == GetTimeRva && _getTimeHook is { } lease) VerifyHookBody(lease, _methodInfo, body.Length, body.Sha256);
            else VerifyBody(body.Producer, body.Rva, body.Length, body.Sha256);
        }
    }
    // Startup/Attach and explicit VerifyCurrent retain the complete fixed-body
    // audit. A Standard read checks this installed consumer registration and
    // its actual native GetTime channel, without re-hashing unrelated code.
    internal void VerifyOperation()
    {
        if (!ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId)
            throw new UnsupportedSignatureException("The clock consumer profile belongs to its actual registered owner thread.");
        if (_getTimeHook is null)
        {
            // Without a registered native channel, the fixed body audit remains
            // the only authority for this standalone consumer.
            VerifyCurrent();
            return;
        }
        VerifyModule();
        if (_getTimeHook is { } hook)
            hook.VerifyOperationCurrent(_module, IntPtr.Add(_module, GetTimeRva), _methodInfo);
        foreach (var body in Bodies)
            HookTargetValidation.VerifyExecutable(IntPtr.Add(_module, body.Rva), _module);
        VerifyModule();
    }

    // A consumer must retain the same concrete broker lease attached at
    // installation. Equal module/MethodInfo scalars and a copied lease do not
    // establish ownership of this running channel.
    internal void VerifyOperation(NativePlaybackGetTimeHookLease expectedHook)
    {
        ArgumentNullException.ThrowIfNull(expectedHook);
        if (!ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId
            || !ReferenceEquals(_getTimeHook, expectedHook))
            throw new UnsupportedSignatureException("The running clock consumer does not own its originally attached GetTime lease.");
        VerifyOperation();
    }
    // GetTime is verified by its retained broker lease. A caller brackets
    // these other fixed body checks with that exact channel's live checks.
    internal void VerifyReadOnlyBodies()
    {
        VerifyModule();
        foreach (var body in Bodies)
            if (body.Rva != GetTimeRva) VerifyBody(body.Producer, body.Rva, body.Length, body.Sha256);
    }

    private void VerifyModule()
    {
        if (GetModuleHandle(_modulePath) != _module)
            throw new UnsupportedSignatureException("The mapped clock code image changed or was unloaded.");
    }
    private void VerifyHookBody(NativePlaybackGetTimeHookLease lease, IntPtr methodInfo, int length, string sha256)
    {
        if (!lease.MatchesOriginalBody(_module, IntPtr.Add(_module, GetTimeRva), methodInfo, sha256, length))
            throw new UnsupportedSignatureException("The broker's complete original GetTime body differs from the pinned unit profile.");
    }
    private void VerifyBody(string producer, int rva, int length, string sha256)
    {
        var address = IntPtr.Add(_module, rva);
        if (ExactHookBroker.TryVerifyLocalProgressUpdateBody(_module, address, length, sha256)) return;
        HookTargetValidation.VerifyExecutable(address, _module);
        HookTargetValidation.VerifyReadable(address, (nuint)length);
        var bytes = new byte[length]; Marshal.Copy(address, bytes, 0, bytes.Length);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException($"The body of clock producer {producer} at RVA 0x{rva:X} differs from the pinned SHA-256 digest.");
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
