using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class DobbyDetourEngine
{
    private NativePlaybackExecutableSeal? _playbackSeal;
    private long _actualPreparedCalls, _actualCommittedCalls;
    internal (long Prepared, long Committed) ActualPreparedCallCounts
        => (Interlocked.Read(ref _actualPreparedCalls), Interlocked.Read(ref _actualCommittedCalls));
    internal bool SharesPlaybackTransport(DobbyDetourEngine other)
        => _library == other._library && _path.Equals(other._path, StringComparison.OrdinalIgnoreCase);

    internal NativePlaybackExecutableSeal RetainPlaybackAuthority()
    {
        _playbackSeal ??= NativePlaybackExecutableSeal.Capture(_library, PreparedImageSha256);
        _playbackSeal.Verify();
        if (!NativePlaybackExecutableSeal.ModulePath(_library).Equals(_path, StringComparison.OrdinalIgnoreCase)
            || NativeLibrary.GetExport(_library, "DobbyPrepare") != IntPtr.Add(_library, 0x3060)
            || NativeLibrary.GetExport(_library, "DobbyCommit") != IntPtr.Add(_library, 0x2d30))
            throw new UnsupportedSignatureException("Playback installation requires the exact retained Dobby Prepare/Commit image and exports.");
        return _playbackSeal;
    }
}
