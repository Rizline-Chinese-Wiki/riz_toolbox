using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockSourceHub
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConfigureStockStopNative(IntPtr owner, IntPtr replayCaller, IntPtr resumeCaller,
        IntPtr replayMethod, IntPtr resumeMethod);

    internal void ConfigureStockStop(IntPtr replayCaller, IntPtr resumeCaller, IntPtr replayMethod, IntPtr resumeMethod)
    {
        VerifyThread(); _image.Verify();
        if (Export<VersionNative>("RtPlaybackStockStopVersion")() != 1
            || Export<ConfigureStockStopNative>("RtPlaybackSourceConfigureStockStop")(_owner,
                replayCaller, resumeCaller, replayMethod, resumeMethod) != 1)
            throw new UnsupportedSignatureException("The exact once-only stock Stop CALL profile was rejected.");
    }
}
